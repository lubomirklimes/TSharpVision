using NativeSdl = global::SDL3.SDL;
using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests;

[Collection("SDL initialization")]
public sealed class SdlIdleWaitTests
{
    [Fact]
    public void ManagedSignalWakesNativeSdlWait()
    {
        Assert.True(NativeSdl.Init(NativeSdl.InitFlags.Events), NativeSdl.GetError());
        try
        {
            var waiting = new SdlIdleWait();
            waiting.Initialize();
            using var signal = new ManualResetEvent(false);
            var producer = new Thread(() => signal.Set());
            producer.Start();
            waiting.Wait(-1, signal, _ => { });
            Assert.True(producer.Join(2000));
            waiting.Stop();
        }
        finally { NativeSdl.Quit(); }
    }

    [Fact]
    public void NativeEventWakesAndIsDeliveredOnWaitingThread()
    {
        Assert.True(NativeSdl.Init(NativeSdl.InitFlags.Events), NativeSdl.GetError());
        try
        {
            var waiting = new SdlIdleWait();
            waiting.Initialize();
            uint type = NativeSdl.RegisterEvents(1);
            using var signal = new ManualResetEvent(false);
            var ev = new NativeSdl.Event { Type = type };
            Assert.True(NativeSdl.PushEvent(ref ev));
            int thread = 0;
            waiting.Wait(2000, signal, received => { Assert.Equal(type, received.Type); thread = Environment.CurrentManagedThreadId; });
            Assert.Equal(Environment.CurrentManagedThreadId, thread);
            waiting.Stop();
        }
        finally { NativeSdl.Quit(); }
    }

    [Fact]
    public void NativeTimeoutReturnsAndShutdownPreventsRetry()
    {
        Assert.True(NativeSdl.Init(NativeSdl.InitFlags.Events), NativeSdl.GetError());
        try
        {
            var waiting = new SdlIdleWait();
            waiting.Initialize();
            using var signal = new ManualResetEvent(false);
            waiting.Wait(1, signal, _ => { });
            waiting.Stop();
            Assert.Throws<ObjectDisposedException>(() => waiting.Wait(-1, signal, _ => { }));
        }
        finally { NativeSdl.Quit(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirtyRenderingSkipsNativeWaitBeforeAnyNativeCalls(bool gpu)
    {
        using var signal = new ManualResetEvent(false);
        object driver = gpu ? new SDLGpuDriver() : new SDLDriver();
        var attached = driver.GetType().GetField("_attached", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        // Minimal attached fixture with pending rendering. Entering SDL would be
        // invalid without initialization, so this proves the dirty guard runs first.
        attached.SetValue(driver, true);
        try { ((TSharpVision.Drivers.IIdleWaitDriver)driver).WaitForEvent(-1, signal); }
        finally { attached.SetValue(driver, false); ((IDisposable)driver).Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposedRendererAndGpuCannotWait(bool gpu)
    {
        using var signal = new ManualResetEvent(false);
        if (gpu)
        {
            using var driver = new SDLGpuDriver();
            driver.Shutdown();
            Assert.Throws<ObjectDisposedException>(() => driver.WaitForEvent(-1, signal));
        }
        else
        {
            using var driver = new SDLDriver();
            driver.Shutdown();
            Assert.Throws<ObjectDisposedException>(() => driver.WaitForEvent(-1, signal));
        }
    }
}
