#pragma warning disable CA1416 // PtyAvailability guards each platform-specific session at runtime.
using TSharpVision.Constants;
using TSharpVision.Terminal;
using TSharpVision.Terminal.Posix;
using TSharpVision.Terminal.Windows;

namespace TSharpVision.Samples.TVDemo;

public partial class TVDemoApp
{
    private void OpenTerminalDemo()
    {
        if (DeskTop == null) return;
        InsertDemoWindow(new DemoTerminalWindow(DemoBounds(72, 18), _nextWinNum++));
    }
}

internal sealed class DemoTerminalWindow : TWindow
{
    // Physical attributes: inactive gray, active white, cyan controls/emphasis,
    // all on black. Resolve locally so the application's blue mapping is skipped.
    private static readonly TPalette TerminalPalette = new("\x07\x0F\x0B\x07\x0F\x0B\x07\x0F", 8);

    public override TPalette GetPalette() => TerminalPalette;

    public override byte MapColor(int index) => index > 0 && index <= TerminalPalette.Size
        ? TerminalPalette[index] : errorAttr;

    private readonly TTerminal terminal;
    private readonly ITerminalSession session;
    private bool stopped;

    public DemoTerminalWindow(TRect bounds, ushort number) : base(bounds, "Terminal", number)
    {
        int w = size.x, h = size.y;
        var scroll = new TScrollBar(new TRect(w - 1, 1, w, h - 1));
        Insert(scroll);
        terminal = new TTerminal(new TRect(1, 1, w - 1, h - 1));
        terminal.growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
        Insert(terminal);
        terminal.AttachVerticalScrollBar(scroll);

        if (PtyAvailability.IsAnyPtySupported)
        {
            string shell = OperatingSystem.IsWindows()
                ? (Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
                : (Environment.GetEnvironmentVariable("SHELL") ?? "/bin/sh");
            session = OperatingSystem.IsWindows() && PtyAvailability.IsConPtySupported
                ? new ConPtyTerminalSession(new ConPtyTerminalSessionOptions { FileName = shell })
                : new PosixPtyTerminalSession(new PosixPtyTerminalSessionOptions
                {
                    FileName = shell,
                    Environment = new Dictionary<string, string?> { ["TERM"] = TTerminal.TermName },
                });
            terminal.InputMode = TerminalInputMode.RawSession;
        }
        else
        {
            session = new InMemoryTerminalSession();
            terminal.NewLineMode = true;   // scripted text uses bare "\n"; a PTY translates it itself
            terminal.InputMode = TerminalInputMode.Command;
            terminal.Prompt = "> ";
            terminal.CommandSubmitted += (_, e) => terminal.WriteLine($"Entered: {e.Command}");
        }

        terminal.AttachSession(session);
        try
        {
            _ = session.StartAsync();
            if (session is InMemoryTerminalSession memory)
                memory.Emit("PTY unavailable; type a command to see session input.\n");
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"Session failed: {ex.Message}");
        }
        SelectNext(false);
    }

    public override void ShutDown()
    {
        if (!stopped)
        {
            stopped = true;
            terminal.DetachSession();
            try { session.StopAsync().GetAwaiter().GetResult(); }
            catch { /* A session may already have exited. */ }
            session.Dispose();
        }
        base.ShutDown();
    }
}
