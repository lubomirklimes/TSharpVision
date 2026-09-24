using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TSharpVision.Terminal.Posix;

/// <summary>
/// POSIX P/Invoke declarations for PTY creation, process control, and terminal
/// window sizing. Supported on Linux and macOS only.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal static class NativeMethods
{
    // posix_spawnp performs all child setup in libc;
    // no managed code executes between fork and exec.
    [DllImport("libc", SetLastError = true, EntryPoint = "openpty")]
    private static extern int OpenPtyLinux(out int masterFd, out int slaveFd,
        IntPtr name, IntPtr termios, ref WinSize winsize);

    [DllImport("libutil", SetLastError = true, EntryPoint = "openpty")]
    private static extern int OpenPtyMacOs(out int masterFd, out int slaveFd,
        IntPtr name, IntPtr termios, ref WinSize winsize);

    internal static int OpenPty(out int masterFd, out int slaveFd,
        IntPtr name, IntPtr termios, ref WinSize winsize)
        => OperatingSystem.IsLinux()
            ? OpenPtyLinux(out masterFd, out slaveFd, name, termios, ref winsize)
            : OpenPtyMacOs(out masterFd, out slaveFd, name, termios, ref winsize);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_init")]
    internal static extern int SpawnActionsInit(IntPtr actions);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_destroy")]
    internal static extern int SpawnActionsDestroy(IntPtr actions);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addopen")]
    internal static extern int SpawnActionsAddOpen(IntPtr actions, int fd,
        IntPtr path, int flags, uint mode);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2")]
    internal static extern int SpawnActionsAddDup2(IntPtr actions, int fd, int newFd);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addclose")]
    internal static extern int SpawnActionsAddClose(IntPtr actions, int fd);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addchdir_np")]
    internal static extern int SpawnActionsAddChdir(IntPtr actions, IntPtr path);

    [DllImport("libc", EntryPoint = "posix_spawnattr_init")]
    internal static extern int SpawnAttrInit(IntPtr attributes);

    [DllImport("libc", EntryPoint = "posix_spawnattr_destroy")]
    internal static extern int SpawnAttrDestroy(IntPtr attributes);

    [DllImport("libc", EntryPoint = "posix_spawnattr_setflags")]
    internal static extern int SpawnAttrSetFlags(IntPtr attributes, short flags);

    [DllImport("libc", EntryPoint = "posix_spawnp")]
    internal static extern int Spawn(out int pid, IntPtr file, IntPtr actions,
        IntPtr attributes, IntPtr argv, IntPtr environment);

    // ── Terminal window size ──────────────────────────────────────────────────

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int IoctlFixed(int fd, nuint request, ref WinSize winsize);

    // Darwin ARM64's unnamed varargs live on the stack, unlike a fixed third
    // argument in x2. Occupy x2-x7 so the winsize pointer lands in the first stack
    // slot. Strictly for Darwin ARM64 ioctl with one pointer, not general varargs.
    // https://developer.apple.com/documentation/xcode/writing-arm64-code-for-apple-platforms
    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int IoctlDarwinArm64(int fd, nuint request,
        nuint pad2, nuint pad3, nuint pad4, nuint pad5, nuint pad6, nuint pad7,
        ref WinSize winsize);

    internal static int Ioctl(int fd, nuint request, ref WinSize winsize)
        => OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? IoctlDarwinArm64(fd, request, 0, 0, 0, 0, 0, 0, ref winsize)
            : IoctlFixed(fd, request, ref winsize);

    // ── Process management ────────────────────────────────────────────────────

    [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
    internal static extern int Kill(int pid, int sig);

    /// <summary>
    /// Waits for child <paramref name="pid"/> to change state.
    /// Pass <see cref="WNOHANG"/> in <paramref name="options"/> to avoid blocking.
    /// </summary>
    [DllImport("libc", SetLastError = true, EntryPoint = "waitpid")]
    internal static extern int WaitPid(int pid, out int status, int options);

    // ── File descriptor I/O ───────────────────────────────────────────────────

    [DllImport("libc", SetLastError = true, EntryPoint = "read")]
    internal static extern nint Read(int fd, IntPtr buf, nint count);

    [DllImport("libc", SetLastError = true, EntryPoint = "write")]
    internal static extern nint Write(int fd, IntPtr buf, nint count);

    [DllImport("libc", SetLastError = true, EntryPoint = "close")]
    internal static extern int Close(int fd);

    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>ioctl request to set the terminal window size.</summary>
    internal static readonly nuint TIOCSWINSZ = GetTiocsWinSz();

    private static nuint GetTiocsWinSz()
    {
        if (OperatingSystem.IsLinux()) return 0x5414u;
        if (OperatingSystem.IsMacOS()) return 0x80087467u;
        return 0u;
    }

    /// <summary>SIGTERM — request graceful process termination.</summary>
    internal const int SIGTERM = 15;

    /// <summary>SIGKILL — forcibly terminate a process; cannot be caught or ignored.</summary>
    internal const int SIGKILL = 9;

    /// <summary>waitpid flag: return immediately if no child has exited yet.</summary>
    internal const int WNOHANG = 1;

    // ── Structures ────────────────────────────────────────────────────────────

    /// <summary>Terminal window dimensions passed to <c>ioctl(TIOCSWINSZ)</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WinSize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel; // unused; set to 0
        public ushort ws_ypixel; // unused; set to 0
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Decodes a raw <c>waitpid</c> exit status into an exit code.
    /// Returns <see langword="null"/> when the child was terminated by a signal.
    /// </summary>
    internal static int? DecodeExitStatus(int status)
    {
        // WIFEXITED: (status & 0x7F) == 0  →  exit code is (status >> 8) & 0xFF
        if ((status & 0x7F) == 0)
            return (status >> 8) & 0xFF;
        return null; // killed by signal
    }
}
