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
/// Requires Windows 10 version 1809 (build 17763) or later.
/// On earlier Windows versions or non-Windows platforms,
/// <see cref="StartAsync"/> throws <see cref="PlatformNotSupportedException"/>.
/// The assembly still compiles on all platforms; the guard is runtime-only.
/// ANSI/VT output is forwarded as raw bytes decoded to UTF-8; parsing is left
/// to <see cref="TTerminal"/> (which contains the ANSI parser).
/// </remarks>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class ConPtyTerminalSession : ITerminalSession, IResizableTerminalSession, IInterruptibleTerminalSession
{
    private readonly ConPtyTerminalSessionOptions _options;

    // ConPTY handle — non-null and valid between StartAsync and cleanup.
    private SafePseudoConsoleHandle _hPseudoConsole;
    private int _conPtyClosed;   // 0 = open, 1 = closed; guarded by CompareExchange

    // Pipe streams — wrap the parent-side pipe handles.
    private FileStream _inputStream;    // parent writes input here
    private FileStream _outputStream;   // parent reads output here

    // Process handle.
    private SafeProcessHandle _hProcess;

    // Job Object for process-tree cleanup (best-effort; null when unavailable).
    private SafeJobObjectHandle _hJob;

    // Background tasks.
    private Task _outputReaderTask;
    private Task _processWatcherTask;

    // Thread-safety for input writes.
    private readonly object _inputLock = new();

    // State flags.
    private volatile bool _isRunning;
    private int _exitedFired;   // 0 = not fired; guarded by CompareExchange
    private int _disposed;      // 0 = alive; guarded by CompareExchange

    /// <inheritdoc/>
    public event EventHandler<TerminalOutputEventArgs> OutputReceived;

    /// <inheritdoc/>
    public event EventHandler Exited;

    /// <inheritdoc/>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Exit code of the child process, populated after <see cref="Exited"/> fires.
    /// <see langword="null"/> if the process has not yet exited or if the exit
    /// code was unavailable.
    /// </summary>
    public int? ExitCode { get; private set; }

    /// <param name="options">Session configuration.</param>
    public ConPtyTerminalSession(ConPtyTerminalSessionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrEmpty(options.FileName))
            throw new ArgumentException("FileName must be specified.", nameof(options));
    }

    /// <summary>
    /// Convenience constructor: creates options from individual parameters.
    /// </summary>
    public ConPtyTerminalSession(string fileName, string arguments = "", TerminalSize? initialSize = null)
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
    /// Thrown if the session is already running.
    /// </exception>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            throw new PlatformNotSupportedException(
                "ConPtyTerminalSession requires Windows 10 version 1809 (build 17763) or later.");

        if (_isRunning)
            throw new InvalidOperationException("Session is already running.");

        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0)
            throw new ObjectDisposedException(nameof(ConPtyTerminalSession));

        StartCore();
        return Task.CompletedTask;
    }

    private void StartCore()
    {
        SafeFileHandle inputRead   = null;
        SafeFileHandle inputWrite  = null;
        SafeFileHandle outputRead  = null;
        SafeFileHandle outputWrite = null;
        SafePseudoConsoleHandle hPseudoConsole = null;
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
            var coord = new NativeMethods.COORD
            {
                X = (short)_options.InitialSize.Columns,
                Y = (short)_options.InitialSize.Rows
            };

            int hr = NativeMethods.CreatePseudoConsole(coord, inputRead, outputWrite, 0, out hPseudoConsole);

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
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to initialize process attribute list.");

            if (!NativeMethods.UpdateProcThreadAttribute(
                    attributeList, 0,
                    new IntPtr(NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE),
                    hPseudoConsole.DangerousGetHandle(), new IntPtr(IntPtr.Size),
                    IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to set PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE.");

            // ── Create child process ──────────────────────────────────────────
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

            var cmdLine = new StringBuilder(_options.FileName);
            if (!string.IsNullOrEmpty(_options.Arguments))
                cmdLine.Append(' ').Append(_options.Arguments);

            if (!NativeMethods.CreateProcess(
                    null, cmdLine,
                    IntPtr.Zero, IntPtr.Zero,
                    false,
                    NativeMethods.EXTENDED_STARTUPINFO_PRESENT,
                    IntPtr.Zero,
                    _options.WorkingDirectory,
                    ref startupInfoEx,
                    out var processInfo))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create child process.");

            // Thread handle is not needed; close it immediately.
            NativeMethods.CloseHandle(processInfo.hThread);

            // ── Store live state ──────────────────────────────────────────────
            _hPseudoConsole = hPseudoConsole; hPseudoConsole = null;
            _hProcess       = new SafeProcessHandle(processInfo.hProcess, ownsHandle: true);
            _inputStream    = new FileStream(inputWrite,  FileAccess.Write, bufferSize: 256,  isAsync: false);
            _outputStream   = new FileStream(outputRead,  FileAccess.Read,  bufferSize: 1, isAsync: false);
            inputWrite  = null;
            outputRead  = null;

            // Assign the child to a job with KILL_ON_JOB_CLOSE so the entire
            // process tree is cleaned up when the job handle is released.
            _hJob = TryCreateJobForProcess(_hProcess.DangerousGetHandle());

            // ── Start background tasks ────────────────────────────────────────
            _outputReaderTask   = Task.Run(RunOutputReader);
            _processWatcherTask = Task.Run(RunProcessWatcher);

            _isRunning = true;
            success    = true;
        }
        finally
        {
            // Always free the attribute list (safe to free after CreateProcess returns).
            if (attributeList != IntPtr.Zero)
            {
                NativeMethods.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            // On failure, release any handles that were not transferred to fields.
            if (!success)
            {
                hPseudoConsole?.Dispose();
                inputRead?.Dispose();
                inputWrite?.Dispose();
                outputRead?.Dispose();
                outputWrite?.Dispose();
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// If the session is not running, the call is a no-op. The input string is
    /// encoded as UTF-8 before being written to the PTY master.
    /// </remarks>
    public Task SendInputAsync(string input, CancellationToken cancellationToken = default)
    {
        if (!_isRunning || _inputStream == null) return Task.CompletedTask;

        lock (_inputLock)
        {
            if (_inputStream == null) return Task.CompletedTask;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(input);
                _inputStream.Write(bytes, 0, bytes.Length);
                _inputStream.Flush();
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Request orderly termination of the session.
    /// Safe to call before <see cref="StartAsync"/>, multiple times, and after
    /// natural process exit.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return;

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

        _isRunning = false;
        FireExited();
        ReleaseHandles();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        TerminateChildIfAlive();
        _isRunning = false;
        CloseConPty();
        ReleaseHandles();
        // Exited is intentionally not fired from Dispose.
    }

    // ── IResizableTerminalSession ─────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>If the session is not running, the call is a no-op.</remarks>
    public Task ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default)
    {
        if (!_isRunning || _hPseudoConsole == null || _hPseudoConsole.IsInvalid || _hPseudoConsole.IsClosed)
            return Task.CompletedTask;

        var coord = new NativeMethods.COORD
        {
            X = (short)size.Columns,
            Y = (short)size.Rows
        };
        NativeMethods.ResizePseudoConsole(_hPseudoConsole, coord);
        return Task.CompletedTask;
    }

    // ── IInterruptibleTerminalSession ─────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Writes the ETX character (0x03) to the PTY master, which the child
    /// process receives as a Ctrl+C signal via the Windows console. If the
    /// session is not running, the call is a no-op.
    /// </remarks>
    public Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning || _inputStream == null) return Task.CompletedTask;

        lock (_inputLock)
        {
            if (_inputStream == null) return Task.CompletedTask;
            try
            {
                _inputStream.WriteByte(0x03); // ETX = Ctrl+C
                _inputStream.Flush();
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
        return Task.CompletedTask;
    }

    // ── Background tasks ──────────────────────────────────────────────────────

    private void RunOutputReader()
    {
        var buffer   = new byte[4096];
        var charBuf  = new char[Encoding.UTF8.GetMaxCharCount(4096)];
        var decoder  = Encoding.UTF8.GetDecoder();
        try
        {
            int bytesRead;
            while ((bytesRead = _outputStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                int charCount = decoder.GetChars(buffer, 0, bytesRead, charBuf, 0);
                if (charCount > 0)
                    OutputReceived?.Invoke(this, new TerminalOutputEventArgs(new string(charBuf, 0, charCount)));
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    // Creates a Job Object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE and assigns
    // the child process to it. Returns an invalid handle on failure (non-fatal).
    private static SafeJobObjectHandle TryCreateJobForProcess(IntPtr hProcess)
    {
        SafeJobObjectHandle hJob = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (hJob.IsInvalid) return hJob;
        try
        {
            var info = new NativeMethods.JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            };
            NativeMethods.SetInformationJobObject(
                hJob,
                NativeMethods.JobObjectBasicLimitInformation,
                ref info,
                Marshal.SizeOf<NativeMethods.JOBOBJECT_BASIC_LIMIT_INFORMATION>());
            NativeMethods.AssignProcessToJobObject(hJob, hProcess);
        }
        catch
        {
            // Non-fatal: fall back to direct-process termination only.
        }
        return hJob;
    }

    private async Task RunProcessWatcher()
    {
        if (_hProcess == null || _hProcess.IsInvalid) return;

        // Block the thread pool thread until the process exits.
        await Task.Run(() => NativeMethods.WaitForSingleObject(
            _hProcess.DangerousGetHandle(), NativeMethods.INFINITE)).ConfigureAwait(false);

        // Capture exit code before closing anything.
        if (NativeMethods.GetExitCodeProcess(_hProcess.DangerousGetHandle(), out uint code)
            && code != NativeMethods.STILL_ACTIVE)
        {
            ExitCode = (int)code;
        }

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

        _isRunning = false;
        FireExited();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void CloseConPty()
    {
        if (Interlocked.CompareExchange(ref _conPtyClosed, 1, 0) != 0) return;
        _hPseudoConsole?.Dispose();
    }

    private void TerminateChildIfAlive()
    {
        if (_hProcess == null || _hProcess.IsInvalid || _hProcess.IsClosed) return;
        try
        {
            NativeMethods.GetExitCodeProcess(_hProcess.DangerousGetHandle(), out uint code);
            if (code == NativeMethods.STILL_ACTIVE)
                NativeMethods.TerminateProcess(_hProcess.DangerousGetHandle(), 0);
        }
        catch { }
    }

    private void FireExited()
    {
        if (Interlocked.CompareExchange(ref _exitedFired, 1, 0) == 0)
            Exited?.Invoke(this, EventArgs.Empty);
    }

    private void ReleaseHandles()
    {
        try { _outputStream?.Dispose(); } catch { }
        try { _inputStream?.Dispose();  } catch { }
        try { _hProcess?.Dispose();     } catch { }
        // Closing the job handle triggers KILL_ON_JOB_CLOSE for any remaining
        // child processes (grandchildren, etc.) that the direct TerminateProcess
        // call may not have reached.
        try { _hJob?.Dispose();         } catch { }
    }
}
