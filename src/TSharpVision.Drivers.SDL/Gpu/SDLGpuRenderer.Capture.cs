using System.Diagnostics;
using System.Runtime.InteropServices;
using TSharpVision.Drivers.SDL.Graphics;

namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>
/// Pixels read back from a frame the GPU renderer actually rendered. RGBA8 (R = byte 0), top row
/// first, <see cref="Width"/> x <see cref="Height"/> device pixels.
/// </summary>
internal sealed record GpuFrameCapture(
    int    Width,
    int    Height,
    string NativeFormat,
    byte[] Rgba,
    string Mode,
    string Backend,
    long   Frame,
    int    Columns,
    int    Rows,
    int    CursorX,
    int    CursorY,
    ushort CursorType,
    long   FramesPresented,
    long   FramesSkipped,
    TimeSpan GpuWait,
    GpuDisplayEffect Effect = GpuDisplayEffect.Default);

/// <summary>
/// Native-pixel qualification seams. Nothing here runs unless a capture is requested: the render
/// loop checks one field per frame.
/// </summary>
internal sealed partial class SDLGpuRenderer
{
    /// <summary>
    /// Makes <see cref="TryCreateGpuPipeline"/> fail inside its own try block so the production
    /// CPU-compositing fallback is selected. False in normal operation.
    /// </summary>
    internal bool SimulatePipelineFailureForTesting { get; init; }

    /// <summary>True when frames go through the shader pipeline, false on the CPU-compositing fallback.</summary>
    internal bool ShaderPipelineActive => _termPipeline != null;

    /// <summary>The SDL_GPU device handle (zero before <see cref="Initialize"/>).</summary>
    internal IntPtr DeviceForTesting => _device;

    // Frames submitted to a swapchain image / frames dropped because no swapchain image was available.
    private long _framesPresented;
    private long _framesSkipped;

    internal long FramesPresented => Interlocked.Read(ref _framesPresented);
    internal long FramesSkipped   => Interlocked.Read(ref _framesSkipped);

    // ── Capture request lifecycle ─────────────────────────────────────────────
    //
    // One slot. The UI thread registers a request; the next frame it submits pins an exact copy of
    // that frame into the request, so a later frame can never be captured in its place. The render
    // worker claims a pinned request after rendering, rerenders the pinned frame into a readable
    // target and downloads it. An unclaimed request can be cancelled at any time; a claimed one is
    // finished by the worker, which observes cancellation while it polls the fence.
    //
    // The readable texture and download buffer belong to the worker from the claim on. They are
    // freed at once before submission, or once the fence is known signalled and waited. When
    // submission or completion is uncertain (no fence, fence timeout or cancellation, failed wait)
    // the GPU may still write them, so they are retained instead: released when a later claim finds
    // their fence signalled, or at device teardown after WaitForGPUIdle. With
    // MaximumRetainedCaptures retained, further claims fail rather than allocate more.

    private sealed class CaptureRequest
    {
        public required long MinFrame { get; init; }
        public required uint TargetWidth { get; init; }
        public required uint TargetHeight { get; init; }
        public required TimeSpan GpuTimeout { get; init; }
        public required CancellationToken Cancellation { get; init; }
        public TaskCompletionSource<GpuFrameCapture> Done { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Set once, under _snapLock, by the first submitted frame at or after MinFrame.
        public volatile PinnedFrame? Pinned;
    }

    private sealed record PinnedFrame(ScreenBuffer Buffer, uint Columns, uint Rows, long Frame,
        int CursorX, int CursorY, ushort CursorType, PixelGraphicsFrame Graphics);

    private readonly record struct RetainedCapture(IntPtr Fence, IntPtr Texture, IntPtr Transfer);

    /// <summary>Retained captures at which new claims fail instead of allocating more GPU memory.</summary>
    internal const int MaximumRetainedCaptures = 2;

    /// <summary>Longest GPU fence poll a request may ask for.</summary>
    internal static readonly TimeSpan MaximumGpuTimeout = TimeSpan.FromSeconds(30);

    // The render worker adds and reclaims; teardown releases after the worker has stopped.
    private readonly List<RetainedCapture> _retainedCaptures = new();

    internal int RetainedCaptureCount { get { lock (_retainedCaptures) return _retainedCaptures.Count; } }

    private CaptureRequest? _captureRequest;

    /// <summary>Native calls that finish a capture; tests substitute deterministic faults here only.</summary>
    internal CaptureGpuCalls CaptureCallsForTesting { get; init; } = CaptureGpuCalls.Sdl;

    /// <summary>Cancels whichever request is still unclaimed (teardown).</summary>
    internal void CancelDiagnosticCapture()
    {
        // A claimed request finishes under its existing bounded GPU fence wait.
        Interlocked.Exchange(ref _captureRequest, null)?.Done.TrySetCanceled();
    }

    /// <summary>Cancels <paramref name="pending"/> if it is still unclaimed; never touches a later request.</summary>
    internal void CancelDiagnosticCapture(Task<GpuFrameCapture> pending)
    {
        CaptureRequest? request = Volatile.Read(ref _captureRequest);
        if (request != null && request.Done.Task == pending
            && Interlocked.CompareExchange(ref _captureRequest, null, request) == request)
            request.Done.TrySetCanceled();
    }

    /// <summary>
    /// False while <paramref name="pending"/> still waits for a frame to be submitted; true once a
    /// submitted frame was pinned for it, or it has already left the slot.
    /// </summary>
    internal bool CaptureFramePinned(Task<GpuFrameCapture> pending)
    {
        CaptureRequest? request = Volatile.Read(ref _captureRequest);
        return request == null || request.Done.Task != pending || request.Pinned != null;
    }

    /// <summary>
    /// Asks the render thread to read back the next frame submitted after this call. The frame is
    /// rendered a second time, by the same production code (shader pipeline or CPU fallback, same
    /// cursor, same buffer), into a readable texture of the swapchain's format and size
    /// <paramref name="targetWidth"/> x <paramref name="targetHeight"/>, then downloaded.
    /// Swapchain textures are not used as a copy source: SDL_GPU does not guarantee that.
    /// Geometry, timeout and cancellation are validated before anything is allocated.
    /// </summary>
    internal Task<GpuFrameCapture> RequestCaptureForTesting(uint targetWidth, uint targetHeight, TimeSpan gpuTimeout,
        CancellationToken cancellation = default)
    {
        if (targetWidth > int.MaxValue || targetHeight > int.MaxValue)
            throw new NotSupportedException("Native capture exceeds the 16 MiB RGBA image limit.");
        DiagnosticCaptureLimits.Validate((int)targetWidth, (int)targetHeight);
        if (gpuTimeout <= TimeSpan.Zero || gpuTimeout > MaximumGpuTimeout)
            throw new ArgumentOutOfRangeException(nameof(gpuTimeout));
        cancellation.ThrowIfCancellationRequested();
        if (_disposed || _renderCts.IsCancellationRequested)
            throw new InvalidOperationException("GPU renderer is shutting down.");
        long next;
        lock (_snapLock) next = _snapFrame + 1;
        var request = new CaptureRequest
        {
            MinFrame = next, TargetWidth = targetWidth, TargetHeight = targetHeight, GpuTimeout = gpuTimeout,
            Cancellation = cancellation,
        };
        if (Interlocked.CompareExchange(ref _captureRequest, request, null) != null)
            throw new InvalidOperationException("A capture is already pending.");
        return request.Done.Task;
    }

    // SubmitFrame, under _snapLock, once _snap holds frame _snapFrame.
    private void PinCaptureFrame(uint cols, uint rows)
    {
        CaptureRequest? request = Volatile.Read(ref _captureRequest);
        if (request == null || request.Pinned != null || _snapFrame < request.MinFrame) return;
        var copy = new ScreenBuffer((ushort)cols, (ushort)rows);
        for (uint row = 0; row < rows; row++)
            for (uint col = 0; col < cols; col++)
                copy.SetChar(col, row, _snap!.GetChar(col, row));
        request.Pinned = new PinnedFrame(copy, cols, rows, _snapFrame, _cursorX, _cursorY, _cursorType, _snapGraphics);
    }

    private void ServeCaptureRequest()
    {
        CaptureRequest? request = Volatile.Read(ref _captureRequest);
        PinnedFrame? pinned = request?.Pinned;
        if (request == null || pinned == null) return;
        if (Interlocked.CompareExchange(ref _captureRequest, null, request) != request) return;
        if (request.Cancellation.IsCancellationRequested || _renderCts.IsCancellationRequested)
        {
            request.Done.TrySetCanceled();
            return;
        }
        try
        {
            ReclaimRetainedCaptures();
            if (RetainedCaptureCount >= MaximumRetainedCaptures)
                throw new InvalidOperationException(
                    "Earlier captures with unknown GPU completion are retained; capture is refused until they complete or the device is torn down.");
            request.Done.TrySetResult(CaptureFrame(request, pinned));
        }
        catch (OperationCanceledException) { request.Done.TrySetCanceled(); }
        catch (Exception ex) { request.Done.TrySetException(ex); }
    }

    // Shared by native capture and device-free pixel regression tests. Applies the display effect
    // of the frame being presented (_frameEffect), exactly as the CPU-fallback presentation does.
    private void CompositeCapturedFrame(PinnedFrame pinned)
    {
        uint screenW = pinned.Columns * (uint)_cellWidth;
        CompositeScreen(pinned.Buffer, 0, 0, pinned.Columns, pinned.Rows,
            screenW, pinned.CursorX, pinned.CursorY, pinned.CursorType);
        RasterizeGraphics(pinned.Graphics, screenW, pinned.Rows * (uint)_cellHeight);
        ApplyDisplayEffect(_frameEffect, screenW, pinned.Rows * (uint)_cellHeight);
    }

    private GpuFrameCapture CaptureFrame(CaptureRequest request, PinnedFrame pinned)
    {
        if (_device == IntPtr.Zero) throw new InvalidOperationException("No GPU device.");
        ScreenBuffer buf = pinned.Buffer;
        uint cols = pinned.Columns, rows = pinned.Rows;
        uint w = request.TargetWidth, h = request.TargetHeight;
        SDL3.SDL.GPUTextureFormat format = SDL3.SDL.GetGPUSwapchainTextureFormat(_device, _window);
        string formatName = format.ToString();
        bool bgra = formatName.StartsWith("B8G8R8A8", StringComparison.Ordinal);
        if (!bgra && !formatName.StartsWith("R8G8B8A8", StringComparison.Ordinal))
            throw new NotSupportedException($"Swapchain format {formatName} is not an 8-bit RGBA/BGRA format.");

        // Same pre-acquire work RenderSync does (both may wait for GPU idle when they reallocate).
        uint screenW = cols * (uint)_cellWidth, screenH = rows * (uint)_cellHeight;
        if (_termPipeline != null) _termPipeline.EnsureCapacity(cols, rows);
        else
        {
            EnsureScreenTexture(screenW, screenH);
            if (_screenTex == IntPtr.Zero) throw new InvalidOperationException("Fallback screen texture unavailable.");
        }

        var texInfo = new SDL3.SDL.GPUTextureCreateInfo
        {
            Type              = SDL3.SDL.GPUTextureType.TextureType2D,
            Format            = format,
            Usage             = SDL3.SDL.GPUTextureUsageFlags.ColorTarget | SDL3.SDL.GPUTextureUsageFlags.Sampler,
            Width             = w,
            Height            = h,
            LayerCountOrDepth = 1,
            NumLevels         = 1,
            SampleCount       = SDL3.SDL.GPUSampleCount.SampleCount1,
        };
        IntPtr target = SDL3.SDL.CreateGPUTexture(_device, in texInfo);
        if (target == IntPtr.Zero)
            throw new InvalidOperationException($"CreateGPUTexture (readback target) failed: {SDL3.SDL.GetError()}");

        // Bounded at request time: w * h * 4 <= DiagnosticCaptureLimits.MaximumRgbaBytes.
        uint byteCount = w * h * 4;
        var dlInfo = new SDL3.SDL.GPUTransferBufferCreateInfo
        {
            Usage = SDL3.SDL.GPUTransferBufferUsage.Download,
            Size  = byteCount,
        };
        IntPtr download = SDL3.SDL.CreateGPUTransferBuffer(_device, in dlInfo);
        IntPtr cmdBuf = IntPtr.Zero;
        string mode;
        GpuDisplayEffect drawnEffect = _frameEffect;
        CaptureGpuCalls calls = CaptureCallsForTesting;
        try
        {
            if (download == IntPtr.Zero)
                throw new InvalidOperationException($"CreateGPUTransferBuffer (download) failed: {SDL3.SDL.GetError()}");

            cmdBuf = SDL3.SDL.AcquireGPUCommandBuffer(_device);
            if (cmdBuf == IntPtr.Zero)
                throw new InvalidOperationException($"AcquireGPUCommandBuffer failed: {SDL3.SDL.GetError()}");

            if (_termPipeline != null)
            {
                _termPipeline.SetGraphics(pinned.Graphics, GraphicsScale);
                _termPipeline.RenderFrame(cmdBuf, target, w, h, buf, cols, rows,
                    pinned.CursorX, pinned.CursorY, pinned.CursorType, _physicalPalette,
                    _frameEffect, _metrics.Density);
                mode = "shader";
                // The effect actually drawn: Default when the requested effect is unavailable or
                // not implemented. Post effects are captured post-processed (same RenderFrame).
                drawnEffect = _termPipeline.LastDrawnEffect;
            }
            else
            {
                CompositeCapturedFrame(pinned);
                RecordFallbackFrame(cmdBuf, target, w, h, cols, rows, screenW, screenH);
                mode = "cpu-composite";
                drawnEffect = CpuFallbackEffect(_frameEffect);
            }

            IntPtr copyPass = SDL3.SDL.BeginGPUCopyPass(cmdBuf);
            var source = new SDL3.SDL.GPUTextureRegion
            {
                Texture = target, MipLevel = 0, Layer = 0, X = 0, Y = 0, Z = 0, W = w, H = h, D = 1,
            };
            var destination = new SDL3.SDL.GPUTextureTransferInfo
            {
                TransferBuffer = download, Offset = 0, PixelsPerRow = w, RowsPerLayer = h,
            };
            SDL3.SDL.DownloadFromGPUTexture(copyPass, in source, in destination);
            SDL3.SDL.EndGPUCopyPass(copyPass);
        }
        catch
        {
            // The GPU never saw this command buffer: cancel it and free everything now.
            if (cmdBuf != IntPtr.Zero) calls.CancelCommandBuffer(cmdBuf);
            if (download != IntPtr.Zero) calls.ReleaseTransferBuffer(_device, download);
            calls.ReleaseTexture(_device, target);
            throw;
        }

        byte[] rgba = SubmitAndReadBack(cmdBuf, target, download, byteCount,
            request.GpuTimeout, request.Cancellation, out TimeSpan gpuWait);
        if (bgra)
            for (int i = 0; i < rgba.Length; i += 4)
                (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);

        return new GpuFrameCapture((int)w, (int)h, formatName, rgba, mode, _backend, pinned.Frame,
            (int)cols, (int)rows, pinned.CursorX, pinned.CursorY, pinned.CursorType,
            FramesPresented, FramesSkipped, gpuWait, drawnEffect);
    }

    /// <summary>
    /// Render worker: submits a recorded capture and reads it back. Owns <paramref name="target"/> and
    /// <paramref name="download"/> from the call on: frees them once the GPU is known done, otherwise
    /// retains them (see the lifecycle notes above) and throws.
    /// </summary>
    internal byte[] SubmitAndReadBack(IntPtr cmdBuf, IntPtr target, IntPtr download, uint byteCount,
        TimeSpan gpuTimeout, CancellationToken cancellation, out TimeSpan gpuWait)
    {
        CaptureGpuCalls calls = CaptureCallsForTesting;
        IntPtr fence = calls.SubmitAndAcquireFence(cmdBuf);
        // From here the command buffer belongs to SDL; completion is unknown until a fence says so.
        if (fence == IntPtr.Zero)
        {
            Retain(new RetainedCapture(IntPtr.Zero, target, download));
            throw new InvalidOperationException(
                $"SubmitGPUCommandBufferAndAcquireFence failed: {calls.Error()}; completion unknown, target and transfer buffer retained until device teardown.");
        }

        // Bounded poll on the worker: never block forever on the GPU. A single native call is not interruptible.
        var sw = Stopwatch.StartNew();
        while (!calls.QueryFence(_device, fence))
        {
            if (cancellation.IsCancellationRequested || _renderCts.IsCancellationRequested)
            {
                Retain(new RetainedCapture(fence, target, download));
                throw new OperationCanceledException("GPU readback cancelled before its fence signalled; target and transfer buffer retained.");
            }
            if (sw.Elapsed > gpuTimeout)
            {
                Retain(new RetainedCapture(fence, target, download));
                throw new TimeoutException($"GPU readback fence not signalled within {gpuTimeout.TotalSeconds:F0} s; target and transfer buffer retained.");
            }
            Thread.Sleep(1);
        }
        sw.Stop();
        gpuWait = sw.Elapsed;
        // The fence is signalled, so this returns at once — but it is what makes SDL clean up
        // the finished command buffer. On Direct3D 12 a download whose row pitch is not
        // 256-byte aligned is staged and only copied into the transfer buffer by that cleanup;
        // QueryGPUFence alone leaves the transfer buffer unwritten.
        if (!calls.WaitFence(_device, fence))
        {
            Retain(new RetainedCapture(fence, target, download));
            throw new InvalidOperationException($"WaitForGPUFences failed: {calls.Error()}; target and transfer buffer retained.");
        }
        calls.ReleaseFence(_device, fence);

        try
        {
            byte[] rgba = new byte[byteCount];
            IntPtr mapped = calls.Map(_device, download);
            if (mapped == IntPtr.Zero)
                throw new InvalidOperationException($"MapGPUTransferBuffer (download) failed: {calls.Error()}");
            Marshal.Copy(mapped, rgba, 0, rgba.Length);
            calls.Unmap(_device, download);
            return rgba;
        }
        finally
        {
            calls.ReleaseTransferBuffer(_device, download);
            calls.ReleaseTexture(_device, target);
        }
    }

    private void Retain(RetainedCapture capture)
    {
        lock (_retainedCaptures) _retainedCaptures.Add(capture);
    }

    /// <summary>Render worker: frees retained captures whose fence is now signalled and waited.</summary>
    internal void ReclaimRetainedCaptures()
    {
        CaptureGpuCalls calls = CaptureCallsForTesting;
        lock (_retainedCaptures)
        {
            for (int i = _retainedCaptures.Count - 1; i >= 0; i--)
            {
                RetainedCapture r = _retainedCaptures[i];
                if (r.Fence == IntPtr.Zero || !calls.QueryFence(_device, r.Fence) || !calls.WaitFence(_device, r.Fence))
                    continue;
                calls.ReleaseFence(_device, r.Fence);
                calls.ReleaseTransferBuffer(_device, r.Transfer);
                calls.ReleaseTexture(_device, r.Texture);
                _retainedCaptures.RemoveAt(i);
            }
        }
    }

    /// <summary>Teardown only — the worker has stopped and WaitForGPUIdle returned: frees every retained capture.</summary>
    internal void ReleaseRetainedCapturesAfterIdle()
    {
        CaptureGpuCalls calls = CaptureCallsForTesting;
        lock (_retainedCaptures)
        {
            foreach (RetainedCapture r in _retainedCaptures)
            {
                if (r.Fence != IntPtr.Zero) calls.ReleaseFence(_device, r.Fence);
                calls.ReleaseTransferBuffer(_device, r.Transfer);
                calls.ReleaseTexture(_device, r.Texture);
            }
            _retainedCaptures.Clear();
        }
    }
}

/// <summary>
/// The native calls that submit, complete and release a capture. <see cref="Sdl"/> is production;
/// tests replace individual calls to inject submission, fence and wait faults without a GPU.
/// </summary>
internal sealed class CaptureGpuCalls
{
    internal static readonly CaptureGpuCalls Sdl = new();
    internal Func<IntPtr, IntPtr> SubmitAndAcquireFence { get; init; } = buffer => SDL3.SDL.SubmitGPUCommandBufferAndAcquireFence(buffer);
    internal Func<IntPtr, IntPtr, bool> QueryFence { get; init; } = (device, fence) => SDL3.SDL.QueryGPUFence(device, fence);
    internal Func<IntPtr, IntPtr, bool> WaitFence { get; init; } = (device, fence) => SDL3.SDL.WaitForGPUFences(device, true, [fence], 1);
    internal Action<IntPtr, IntPtr> ReleaseFence { get; init; } = (device, fence) => SDL3.SDL.ReleaseGPUFence(device, fence);
    internal Func<IntPtr, IntPtr, IntPtr> Map { get; init; } = (device, buffer) => SDL3.SDL.MapGPUTransferBuffer(device, buffer, false);
    internal Action<IntPtr, IntPtr> Unmap { get; init; } = (device, buffer) => SDL3.SDL.UnmapGPUTransferBuffer(device, buffer);
    internal Action<IntPtr, IntPtr> ReleaseTransferBuffer { get; init; } = (device, buffer) => SDL3.SDL.ReleaseGPUTransferBuffer(device, buffer);
    internal Action<IntPtr, IntPtr> ReleaseTexture { get; init; } = (device, texture) => SDL3.SDL.ReleaseGPUTexture(device, texture);
    internal Action<IntPtr> CancelCommandBuffer { get; init; } = buffer => SDL3.SDL.CancelGPUCommandBuffer(buffer);
    internal Func<string> Error { get; init; } = () => SDL3.SDL.GetError() ?? "";
}
