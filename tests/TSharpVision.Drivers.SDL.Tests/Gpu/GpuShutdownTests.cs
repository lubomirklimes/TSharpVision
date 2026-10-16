using System.Reflection;
using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

public sealed class GpuShutdownTests
{
    [Fact]
    public void DelayedWorkerPreventsCleanupAndShutdownCanBeRetried()
    {
        var renderer = new SDLGpuRenderer(IntPtr.Zero, new SDLGpuOptions());
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var worker = new Thread(() => { entered.Set(); release.Wait(); }) { IsBackground = true };
        Set(renderer, "_renderThread", worker);
        worker.Start();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.Throws<TimeoutException>(() => renderer.Dispose(TimeSpan.Zero));
            Assert.False(Get<bool>(renderer, "_disposed"));
            Assert.Same(worker, Get<Thread>(renderer, "_renderThread"));
            Assert.True(Get<CancellationTokenSource>(renderer, "_renderCts").IsCancellationRequested);
        }
        finally
        {
            release.Set();
            Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
            renderer.Dispose(TimeSpan.Zero);
        }
        Assert.True(Get<bool>(renderer, "_disposed"));
        Assert.Null(Get<Thread?>(renderer, "_renderThread"));
        renderer.Dispose(TimeSpan.Zero);
    }

    [Fact]
    public void DriverDoesNotDestroyWindowAfterRendererTimeout()
    {
        var renderer = new SDLGpuRenderer(IntPtr.Zero, new SDLGpuOptions());
        using var release = new ManualResetEventSlim();
        var worker = new Thread(() => release.Wait()) { IsBackground = true };
        Set(renderer, "_renderThread", worker);
        var driver = new SDLGpuDriver();
        Set(driver, "_gpuRenderer", renderer);
        Set(driver, "_attached", true);
        Set(driver, "_window", new IntPtr(123));
        worker.Start();
        try
        {
            Assert.Throws<TimeoutException>(() => driver.Shutdown());
            Assert.Same(renderer, Get<SDLGpuRenderer>(driver, "_gpuRenderer"));
            Assert.Equal(new IntPtr(123), Get<IntPtr>(driver, "_window"));
            Assert.True(Get<bool>(driver, "_attached"));
        }
        finally
        {
            release.Set();
            Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
            renderer.Dispose(TimeSpan.Zero);
        }
    }

    [Fact]
    public void IdleWorkerStopsNormally()
    {
        var renderer = new SDLGpuRenderer(IntPtr.Zero, new SDLGpuOptions());
        var loop = typeof(SDLGpuRenderer).GetMethod("RenderLoop", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var worker = new Thread(() => loop.Invoke(renderer, null)) { IsBackground = true };
        Set(renderer, "_renderThread", worker);
        worker.Start();
        renderer.Dispose(TimeSpan.FromSeconds(1));
        Assert.False(worker.IsAlive);
        Assert.True(Get<bool>(renderer, "_disposed"));
    }

    private static void Set(object target, string name, object value)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static T Get<T>(object target, string name)
        => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
}
