using System.Runtime.InteropServices;

namespace TSharpVision.Drivers.Terminal;

// Only native declarations and ABI differences belong here. Terminal lifecycle,
// rendering and input decoding remain in AnsiTerminalDriver.
internal static class TerminalNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct WinSize
    {
        internal ushort Rows;
        internal ushort Columns;
        internal ushort XPixel;
        internal ushort YPixel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        internal int Fd;
        internal short Events;
        internal short ReturnedEvents;
    }

    internal static int GetWindowSize(int fd, out WinSize size)
    {
        size = default;
        nuint request = OperatingSystem.IsMacOS() ? 0x40087468u : 0x5413u;
        return OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? IoctlDarwinArm64(fd, request, 0, 0, 0, 0, 0, 0, ref size)
            : Ioctl(fd, request, ref size);
    }

    internal static bool IsInputReady(int fd)
    {
        const short PollIn = 0x0001;
        var descriptor = new PollFd { Fd = fd, Events = PollIn };
        int result = OperatingSystem.IsMacOS()
            ? PollDarwin(ref descriptor, 1, 0)
            : PollLinux(ref descriptor, 1, 0);
        return result > 0 && (descriptor.ReturnedEvents & PollIn) != 0;
    }

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int Ioctl(int fd, nuint request, ref WinSize size);

    // Darwin ARM64 puts unnamed varargs on the stack. A fixed third parameter
    // would instead pass the pointer in x2, letting ioctl overwrite caller memory.
    // Occupy x2-x7 so the pointer is in the first stack slot. This adapter is ONLY
    // for Darwin ARM64 ioctl with one pointer vararg, not portable general varargs.
    // https://developer.apple.com/documentation/xcode/writing-arm64-code-for-apple-platforms
    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int IoctlDarwinArm64(int fd, nuint request,
        nuint pad2, nuint pad3, nuint pad4, nuint pad5, nuint pad6, nuint pad7,
        ref WinSize size);

    // nfds_t is unsigned int on Darwin, unsigned long on Linux/glibc.
    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern int PollDarwin(ref PollFd descriptors, uint count, int timeout);

    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern int PollLinux(ref PollFd descriptors, nuint count, int timeout);

    // ssize_t read(int, void *, size_t): signed result, unsigned native-sized count.
    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    internal static extern unsafe nint Read(int fd, byte* buffer, nuint count);

    [DllImport("libc", EntryPoint = "isatty", SetLastError = true)]
    internal static extern int IsATty(int fd);

    [DllImport("libc", EntryPoint = "tcgetattr", SetLastError = true)]
    internal static extern int GetAttributes(int fd, IntPtr attributes);

    internal static int SetAttributesNow(int fd, IntPtr attributes)
        => SetAttributes(fd, 0 /* TCSANOW on Linux and Darwin */, attributes);

    [DllImport("libc", EntryPoint = "tcsetattr", SetLastError = true)]
    private static extern int SetAttributes(int fd, int action, IntPtr attributes);

    [DllImport("libc", EntryPoint = "cfmakeraw", SetLastError = true)]
    internal static extern void MakeRaw(IntPtr attributes);
}
