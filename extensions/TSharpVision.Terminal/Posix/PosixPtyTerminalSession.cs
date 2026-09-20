using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace TSharpVision.Terminal.Posix;

/// <summary>
/// POSIX pseudo-terminal session backed by <c>openpty(3)</c> and <c>posix_spawnp(3)</c>.
/// Starts a child process attached to a PTY slave, streams output as
/// <see cref="ITerminalSession.OutputReceived"/> events, and forwards input
/// through the PTY master.
/// </summary>
/// <remarks>
/// Requires Linux or macOS. On other platforms, <see cref="StartAsync"/> throws
/// <see cref="PlatformNotSupportedException"/>. The assembly still compiles on
/// Windows; the guard is runtime-only.
/// ANSI/VT output is forwarded as raw bytes decoded as UTF-8; parsing is left
/// to <see cref="TTerminal"/> (which contains the ANSI parser).
/// </remarks>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public sealed class PosixPtyTerminalSession : ITerminalSession, IResizableTerminalSession,
    IInterruptibleTerminalSession, IExitCodeTerminalSession
{
    private readonly PosixPtyTerminalSessionOptions _options;

    // PTY master file descriptor; -1 when not open.
    private int _masterFd = -1;
    private int _masterClosed;  // 0 = open, 1 = closed; guarded by CompareExchange

    // Child process identifier; -1 when not running.
    private int _childPid = -1;

    // Background tasks started after a successful StartAsync.
    private Task? _outputReaderTask;
    private Task? _processWatcherTask;

    // Serialises input writes to the PTY master.
    private readonly object _inputLock = new();

    // State flags.
    private volatile bool _isRunning;
    private int _exitedFired;  // 0 = not fired; guarded by CompareExchange
    private int _disposed;     // 0 = alive;  guarded by CompareExchange

    /// <inheritdoc/>
    public event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    /// <inheritdoc/>
    public event EventHandler? Exited;

    /// <inheritdoc/>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Exit code of the child process, populated after <see cref="Exited"/> fires.
    /// <see langword="null"/> if the process has not yet exited, was killed by a
    /// signal, or if the exit code was unavailable.
    /// </summary>
    public int? ExitCode { get; private set; }

    /// <param name="options">Session configuration.</param>
    public PosixPtyTerminalSession(PosixPtyTerminalSessionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrEmpty(options.FileName))
            throw new ArgumentException("FileName must be specified.", nameof(options));
    }

    /// <summary>Convenience constructor.</summary>
    public PosixPtyTerminalSession(string fileName, string? arguments = "", TerminalSize? initialSize = null)
        : this(new PosixPtyTerminalSessionOptions
        {
            FileName    = fileName,
            Arguments   = arguments ?? string.Empty,
            InitialSize = initialSize ?? new TerminalSize(80, 24)
        })
    { }

    // ── ITerminalSession ──────────────────────────────────────────────────────

    /// <summary>
    /// Starts the child process inside a POSIX pseudo-terminal.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown on platforms other than Linux and macOS.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the session is already running.
    /// </exception>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException(
                "PosixPtyTerminalSession requires Linux or macOS.");

        if (_isRunning)
            throw new InvalidOperationException("Session is already running.");

        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0)
            throw new ObjectDisposedException(nameof(PosixPtyTerminalSession));

        StartCore();
        return Task.CompletedTask;
    }

    private void StartCore()
    {
        // Prepare unmanaged arguments for posix_spawnp.
        string?[] arguments = BuildArgv();
        IntPtr file = IntPtr.Zero;
        IntPtr directory = IntPtr.Zero;
        IntPtr argv = IntPtr.Zero;
        var argumentPointers = new IntPtr[arguments.Length];
        try
        {
            file = Marshal.StringToCoTaskMemUTF8(_options.FileName);
            if (!string.IsNullOrEmpty(_options.WorkingDirectory))
                directory = Marshal.StringToCoTaskMemUTF8(_options.WorkingDirectory);
            argv = Marshal.AllocHGlobal(arguments.Length * IntPtr.Size);
            for (int i = 0; i < arguments.Length; i++)
            {
                argumentPointers[i] = arguments[i] is { } argument
                    ? Marshal.StringToCoTaskMemUTF8(argument)
                    : IntPtr.Zero;
                Marshal.WriteIntPtr(argv, i * IntPtr.Size, argumentPointers[i]);
            }

            StartNativeProcess(file, directory, argv);
        }
        finally
        {
            foreach (IntPtr pointer in argumentPointers)
                Marshal.FreeCoTaskMem(pointer);
            Marshal.FreeHGlobal(argv);
            Marshal.FreeCoTaskMem(directory);
            Marshal.FreeCoTaskMem(file);
        }
    }

    private void StartNativeProcess(IntPtr file, IntPtr directory, IntPtr argv)
    {
        var winsize = new NativeMethods.WinSize
        {
            ws_row = (ushort)_options.InitialSize.Rows,
            ws_col = (ushort)_options.InitialSize.Columns
        };

        IntPtr slaveName = Marshal.AllocHGlobal(256);
        IntPtr actions = Marshal.AllocHGlobal(1024);
        IntPtr attributes = Marshal.AllocHGlobal(1024);
        IntPtr environment = IntPtr.Zero;
        IntPtr[] environmentPointers = Array.Empty<IntPtr>();
        int masterFd = -1;
        int slaveFd = -1;
        bool actionsInitialized = false;
        bool attributesInitialized = false;
        try
        {
            if (NativeMethods.OpenPty(out masterFd, out slaveFd, slaveName,
                    IntPtr.Zero, ref winsize) != 0)
                throw new InvalidOperationException(
                    $"openpty failed (errno={Marshal.GetLastWin32Error()}).");

            // POSIX_SPAWN_SETSID starts a new session. Opening
            // the slave inside that session makes it the controlling terminal.
            CheckSpawn(NativeMethods.SpawnActionsInit(actions), "file actions init");
            actionsInitialized = true;
            CheckSpawn(NativeMethods.SpawnAttrInit(attributes), "attributes init");
            attributesInitialized = true;
            short setsidFlag = OperatingSystem.IsLinux() ? (short)0x80 : (short)0x400;
            CheckSpawn(NativeMethods.SpawnAttrSetFlags(attributes, setsidFlag), "setsid flag");
            if (directory != IntPtr.Zero)
                CheckSpawn(NativeMethods.SpawnActionsAddChdir(actions, directory), "chdir action");
            CheckSpawn(NativeMethods.SpawnActionsAddOpen(actions, 0, slaveName, 2, 0), "open slave action");
            CheckSpawn(NativeMethods.SpawnActionsAddDup2(actions, 0, 1), "stdout action");
            CheckSpawn(NativeMethods.SpawnActionsAddDup2(actions, 0, 2), "stderr action");
            CheckSpawn(NativeMethods.SpawnActionsAddClose(actions, masterFd), "close master action");
            CheckSpawn(NativeMethods.SpawnActionsAddClose(actions, slaveFd), "close slave action");

            var variables = Environment.GetEnvironmentVariables();
            environmentPointers = new IntPtr[variables.Count];
            environment = Marshal.AllocHGlobal((variables.Count + 1) * IntPtr.Size);
            int index = 0;
            foreach (System.Collections.DictionaryEntry variable in variables)
            {
                environmentPointers[index] = Marshal.StringToCoTaskMemUTF8(
                    $"{variable.Key}={variable.Value}");
                Marshal.WriteIntPtr(environment, index * IntPtr.Size, environmentPointers[index]);
                index++;
            }
            Marshal.WriteIntPtr(environment, index * IntPtr.Size, IntPtr.Zero);

            int error = NativeMethods.Spawn(out int childPid, file, actions,
                attributes, argv, environment);
            if (error == 2) // ENOENT: forkpty/execvp used to report exit 127 asynchronously.
                error = SpawnMissingExecutable(actions, attributes, environment, out childPid);
            CheckSpawn(error, "posix_spawnp");

            NativeMethods.Close(slaveFd);
            slaveFd = -1;
            int ownedMasterFd = masterFd;
            masterFd = -1;
            StartParent(childPid, ownedMasterFd);
        }
        finally
        {
            if (masterFd >= 0) NativeMethods.Close(masterFd);
            if (slaveFd >= 0) NativeMethods.Close(slaveFd);
            if (attributesInitialized) NativeMethods.SpawnAttrDestroy(attributes);
            if (actionsInitialized) NativeMethods.SpawnActionsDestroy(actions);
            foreach (IntPtr pointer in environmentPointers)
                Marshal.FreeCoTaskMem(pointer);
            Marshal.FreeHGlobal(environment);
            Marshal.FreeHGlobal(attributes);
            Marshal.FreeHGlobal(actions);
            Marshal.FreeHGlobal(slaveName);
        }
    }

    private static void CheckSpawn(int error, string operation)
    {
        if (error != 0)
            throw new InvalidOperationException($"{operation} failed (errno={error}).");
    }

    private static int SpawnMissingExecutable(IntPtr actions, IntPtr attributes,
        IntPtr environment, out int childPid)
    {
        IntPtr shell = Marshal.StringToCoTaskMemUTF8("/bin/sh");
        IntPtr option = Marshal.StringToCoTaskMemUTF8("-c");
        IntPtr command = Marshal.StringToCoTaskMemUTF8("exit 127");
        IntPtr argv = Marshal.AllocHGlobal(4 * IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(argv, 0, shell);
            Marshal.WriteIntPtr(argv, IntPtr.Size, option);
            Marshal.WriteIntPtr(argv, 2 * IntPtr.Size, command);
            Marshal.WriteIntPtr(argv, 3 * IntPtr.Size, IntPtr.Zero);
            return NativeMethods.Spawn(out childPid, shell, actions,
                attributes, argv, environment);
        }
        finally
        {
            Marshal.FreeHGlobal(argv);
            Marshal.FreeCoTaskMem(command);
            Marshal.FreeCoTaskMem(option);
            Marshal.FreeCoTaskMem(shell);
        }
    }

    private void StartParent(int childPid, int masterFd)
    {
        bool success = false;
        try
        {
            _masterFd = masterFd;
            _childPid = childPid;

            _isRunning = true;
            _outputReaderTask   = Task.Run(RunOutputReader);
            _processWatcherTask = Task.Run(RunProcessWatcher);
            success    = true;
        }
        finally
        {
            if (!success)
            {
                NativeMethods.Kill(childPid, NativeMethods.SIGKILL);
                NativeMethods.WaitPid(childPid, out _, NativeMethods.WNOHANG);
                NativeMethods.Close(masterFd);
                _masterFd = -1;
                _childPid = -1;
            }
        }
    }

    // Builds a null-terminated argv array suitable for posix_spawnp.
    // argv[0] is the basename of the executable. The remaining elements are
    // derived from Arguments using a simple shell-like tokenizer that honours
    // double-quoted and single-quoted arguments. A null entry terminates.
    private string?[] BuildArgv()
    {
        string name = Path.GetFileName(_options.FileName);
        if (string.IsNullOrEmpty(name)) name = _options.FileName;

        if (string.IsNullOrWhiteSpace(_options.Arguments))
            return new string?[] { name, null };

        string[] parts = TokenizeArguments(_options.Arguments ?? string.Empty);
        var argv = new string?[parts.Length + 2]; // name + parts + null terminator
        argv[0] = name;
        for (int i = 0; i < parts.Length; i++)
            argv[i + 1] = parts[i];
        argv[argv.Length - 1] = null;
        return argv;
    }

    /// <summary>
    /// Splits <paramref name="args"/> into tokens using shell-like rules:
    /// whitespace separates tokens; double-quoted strings preserve interior
    /// whitespace; backslash escapes one character inside double quotes;
    /// single-quoted strings are treated literally.
    /// </summary>
    internal static string[] TokenizeArguments(string args)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < args.Length)
        {
            // Skip inter-token whitespace.
            while (i < args.Length && (args[i] == ' ' || args[i] == '\t')) i++;
            if (i >= args.Length) break;

            var token = new StringBuilder();
            while (i < args.Length && args[i] != ' ' && args[i] != '\t')
            {
                if (args[i] == '"')
                {
                    i++; // skip opening "
                    while (i < args.Length && args[i] != '"')
                    {
                        if (args[i] == '\\' && i + 1 < args.Length)
                            i++; // consume backslash; next char is literal
                        token.Append(args[i++]);
                    }
                    if (i < args.Length) i++; // skip closing "
                }
                else if (args[i] == '\'')
                {
                    i++; // skip opening '
                    while (i < args.Length && args[i] != '\'') token.Append(args[i++]);
                    if (i < args.Length) i++; // skip closing '
                }
                else
                {
                    token.Append(args[i++]);
                }
            }
            tokens.Add(token.ToString());
        }
        return tokens.ToArray();
    }

    /// <inheritdoc/>
    /// <remarks>If the session is not running, the call is a no-op.</remarks>
    public Task SendInputAsync(string input, CancellationToken cancellationToken = default)
    {
        if (!_isRunning || _masterFd < 0) return Task.CompletedTask;

        lock (_inputLock)
        {
            if (_masterFd < 0) return Task.CompletedTask;
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(input);
                WriteToMaster(data);
            }
            catch (Exception) { }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Requests orderly session termination. Sends SIGTERM and, if the process
    /// does not exit within three seconds, sends SIGKILL.
    /// Safe to call before start, after natural exit, and multiple times.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return;

        // Request graceful termination.
        KillChild(NativeMethods.SIGTERM);

        // Allow up to 3 seconds for the process to exit naturally.
        if (_processWatcherTask != null)
        {
            bool exited = await Task.WhenAny(_processWatcherTask, Task.Delay(3000, CancellationToken.None))
                                    .ConfigureAwait(false) == _processWatcherTask;
            if (!exited)
                KillChild(NativeMethods.SIGKILL);
        }

        // Close the master fd; this delivers EIO/EBADF to the output reader.
        CloseMasterFd();

        // Wait for background tasks to drain (with timeout).
        if (_outputReaderTask != null)
            await Task.WhenAny(_outputReaderTask, Task.Delay(5000, CancellationToken.None))
                      .ConfigureAwait(false);
        if (_processWatcherTask != null)
            await Task.WhenAny(_processWatcherTask, Task.Delay(5000, CancellationToken.None))
                      .ConfigureAwait(false);

        _isRunning = false;
        FireExited();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        KillChild(NativeMethods.SIGKILL);
        _isRunning = false;
        CloseMasterFd();
        // RunProcessWatcher's waitpid will reap the child after it dies from SIGKILL.
        // Exited is intentionally not fired from Dispose.
    }

    // ── IResizableTerminalSession ─────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>If the session is not running, the call is a no-op.</remarks>
    public Task ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default)
    {
        if (!_isRunning || _masterFd < 0) return Task.CompletedTask;

        var winsize = new NativeMethods.WinSize
        {
            ws_row = (ushort)size.Rows,
            ws_col = (ushort)size.Columns
        };
        NativeMethods.Ioctl(_masterFd, NativeMethods.TIOCSWINSZ, ref winsize);
        return Task.CompletedTask;
    }

    // ── IInterruptibleTerminalSession ─────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Writes the ETX character (0x03) to the PTY master, which the child
    /// process receives as a Ctrl+C signal via the terminal line discipline.
    /// If the session is not running, the call is a no-op.
    /// </remarks>
    public Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning || _masterFd < 0) return Task.CompletedTask;

        lock (_inputLock)
        {
            if (_masterFd < 0) return Task.CompletedTask;
            try { WriteToMaster(new byte[] { 0x03 }); } // ETX = Ctrl+C
            catch (Exception) { }
        }
        return Task.CompletedTask;
    }

    // ── Background tasks ──────────────────────────────────────────────────────

    private void RunOutputReader()
    {
        const int bufSize = 4096;
        IntPtr nativeBuf  = Marshal.AllocHGlobal(bufSize);
        byte[] managedBuf = new byte[bufSize];
        char[] charBuf    = new char[Encoding.UTF8.GetMaxCharCount(bufSize)];
        var decoder       = Encoding.UTF8.GetDecoder();
        try
        {
            while (true)
            {
                int fd = _masterFd;
                if (fd < 0) break;

                nint n = NativeMethods.Read(fd, nativeBuf, bufSize);
                if (n <= 0) break; // EOF (0) or error (-1, e.g. EIO/EBADF after close)

                Marshal.Copy(nativeBuf, managedBuf, 0, (int)n);
                int charCount = decoder.GetChars(managedBuf, 0, (int)n, charBuf, 0);
                if (charCount > 0)
                    OutputReceived?.Invoke(this, new TerminalOutputEventArgs(new string(charBuf, 0, charCount)));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(nativeBuf);
        }
    }

    private async Task RunProcessWatcher()
    {
        int pid = _childPid;
        if (pid <= 0) return;

        // Block a thread pool thread until the child process exits.
        int rawStatus = await Task.Run(() =>
        {
            int rc = NativeMethods.WaitPid(pid, out int status, 0);
            return rc > 0 ? status : -1;
        }).ConfigureAwait(false);

        if (rawStatus >= 0)
            ExitCode = NativeMethods.DecodeExitStatus(rawStatus);

        // Close the master fd; this delivers EIO/EBADF to RunOutputReader.
        CloseMasterFd();

        // Wait for the reader to drain any remaining output before signalling exit.
        if (_outputReaderTask != null)
        {
            try { await _outputReaderTask.ConfigureAwait(false); }
            catch { }
        }

        _isRunning = false;
        FireExited();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void WriteToMaster(byte[] data)
    {
        if (data.Length == 0) return;
        IntPtr buf = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, buf, data.Length);
            int written = 0;
            while (written < data.Length)
            {
                nint n = NativeMethods.Write(_masterFd, buf + written, data.Length - written);
                if (n <= 0) break;
                written += (int)n;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private void CloseMasterFd()
    {
        if (Interlocked.CompareExchange(ref _masterClosed, 1, 0) != 0) return;
        int fd = _masterFd;
        if (fd >= 0)
            NativeMethods.Close(fd);
    }

    private void KillChild(int signal)
    {
        int pid = _childPid;
        if (pid <= 0) return;
        // Send to the process group (negative pid) so any child processes
        // spawned by the shell also receive the signal.
        try { NativeMethods.Kill(-pid, signal); }
        catch { }
        // Direct kill as a fallback in case process group kill fails.
        try { NativeMethods.Kill(pid, signal); }
        catch { }
    }

    private void FireExited()
    {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0) return;
        if (Interlocked.CompareExchange(ref _exitedFired, 1, 0) == 0)
            Exited?.Invoke(this, EventArgs.Empty);
    }
}
