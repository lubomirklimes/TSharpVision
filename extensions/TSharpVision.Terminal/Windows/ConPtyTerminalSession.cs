using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TSharpVision.Terminal.Windows;

/// <summary>
/// Windows ConPTY-backed terminal session. Starts a child process inside a
/// Windows pseudo console, streams output as <see cref="ITerminalSession.OutputReceived"/>
/// events, and forwards input through the PTY master.
/// </summary>
/// <remarks>
/// <para>
/// Requires Windows 10 version 1809 (build 17763) or later.
/// On earlier Windows versions or non-Windows platforms,
/// <see cref="StartAsync"/> throws <see cref="PlatformNotSupportedException"/>.
/// The assembly still compiles on all platforms; the guard is runtime-only.
/// ANSI/VT output is forwarded as raw bytes decoded to UTF-8; parsing is left
/// to <see cref="TTerminal"/> (which contains the ANSI parser).
/// </para>
/// <para>
/// <b>Ownership.</b> Every native resource has one owner and is released once: the pseudo console
/// (<see cref="SafePseudoConsoleHandle"/>), the parent pipe ends (unbuffered
/// <see cref="FileStream"/>s over <see cref="SafeFileHandle"/>s), the process
/// (<see cref="SafeProcessHandle"/>), the primary thread (closed as soon as it is resumed) and the
/// Job Object (<see cref="SafeJobObjectHandle"/>). Background users of a handle hold a
/// <see cref="SafeHandle"/> reference, so a concurrent release is deferred rather than racing.
/// </para>
/// <para>
/// <b>Process tree.</b> The child is created suspended, joins a Job Object configured with
/// <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>, and only then runs, so no descendant can be created
/// outside the job. Releasing the job handle — on <see cref="StopAsync"/>, on
/// <see cref="Dispose"/>, or by the OS when this process dies — terminates every process still in
/// it, and nothing else.
/// </para>
/// <para>
/// A session is single-use: it can be started once.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class ConPtyTerminalSession : ITerminalSession, IResizableTerminalSession,
    IInterruptibleTerminalSession, IExitCodeTerminalSession
{
    private readonly ConPtyTerminalSessionOptions _options;
    private readonly TerminalInputPump _input;

    // ConPTY handle — non-null and valid between StartAsync and cleanup.
    private SafePseudoConsoleHandle? _hPseudoConsole;
    private int _conPtyClosed;   // 0 = open, 1 = closed; guarded by CompareExchange

    // Pipe streams — wrap the parent-side pipe handles. Unbuffered, so disposing one never has
    // anything to flush and never blocks.
    private FileStream? _inputStream;    // parent writes input here
    private FileStream? _outputStream;   // parent reads output here

    // Process handle.
    private SafeProcessHandle? _hProcess;
    private int _processId;

    // Job Object for process-tree cleanup (best-effort; null when unavailable).
    private SafeJobObjectHandle? _hJob;

    // Background tasks.
    private Task? _outputReaderTask;
    private Task? _processWatcherTask;

    // Serialises writes to the input pipe (queued input and interrupts).
    private readonly object _inputLock = new();

    // State flags.
    private volatile bool _isRunning;
    private int _started;       // 0 = never started; guarded by CompareExchange
    private int _exitedFired;   // 0 = not fired; guarded by CompareExchange
    private int _released;      // 0 = handles live; guarded by CompareExchange
    private int _disposed;      // 0 = alive; guarded by CompareExchange

    /// <inheritdoc/>
    public event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    /// <inheritdoc/>
    public event EventHandler? Exited;

    /// <inheritdoc/>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Exit code of the child process, populated after <see cref="Exited"/> fires.
    /// <see langword="null"/> if the process has not yet exited or if the exit
    /// code was unavailable.
    /// </summary>
    public int? ExitCode { get; private set; }

    /// <summary>The child's process id, or 0 before a successful start — for a host that tracks or signals it.</summary>
    public int ProcessId => _processId;

    /// <summary>True when the child was placed in a kill-on-close Job Object.</summary>
    internal bool HasJob => _hJob is { IsInvalid: false };

    /// <summary>The output reader, for tests that assert it finished.</summary>
    internal Task? OutputReaderTask => _outputReaderTask;

    /// <param name="options">Session configuration.</param>
    public ConPtyTerminalSession(ConPtyTerminalSessionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrEmpty(options.FileName))
            throw new ArgumentException("FileName must be specified.", nameof(options));
        _input = new TerminalInputPump(WriteInput);
    }

    /// <summary>
    /// Convenience constructor: creates options from individual parameters.
    /// </summary>
    public ConPtyTerminalSession(string fileName, string? arguments = "", TerminalSize? initialSize = null)
        : this(new ConPtyTerminalSessionOptions
        {
            FileName    = fileName,
            Arguments   = arguments ?? string.Empty,
            InitialSize = initialSize ?? new TerminalSize(80, 24)
        })
    { }

    // ── ITerminalSession ──────────────────────────────────────────────────────

    /// <summary>
    /// Start the child process inside a Windows pseudo console.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown on non-Windows or on Windows versions earlier than build 17763.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the session is already running or was started before.
    /// </exception>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            throw new PlatformNotSupportedException(
                "ConPtyTerminalSession requires Windows 10 version 1809 (build 17763) or later.");

        if (_isRunning)
            throw new InvalidOperationException("Session is already running.");

        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0)
            throw new ObjectDisposedException(nameof(ConPtyTerminalSession));

        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            throw new InvalidOperationException("A terminal session can be started only once.");

        StartCore();
        return Task.CompletedTask;
    }

    private void StartCore()
    {
        SafeFileHandle? inputRead   = null;
        SafeFileHandle? inputWrite  = null;
        SafeFileHandle? outputRead  = null;
        SafeFileHandle? outputWrite = null;
        SafePseudoConsoleHandle? hPseudoConsole = null;
        SafeProcessHandle? process = null;
        SafeJobObjectHandle? job = null;
        IntPtr thread = IntPtr.Zero;
        IntPtr attributeList = IntPtr.Zero;
        bool success = false;

        try
        {
            // ── Create pipe pair for stdin (parent→PTY→child) ─────────────────
            if (!NativeMethods.CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create ConPTY input pipe.");

            // ── Create pipe pair for stdout (child→PTY→parent) ────────────────
            if (!NativeMethods.CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create ConPTY output pipe.");

            // ── Create pseudo console ─────────────────────────────────────────
            int hr = NativeMethods.CreatePseudoConsole(ToCoord(_options.InitialSize), inputRead, outputWrite,
                0, out hPseudoConsole);

            // Close the child-side pipe handles now; ConPTY owns copies of them.
            inputRead.Dispose();  inputRead  = null;
            outputWrite.Dispose(); outputWrite = null;

            if (hr != 0)
                Marshal.ThrowExceptionForHR(hr);

            // ── Build attribute list with PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE ──
            IntPtr attrListSize = IntPtr.Zero;
            NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrListSize);
            attributeList = Marshal.AllocHGlobal(attrListSize);

            if (!NativeMethods.InitializeProcThreadAttributeList(attributeList, 1, 0, ref attrListSize))
            {
                Marshal.FreeHGlobal(attributeList);
                attributeList = IntPtr.Zero;
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to initialize process attribute list.");
            }

            if (!NativeMethods.UpdateProcThreadAttribute(
                    attributeList, 0,
                    new IntPtr(NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE),
                    hPseudoConsole.DangerousGetHandle(), new IntPtr(IntPtr.Size),
                    IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to set PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE.");

            // ── Create child process, suspended ───────────────────────────────
            var startupInfoEx = new NativeMethods.STARTUPINFOEX();
            startupInfoEx.StartupInfo.cb = Marshal.SizeOf<NativeMethods.STARTUPINFOEX>();
            // Set STARTF_USESTDHANDLES with INVALID_HANDLE_VALUE so the child's
            // console output is routed through the ConPTY output pipe rather than
            // going directly to the parent's console window.
            startupInfoEx.StartupInfo.dwFlags = (int)NativeMethods.STARTF_USESTDHANDLES;
            startupInfoEx.StartupInfo.hStdInput  = new IntPtr(-1); // INVALID_HANDLE_VALUE
            startupInfoEx.StartupInfo.hStdOutput = new IntPtr(-1);
            startupInfoEx.StartupInfo.hStdError  = new IntPtr(-1);
            startupInfoEx.lpAttributeList = attributeList;

            var cmdLine = new StringBuilder(BuildCommandLine(_options.FileName, _options.Arguments));

            EnsureChildrenProcessCtrlC();
            if (!NativeMethods.CreateProcess(
                    null, cmdLine,
                    IntPtr.Zero, IntPtr.Zero,
                    false,
                    NativeMethods.EXTENDED_STARTUPINFO_PRESENT | NativeMethods.CREATE_SUSPENDED,
                    IntPtr.Zero,
                    _options.WorkingDirectory,
                    ref startupInfoEx,
                    out var processInfo))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create child process.");

            thread  = processInfo.hThread;
            process = new SafeProcessHandle(processInfo.hProcess, ownsHandle: true);

            // Join the kill-on-close job while the child has not executed a single instruction, so
            // nothing it starts can be created outside the job.
            job = TryCreateJobForProcess(process);

            if (NativeMethods.ResumeThread(thread) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to resume the child process.");

            // ── Store live state ──────────────────────────────────────────────
            _inputStream  = new FileStream(inputWrite, FileAccess.Write, bufferSize: 1, isAsync: false);
            inputWrite    = null;
            _outputStream = new FileStream(outputRead, FileAccess.Read, bufferSize: 1, isAsync: false);
            outputRead    = null;
            _hPseudoConsole = hPseudoConsole; hPseudoConsole = null;
            _hProcess       = process;        process        = null;
            _hJob           = job;            job            = null;
            _processId      = processInfo.dwProcessId;

            // ── Start background tasks ────────────────────────────────────────
            _isRunning = true;
            _outputReaderTask   = Task.Run(RunOutputReader);
            _processWatcherTask = Task.Run(RunProcessWatcher);
            success    = true;
        }
        finally
        {
            // The primary thread handle is never needed after the resume.
            if (thread != IntPtr.Zero) NativeMethods.CloseHandle(thread);

            // Always free the attribute list (safe to free after CreateProcess returns).
            if (attributeList != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            // On failure, release any handles that were not transferred to fields.
            if (!success)
            {
                if (process is { IsInvalid: false })
                {
                    try { NativeMethods.TerminateProcess(process.DangerousGetHandle(), 1); } catch { }
                }
                process?.Dispose();
                job?.Dispose();
                hPseudoConsole?.Dispose();
                inputRead?.Dispose();
                inputWrite?.Dispose();
                outputRead?.Dispose();
                outputWrite?.Dispose();
                _inputStream?.Dispose();
                _outputStream?.Dispose();
                _inputStream = null;
                _outputStream = null;
            }
        }
    }

    private static int s_ctrlCNormalized;

    /// <summary>
    /// Makes sure programs in the pseudo console can be interrupted. Windows passes a process's "ignore Ctrl+C" flag on
    /// to every process it creates; a host started by a launcher that set it (a test host, some IDEs and shells) would
    /// otherwise start shells whose programs ignore the ETX that <see cref="InterruptAsync"/> writes — found in U-1b,
    /// where <c>ping -t</c> in cmd.exe could not be stopped. The flag is cleared once, before the first child is
    /// created, which is the state of a process started normally. Handlers the host registered with
    /// <c>SetConsoleCtrlHandler</c> (or <c>Console.CancelKeyPress</c>) are not affected, and the pseudo console's own
    /// Ctrl+C never reaches the host's console.
    /// </summary>
    internal static void EnsureChildrenProcessCtrlC()
    {
        if (Interlocked.Exchange(ref s_ctrlCNormalized, 1) == 0)
            NativeMethods.SetConsoleCtrlHandler(IntPtr.Zero, false);
    }

    /// <summary>
    /// The command line <c>CreateProcess</c> receives. An executable path containing whitespace is
    /// quoted — otherwise <c>C:\Program Files\x.exe</c> would be parsed as <c>C:\Program</c> with
    /// arguments. <paramref name="arguments"/> are passed through verbatim, as before.
    /// </summary>
    internal static string BuildCommandLine(string fileName, string? arguments)
    {
        string file = fileName.Length > 0 && fileName[0] != '"' && fileName.AsSpan().IndexOfAny(' ', '\t') >= 0
            ? "\"" + fileName + "\""
            : fileName;
        return string.IsNullOrEmpty(arguments) ? file : file + " " + arguments;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// If the session is not running, the call is a no-op. The bytes are copied and queued; the
    /// returned task completes once they reached the pseudo console or were discarded because the
    /// session ended. The caller's thread never blocks on the pipe.
    /// </remarks>
    public Task SendInputAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default)
    {
        if (!_isRunning || input.IsEmpty) return Task.CompletedTask;
        return _input.EnqueueAsync(input.ToArray());
    }

    /// <summary>
    /// Request orderly termination of the session.
    /// Safe to call before <see cref="StartAsync"/>, multiple times, and after
    /// natural process exit.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return;

        _input.Close();

        // Kill the child process.
        TerminateChildIfAlive();

        // Closing the ConPTY causes EOF on the output pipe so the reader exits.
        CloseConPty();

        // Wait for the output reader to drain (timeout prevents indefinite block).
        if (_outputReaderTask != null)
        {
            await Task.WhenAny(_outputReaderTask, Task.Delay(5000, CancellationToken.None))
                      .ConfigureAwait(false);
        }

        if (_processWatcherTask != null)
        {
            await Task.WhenAny(_processWatcherTask, Task.Delay(5000, CancellationToken.None))
                      .ConfigureAwait(false);
        }

        _isRunning = false;
        FireExited();
        ReleaseHandles();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        _input.Close();
        TerminateChildIfAlive();
        _isRunning = false;
        CloseConPty();
        ReleaseHandles();
        // Exited is intentionally not fired from Dispose.
    }

    // ── IResizableTerminalSession ─────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// If the session is not running, or its pseudo console is already closed, the call is a
    /// no-op.
    /// </remarks>
    public Task ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return Task.CompletedTask;

        SafePseudoConsoleHandle? pseudoConsole = _hPseudoConsole;
        if (pseudoConsole == null || pseudoConsole.IsInvalid || pseudoConsole.IsClosed)
            return Task.CompletedTask;

        try
        {
            // The SafeHandle marshaller holds a reference for the call, so a concurrent close waits.
            NativeMethods.ResizePseudoConsole(pseudoConsole, ToCoord(size));
        }
        catch (ObjectDisposedException)
        {
            // Closed between the check and the call: the session is ending; nothing to resize.
        }
        return Task.CompletedTask;
    }

    // ── IInterruptibleTerminalSession ─────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Writes the ETX character (0x03) to the PTY master, which the child
    /// process receives as a Ctrl+C signal via the Windows console. If the
    /// session is not running, the call is a no-op. The write runs on a pool thread.
    /// </remarks>
    public Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return Task.CompletedTask;
        return Task.Run(() => { WriteInput(new byte[] { 0x03 }); }, CancellationToken.None);
    }

    // ── Background tasks ──────────────────────────────────────────────────────

    private void RunOutputReader()
    {
        FileStream? stream = _outputStream;
        if (stream is null) return;
        var buffer   = new byte[4096];
        try
        {
            int bytesRead;
            // Raw bytes: the emulator owns decoding, so a split UTF-8 character or escape sequence is joined there.
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                OutputReceived?.Invoke(this, new TerminalOutputEventArgs(buffer.AsMemory(0, bytesRead)));
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    // Creates a Job Object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE and assigns
    // the child process to it. Returns null on failure (non-fatal).
    private static SafeJobObjectHandle? TryCreateJobForProcess(SafeProcessHandle process)
    {
        SafeJobObjectHandle hJob = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (hJob.IsInvalid)
        {
            hJob.Dispose();
            return null;
        }
        try
        {
            var info = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new NativeMethods.JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
                },
            };
            bool configured = NativeMethods.SetInformationJobObject(
                hJob,
                NativeMethods.JobObjectExtendedLimitInformation,
                ref info,
                Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>());
            bool assigned = configured && NativeMethods.AssignProcessToJobObject(hJob, process.DangerousGetHandle());
            if (!assigned) { hJob.Dispose(); return null; }
        }
        catch
        {
            // Non-fatal: fall back to direct-process termination only.
            hJob.Dispose();
            return null;
        }
        return hJob;
    }

    private async Task RunProcessWatcher()
    {
        SafeProcessHandle? process = _hProcess;
        if (process == null || process.IsInvalid) return;

        // Block a thread pool thread until the process exits; capture the exit code.
        int? exitCode = await Task.Run(() => WaitForExitCode(process)).ConfigureAwait(false);
        if (exitCode is int code) ExitCode = code;

        // The child process may exit before conhost.exe has written all its output
        // to the pipe. Give conhost a brief window to flush before we close the ConPTY.
        await Task.Delay(50).ConfigureAwait(false);

        // Close ConPTY: conhost closes its write-end of the output pipe, delivering
        // EOF to the reader.  Any output still buffered inside the pipe (between
        // conhost and the read end) is preserved and will be read by RunOutputReader
        // before it observes EOF.
        CloseConPty();

        // Wait for the reader to drain all buffered output and observe the EOF.
        if (_outputReaderTask != null)
        {
            try { await _outputReaderTask.ConfigureAwait(false); }
            catch { }
        }

        _input.Close();
        _isRunning = false;
        FireExited();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Waits for the process under a handle reference, so releasing the handle meanwhile defers
    /// the close instead of pulling the handle out from under the wait.
    /// </summary>
    private static int? WaitForExitCode(SafeProcessHandle process)
    {
        bool added = false;
        try
        {
            process.DangerousAddRef(ref added);
        }
        catch (ObjectDisposedException)
        {
            return null;
        }

        try
        {
            IntPtr handle = process.DangerousGetHandle();
            NativeMethods.WaitForSingleObject(handle, NativeMethods.INFINITE);
            return NativeMethods.GetExitCodeProcess(handle, out uint code) && code != NativeMethods.STILL_ACTIVE
                ? (int)code
                : null;
        }
        finally
        {
            if (added) process.DangerousRelease();
        }
    }

    private static NativeMethods.COORD ToCoord(TerminalSize size) => new()
    {
        X = (short)Math.Clamp(size.Columns, 1, short.MaxValue),
        Y = (short)Math.Clamp(size.Rows, 1, short.MaxValue),
    };

    /// <summary>Writes every byte, or returns false once the pipe is unusable.</summary>
    private bool WriteInput(byte[] data)
    {
        FileStream? stream = Volatile.Read(ref _inputStream);
        if (stream is null) return false;

        lock (_inputLock)
        {
            try
            {
                stream.Write(data, 0, data.Length);
                stream.Flush();
                return true;
            }
            catch (IOException) { return false; }
            catch (ObjectDisposedException) { return false; }
        }
    }

    private void CloseConPty()
    {
        if (Interlocked.CompareExchange(ref _conPtyClosed, 1, 0) != 0) return;
        _hPseudoConsole?.Dispose();
    }

    private void TerminateChildIfAlive()
    {
        SafeProcessHandle? process = _hProcess;
        if (process == null || process.IsInvalid) return;

        bool added = false;
        try
        {
            process.DangerousAddRef(ref added);
            IntPtr handle = process.DangerousGetHandle();
            if (NativeMethods.GetExitCodeProcess(handle, out uint code) && code == NativeMethods.STILL_ACTIVE)
                NativeMethods.TerminateProcess(handle, 0);
        }
        catch (ObjectDisposedException) { }
        finally
        {
            if (added) process.DangerousRelease();
        }
    }

    private void FireExited()
    {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0) return;
        if (Interlocked.CompareExchange(ref _exitedFired, 1, 0) == 0)
            Exited?.Invoke(this, EventArgs.Empty);
    }

    private void ReleaseHandles()
    {
        if (Interlocked.CompareExchange(ref _released, 1, 0) != 0) return;

        try { _outputStream?.Dispose(); } catch { }
        try { _inputStream?.Dispose();  } catch { }
        try { _hProcess?.Dispose();     } catch { }
        // Closing the job handle triggers KILL_ON_JOB_CLOSE for any remaining
        // processes of the tree (grandchildren, etc.) that the direct TerminateProcess
        // call did not reach.
        try { _hJob?.Dispose();         } catch { }
    }
}
