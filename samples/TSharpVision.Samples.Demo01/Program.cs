#pragma warning disable CA1416 // Runtime OS guards in each method body make platform-specific calls safe.
using TSharpVision.Config;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Terminal;
using TSharpVision.Terminal.Posix;
using TSharpVision.Terminal.Windows;

namespace TSharpVision.Demo01;

public class TVDemo : TApplication
{
    public TVDemo()
        : base()
    {
        TWindow window = new TWindow(new TRect(1, 1, 60, 24), "TSharpVision Demo", 0);
        //{
        //    Options = Views.ofCentered | Views.ofPreProcess,
        //    HelpCtx = Views.hcNoContext
        //};
        DeskTop.Insert(window);
    }

    public override TStatusLine InitStatusLine(TRect r)
    {
        r.a.y = r.b.y - 1;

        return new TStatusLine(r,
            new TStatusDef(0, 0xFFFF) +
            new TStatusItem("~F1~ Help", Keys.kbF1, Views.cmHelp) +
            new TStatusItem("~F2~ Menu", Keys.kbF2, Views.cmMenu) +
            new TStatusItem("~F3~ View", Keys.kbF3, Views.cmMenu) +
            new TStatusItem("~F4~ Edit", Keys.kbF4, Views.cmMenu) +
            new TStatusItem("~F5~ Copy", Keys.kbF5, Views.cmMenu) +
            new TStatusItem("~F6~ Move", Keys.kbF6, Views.cmMenu) +
            new TStatusItem("~F7~ New Folder", Keys.kbF7, Views.cmMenu) +
            new TStatusItem("~F8~ Delete", Keys.kbF8, Views.cmMenu) +
            new TStatusItem("~Alt-X~ Exit", Keys.kbAltX, Views.cmQuit) +
            new TStatusItem(null, Keys.kbF10, Views.cmMenu) +
            new TStatusItem(null, Keys.kbAltF3, Views.cmClose) +
            new TStatusItem(null, Keys.kbF5, Views.cmZoom) +
            new TStatusItem(null, Keys.kbCtrlF5, Views.cmResize)
            );
    }

    public static class MyCommands 
    {
        public static ushort cmMyFileOpen = 1000;
        public static ushort cmMyNewWin = 1001;
        public static ushort cmDemoTerminal = 1002;
        public static ushort cmAbout = 1002;
    }

    public override TMenuBar InitMenuBar(TRect r)
    {
        /*
    r.b.y = r.a.y + 1;    // set bottom line 1 line below top line
    return new TMenuBar( r,
        *new TSubMenu( "~F~ile", kbAltF )+
            *new TMenuItem( "~O~pen", cmMyFileOpen, kbF3, hcNoContext, "F3" )+
            *new TMenuItem( "~N~ew",  cmMyNewWin,   kbF4, hcNoContext, "F4" )+
            newLine()+
            *new TMenuItem( "E~x~it", cmQuit, cmQuit, hcNoContext, "Alt-X" )+
        *new TSubMenu( "~W~indow", kbAltW )+
            *new TMenuItem( "~N~ext", cmNext,     kbF6, hcNoContext, "F6" )+
            *new TMenuItem( "~Z~oom", cmZoom,     kbF5, hcNoContext, "F5" )
        );         
         */

        r.b.y = r.a.y + 1;

        // \360
        //TSubMenu sub1 = new TSubMenu("~≡~", 0, 0 /*Views.hcSystem*/) +
        //    new TMenuItem("~A~bout...", Views.cmCancel, Keys.kbNoKey, 0 /*hcSAbout*/);

        //return (new TMenuBar(r, sub1));

        //return new TMenuBar(r,
        //    new TSubMenu("~≡~", Keys.kbAltF) +
        //        new TMenuItem("~O~pen", MyCommands.cmMyFileOpen, Keys.kbF3, Views.hcNoContext, "F3") +
        //        new TMenuItem("~N~ew", MyCommands.cmMyNewWin, Keys.kbF4, Views.hcNoContext, "F4") +
        //        //TMenuItem.NewLine() +
        //        new TMenuItem("E~x~it", Views.cmQuit, Views.cmQuit, Views.hcNoContext, "Alt-X")
        //    );

        return new TMenuBar(r,
            new TSubMenu("~≡~", Keys.kbAltF) +
                new TMenuItem("~T~erminal", MyCommands.cmDemoTerminal, Keys.kbNoKey, Views.hcNoContext) +
                TMenuItem.NewLine() +
                new TMenuItem("~A~bout", MyCommands.cmAbout, Keys.kbNoKey, Views.hcNoContext) +
            new TSubMenu("~F~ile", Keys.kbAltF) +
                new TMenuItem("~O~pen", MyCommands.cmMyFileOpen, Keys.kbF3, Views.hcNoContext, "F3") +
                new TMenuItem("~N~ew", MyCommands.cmMyNewWin, Keys.kbF4, Views.hcNoContext, "F4") +
                TMenuItem.NewLine() +
                new TMenuItem("E~x~it", Views.cmQuit, Views.cmQuit, Views.hcNoContext, "Alt-X") +
            new TSubMenu("~E~dit", Keys.kbAltF) +
            new TSubMenu("~S~earch", Keys.kbAltF) +
            new TSubMenu("~R~un", Keys.kbAltF) +
            new TSubMenu("~C~ompile", Keys.kbAltF) +
            new TSubMenu("~D~ebug", Keys.kbAltF) +
            new TSubMenu("~P~roject", Keys.kbAltF) +
            new TSubMenu("~O~ptions", Keys.kbAltF) +
            new TSubMenu("~W~indow", Keys.kbAltW) +
                new TMenuItem("~N~ext", Views.cmNext, Keys.kbF6, Views.hcNoContext, "F6") +
                new TMenuItem("~Z~oom", Views.cmZoom, Keys.kbF5, Views.hcNoContext, "F5") +
            new TSubMenu("~H~elp", Keys.kbAltW)
            );
    }

    public override TDeskTop InitDesktop(TRect r)
    {
        return base.InitDesktop(r);
    }

    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);
        if (ev.What == Events.evCommand && ev.message.command == MyCommands.cmDemoTerminal)
        {
            OpenTerminalDemo();
            ClearEvent(ref ev);
        }
    }

    private void OpenTerminalDemo()
    {
        // Window: 70 cols wide, 20 rows tall → interior 68×18 + 1 scrollbar col
        var win = new TerminalDemoWindow(new TRect(5, 2, 75, 22), "Terminal Demo", Views.wnNoNumber);
        win.flags |= Views.wfClose | Views.wfMove;

        // Scrollbar occupies the rightmost interior column (col 69, rows 1-18)
        var vBar = new TScrollBar(new TRect(69, 1, 70, 19));
        win.Insert(vBar);

        // Terminal fills the rest of the interior (cols 1-68, rows 1-18)
        var term = new TTerminal(new TRect(1, 1, 69, 19));
        win.Insert(term);

        term.AttachVerticalScrollBar(vBar);

        var memSession = new InMemoryTerminalSession();
        term.AttachSession(memSession);
        memSession.StartAsync().GetAwaiter().GetResult();

        term.InputEnabled = true;
        term.Prompt = "> ";

        // Tracks the currently active external session and its type.
        ITerminalSession?[] sessionHolder = { null };
        string[] sessionTypeHolder = { "" };  // "pipe" | "pty" | "shell" | ""
        bool[] userStoppedHolder = { false };

        term.CommandSubmitted += (_, e) =>
            HandleDemoCommand(term, memSession, e.Command, sessionHolder, sessionTypeHolder, userStoppedHolder);

        memSession.Emit("TSharpVision TTerminal \u2014 interactive demo\n");
        memSession.Emit("------------------------------------------\n");
        memSession.Emit("Type a command and press Enter.\n");
        memSession.Emit("Commands: help, echo <text>, ansi, size, selection-demo,\n");
        memSession.Emit("          run-demo, run-pty-demo, shell, stop-demo, cls, clear\n");
        memSession.Emit("Scroll: PgUp/PgDn, Up/Down (history), mouse wheel, or drag the scrollbar.\n");
        memSession.Emit("Ctrl+C: copies selection if any; otherwise interrupts a running process.\n");
        memSession.Emit("Select: drag with left mouse button. Ctrl+V pastes. Esc clears selection.\n");
        memSession.Emit("\n");

        DeskTop.Insert(win);
    }

    private static void HandleDemoCommand(
        TTerminal term,
        InMemoryTerminalSession memSession,
        string command,
        ITerminalSession?[] sessionHolder,
        string[] sessionTypeHolder,
        bool[] userStoppedHolder)
    {
        string trimmed = command.Trim();

        // In shell mode only meta-commands are intercepted; everything else is forwarded
        // to the shell automatically by TTerminal via SendInputAsync.
        if (sessionTypeHolder[0] == "shell")
        {
            if (trimmed.Equals("stop-demo", StringComparison.OrdinalIgnoreCase))
                StopExternalSession(term, memSession, sessionHolder, sessionTypeHolder, userStoppedHolder);
            else if (trimmed.Equals("cls", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase))
                term.Clear();
            return;
        }

        if (trimmed.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
                    term.WriteLine("Available commands:");
            term.WriteLine("  help            show this help");
            term.WriteLine("  echo <text>     write text to the terminal");
            term.WriteLine("  ansi            show ANSI color demo");
            term.WriteLine("  size            print the current terminal size");
            term.WriteLine("  selection-demo  print lines for manual selection testing");
            term.WriteLine("  run-demo        run 'dotnet --info' via redirected pipes (ProcessTerminalSession)");
            term.WriteLine("  run-pty-demo    run a short command via real PTY/ConPTY");
            term.WriteLine("  shell           start an interactive PTY shell in raw session mode");
            term.WriteLine("  stop-demo       stop the active external session (non-shell)");
            term.WriteLine("  cls | clear     clear the terminal output");
            term.WriteLine("Input modes:");
            term.WriteLine("  Normal demo commands use Command mode (local prompt, history, Enter submits).");
            term.WriteLine("  'shell' switches to Raw Session mode: keystrokes go directly to the shell.");
            term.WriteLine("  In the shell, type 'exit' to return to demo command mode.");
            term.WriteLine("  In the shell, up/down arrows are forwarded to the shell for its own history.");
            term.WriteLine("  stop-demo can stop a non-shell PTY session from Command mode.");
            term.WriteLine("Ctrl+C: copies selection if text is selected; otherwise interrupts a running process.");
            term.WriteLine("Ctrl+V pastes clipboard text. Esc clears selection.");
        }
        else if (trimmed.Equals("size", StringComparison.OrdinalIgnoreCase))
        {
            var ts = term.TerminalSize;
            term.WriteLine($"Terminal size: {ts.Columns} columns x {ts.Rows} rows");
        }
        else if (trimmed.Equals("cls", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            term.Clear();
        }
        else if (trimmed.Equals("ansi", StringComparison.OrdinalIgnoreCase))
        {
            term.Write("Normal text\n");
            term.Write("\x1B[31mRed text\x1B[0m\n");
            term.Write("\x1B[32mGreen text\x1B[0m\n");
            term.Write("\x1B[33mBrown/Yellow text\x1B[0m\n");
            term.Write("\x1B[93mBright yellow text\x1B[0m\n");
            term.Write("\x1B[1;36mBold cyan text\x1B[0m\n");
            term.Write("\x1B[37;41m White on red \x1B[0m\n");
            term.Write("Back to \x1B[0mwhite on black\n");
        }
        else if (trimmed.Equals("selection-demo", StringComparison.OrdinalIgnoreCase))
        {
            RunSelectionDemo(term);
        }
        else if (trimmed.Equals("run-demo", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.Equals("run-pipe-demo", StringComparison.OrdinalIgnoreCase))
        {
            RunPipeDemo(term, memSession, sessionHolder, sessionTypeHolder, userStoppedHolder);
        }
        else if (trimmed.Equals("run-pty-demo", StringComparison.OrdinalIgnoreCase))
        {
            RunPtyDemo(term, memSession, sessionHolder, sessionTypeHolder, userStoppedHolder);
        }
        else if (trimmed.Equals("shell", StringComparison.OrdinalIgnoreCase))
        {
            StartShell(term, memSession, sessionHolder, sessionTypeHolder, userStoppedHolder);
        }
        else if (trimmed.Equals("stop-demo", StringComparison.OrdinalIgnoreCase))
        {
            StopExternalSession(term, memSession, sessionHolder, sessionTypeHolder, userStoppedHolder);
        }
        else if (trimmed.StartsWith("echo ", StringComparison.OrdinalIgnoreCase))
        {
            term.WriteLine(trimmed.Substring(5));
        }
        else if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            term.WriteLine("Exit is not connected in this demo.");
        }
        else if (trimmed.Length > 0)
        {
            term.WriteLine($"Unknown command: {trimmed}");
        }
    }

    private static void RunSelectionDemo(TTerminal term)
    {
        term.WriteLine("--- Selection demo ---");
        term.WriteLine("Drag the mouse over any text to select it.");
        term.WriteLine("Press Ctrl+C to copy the selection to the clipboard.");
        term.WriteLine("Press Ctrl+V to paste clipboard text into the input line.");
        term.WriteLine("Press Escape to clear the selection.");
        term.WriteLine("Row A: The quick brown fox jumps over the lazy dog");
        term.WriteLine("Row B: 0123456789 abcdefghijklmnopqrstuvwxyz");
        term.WriteLine("Row C: TSharpVision TTerminal selection test line");
        term.WriteLine("--- end of selection demo ---");
    }

    private static void RunPipeDemo(
        TTerminal term,
        InMemoryTerminalSession memSession,
        ITerminalSession?[] sessionHolder,
        string[] sessionTypeHolder,
        bool[] userStoppedHolder)
    {
        if (sessionHolder[0] is { IsRunning: true })
        {
            term.WriteLine("A session is already running. Use stop-demo first.");
            return;
        }

        sessionHolder[0]?.Dispose();
        sessionHolder[0] = null;
        sessionTypeHolder[0] = "";
        userStoppedHolder[0] = false;

        term.WriteLine("[Session: pipe] Starting redirected-pipe demo...");
        term.WriteLine("Running: dotnet --info");
        term.WriteLine("(output via ProcessTerminalSession — stdin is forwarded but ignored by dotnet --info)");

        try
        {
            var session = new ProcessTerminalSession("dotnet", "--info");
            sessionHolder[0] = session;
            sessionTypeHolder[0] = "pipe";
            term.AttachSession(session);

            session.Exited += (_, _) =>
            {
                if (userStoppedHolder[0]) return;
                string exitMsg = session.ExitCode.HasValue
                    ? $"[Session: pipe] Process exited with code {session.ExitCode}."
                    : "[Session: pipe] Process exited.";
                term.Write(exitMsg + "\n");
                sessionHolder[0] = null;
                sessionTypeHolder[0] = "";
                term.AttachSession(memSession);
                session.Dispose();
            };

            _ = session.StartAsync();
        }
        catch (Exception ex)
        {
            term.WriteLine($"Failed to start 'dotnet --info': {ex.Message}");
            term.WriteLine("Ensure the 'dotnet' SDK is on your PATH.");
            sessionHolder[0]?.Dispose();
            sessionHolder[0] = null;
            sessionTypeHolder[0] = "";
            term.AttachSession(memSession);
        }
    }

    private static void RunPtyDemo(
        TTerminal term,
        InMemoryTerminalSession memSession,
        ITerminalSession?[] sessionHolder,
        string[] sessionTypeHolder,
        bool[] userStoppedHolder)
    {
        if (!PtyAvailability.IsAnyPtySupported)
        {
            term.WriteLine("PTY is not available on this platform.");
            term.WriteLine("Use run-demo for redirected-pipe output.");
            return;
        }

        if (sessionHolder[0] is { IsRunning: true })
        {
            term.WriteLine("A session is already running. Use stop-demo first.");
            return;
        }

        sessionHolder[0]?.Dispose();
        sessionHolder[0] = null;
        sessionTypeHolder[0] = "";
        userStoppedHolder[0] = false;

        term.WriteLine("[Session: pty] Starting PTY demo...");

        ITerminalSession session;
        if (PtyAvailability.IsConPtySupported)
        {
            const string fileName = "cmd.exe";
            const string arguments = "/c echo hello from ConPTY";
            term.WriteLine($"Running: {fileName} {arguments}");
            session = new ConPtyTerminalSession(fileName, arguments, term.TerminalSize);
        }
        else
        {
            const string fileName = "sh";
            const string arguments = "-c \"echo hello from POSIX PTY\"";
            term.WriteLine($"Running: {fileName} {arguments}");
            session = new PosixPtyTerminalSession(fileName, arguments, term.TerminalSize);
        }

        sessionHolder[0] = session;
        sessionTypeHolder[0] = "pty";
        term.AttachSession(session);

        session.Exited += (_, _) =>
        {
            if (userStoppedHolder[0]) return;
            int? exitCode = GetSessionExitCode(session);
            string exitMsg = exitCode.HasValue
                ? $"[Session: pty] Process exited with code {exitCode}."
                : "[Session: pty] Process exited.";
            term.Write(exitMsg + "\n");
            sessionHolder[0] = null;
            sessionTypeHolder[0] = "";
            term.AttachSession(memSession);
            session.Dispose();
        };

        try
        {
            _ = session.StartAsync();
        }
        catch (Exception ex)
        {
            term.WriteLine($"Failed to start PTY session: {ex.Message}");
            sessionHolder[0]?.Dispose();
            sessionHolder[0] = null;
            sessionTypeHolder[0] = "";
            term.AttachSession(memSession);
        }
    }

    private static void StartShell(
        TTerminal term,
        InMemoryTerminalSession memSession,
        ITerminalSession?[] sessionHolder,
        string[] sessionTypeHolder,
        bool[] userStoppedHolder)
    {
        if (!PtyAvailability.IsAnyPtySupported)
        {
            term.WriteLine("Interactive PTY shell is not available on this platform.");
            term.WriteLine("Use run-demo for redirected-pipe output.");
            return;
        }

        if (sessionHolder[0] is { IsRunning: true })
        {
            term.WriteLine("A session is already running. Use stop-demo first.");
            return;
        }

        sessionHolder[0]?.Dispose();
        sessionHolder[0] = null;
        sessionTypeHolder[0] = "";
        userStoppedHolder[0] = false;

        term.WriteLine("[Session: pty] Starting interactive shell in raw session mode...");
        term.WriteLine("Type shell commands directly. Type 'exit' in the shell to return to demo command mode.");

        ITerminalSession session;
        if (PtyAvailability.IsConPtySupported)
        {
            const string shellExe = "cmd.exe";
            term.WriteLine($"Shell: {shellExe}");
            session = new ConPtyTerminalSession(shellExe, string.Empty, term.TerminalSize);
        }
        else
        {
            string shellExe = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh";
            term.WriteLine($"Shell: {shellExe}");
            session = new PosixPtyTerminalSession(shellExe, string.Empty, term.TerminalSize);
        }

        sessionHolder[0] = session;
        sessionTypeHolder[0] = "shell";
        term.AttachSession(session);
        term.InputMode = TerminalInputMode.RawSession;

        session.Exited += (_, _) =>
        {
            if (userStoppedHolder[0]) return;
            int? exitCode = GetSessionExitCode(session);
            string exitMsg = exitCode.HasValue
                ? $"[Session: pty] Shell exited with code {exitCode}."
                : "[Session: pty] Shell exited.";
            term.Write(exitMsg + "\n");
            sessionHolder[0] = null;
            sessionTypeHolder[0] = "";
            term.InputMode = TerminalInputMode.Command;
            term.AttachSession(memSession);
            term.Write("[Demo command mode active. Type 'help' for available commands.]\n");
            session.Dispose();
        };

        try
        {
            _ = session.StartAsync();
        }
        catch (Exception ex)
        {
            term.WriteLine($"Failed to start shell: {ex.Message}");
            sessionHolder[0]?.Dispose();
            sessionHolder[0] = null;
            sessionTypeHolder[0] = "";
            term.InputMode = TerminalInputMode.Command;
            term.AttachSession(memSession);
        }
    }

    private static void StopExternalSession(
        TTerminal term,
        InMemoryTerminalSession memSession,
        ITerminalSession?[] sessionHolder,
        string[] sessionTypeHolder,
        bool[] userStoppedHolder)
    {
        var session = sessionHolder[0];
        if (session == null || !session.IsRunning)
        {
            term.WriteLine("No external session is running.");
            return;
        }

        userStoppedHolder[0] = true;
        term.WriteLine("Stopping session...");
        _ = StopExternalSessionAsync(term, memSession, session, sessionHolder, sessionTypeHolder);
    }

    private static async Task StopExternalSessionAsync(
        TTerminal term,
        InMemoryTerminalSession memSession,
        ITerminalSession session,
        ITerminalSession?[] sessionHolder,
        string[] sessionTypeHolder)
    {
        try
        {
            await session.StopAsync().ConfigureAwait(false);
            term.Write("Session stopped.\n");
        }
        catch (Exception ex)
        {
            term.Write($"Error stopping session: {ex.Message}\n");
        }
        finally
        {
            sessionHolder[0] = null;
            sessionTypeHolder[0] = "";
            term.InputMode = TerminalInputMode.Command;
            term.AttachSession(memSession);
            term.Write("[Demo command mode active. Type 'help' for available commands.]\n");
            session.Dispose();
        }
    }

    private static int? GetSessionExitCode(ITerminalSession session) => session switch
    {
        ProcessTerminalSession pts => pts.ExitCode,
        ConPtyTerminalSession cpts => cpts.ExitCode,
        PosixPtyTerminalSession ppyx => ppyx.ExitCode,
        _ => null
    };

    //    static TStatusLine* initStatusLine(TRect r);
    //    static TMenuBar* initMenuBar(TRect r);
    //    virtual void handleEvent(TEvent& Event);
    //    virtual void getEvent(TEvent& event);
    ////    virtual TPalette& getPalette() const;
    //    virtual void idle();              // Updates heap and clock views
}

/// <summary>
/// TWindow subclass that renders the frame and desktop area in black/dark
/// colours instead of the default blue palette. Only used for the terminal
/// demo window.
/// </summary>
internal sealed class TerminalDemoWindow : TWindow
{
    public TerminalDemoWindow(TRect bounds, string title, ushort num)
        : base(bounds, title, num) { }

    // TWindow palette indices 1-8 map to the cpBlueWindow range.
    // We override MapColor to return raw VGA attributes so the frame and
    // interior are drawn in black rather than blue.
    public override byte MapColor(int index) => index switch
    {
        1 => 0x07, // frame passive       (light gray on black)
        2 => 0x0F, // frame active        (bright white on black)
        3 => 0x07, // frame icon          (light gray on black)
        4 => 0x08, // scroll-bar page     (dark gray on black)
        5 => 0x07, // scroll-bar reserved (light gray on black)
        6 => 0x0F, // title text active   (bright white on black)
        7 => 0x07, // title text passive  (light gray on black)
        8 => 0x07, // interior            (light gray on black)
        _ => base.MapColor(index),
    };
}

internal class Program
{
    static void Main(string[] args)
    {
        // StreamableRegistration ensures all streamable types are available if
        // any stream/resource code path is exercised (defensive, same pattern as Demo01).
        StreamableRegistration.RegisterAll();

        // Load configuration before the driver is initialized.
        var config = TSharpVisionConfigurationLoader.Load();
        ScreenDriverFactory.ConfiguredDriverName = config.DriverName;
        ScreenDriverFactory.ConfiguredSdlFontName = config.SdlFontName;

        TVDemo demo = new TVDemo();
        demo.Run();
    }
}
