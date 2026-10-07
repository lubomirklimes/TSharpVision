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
/// <para>
/// Requires Linux or macOS. On other platforms, <see cref="StartAsync"/> throws
/// <see cref="PlatformNotSupportedException"/>. The assembly still compiles on
/// Windows; the guard is runtime-only.
/// ANSI/VT output is forwarded as raw bytes decoded as UTF-8; parsing is left
/// to <see cref="TTerminal"/> (which contains the ANSI parser).
/// </para>
/// <para>
/// <b>Ownership.</b> The master descriptor is owned by a <see cref="PtyMasterHandle"/> and is only
/// ever used under a reference, so it cannot be used after close or closed twice. The slave
/// descriptor lives only inside <c>StartNativeProcess</c> and is closed there once the child owns
/// its own copy. The child is reaped by the process watcher, and is never signalled after it was
/// reaped, so a recycled pid is never hit. Input goes through a <see cref="TerminalInputPump"/>.
/// </para>
/// <para>
/// A session is single-use: it can be started once.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public sealed class PosixPtyTerminalSession : ITerminalSession, IResizableTerminalSession,
    IInterruptibleTerminalSession, IExitCodeTerminalSession
{
    // How long one reader poll waits before it looks at the stop flag again.
    private const int ReaderPollMilliseconds = 200;

    // After the child exits, output still in flight is drained until nothing arrives for one short
    // poll, or until this cap — a background job that inherited the slave could otherwise keep the
    // reader, and so the Exited event, alive forever.
    private const int DrainPollMilliseconds = 50;
    private static readonly TimeSpan DrainLimit = TimeSpan.FromSeconds(2);

    private readonly PosixPtyTerminalSessionOptions _options;
    private readonly TerminalInputPump _input;

    // The PTY master; null before start and once released.
    private PtyMasterHandle? _master;

    // Child process identifier; -1 when never started. Guarded with _childReaped by _processLock.
    private int _childPid = -1;
    private bool _childReaped;
    private readonly object _processLock = new();

    // Background tasks started after a successful StartAsync.
    private Task? _outputReaderTask;
    private Task? _processWatcherTask;

    // State flags.
    private volatile bool _isRunning;
    private volatile bool _stopRequested;
    private volatile bool _childExited;
    private long _drainDeadlineTicks;
    private int _started;      // 0 = never started; guarded by CompareExchange
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

    /// <summary>The child's process id, or -1 before a successful start — for a host that tracks or signals it.</summary>
    public int ProcessId => _childPid;

    /// <summary>True once the watcher has reaped the child.</summary>
    internal bool IsChildReaped
    {
        get { lock (_processLock) return _childReaped; }
    }

    /// <summary>True while the master descriptor is still owned by this session.</summary>
    internal bool HasMaster => Volatile.Read(ref _master) is { IsClosed: false };

    /// <summary>The master descriptor number while owned, else -1. For tests that inspect its flags.</summary>
    internal int MasterDescriptorForDiagnostics => Volatile.Read(ref _master) is { IsClosed: false } master ? master.Fd : -1;

    /// <summary>The output reader, for tests that assert it finished.</summary>
    internal Task? OutputReaderTask => _outputReaderTask;

    /// <param name="options">Session configuration.</param>
    public PosixPtyTerminalSession(PosixPtyTerminalSessionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrEmpty(options.FileName))
            throw new ArgumentException("FileName must be specified.", nameof(options));
        _input = new TerminalInputPump(WriteToMaster);
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
    /// Thrown if the session is already running or was started before.
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

        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            throw new InvalidOperationException("A terminal session can be started only once.");

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
            ws_row = ClampDimension(_options.InitialSize.Rows),
            ws_col = ClampDimension(_options.InitialSize.Columns)
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
            // Neither descriptor may leak into a process some other thread spawns — another
            // terminal's shell, or a git the Commander runs in the background, would otherwise hold
            // this terminal's master and could read and write it. The child below gets the slave
            // by opening it by name, never by inheritance.
            if (OperatingSystem.IsLinux())
            {
                // Atomic: the master is close-on-exec from birth and no slave is opened here.
                masterFd = NativeMethods.PosixOpenPt(NativeMethods.O_RDWR_NOCTTY_CLOEXEC_LINUX);
                if (masterFd < 0)
                    throw new InvalidOperationException(
                        $"posix_openpt failed (errno={Marshal.GetLastPInvokeError()}).");
                if (NativeMethods.GrantPt(masterFd) != 0 || NativeMethods.UnlockPt(masterFd) != 0)
                    throw new InvalidOperationException(
                        $"grantpt/unlockpt failed (errno={Marshal.GetLastPInvokeError()}).");
                int nameError = NativeMethods.PtsNameR(masterFd, slaveName, 256);
                if (nameError != 0)
                    throw new InvalidOperationException($"ptsname_r failed (errno={nameError}).");
                // The window size lives on the pair; setting it through the master is enough.
                NativeMethods.Ioctl(masterFd, NativeMethods.TIOCSWINSZ, ref winsize);
            }
            else
            {
                if (NativeMethods.OpenPty(out masterFd, out slaveFd, slaveName,
                        IntPtr.Zero, ref winsize) != 0)
                    throw new InvalidOperationException(
                        $"openpty failed (errno={Marshal.GetLastWin32Error()}).");

                // Darwin: openpty has no O_CLOEXEC and posix_openpt does not document it, so the
                // flag is set straight after. A process spawned by another thread in that instant
                // can still inherit the pair; see the U-1a report.
                NativeMethods.SetCloseOnExec(masterFd);
                NativeMethods.SetCloseOnExec(slaveFd);
            }

            // POSIX_SPAWN_SETSID starts a new session. Linux makes the opened slave its
            // controlling terminal; Darwin only redirects the child's standard descriptors.
            CheckSpawn(NativeMethods.SpawnActionsInit(actions), "file actions init");
            actionsInitialized = true;
            CheckSpawn(NativeMethods.SpawnAttrInit(attributes), "attributes init");
            attributesInitialized = true;
            // Darwin also closes every descriptor the file actions below do not name, so the shell
            // inherits nothing of this process but its own terminal (see the Linux equivalent below).
            short spawnFlags = OperatingSystem.IsLinux()
                ? (short)0x80
                : (short)(0x400 | NativeMethods.POSIX_SPAWN_CLOEXEC_DEFAULT_DARWIN);
            CheckSpawn(NativeMethods.SpawnAttrSetFlags(attributes, spawnFlags), "spawn flags");
            if (directory != IntPtr.Zero)
                CheckSpawn(NativeMethods.SpawnActionsAddChdir(actions, directory), "chdir action");
            CheckSpawn(NativeMethods.SpawnActionsAddOpen(actions, 0, slaveName, 2, 0), "open slave action");
            CheckSpawn(NativeMethods.SpawnActionsAddDup2(actions, 0, 1), "stdout action");
            CheckSpawn(NativeMethods.SpawnActionsAddDup2(actions, 0, 2), "stderr action");
            CheckSpawn(NativeMethods.SpawnActionsAddClose(actions, masterFd), "close master action");
            if (slaveFd >= 0)
                CheckSpawn(NativeMethods.SpawnActionsAddClose(actions, slaveFd), "close slave action");
            if (OperatingSystem.IsLinux())
                AddCloseFromThree(actions);

            Dictionary<string, string> variables = ChildEnvironment(_options.Environment);
            environmentPointers = new IntPtr[variables.Count];
            environment = Marshal.AllocHGlobal((variables.Count + 1) * IntPtr.Size);
            int index = 0;
            foreach ((string name, string value) in variables)
            {
                environmentPointers[index] = Marshal.StringToCoTaskMemUTF8($"{name}={value}");
                Marshal.WriteIntPtr(environment, index * IntPtr.Size, environmentPointers[index]);
                index++;
            }
            Marshal.WriteIntPtr(environment, index * IntPtr.Size, IntPtr.Zero);

            int error = NativeMethods.Spawn(out int childPid, file, actions,
                attributes, argv, environment);
            if (error == 2) // ENOENT: forkpty/execvp used to report exit 127 asynchronously.
                error = SpawnMissingExecutable(actions, attributes, environment, out childPid);
            CheckSpawn(error, "posix_spawnp");

            // The child has its own slave now; the parent's copy (Darwin only) is closed exactly here.
            if (slaveFd >= 0) NativeMethods.Close(slaveFd);
            slaveFd = -1;
            // Ownership of the master moves to the handle; from here nothing closes the integer.
            var master = new PtyMasterHandle(masterFd);
            masterFd = -1;
            StartParent(childPid, master);
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

    /// <summary>
    /// The shell gets descriptors 0, 1 and 2 — its terminal — and nothing else of this process:
    /// no socket, pipe, file or other terminal's master that some code opened without
    /// close-on-exec. Needs glibc 2.34+; elsewhere the child keeps inheriting such descriptors, as it
    /// always did, and this session's own descriptors are still close-on-exec.
    /// </summary>
    private static void AddCloseFromThree(IntPtr actions)
    {
        try
        {
            CheckSpawn(NativeMethods.SpawnActionsAddCloseFrom(actions, 3), "closefrom action");
        }
        catch (EntryPointNotFoundException)
        {
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

    private void StartParent(int childPid, PtyMasterHandle master)
    {
        bool success = false;
        try
        {
            Volatile.Write(ref _master, master);
            lock (_processLock) _childPid = childPid;

            _isRunning = true;
            _outputReaderTask   = Task.Run(RunOutputReader);
            _processWatcherTask = Task.Run(RunProcessWatcher);
            success    = true;
        }
        finally
        {
            if (!success)
            {
                // Nothing watches the child yet, so it is killed and reaped here, synchronously:
                // SIGKILL cannot be ignored, so the blocking waitpid returns promptly.
                NativeMethods.Kill(childPid, NativeMethods.SIGKILL);
                WaitForExit(childPid);
                lock (_processLock) _childReaped = true;
                _isRunning = false;
                ReleaseMaster();
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

    /// <summary>
    /// The child's environment: this process's, with <paramref name="overrides"/> applied (a null value removes a
    /// variable). Built in the parent, before spawning, so nothing managed runs in the child.
    /// </summary>
    internal static Dictionary<string, string> ChildEnvironment(IReadOnlyDictionary<string, string?>? overrides)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables())
            if (variable.Key is string name && variable.Value is string value) variables[name] = value;
        if (overrides is not null)
        {
            foreach ((string name, string? value) in overrides)
            {
                if (string.IsNullOrEmpty(name) || name.Contains('=') || name.Contains('\0')) continue;
                if (value is null) variables.Remove(name);
                else if (!value.Contains('\0')) variables[name] = value;
            }
        }

        return variables;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// If the session is not running, the call is a no-op. The bytes are copied and queued; the
    /// returned task completes once they reached the PTY or were discarded because the session
    /// ended. The caller's thread never blocks on the PTY.
    /// </remarks>
    public Task SendInputAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default)
    {
        if (!_isRunning || input.IsEmpty) return Task.CompletedTask;
        return _input.EnqueueAsync(input.ToArray());
    }

    /// <summary>
    /// Requests orderly session termination. Sends SIGHUP and SIGTERM and, if the process
    /// does not exit within three seconds, sends SIGKILL.
    /// Safe to call before start, after natural exit, and multiple times.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return;

        // Request graceful termination. SIGHUP is what closing a terminal means, and an interactive
        // shell — which ignores SIGTERM — exits on it; SIGTERM covers a program that ignores SIGHUP.
        KillChild(NativeMethods.SIGHUP);
        KillChild(NativeMethods.SIGTERM);

        // Allow up to 3 seconds for the process to exit naturally.
        if (_processWatcherTask != null)
        {
            bool exited = await Task.WhenAny(_processWatcherTask, Task.Delay(3000, CancellationToken.None))
                                    .ConfigureAwait(false) == _processWatcherTask;
            if (!exited)
                KillChild(NativeMethods.SIGKILL);
        }

        // The reader notices within one poll interval; no descriptor is closed underneath it.
        _stopRequested = true;
        _input.Close();

        // Wait for background tasks to drain (with timeout).
        if (_outputReaderTask != null)
            await Task.WhenAny(_outputReaderTask, Task.Delay(5000, CancellationToken.None))
                      .ConfigureAwait(false);
        if (_processWatcherTask != null)
            await Task.WhenAny(_processWatcherTask, Task.Delay(5000, CancellationToken.None))
                      .ConfigureAwait(false);

        ReleaseMaster();
        _isRunning = false;
        FireExited();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        _stopRequested = true;
        KillChild(NativeMethods.SIGKILL);
        _isRunning = false;
        _input.Close();
        // Refuses new uses at once; the descriptor itself is closed when the reader or a writer
        // still inside a call lets go of it. RunProcessWatcher reaps the child after SIGKILL.
        ReleaseMaster();
        // Exited is intentionally not fired from Dispose.
    }

    // ── IResizableTerminalSession ─────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>If the session is not running, the call is a no-op.</remarks>
    public Task ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return Task.CompletedTask;

        PtyMasterHandle? master = AcquireMaster();
        if (master is null) return Task.CompletedTask;
        try
        {
            var winsize = new NativeMethods.WinSize
            {
                ws_row = ClampDimension(size.Rows),
                ws_col = ClampDimension(size.Columns)
            };
            // The kernel sends SIGWINCH to the foreground process group.
            NativeMethods.Ioctl(master.Fd, NativeMethods.TIOCSWINSZ, ref winsize);
        }
        finally
        {
            master.DangerousRelease();
        }
        return Task.CompletedTask;
    }

    // ── IInterruptibleTerminalSession ─────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Writes the ETX character (0x03) to the PTY master, which the child
    /// process receives as a Ctrl+C signal via the terminal line discipline.
    /// If the session is not running, the call is a no-op. The write runs on a pool thread, and
    /// deliberately not behind queued input, so a backlog of pasted text cannot delay it.
    /// </remarks>
    public Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return Task.CompletedTask;
        return Task.Run(() => { WriteToMaster(new byte[] { 0x03 }); }, CancellationToken.None);
    }

    // ── Background tasks ──────────────────────────────────────────────────────

    private void RunOutputReader()
    {
        const int bufSize = 4096;
        IntPtr nativeBuf  = Marshal.AllocHGlobal(bufSize);
        byte[] managedBuf = new byte[bufSize];
        // Some kernels report POLLNVAL for device descriptors; the reader then falls back to plain
        // blocking reads, which end when the slave side hangs up.
        bool pollUsable   = true;
        try
        {
            while (!_stopRequested)
            {
                PtyMasterHandle? master = AcquireMaster();
                if (master is null) break;

                nint n;
                try
                {
                    int fd = master.Fd;
                    if (pollUsable)
                    {
                        bool draining = _childExited;
                        var descriptor = new NativeMethods.PollFd { Fd = fd, Events = NativeMethods.POLLIN };
                        int ready = NativeMethods.Poll(ref descriptor,
                            draining ? DrainPollMilliseconds : ReaderPollMilliseconds);
                        if (ready < 0)
                        {
                            if (Marshal.GetLastPInvokeError() == NativeMethods.EINTR) continue;
                            pollUsable = false;
                        }
                        else if (ready == 0)
                        {
                            if (draining) break; // the child is gone and nothing more arrived
                            continue;
                        }
                        else if ((descriptor.ReturnedEvents & NativeMethods.POLLNVAL) != 0)
                        {
                            pollUsable = false;
                        }
                        // POLLIN, POLLHUP or POLLERR: read returns data, end of file or an error.
                    }

                    n = NativeMethods.Read(fd, nativeBuf, bufSize);
                    if (n < 0 && Marshal.GetLastPInvokeError() == NativeMethods.EINTR) continue;
                }
                finally
                {
                    master.DangerousRelease();
                }

                if (n <= 0) break; // EOF (0) or error (-1, e.g. EIO once every slave is closed)

                // Raw bytes: a character or an escape sequence split between reads is joined by the
                // emulator, which owns decoding.
                Marshal.Copy(nativeBuf, managedBuf, 0, (int)n);
                OutputReceived?.Invoke(this, new TerminalOutputEventArgs(managedBuf.AsMemory(0, (int)n)));

                if (_childExited && DateTime.UtcNow.Ticks > Interlocked.Read(ref _drainDeadlineTicks))
                    break;
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

        // Block a thread pool thread until the child process exits, and reap it.
        int rawStatus = await Task.Run(() => WaitForExit(pid)).ConfigureAwait(false);

        // From here the pid may belong to somebody else; KillChild checks this under the same lock.
        lock (_processLock) _childReaped = true;

        if (rawStatus >= 0)
            ExitCode = NativeMethods.DecodeExitStatus(rawStatus);

        // Let the reader drain what is still in flight, within a bound.
        Interlocked.Exchange(ref _drainDeadlineTicks, (DateTime.UtcNow + DrainLimit).Ticks);
        _childExited = true;
        if (_outputReaderTask != null)
        {
            try { await _outputReaderTask.ConfigureAwait(false); }
            catch { }
        }

        _input.Close();
        ReleaseMaster();
        _isRunning = false;
        FireExited();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Waits for and reaps <paramref name="pid"/>; returns the raw status or -1.</summary>
    private static int WaitForExit(int pid)
    {
        while (true)
        {
            int rc = NativeMethods.WaitPid(pid, out int status, 0);
            if (rc == pid) return status;
            if (rc < 0 && Marshal.GetLastPInvokeError() == NativeMethods.EINTR) continue;
            return -1; // ECHILD: already reaped elsewhere; nothing left to wait for
        }
    }

    private static ushort ClampDimension(int value) => (ushort)Math.Clamp(value, 1, ushort.MaxValue);

    /// <summary>A referenced master, or null once it has been released.</summary>
    private PtyMasterHandle? AcquireMaster()
    {
        PtyMasterHandle? master = Volatile.Read(ref _master);
        return master is not null && master.TryAcquire() ? master : null;
    }

    /// <summary>Gives up this session's ownership of the master. Idempotent.</summary>
    private void ReleaseMaster() => Interlocked.Exchange(ref _master, null)?.Dispose();

    /// <summary>Writes every byte, or returns false once the master is unusable.</summary>
    private bool WriteToMaster(byte[] data)
    {
        if (data.Length == 0) return true;

        PtyMasterHandle? master = AcquireMaster();
        if (master is null) return false;
        try
        {
            int written = 0;
            while (written < data.Length)
            {
                nint n = NativeMethods.Write(master.Fd, ref data[written], data.Length - written);
                if (n > 0)
                {
                    written += (int)n;
                    continue;
                }
                if (n < 0 && Marshal.GetLastPInvokeError() == NativeMethods.EINTR) continue;
                return false; // EIO once the child side is gone
            }
            return true;
        }
        finally
        {
            master.DangerousRelease();
        }
    }

    private void KillChild(int signal)
    {
        lock (_processLock)
        {
            int pid = _childPid;
            // A reaped pid may already name an unrelated process (group); never signal it.
            if (pid <= 0 || _childReaped) return;
            // Send to the process group (negative pid) so any child processes
            // spawned by the shell also receive the signal.
            try { NativeMethods.Kill(-pid, signal); }
            catch { }
            // Direct kill as a fallback in case process group kill fails.
            try { NativeMethods.Kill(pid, signal); }
            catch { }
        }
    }

    private void FireExited()
    {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0) return;
        if (Interlocked.CompareExchange(ref _exitedFired, 1, 0) == 0)
            Exited?.Invoke(this, EventArgs.Empty);
    }
}
