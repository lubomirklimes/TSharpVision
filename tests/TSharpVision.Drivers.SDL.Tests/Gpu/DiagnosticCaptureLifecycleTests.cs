using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

public sealed class DiagnosticCaptureLifecycleTests
{
    [Fact]
    public void DetachedDriversRejectCaptureWithoutCreatingResources()
    {
        using var renderer = new SDLDriver();
        using var gpu = new SDLGpuDriver();
        Assert.Throws<InvalidOperationException>(() => renderer.CaptureDiagnosticPixels());
        Assert.Throws<InvalidOperationException>(() => { _ = gpu.BeginDiagnosticCapture(default); });
    }

    [Fact]
    public async Task PendingCaptureIsSingleSlotAndCancellationAllowsNextRequest()
    {
        using var renderer = new SDLGpuRenderer(IntPtr.Zero,new SDLGpuOptions());
        var first=renderer.RequestCaptureForTesting(80,25,TimeSpan.FromSeconds(1));
        Assert.Throws<InvalidOperationException>(()=> { _ = renderer.RequestCaptureForTesting(80,25,TimeSpan.FromSeconds(1)); });
        renderer.CancelDiagnosticCapture();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>first);
        var second=renderer.RequestCaptureForTesting(80,25,TimeSpan.FromSeconds(1));
        renderer.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>second);
    }

    [Theory]
    [InlineData(0,1)]
    [InlineData(100000,100000)]
    public void OversizedOrEmptyReadbackIsRejectedBeforeAllocation(int width,int height) =>
        Assert.Throws<NotSupportedException>(()=>DiagnosticCaptureLimits.Validate(width,height));
}
