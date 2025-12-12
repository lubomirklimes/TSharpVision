using TSharpVision;
using TSharpVision.Constants;
using System.IO;

namespace TSharpVision.Samples.TVDemo;

// ---------------------------------------------------------------------------
// TVDemo command constants.
// Use range 300+ to avoid collisions with library (1-199) and Demo01 (200-210).
// ---------------------------------------------------------------------------
public static class TVDemoCmd
{
    public const ushort cmAsciiTable  = 300;
    public const ushort cmCalculator  = 301;
    public const ushort cmAbout       = 302;
    public const ushort cmCalendar    = 303;
    public const ushort cmPuzzle      = 304;
    public const ushort cmFileViewer  = 305;
    public const ushort cmMouseDlg    = 306;
    public const ushort cmClock       = 307;
    public const ushort cmHeap        = 308;
}

// ---------------------------------------------------------------------------
// TVDemoApp — the TVDemo sample application.
// Provides a File / Demo / Window / Help menu, standard status line,
// empty desktop background, and routes all Demo commands.
// ---------------------------------------------------------------------------
public class TVDemoApp : TApplication
{
    // Window-stagger origin for new modeless windows.
    private int _cascade = 0;

    // Live gadget references used by Idle() for periodic tick.
    // Set when opened, cleared when the dialogs are no longer needed.
    private ClockDialog? _clockDialog;
    private HeapDialog?  _heapDialog;

    public TVDemoApp() : base()
    {
    }

    // -----------------------------------------------------------------------
    // InitMenuBar
    // -----------------------------------------------------------------------
    public override TMenuBar InitMenuBar(TRect r)
    {
        r.b.y = r.a.y + 1;
        return new TMenuBar(r,
            new TSubMenu("~F~ile", Keys.kbAltF) +
                new TMenuItem("E~x~it", Views.cmQuit, Keys.kbAltX, Views.hcNoContext, "Alt-X") +
            new TSubMenu("~D~emo", Keys.kbAltD) +
                new TMenuItem("~A~SCII Table",  TVDemoCmd.cmAsciiTable, Keys.kbNoKey) +
                new TMenuItem("~C~alculator",   TVDemoCmd.cmCalculator,  Keys.kbNoKey) +
                new TMenuItem("Ca~l~endar",      TVDemoCmd.cmCalendar,    Keys.kbNoKey) +
                new TMenuItem("~P~uzzle",        TVDemoCmd.cmPuzzle,      Keys.kbNoKey) +
                new TMenuItem("~F~ile Viewer",   TVDemoCmd.cmFileViewer,  Keys.kbNoKey) +
                new TMenuItem("~M~ouse Dialog",   TVDemoCmd.cmMouseDlg,    Keys.kbNoKey) +
                TMenuItem.NewLine() +
                new TMenuItem("C~l~ock",           TVDemoCmd.cmClock,       Keys.kbNoKey) +
                new TMenuItem("~H~eap / Memory",  TVDemoCmd.cmHeap,        Keys.kbNoKey) +
            new TSubMenu("~W~indow", Keys.kbAltW) +
                new TMenuItem("~N~ext",     Views.cmNext,    Keys.kbF6, Views.hcNoContext, "F6") +
                new TMenuItem("~P~revious", Views.cmPrev,    Keys.kbF5, Views.hcNoContext, "F5") +
                TMenuItem.NewLine() +
                new TMenuItem("~C~ascade",  Views.cmCascade, Keys.kbNoKey) +
                new TMenuItem("~T~ile",     Views.cmTile,    Keys.kbNoKey) +
                TMenuItem.NewLine() +
                new TMenuItem("~C~lose",    Views.cmClose,   Keys.kbAltF3) +
                new TMenuItem("~Z~oom",     Views.cmZoom,    Keys.kbF5) +
            new TSubMenu("~H~elp", Keys.kbAltH) +
                new TMenuItem("~A~bout...", TVDemoCmd.cmAbout, Keys.kbNoKey)
        );
    }

    // -----------------------------------------------------------------------
    // InitStatusLine
    // -----------------------------------------------------------------------
    public override TStatusLine InitStatusLine(TRect r)
    {
        r.a.y = r.b.y - 1;
        return new TStatusLine(r,
            new TStatusDef(0, 0xFFFF) +
            new TStatusItem("~F10~ Menu",   Keys.kbF10,  Views.cmMenu) +
            new TStatusItem("~Alt+X~ Exit", Keys.kbAltX, Views.cmQuit) +
            new TStatusItem(null, Keys.kbAltF3, Views.cmClose) +
            new TStatusItem(null, Keys.kbF5,    Views.cmZoom) +
            new TStatusItem(null, Keys.kbF6,    Views.cmNext)
        );
    }

    // -----------------------------------------------------------------------
    // HandleEvent — dispatch TVDemo commands
    // -----------------------------------------------------------------------
    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);

        if (ev.What != Events.evCommand) return;

        switch (ev.message.command)
        {
            case TVDemoCmd.cmAsciiTable:
                OpenAsciiTable();
                ClearEvent(ref ev);
                break;

            case TVDemoCmd.cmCalculator:
                OpenCalculator();
                ClearEvent(ref ev);
                break;

            case TVDemoCmd.cmAbout:
                ShowAbout();
                ClearEvent(ref ev);
                break;

            case TVDemoCmd.cmCalendar:
                OpenCalendar();
                ClearEvent(ref ev);
                break;

            case TVDemoCmd.cmPuzzle:
                OpenPuzzle();
                ClearEvent(ref ev);
                break;

            case TVDemoCmd.cmFileViewer:
                OpenFileViewer();
                ClearEvent(ref ev);
                break;

            case TVDemoCmd.cmMouseDlg:
                OpenMouseDlg();
                ClearEvent(ref ev);
                break;

            case TVDemoCmd.cmClock:
                OpenClock();
                ClearEvent(ref ev);
                break;

            case TVDemoCmd.cmHeap:
                OpenHeap();
                ClearEvent(ref ev);
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Idle — tick live gadgets once per idle cycle.
    // -----------------------------------------------------------------------
    public override void Idle()
    {
        base.Idle();
        // Tick clock and heap if their dialogs are alive (still have an owner).
        if (_clockDialog?.owner != null) _clockDialog.Tick();
        if (_heapDialog?.owner  != null) _heapDialog.Tick();
    }

    // -----------------------------------------------------------------------
    // OpenAsciiTable — opens a modeless ASCII table dialog on the desktop.
    // Multiple instances are allowed (same as upstream tvdemo).
    // -----------------------------------------------------------------------
    private void OpenAsciiTable()
    {
        // Stagger successive dialogs slightly.
        int x = 5 + (_cascade % 4) * 2;
        int y = 1 + (_cascade % 3) * 2;
        _cascade++;

        var dlg = new AsciiTableDialog(x, y);
        var valid = ValidView(dlg);
        if (valid != null)
            DeskTop!.Insert(valid);
    }

    // -----------------------------------------------------------------------
    // OpenCalculator — opens a modeless calculator dialog on the desktop.
    // -----------------------------------------------------------------------
    private void OpenCalculator()
    {
        // Stagger successive calculators away from the ASCII table dialogs.
        int x = 25 + (_cascade % 4) * 2;
        int y = 3 + (_cascade % 3) * 2;
        _cascade++;

        var calc = new CalculatorDialog(x, y);
        var valid = ValidView(calc);
        if (valid != null)
            DeskTop!.Insert(valid);
    }

    // -----------------------------------------------------------------------
    // OpenCalendar — opens a modeless calendar dialog on the desktop.
    // -----------------------------------------------------------------------
    private void OpenCalendar()
    {
        int x = 5 + (_cascade % 4) * 2;
        int y = 1 + (_cascade % 3) * 2;
        _cascade++;
        var dlg = new CalendarDialog(x, y);
        var valid = ValidView(dlg);
        if (valid != null)
            DeskTop!.Insert(valid);
    }

    // -----------------------------------------------------------------------
    // OpenPuzzle — opens a modeless puzzle dialog on the desktop.
    // -----------------------------------------------------------------------
    private void OpenPuzzle()
    {
        int x = 30 + (_cascade % 4) * 2;
        int y = 3 + (_cascade % 3) * 2;
        _cascade++;
        var dlg = new PuzzleDialog(x, y);
        var valid = ValidView(dlg);
        if (valid != null)
            DeskTop!.Insert(valid);
    }

    // -----------------------------------------------------------------------
    // OpenFileViewer — shows a modal file-open dialog; if confirmed opens a
    // modeless FileViewerWindow on the desktop.
    // -----------------------------------------------------------------------
    private void OpenFileViewer()
    {
        var fd = new TFileDialog("*.*", "View File", "~F~ile name",
                                 FileDialogOptions.fdOpenButton, 0);
        if (ValidView(fd) == null) return;

        ushort result = DeskTop!.ExecView(fd);
        if (result == Views.cmOK)
        {
            fd.GetData(out string path);
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                var win = new FileViewerWindow(path);
                var wv  = ValidView(win);
                if (wv != null)
                    DeskTop!.Insert(wv);
            }
        }
    }

    // -----------------------------------------------------------------------
    // OpenMouseDlg — opens a modeless mouse-state dialog on the desktop.
    // -----------------------------------------------------------------------
    private void OpenMouseDlg()
    {
        int x = 4 + (_cascade % 4) * 2;
        int y = 3 + (_cascade % 3) * 2;
        _cascade++;
        var dlg = new MouseDialog(x, y);
        var valid = ValidView(dlg);
        if (valid != null)
            DeskTop!.Insert(valid);
    }

    // -----------------------------------------------------------------------
    // OpenClock — opens a modeless clock gadget on the desktop.
    // -----------------------------------------------------------------------
    private void OpenClock()
    {
        int x = 50 + (_cascade % 2);
        int y = 3  + (_cascade % 3) * 2;
        _cascade++;
        _clockDialog = new ClockDialog(x, y);
        var valid = ValidView(_clockDialog);
        if (valid != null)
            DeskTop!.Insert(valid);
        else
            _clockDialog = null;
    }

    // -----------------------------------------------------------------------
    // OpenHeap — opens a modeless managed-memory gadget on the desktop.
    // -----------------------------------------------------------------------
    private void OpenHeap()
    {
        int x = 45 + (_cascade % 2);
        int y = 10 + (_cascade % 2);
        _cascade++;
        _heapDialog = new HeapDialog(x, y);
        var valid = ValidView(_heapDialog);
        if (valid != null)
            DeskTop!.Insert(valid);
        else
            _heapDialog = null;
    }

    // -----------------------------------------------------------------------
    // ShowAbout — simple about message box.
    // -----------------------------------------------------------------------
    private void ShowAbout()
    {
        var dlg = new TDialog(new TRect(20, 7, 60, 17), "About TSharpVision TVDemo");
        var st = new TStaticText(new TRect(1, 2, 38, 5),
            "TSharpVision TVDemo\n" +
            "\n" +
            "A TSharpVision sample application.");
        var btn = new TButton(new TRect(14, 6, 26, 8), "~O~K", Views.cmOK, ButtonConstants.bfDefault);
        dlg.Insert(st);
        dlg.Insert(btn);
        var valid = ValidView(dlg);
        if (valid != null)
            DeskTop!.ExecView(valid);
    }
}
