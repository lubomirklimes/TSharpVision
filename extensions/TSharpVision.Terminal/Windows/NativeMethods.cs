using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace TSharpVision.Terminal.Windows;

/// <summary>
/// Win32 P/Invoke declarations for the ConPTY API and supporting process
/// creation infrastructure.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    // ── ConPTY API ────────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = false)]
    internal static extern int CreatePseudoConsole(
        COORD size,
        SafeFileHandle hInput,
        SafeFileHandle hOutput,
        uint dwFlags,
        out SafePseudoConsoleHandle phPC);

    [DllImport("kernel32.dll", SetLastError = false)]
    internal static extern int ResizePseudoConsole(
        SafePseudoConsoleHandle hPC,
        COORD size);

    [DllImport("kernel32.dll")]
    internal static extern void ClosePseudoConsole(IntPtr hPC);

    // ── Pipe creation ─────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        IntPtr lpPipeAttributes,
        int nSize);

    // ── Process creation ──────────────────────────────────────────────────────

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool CreateProcess(
        string lpApplicationName,
        System.Text.StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    // ── Attribute list ────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr Attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    internal static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    // ── Process lifecycle ─────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr hObject);

    // ── Job Object ────────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeJobObjectHandle CreateJobObject(
        IntPtr lpJobAttributes,
        string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetInformationJobObject(
        SafeJobObjectHandle hJob,
        int JobObjectInformationClass,
        ref JOBOBJECT_BASIC_LIMIT_INFORMATION lpJobObjectInformation,
        int cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool AssignProcessToJobObject(
        SafeJobObjectHandle hJob,
        IntPtr hProcess);

    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>dwCreationFlags flag: startup info is STARTUPINFOEX.</summary>
    internal const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;

    /// <summary>Startup info dwFlags: redirect standard handles.</summary>
    internal const uint STARTF_USESTDHANDLES = 0x00000100;

    /// <summary>Attribute number for associating a process with a pseudo console.</summary>
    /// <remarks>Value: ProcThreadAttributeValue(22, FALSE, TRUE, FALSE) = 22 | 0x00020000.</remarks>
    internal const uint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    /// <summary>GetExitCodeProcess exit code when the process is still running.</summary>
    internal const uint STILL_ACTIVE = 259;

    /// <summary>WaitForSingleObject timeout value that waits indefinitely.</summary>
    internal const uint INFINITE = 0xFFFFFFFF;

    /// <summary>Job object information class for basic limit information.</summary>
    internal const int JobObjectBasicLimitInformation = 2;

    /// <summary>
    /// Job limit flag: kill all processes in the job when the last job handle is closed.
    /// </summary>
    internal const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    // ── Structures ────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct STARTUPINFO
    {
        public int    cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int    dwX;
        public int    dwY;
        public int    dwXSize;
        public int    dwYSize;
        public int    dwXCountChars;
        public int    dwYCountChars;
        public int    dwFillAttribute;
        public int    dwFlags;
        public short  wShowWindow;
        public short  cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr      lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int    dwProcessId;
        public int    dwThreadId;
    }

    /// <summary>
    /// Mirrors the Win32 <c>JOBOBJECT_BASIC_LIMIT_INFORMATION</c> structure.
    /// Used with <c>JobObjectBasicLimitInformation</c> (class 2) to configure
    /// per-job limits such as <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>.
    /// </summary>
    /// <remarks>
    /// Field order and padding match the C struct on both x64 and x86.
    /// <c>UIntPtr</c> fields (SIZE_T / ULONG_PTR) are 8 bytes on x64 and 4
    /// bytes on x86; the C# runtime inserts the same padding as the C compiler.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long    PerProcessUserTimeLimit;
        public long    PerJobUserTimeLimit;
        public uint    LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint    ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint    PriorityClass;
        public uint    SchedulingClass;
    }
}

/// <summary>
/// Safe handle wrapper for a Windows pseudo console (HPCON).
/// Calls <see cref="NativeMethods.ClosePseudoConsole"/> on release.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SafePseudoConsoleHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Called by P/Invoke marshaler when CreatePseudoConsole succeeds.
    internal SafePseudoConsoleHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle()
    {
        NativeMethods.ClosePseudoConsole(handle);
        return true;
    }
}

/// <summary>
/// Safe handle wrapper for a Windows Job Object.
/// Calls <see cref="NativeMethods.CloseHandle"/> on release, which triggers
/// <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> if that flag was set.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SafeJobObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeJobObjectHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
