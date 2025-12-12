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
    // ── forkpty ───────────────────────────────────────────────────────────────
    // Linux exports forkpty from libc; macOS exports it from libutil.
    // Each declaration is only called on its respective platform, so the
    // other library is never loaded.

    [DllImport("libc", SetLastError = true, EntryPoint = "forkpty")]
    private static extern int ForkPtyLinux(
        out int masterFd,
        IntPtr name,
        IntPtr termios,
        ref WinSize winsize);

    [DllImport("libutil", SetLastError = true, EntryPoint = "forkpty")]
    private static extern int ForkPtyMacOs(
        out int masterFd,
        IntPtr name,
        IntPtr termios,
        ref WinSize winsize);

    /// <summary>
    /// Creates a PTY pair and forks the calling process.
    /// Returns the child PID in the parent, 0 in the child, or -1 on error.
    /// <paramref name="masterFd"/> receives the PTY master file descriptor
    /// in the parent process.
    /// </summary>
    internal static int ForkPty(out int masterFd, IntPtr name, IntPtr termios, ref WinSize winsize)
        => OperatingSystem.IsLinux()
            ? ForkPtyLinux(out masterFd, name, termios, ref winsize)
            : ForkPtyMacOs(out masterFd, name, termios, ref winsize);

    // ── Terminal window size ──────────────────────────────────────────────────

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    internal static extern int Ioctl(int fd, uint request, ref WinSize winsize);

    // ── Process execution ─────────────────────────────────────────────────────

    /// <summary>Searches PATH for <paramref name="file"/> and executes it.</summary>
    [DllImport("libc", SetLastError = true, EntryPoint = "execvp")]
    internal static extern int Execvp(string file, string[] argv);

    /// <summary>Changes the current working directory.</summary>
    [DllImport("libc", SetLastError = true, EntryPoint = "chdir")]
    internal static extern int Chdir(string path);

    /// <summary>
    /// Terminates the calling process immediately without running any cleanup
    /// handlers. Safe to call in a forked child before exec.
    /// </summary>
    [DllImport("libc", SetLastError = false, EntryPoint = "_exit")]
    internal static extern void ExitImmediately(int status);

    // ── Process management ────────────────────────────────────────────────────

    [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
    internal static extern int Kill(int pid, int sig);

    /// <summary>
    /// Sets the process group ID of <paramref name="pid"/> to <paramref name="pgid"/>.
    /// Pass <c>0</c> for both parameters to make the calling process its own process
    /// group leader, which is required for group-wide signal delivery.
    /// </summary>
    [DllImport("libc", SetLastError = true, EntryPoint = "setpgid")]
    internal static extern int Setpgid(int pid, int pgid);

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
    internal static readonly uint TIOCSWINSZ = GetTiocsWinSz();

    private static uint GetTiocsWinSz()
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
