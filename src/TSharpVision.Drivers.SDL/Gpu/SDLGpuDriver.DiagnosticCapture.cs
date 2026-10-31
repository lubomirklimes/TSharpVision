using System.Globalization;

namespace TSharpVision.Drivers.SDL.Gpu;

public partial class SDLGpuDriver
{
    /// <summary>GPU fence poll granted to the render worker for one borrowed capture.</summary>
    internal static readonly TimeSpan DiagnosticFenceTimeout = TimeSpan.FromSeconds(2);
    /// <summary>Caller deadline for one borrowed capture, measured after its frame was submitted.</summary>
    internal static readonly TimeSpan DiagnosticCaptureDeadline = TimeSpan.FromSeconds(3);

    internal bool DiagnosticShaderPipelineActive => _gpuRenderer?.ShaderPipelineActive == true;

    /// <summary>
    /// UI owner thread: validates, registers one capture and submits exactly one frame through the
    /// application's MessageLoop, which pins that frame. Returns without waiting for the GPU: the
    /// fence, readback and metadata complete on the render worker and the returned task.
    /// </summary>
    internal Task<SdlDiagnosticCapture> BeginDiagnosticCapture(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var active = _gpuRenderer ?? throw new InvalidOperationException("GPU renderer detached.");
        if (MessageLoop is null || _window == IntPtr.Zero) throw new InvalidOperationException("GPU driver is not active.");
        if (!SDL3.SDL.GetWindowSizeInPixels(_window, out int w, out int h)) throw new InvalidOperationException("GPU window geometry unavailable.");
        DiagnosticCaptureLimits.Validate(w, h);
        uint props = SDL3.SDL.GetGPUDeviceProperties(active.DeviceForTesting);
        string adapter = SDL3.SDL.GetStringProperty(props, "SDL.gpu.device.name", "") ?? "";
        var submitted = new SubmittedCapture(w, h, _cols, _rows, active.CellWidth, active.CellHeight,
            adapter.Length == 0 ? "unavailable" : adapter);
        var pending = active.RequestCaptureForTesting((uint)w, (uint)h, DiagnosticFenceTimeout, token);
        try
        {
            MessageLoop(active);
            if (!active.CaptureFramePinned(pending))
                throw new InvalidOperationException("Application render callback did not submit a frame.");
        }
        catch
        {
            active.CancelDiagnosticCapture(pending);
            throw;
        }
        return CompleteDiagnosticCapture(active, pending, submitted, token);
    }

    private sealed record SubmittedCapture(int Width, int Height, int Columns, int Rows,
        int CellWidth, int CellHeight, string Adapter);

    private static async Task<SdlDiagnosticCapture> CompleteDiagnosticCapture(SDLGpuRenderer active,
        Task<GpuFrameCapture> pending, SubmittedCapture submitted, CancellationToken token)
    {
        GpuFrameCapture frame;
        try { frame = await pending.WaitAsync(DiagnosticCaptureDeadline, token).ConfigureAwait(false); }
        finally { active.CancelDiagnosticCapture(pending); }
        if (frame.Columns != submitted.Columns || frame.Rows != submitted.Rows
            || frame.Width != submitted.Width || frame.Height != submitted.Height)
            throw new InvalidOperationException("GPU capture geometry differs from the submitted grid.");
        // CPU compositing is reported as renderer pixels, never upgraded to shader GPU evidence.
        string layer = frame.Mode == "shader" ? "gpu-completed" : "renderer-pixels";
        return new(frame.Width, frame.Height, frame.Rgba, layer, new()
        {
            ["provider"] = "SDL_GPU readable rerender target",
            ["completion"] = "DownloadFromGPUTexture + QueryGPUFence + WaitForGPUFences completed",
            ["renderingPath"] = frame.Mode, ["backend"] = frame.Backend,
            ["gpu.device.name"] = submitted.Adapter,
            ["rendererRevision"] = frame.Frame.ToString(),
            ["fenceWaitMs"] = frame.GpuWait.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture),
            ["cellWidth"] = submitted.CellWidth.ToString(), ["cellHeight"] = submitted.CellHeight.ToString(),
            ["cursor"] = $"{frame.CursorX},{frame.CursorY}/{frame.CursorType}",
            ["presentation"] = "readable rerender target; swapchain/compositor/physical display not measured"
        });
    }
}
