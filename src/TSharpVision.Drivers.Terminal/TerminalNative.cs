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

    internal static unsafe void WaitForInput(int inputFd, WaitHandle wakeSignal, int timeout)
    {
        int[] pipe = new int[2];
        if (OperatingSystem.IsLinux())
        {
            // Atomic close-on-exec on Linux: a concurrently launched child must not
            // inherit this temporary framework wake source.
            if (CreatePipeCloseOnExec(pipe, 0x80000 /* O_CLOEXEC */) != 0) throw NativeFailure("pipe2");
        }
        else
        {
            if (CreatePipe(pipe) != 0) throw NativeFailure("pipe");
            if (SetDescriptorFlags(pipe[0], 2 /* F_SETFD */, 1 /* FD_CLOEXEC */) < 0
                || SetDescriptorFlags(pipe[1], 2, 1) < 0)
            {
                var error = NativeFailure("fcntl(FD_CLOEXEC)");
                Close(pipe[0]); Close(pipe[1]);
                throw error;
            }
        }
        RegisteredWaitHandle? registration = null;
        Exception? wakeError = null;
        var callbackGate = new object();
        bool callbackActive = true;
        try
        {
            // One callback writes one byte into an otherwise empty pipe: no full-pipe
            // blocking is possible. Unregister/join before either descriptor is closed.
            registration = ThreadPool.RegisterWaitForSingleObject(wakeSignal, (_, _) =>
            {
                lock (callbackGate)
                {
                    if (!callbackActive) return;
                    byte value = 1;
                    nint written;
                    do { written = Write(pipe[1], &value, 1); }
                    while (written < 0 && Marshal.GetLastPInvokeError() == 4);
                    if (written != 1) Volatile.Write(ref wakeError, NativeFailure("wake pipe write"));
                }
            }, null, -1, true);
            var descriptors = new[]
            {
                new PollFd { Fd = inputFd, Events = 1 },
                new PollFd { Fd = pipe[0], Events = 1 }
            };
            long deadline = timeout < 0 ? long.MaxValue : Environment.TickCount64 + timeout;
            while (true)
            {
                int remaining = deadline == long.MaxValue ? -1 : (int)Math.Max(0, deadline - Environment.TickCount64);
                int result = OperatingSystem.IsMacOS()
                    ? PollManyDarwin(descriptors, 2, remaining) : PollManyLinux(descriptors, 2, remaining);
                if (result < 0 && Marshal.GetLastPInvokeError() == 4) continue; // EINTR, preserve deadline.
                if (result < 0) throw NativeFailure("poll");
                if (descriptors.Any(d => (d.ReturnedEvents & (8 | 16 | 32)) != 0))
                    throw new IOException("Terminal idle poll reported an invalid, failed, or closed descriptor.");
                break;
            }
        }
        finally
        {
            if (registration != null)
            {
                registration.Unregister(null);
                lock (callbackGate) callbackActive = false;
            }
            Close(pipe[0]);
            Close(pipe[1]);
        }
        if (wakeError != null) throw wakeError;
    }

    private static IOException NativeFailure(string operation)
        => new($"Terminal {operation} failed (errno {Marshal.GetLastPInvokeError()}).");

    [DllImport("libc", EntryPoint = "pipe2", SetLastError = true)]
    private static extern int CreatePipeCloseOnExec([Out] int[] descriptors, int flags);
    private static int SetDescriptorFlags(int fd, int command, int flags)
        => OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? FcntlDarwinArm64(fd, command, 0, 0, 0, 0, 0, 0, flags)
            : Fcntl(fd, command, flags);

    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int Fcntl(int fd, int command, int flags);
    // Like ioctl below, Darwin ARM64 unnamed arguments must start on the stack.
    [DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int FcntlDarwinArm64(int fd, int command,
        nuint pad2, nuint pad3, nuint pad4, nuint pad5, nuint pad6, nuint pad7, int flags);
    [DllImport("libc", EntryPoint = "pipe", SetLastError = true)]
    private static extern int CreatePipe([Out] int[] descriptors);
    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);
    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern unsafe nint Write(int fd, byte* buffer, nuint count);
    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern int PollManyDarwin([In, Out] PollFd[] descriptors, uint count, int timeout);
    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern int PollManyLinux([In, Out] PollFd[] descriptors, nuint count, int timeout);

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
