using System.Runtime.InteropServices;
using TSharpVision.Drivers.Console;
using Xunit;

namespace TSharpVision.Tests.Drivers;

public sealed class ConsoleIdleWaitTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NativeInputOrManagedSignalInterruptsMultiHandleWait(bool nativeInput)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var input = new ManualResetEvent(false);
        using var wake = new ManualResetEvent(false);
        using var ready = new ManualResetEventSlim();
        var worker = new Thread(() => { ready.Wait(); (nativeInput ? input : wake).Set(); });
        worker.Start(); ready.Set();
        Win32ConsoleDriver.WaitOnHandles(input.SafeWaitHandle.DangerousGetHandle(), wake, 2000);
        Assert.True(worker.Join(2000));
        Assert.True((nativeInput ? input : wake).WaitOne(0));
    }

    [Fact]
    public void InvalidNativeHandleFailsInsteadOfRetrying()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var wake = new ManualResetEvent(false);
        Assert.Throws<System.ComponentModel.Win32Exception>(() => Win32ConsoleDriver.WaitOnHandles(new IntPtr(-1), wake, 100));
    }

    [Fact]
    public void DisposedDriverFailsWaitInsteadOfSpinning()
    {
        using var driver = new Win32ConsoleDriver();
        using var wake = new ManualResetEvent(false);
        driver.Shutdown();
        Assert.Throws<ObjectDisposedException>(() => driver.WaitForEvent(-1, wake));
    }
}
