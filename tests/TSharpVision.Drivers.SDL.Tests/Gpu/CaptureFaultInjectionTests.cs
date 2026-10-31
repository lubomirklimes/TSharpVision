using System.Reflection;
using System.Runtime.InteropServices;
using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// Simulated GPU completion faults through <see cref="CaptureGpuCalls"/>. No device is created: this
/// qualifies ownership bookkeeping (release vs retention), not real driver or hardware behaviour.
/// </summary>
public sealed class CaptureFaultInjectionTests : IDisposable
{
    private static readonly IntPtr Fence = new(0x10), Texture = new(0x20), Transfer = new(0x30), Command = new(0x40);
    private const uint Bytes = 16;
    private readonly IntPtr _mapped = Marshal.AllocHGlobal((int)Bytes);
    private readonly Gpu _gpu = new();

    public CaptureFaultInjectionTests()
    {
        for (int i = 0; i < Bytes; i++) Marshal.WriteByte(_mapped, i, (byte)(i + 1));
        _gpu.Mapped = _mapped;
    }
    public void Dispose() => Marshal.FreeHGlobal(_mapped);

    private SDLGpuRenderer Renderer() => new(IntPtr.Zero, new SDLGpuOptions()) { CaptureCallsForTesting = _gpu.Calls() };

    private static byte[] Read(SDLGpuRenderer renderer, TimeSpan? timeout = null, CancellationToken token = default) =>
        renderer.SubmitAndReadBack(Command, Texture, Transfer, Bytes, timeout ?? TimeSpan.FromSeconds(2), token, out _);

    [Fact]
    public void CompletedReadbackReleasesEachResourceOnceAndReturnsAnOwnedCopy()
    {
        using var renderer = Renderer();
        _gpu.SignalAfter = 2;
        byte[] rgba = Read(renderer);
        Marshal.WriteByte(_mapped, 0, 0xFF);
        Assert.Equal(Enumerable.Range(1, (int)Bytes).Select(i => (byte)i), rgba);
        Assert.Equal((1, 1, 1, 1, 1), (_gpu.FenceReleases, _gpu.TextureReleases, _gpu.TransferReleases, _gpu.Waits, _gpu.Unmaps));
        Assert.Equal(0, renderer.RetainedCaptureCount);
    }

    [Fact]
    public void NullFenceIsUncertainCompletionAndRetainsUntilTeardown()
    {
        using var renderer = Renderer();
        _gpu.SubmitFails = true;
        Assert.Contains("retained", Assert.Throws<InvalidOperationException>(() => Read(renderer)).Message);
        Assert.Equal(1, renderer.RetainedCaptureCount);
        renderer.ReclaimRetainedCaptures(); // no fence: nothing can prove completion before teardown
        Assert.Equal(1, renderer.RetainedCaptureCount);
        Assert.Equal((0, 0, 0), (_gpu.FenceReleases, _gpu.TextureReleases, _gpu.TransferReleases));
        renderer.ReleaseRetainedCapturesAfterIdle();
        renderer.ReleaseRetainedCapturesAfterIdle();
        Assert.Equal((0, 1, 1), (_gpu.FenceReleases, _gpu.TextureReleases, _gpu.TransferReleases));
        Assert.Equal(0, renderer.RetainedCaptureCount);
    }

    [Fact]
    public void FenceTimeoutRetainsAndALaterSignalReclaimsExactlyOnce()
    {
        using var renderer = Renderer();
        _gpu.SignalAfter = int.MaxValue; // also how a failing QueryGPUFence presents: it only returns false
        Assert.Throws<TimeoutException>(() => Read(renderer, TimeSpan.FromMilliseconds(20)));
        Assert.Equal(1, renderer.RetainedCaptureCount);
        Assert.Equal((0, 0, 0, 0), (_gpu.FenceReleases, _gpu.TextureReleases, _gpu.TransferReleases, _gpu.Maps));
        renderer.ReclaimRetainedCaptures();
        Assert.Equal(1, renderer.RetainedCaptureCount);
        _gpu.SignalAfter = 0;
        renderer.ReclaimRetainedCaptures();
        renderer.ReclaimRetainedCaptures();
        renderer.ReleaseRetainedCapturesAfterIdle();
        Assert.Equal((1, 1, 1), (_gpu.FenceReleases, _gpu.TextureReleases, _gpu.TransferReleases));
        Assert.Equal(0, renderer.RetainedCaptureCount);
    }

    [Fact]
    public void WaitFailureRetainsFenceAndResourcesUntilTeardown()
    {
        using var renderer = Renderer();
        _gpu.WaitFails = true;
        Assert.Throws<InvalidOperationException>(() => Read(renderer));
        renderer.ReclaimRetainedCaptures();
        Assert.Equal(1, renderer.RetainedCaptureCount);
        Assert.Equal(0, _gpu.Maps);
        renderer.ReleaseRetainedCapturesAfterIdle();
        Assert.Equal((1, 1, 1), (_gpu.FenceReleases, _gpu.TextureReleases, _gpu.TransferReleases));
    }

    [Fact]
    public void CancellationWhileClaimedStopsPollingAndRetains()
    {
        using var renderer = Renderer();
        using var cancel = new CancellationTokenSource();
        _gpu.SignalAfter = int.MaxValue;
        _gpu.OnQuery = cancel.Cancel;
        Assert.ThrowsAny<OperationCanceledException>(() => Read(renderer, TimeSpan.FromSeconds(10), cancel.Token));
        Assert.Equal(1, renderer.RetainedCaptureCount);
        Assert.Equal((0, 0, 0), (_gpu.FenceReleases, _gpu.TextureReleases, _gpu.TransferReleases));
    }

    [Fact]
    public void ShutdownWhileClaimedStopsPollingAndTeardownReleases()
    {
        var renderer = Renderer();
        _gpu.SignalAfter = int.MaxValue;
        _gpu.OnQuery = () => Get<CancellationTokenSource>(renderer, "_renderCts").Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Read(renderer, TimeSpan.FromSeconds(10)));
        Assert.Equal(1, renderer.RetainedCaptureCount);
        renderer.Dispose(TimeSpan.FromSeconds(1));
        // Without a device Dispose has nothing to idle; the device path releases right after WaitForGPUIdle.
        renderer.ReleaseRetainedCapturesAfterIdle();
        Assert.Equal((1, 1, 1), (_gpu.FenceReleases, _gpu.TextureReleases, _gpu.TransferReleases));
        Assert.Throws<InvalidOperationException>(() => { _ = renderer.RequestCaptureForTesting(8, 8, TimeSpan.FromSeconds(1)); });
    }

    [Fact]
    public void MapFailureAfterDefiniteCompletionReleasesImmediately()
    {
        using var renderer = Renderer();
        _gpu.Mapped = IntPtr.Zero;
        Assert.Throws<InvalidOperationException>(() => Read(renderer));
        Assert.Equal((1, 1, 1), (_gpu.FenceReleases, _gpu.TextureReleases, _gpu.TransferReleases));
        Assert.Equal(0, renderer.RetainedCaptureCount);
    }

    [Fact]
    public async Task RetentionLimitRefusesFurtherClaimsBeforeAllocation()
    {
        using var renderer = Renderer();
        _gpu.SubmitFails = true;
        for (int i = 0; i < SDLGpuRenderer.MaximumRetainedCaptures; i++)
            Assert.Throws<InvalidOperationException>(() => Read(renderer));
        var pending = renderer.RequestCaptureForTesting(8, 4, TimeSpan.FromSeconds(1));
        Submit(renderer, 'A');
        Serve(renderer);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Contains("retained", refused.Message);
        Assert.Equal(SDLGpuRenderer.MaximumRetainedCaptures, renderer.RetainedCaptureCount);
        Assert.Equal(SDLGpuRenderer.MaximumRetainedCaptures, _gpu.Submits);
        renderer.ReleaseRetainedCapturesAfterIdle();
        Assert.Equal((0, 2, 2), (_gpu.FenceReleases, _gpu.TextureReleases, _gpu.TransferReleases));
    }

    [Fact]
    public async Task CancelledBeforeClaimNeverAllocates()
    {
        using var renderer = Renderer();
        using var cancel = new CancellationTokenSource();
        var pending = renderer.RequestCaptureForTesting(8, 4, TimeSpan.FromSeconds(1), cancel.Token);
        Submit(renderer, 'A');
        cancel.Cancel();
        Serve(renderer);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, _gpu.Submits);
        Assert.True(renderer.CaptureFramePinned(pending));
        Assert.Throws<OperationCanceledException>(() => { _ = renderer.RequestCaptureForTesting(8, 4, TimeSpan.FromSeconds(1), cancel.Token); });
    }

    [Fact]
    public void SubmittedFrameIsPinnedAndLaterFramesCannotReplaceIt()
    {
        using var renderer = Renderer();
        var pending = renderer.RequestCaptureForTesting(8, 4, TimeSpan.FromSeconds(1));
        Assert.False(renderer.CaptureFramePinned(pending));
        renderer.SetCursor(1, 0, 3);
        Submit(renderer, 'A');
        Assert.True(renderer.CaptureFramePinned(pending));
        renderer.SetCursor(0, 0, 0);
        Submit(renderer, 'B');
        object pinned = Pinned(renderer);
        var buffer = (ScreenBuffer)pinned.GetType().GetProperty("Buffer")!.GetValue(pinned)!;
        Assert.Equal('A', buffer.GetChar(0, 0).Character);
        Assert.Equal(1L, (long)pinned.GetType().GetProperty("Frame")!.GetValue(pinned)!);
        Assert.Equal((ushort)3, (ushort)pinned.GetType().GetProperty("CursorType")!.GetValue(pinned)!);
    }

    // No SDL device: exercise the exact CPU composition method used by CaptureFrame,
    // with the real request/Render pinning path. Native upload/readback is not simulated here.
    [Theory]
    [InlineData(1, 0, 100)]
    [InlineData(2, 1, 3)]
    [InlineData(0, 1, 3)]
    [InlineData(1, 0, 0)]
    [InlineData(-1, 0, 100)]
    [InlineData(4, 2, 3)]
    public void Cpu_capture_pixels_use_pinned_cursor_and_cells(int x, int y, ushort type)
    {
        using var renderer = Renderer();
        var screen = new ScreenBuffer(4, 2);
        for (uint row = 0; row < 2; row++)
            for (uint col = 0; col < 4; col++)
                screen.SetChar(col, row, new TScreenChar(' ', (TColorAttr)0x17));
        for (int round = 0; round < 2; round++)
        {
            renderer.SetCursor(x, y, type);
            byte[] expected = renderer.CompositeForTesting(screen, 4, 2).ToArray();
            var pending = renderer.RequestCaptureForTesting(32, 32, TimeSpan.FromSeconds(1));
            renderer.Render(screen, 0, 0, 4, 2);
            Assert.True(renderer.CaptureFramePinned(pending));
            object pinned = Pinned(renderer);
            Assert.Equal(x, pinned.GetType().GetProperty("CursorX")!.GetValue(pinned));
            Assert.Equal(y, pinned.GetType().GetProperty("CursorY")!.GetValue(pinned));
            Assert.Equal(type, pinned.GetType().GetProperty("CursorType")!.GetValue(pinned));
            renderer.SetCursor(0, 1, 100);
            var later = new ScreenBuffer(4, 2);
            renderer.Render(later, 0, 0, 4, 2);
            byte[] live = renderer.CompositeForTesting(screen, 4, 2).ToArray();
            Assert.False(expected.SequenceEqual(live));
            typeof(SDLGpuRenderer).GetMethod("CompositeCapturedFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(renderer, new[] { pinned });
            byte[] captured = Get<byte[]>(renderer, "_cpuPixels");
            Assert.Equal(expected, captured);
            // Independent pixel oracle for blank cells with attribute 0x17: blue background,
            // gray block/underline. Check every row so type mismatches cannot hide in position checks.
            int width = 4 * renderer.CellWidth;
            for (int py = 0; py < 2 * renderer.CellHeight; py++)
                for (int px = 0; px < width; px++)
                {
                    bool cursor = type != 0 && px / renderer.CellWidth == x && py / renderer.CellHeight == y;
                    bool foreground = cursor && (type >= 100
                        || py % renderer.CellHeight >= renderer.CellHeight - Get<int>(renderer, "_underlineHeight"));
                    int offset = (py * width + px) * 4;
                    Assert.Equal(foreground ? (byte)170 : (byte)0, captured[offset]);
                    Assert.Equal(foreground ? (byte)170 : (byte)0, captured[offset + 1]);
                    Assert.Equal((byte)170, captured[offset + 2]);
                    Assert.Equal((byte)255, captured[offset + 3]);
                }
            // Capturing A must not temporarily overwrite or leave behind A's cursor as live state.
            Assert.Equal(live, renderer.CompositeForTesting(screen, 4, 2));
            Assert.Equal(0, Get<int>(renderer, "_cursorX"));
            Assert.Equal(1, Get<int>(renderer, "_cursorY"));
            Assert.Equal((ushort)100, Get<ushort>(renderer, "_cursorType"));
            renderer.CancelDiagnosticCapture(pending);
        }
    }

    [Fact]
    public async Task CancellingAnEarlierTaskNeverCancelsALaterRequest()
    {
        using var renderer = Renderer();
        var first = renderer.RequestCaptureForTesting(8, 4, TimeSpan.FromSeconds(1));
        renderer.CancelDiagnosticCapture(first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var second = renderer.RequestCaptureForTesting(8, 4, TimeSpan.FromSeconds(1));
        renderer.CancelDiagnosticCapture(first);
        Assert.False(second.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => { _ = renderer.RequestCaptureForTesting(8, 4, TimeSpan.FromSeconds(1)); });
    }

    [Theory]
    [InlineData(0u, 1u, 1000)]
    [InlineData(1u, 0u, 1000)]
    [InlineData(4096u, 1025u, 1000)]
    [InlineData(65536u, 65536u, 1000)]
    [InlineData(uint.MaxValue, 1u, 1000)]
    [InlineData(80u, 25u, 0)]
    [InlineData(80u, 25u, 31000)]
    public void InvalidRequestsAreRejectedWithoutOccupyingTheSlot(uint width, uint height, int timeoutMs)
    {
        using var renderer = Renderer();
        Assert.ThrowsAny<Exception>(() => { _ = renderer.RequestCaptureForTesting(width, height, TimeSpan.FromMilliseconds(timeoutMs)); });
        var valid = renderer.RequestCaptureForTesting(80, 25, TimeSpan.FromSeconds(1));
        Assert.False(valid.IsCompleted);
    }

    private static void Submit(SDLGpuRenderer renderer, char ch)
    {
        var screen = new ScreenBuffer(4, 2);
        screen.SetChar(0, 0, new TScreenChar(ch, (TColorAttr)0x17));
        renderer.Render(screen, 0, 0, 4, 2);
    }
    private static void Serve(SDLGpuRenderer renderer) =>
        typeof(SDLGpuRenderer).GetMethod("ServeCaptureRequest", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(renderer, null);
    private static object Pinned(SDLGpuRenderer renderer)
    {
        object request = Get<object>(renderer, "_captureRequest");
        return request.GetType().GetField("Pinned")!.GetValue(request)!;
    }
    private static T Get<T>(object target, string name)
        => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private sealed class Gpu
    {
        internal int SignalAfter, Queries, Submits, Waits, Maps, Unmaps, FenceReleases, TextureReleases, TransferReleases;
        internal bool SubmitFails, WaitFails;
        internal IntPtr Mapped;
        internal Action? OnQuery;
        internal CaptureGpuCalls Calls() => new()
        {
            SubmitAndAcquireFence = command => { Assert.Equal(Command, command); Submits++; return SubmitFails ? IntPtr.Zero : Fence; },
            QueryFence = (_, fence) => { Assert.Equal(Fence, fence); OnQuery?.Invoke(); return ++Queries > SignalAfter; },
            WaitFence = (_, fence) => { Assert.Equal(Fence, fence); Waits++; return !WaitFails; },
            ReleaseFence = (_, fence) => { Assert.Equal(Fence, fence); FenceReleases++; },
            Map = (_, buffer) => { Assert.Equal(Transfer, buffer); Maps++; return Mapped; },
            Unmap = (_, _) => Unmaps++,
            ReleaseTransferBuffer = (_, buffer) => { Assert.Equal(Transfer, buffer); TransferReleases++; },
            ReleaseTexture = (_, texture) => { Assert.Equal(Texture, texture); TextureReleases++; },
            CancelCommandBuffer = _ => Assert.Fail("No recorded command buffer exists in these tests."),
            Error = () => "simulated",
        };
    }
}
