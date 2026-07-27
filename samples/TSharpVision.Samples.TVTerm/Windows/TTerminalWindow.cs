#pragma warning disable CA1416 // Runtime OS guards in session factories make platform calls safe.
using TSharpVision.Constants;
using TSharpVision.Terminal;
using TSharpVision.Terminal.Posix;
using TSharpVision.Terminal.Windows;

namespace TSharpVision.Samples.TVTerm;

/// <summary>
/// Kind of session a TTerminalWindow is currently driving. Used to decide
/// what Restart/Interrupt/Kill should do and what UI mode to show.
/// </summary>
public enum SessionKind
{
    /// <summary>InMemoryTerminalSession with local command-prompt loop.</summary>
    InMemoryCommand,
    /// <summary>FakePtyTerminalSession — scripted output, no real process.</summary>
    Fake,
    /// <summary>ProcessTerminalSession — redirected pipes around a process.</summary>
    Pipe,
    /// <summary>ConPtyTerminalSession / PosixPtyTerminalSession — real PTY.</summary>
    Pty,
    /// <summary>PTY running an interactive shell in raw session mode.</summary>
    Shell,
}

/// <summary>
/// Parameters used to build the active session. Kept on the window so that
/// Session/Restart can rebuild the same kind of session without reopening.
/// </summary>
public sealed class SessionSpec
{
    public SessionKind Kind { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;
    public bool RawSession { get; init; }
}

/// <summary>
/// Window wrapping a <see cref="TTerminal"/> view plus a vertical scroll bar,
/// owning the currently attached <see cref="ITerminalSession"/>. Knows how to
/// start/stop/restart that session and clean it up on close.
/// </summary>
public sealed class TTerminalWindow : TWindow
{
    private readonly TTerminal _term;
    private readonly InMemoryTerminalSession _memSession;
    private ITerminalSession? _externalSession;
    private SessionSpec? _externalSpec;
    private SessionKind _kind = SessionKind.InMemoryCommand;
    private bool _userStopped;

    /// <summary>The terminal view embedded in the window.</summary>
    public TTerminal Terminal => _term;

    /// <summary>Current session kind.</summary>
    public SessionKind Kind => _kind;

    /// <summary>The external session if any (null when running InMemoryCommand).</summary>
    public ITerminalSession? ExternalSession => _externalSession;

    public TTerminalWindow(TRect bounds, string title, ushort num)
        : base(bounds, title, num)
    {
        flags |= Views.wfClose | Views.wfMove | Views.wfZoom | Views.wfGrow;
        growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);

        int w = bounds.b.x - bounds.a.x;
        int h = bounds.b.y - bounds.a.y;

        var sb = new TScrollBar(new TRect(w - 1, 1, w, h - 1));
        sb.growMode = (byte)(Views.gfGrowLoX | Views.gfGrowHiX | Views.gfGrowHiY);
        Insert(sb);

        _term = new TTerminal(new TRect(1, 1, w - 1, h - 1));
        _term.growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
        Insert(_term);
        _term.AttachVerticalScrollBar(sb);

        _memSession = new InMemoryTerminalSession();
        _term.AttachSession(_memSession);
        _memSession.StartAsync().GetAwaiter().GetResult();

        _term.InputEnabled = true;
        _term.Prompt = "> ";
    }

    /// <summary>
    /// Emit an informational banner line into the in-memory session
    /// (visible regardless of which external session is currently attached).
    /// </summary>
    public void EmitBanner(string text) => _memSession.Emit(text);

    /// <summary>
    /// Start an external session built from the given spec. Any currently
    /// active external session is stopped first.
    /// </summary>
    public void StartSession(SessionSpec spec)
    {
        StopExternalSession();

        ITerminalSession session;
        try
        {
            switch (spec.Kind)
            {
                case SessionKind.Pipe:
                    session = new ProcessTerminalSession(spec.FileName, spec.Arguments, spec.WorkingDirectory);
                    break;
                case SessionKind.Pty:
                case SessionKind.Shell:
                    session = CreatePtySession(spec);
                    break;
                case SessionKind.Fake:
                    session = new FakePtyTerminalSession();
                    break;
                default:
                    return;
            }
        }
        catch (System.Exception ex)
        {
            _term.WriteLine($"Failed to start session: {ex.Message}");
            return;
        }

        _externalSession = session;
        _externalSpec = spec;
        _kind = spec.Kind;
        _userStopped = false;
        _term.AttachSession(session);
        if (spec.RawSession || spec.Kind == SessionKind.Shell)
            _term.InputMode = TerminalInputMode.RawSession;

        session.Exited += (_, _) =>
        {
            if (_userStopped) return;
            int? code = GetExitCode(session);
            string msg = code.HasValue
                ? $"[Session] Exited with code {code}."
                : "[Session] Exited.";
            _term.Write(msg + "\n");
            ResetToInMemory();
        };

        try
        {
            _ = session.StartAsync();
        }
        catch (System.Exception ex)
        {
            _term.WriteLine($"Failed to start session: {ex.Message}");
            ResetToInMemory();
        }
    }

    private static ITerminalSession CreatePtySession(SessionSpec spec)
    {
        if (PtyAvailability.IsConPtySupported)
        {
            var opts = new ConPtyTerminalSessionOptions
            {
                FileName = spec.FileName,
                Arguments = spec.Arguments,
                WorkingDirectory = string.IsNullOrEmpty(spec.WorkingDirectory) ? null : spec.WorkingDirectory,
            };
            return new ConPtyTerminalSession(opts);
        }
        if (PtyAvailability.IsPosixPtySupported)
        {
            var opts = new PosixPtyTerminalSessionOptions
            {
                FileName = spec.FileName,
                Arguments = spec.Arguments,
                WorkingDirectory = string.IsNullOrEmpty(spec.WorkingDirectory) ? null : spec.WorkingDirectory,
            };
            return new PosixPtyTerminalSession(opts);
        }
        throw new System.PlatformNotSupportedException("No PTY backend is available on this platform.");
    }

    /// <summary>Stop the active external session synchronously (best-effort).</summary>
    public void StopExternalSession()
    {
        if (_externalSession == null) return;
        _userStopped = true;
        try { _externalSession.StopAsync().GetAwaiter().GetResult(); }
        catch { }
        try { _externalSession.Dispose(); }
        catch { }
        ResetToInMemory();
    }

    /// <summary>Restart the last external session using its stored spec.</summary>
    public void RestartExternalSession()
    {
        var spec = _externalSpec;
        if (spec == null)
        {
            _term.WriteLine("[Session] Nothing to restart.");
            return;
        }
        _term.WriteLine($"[Session] Restarting {spec.FileName} {spec.Arguments}");
        StartSession(spec);
    }

    /// <summary>Send the platform EOF character into the active session input.</summary>
    public void SendEof()
    {
        var s = _externalSession;
        if (s == null || !s.IsRunning) return;
        // Ctrl+Z on Windows, Ctrl+D on Unix shells.
        string eof = System.OperatingSystem.IsWindows() ? "" : "";
        _ = s.SendInputAsync(eof);
    }

    /// <summary>Interrupt (best-effort SIGINT/Ctrl+C) the active session.</summary>
    public void Interrupt()
    {
        var s = _externalSession;
        if (s == null || !s.IsRunning) return;
        if (s is IInterruptibleTerminalSession i)
            _ = i.InterruptAsync();
        else
            _ = s.SendInputAsync("");
    }

    private void ResetToInMemory()
    {
        _externalSession = null;
        _kind = SessionKind.InMemoryCommand;
        _term.InputMode = TerminalInputMode.Command;
        _term.AttachSession(_memSession);
    }

    private static int? GetExitCode(ITerminalSession session) => session switch
    {
        ProcessTerminalSession pts => pts.ExitCode,
        ConPtyTerminalSession cpts => cpts.ExitCode,
        PosixPtyTerminalSession ppyx => ppyx.ExitCode,
        _ => null
    };

    public override void Close()
    {
        StopExternalSession();
        try { _memSession.StopAsync().GetAwaiter().GetResult(); }
        catch { }
        try { _memSession.Dispose(); }
        catch { }
        base.Close();
    }

    // Black-on-light-gray palette (frame & interior) — matches the previous
    // TerminalDemoWindow look.
    public override byte MapColor(int index) => index switch
    {
        1 => 0x07,
        2 => 0x0F,
        3 => 0x07,
        4 => 0x08,
        5 => 0x07,
        6 => 0x0F,
        7 => 0x07,
        8 => 0x07,
        _ => base.MapColor(index),
    };
}
