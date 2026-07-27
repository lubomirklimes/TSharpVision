using TSharpVision;
using TSharpVision.Constants;
using System.IO;

namespace TSharpVision.Samples.TVDemo;

// ---------------------------------------------------------------------------
// TVDemo command constants.
// Sample commands use the application range.
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
// Routes accessory commands and ticks live views. Showcase menus, controls,
// games, help and persistence are organized in separate partial-class files.
// ---------------------------------------------------------------------------
public partial class TVDemoApp : TApplication
{
    // Window-stagger origin for new modeless windows.
    private int _cascade = 0;

    private ushort _nextWinNum = 1;

    public TVDemoApp() : base()
    {
        OpenWelcomeWindow();
        LoadPalette();
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
            new TStatusItem(null, Keys.kbF6,    Views.cmNext) +
            new TStatusItem(null, Keys.kbShiftF6, Views.cmPrev) +
            new TStatusItem(null, Keys.kbCtrlF5, Views.cmResize) +
            new TStatusItem("~F1~ Help", Keys.kbF1, Views.cmHelp)
        );
    }

    // -----------------------------------------------------------------------
    // HandleEvent — dispatch TVDemo commands
    // -----------------------------------------------------------------------
    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);

        if (ev.What != Events.evCommand) return;

        if (HandleShowcaseCommand(ev.message.command)) { ClearEvent(ref ev); return; }

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
        // Every open instance must keep updating, not just the newest one.
        DeskTop.ForEachView(view =>
        {
            if (view is ClockDialog clock) clock.Tick();
            if (view is HeapDialog heap) heap.Tick();
        });
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
        var clock = new ClockDialog(x, y);
        var valid = ValidView(clock);
        if (valid != null)
            DeskTop!.Insert(valid);
    }

    // -----------------------------------------------------------------------
    // OpenHeap — opens a modeless managed-memory gadget on the desktop.
    // -----------------------------------------------------------------------
    private void OpenHeap()
    {
        int x = 45 + (_cascade % 2);
        int y = 10 + (_cascade % 2);
        _cascade++;
        var heap = new HeapDialog(x, y);
        var valid = ValidView(heap);
        if (valid != null)
            DeskTop!.Insert(valid);
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


