using System.Runtime.InteropServices;
using Xunit;

namespace TSharpVision.Drivers.Terminal.Tests;

internal sealed class PosixFactAttribute : FactAttribute
{
    public PosixFactAttribute() { if (OperatingSystem.IsWindows()) Skip = "Requires POSIX libc poll/pipe; Windows host validates decoder/deadline source and unit tests."; }
}

public sealed class PosixIdleWaitTests
{
    [PosixFact]
    public void ManagedSignalInterruptsNativePoll()
    {
        Assert.Equal(0, Pipe(out var input));
        using var signal = new ManualResetEvent(true);
        try { TerminalNative.WaitForInput(input[0], signal, 2000); }
        finally { Close(input[0]); Close(input[1]); }
    }

    [PosixFact]
    public void NativeInputInterruptsPollAndPreservesByte()
    {
        Assert.Equal(0, Pipe(out var input));
        using var signal = new ManualResetEvent(false);
        try
        {
            Assert.Equal((nint)1, Write(input[1], new byte[] { 42 }, 1));
            TerminalNative.WaitForInput(input[0], signal, 2000);
            var bytes = new byte[1];
            Assert.Equal((nint)1, Read(input[0], bytes, 1));
            Assert.Equal((byte)42, bytes[0]);
        }
        finally { Close(input[0]); Close(input[1]); }
    }

    [PosixFact]
    public void ClosedInputDescriptorFailsRatherThanSpinning()
    {
        using var signal = new ManualResetEvent(false);
        Assert.Throws<IOException>(() => TerminalNative.WaitForInput(int.MaxValue, signal, 10));
    }

    private static int Pipe(out int[] descriptors) { descriptors = new int[2]; return NativePipe(descriptors); }
    [DllImport("libc", EntryPoint = "pipe")] private static extern int NativePipe([Out] int[] descriptors);
    [DllImport("libc", EntryPoint = "close")] private static extern int Close(int descriptor);
    [DllImport("libc", EntryPoint = "read")] private static extern nint Read(int descriptor, [Out] byte[] bytes, nuint count);
    [DllImport("libc", EntryPoint = "write")] private static extern nint Write(int descriptor, byte[] bytes, nuint count);
}
