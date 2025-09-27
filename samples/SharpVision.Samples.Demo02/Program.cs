// SharpVision.Demo01
//
// Run on Windows: dotnet run --project SharpVision.Samples.HelpDemo
// The Win32ConsoleDriver is auto-discovered from SharpVision.Console.dll
// in the output directory and selected by default on Windows.
//
// Force a specific driver: set SHARPVISION_DRIVER=Win32ConsoleDriver
// Headless fallback:       set SHARPVISION_DRIVER=NullDriver
using SharpVision;
using SharpVision.Config;
using SharpVision.Constants;
using SharpVision.Drivers;
using System.IO;

namespace SharpVision.Demo01;

// ---------------------------------------------------------------------------
// Demo-specific command constants (user range 100+, avoiding library range)
// ---------------------------------------------------------------------------
static class DemoCmd
{
    public const ushort cmNewWindow          = 200;
    public const ushort cmShowDialog         = 201;
    public const ushort cmAbout              = 202;
    public const ushort cmControlsShowcase   = 203;
    public const ushort cmColorOptions       = 204;   // Options / Colors...
    public const ushort cmResourceDialog    = 205;   // Help / Resource Dialog...
    public const ushort cmSaveDesktop       = 206;   // Window / Save Desktop State
    public const ushort cmLoadDesktop       = 207;   // Window / Load Desktop State
    public const ushort cmNewEditor         = 208;   // File / New Editor Window
    public const ushort cmHistoryDemo       = 209;   // Edit / History Demo...
    public const ushort cmASCII             = 210;   // Edit / History Demo...
    // cmOpen (100) and cmNext (7) / cmPrev (8) are standard library constants.
}

// ---------------------------------------------------------------------------
// DemoHelpCtx — help context IDs matching demohelp.txt topics
// ---------------------------------------------------------------------------
internal static class DemoHelpCtx
{
    public const ushort Nocontext         = 0;
    public const ushort Index             = 1;
    public const ushort MainMenu          = 2;
    public const ushort WelcomeWindow     = 3;
    public const ushort FileMenu          = 4;
    public const ushort WindowManagement  = 5;
    public const ushort ControlsShowcase  = 6;
    public const ushort ColorDialog       = 7;
    public const ushort ResourceDialog    = 8;
    public const ushort ResourceFiles     = 9;
    public const ushort HelpSystem        = 10;
    public const ushort About             = 11;
    public const ushort EditorPlaceholder = 12;
}

// ---------------------------------------------------------------------------
// TVDemoApp — the demo application
// ---------------------------------------------------------------------------
public class TVDemoApp : TApplication
{
    // Track the next window number for File / New Window.
    private ushort _nextWinNum = 1;

    // Cached help file and its underlying stream (kept open while the app runs)
    private THelpFile _helpFile;
    private Fpstream _helpStream;

    // Register THelpTopic and THelpIndex with the current Pstream registry.
    // Uses THelpFile.RegisterStreamableTypes() so it is safe even if
    // Pstream.DeInitTypes() was called earlier in the same process.
    private static void EnsureHelpTypes()
    {
        THelpFile.RegisterStreamableTypes();
    }

    // Open (and optionally compile) the help file.  On first call, if
    // demohelp.hlp is missing, we try to generate it via svhc.  After that
    // the file is cached for the lifetime of the application.
    private void EnsureHelpFile()
    {
        if (_helpFile != null) return;

        EnsureHelpTypes();

        string hlpPath = Path.Combine(AppContext.BaseDirectory, "Help", "demohelp.hlp");

        if (!File.Exists(hlpPath))
        {
            // Attempt to compile using svhc from the same output directory.
            string txtPath = Path.Combine(AppContext.BaseDirectory, "Help", "demohelp.txt");
            string svhcExe = Path.Combine(AppContext.BaseDirectory,
                System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows)
                ? "svhc.exe" : "svhc");

            if (File.Exists(svhcExe) && File.Exists(txtPath))
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(svhcExe, $"\"{txtPath}\" \"{hlpPath}\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow  = true
                    };
                    var proc = System.Diagnostics.Process.Start(psi);
                    proc?.WaitForExit();
                }
                catch { /* fall through — file won't exist, handled below */ }
            }
        }

        if (!File.Exists(hlpPath)) return;   // svhc not available / failed

        _helpStream = new Fpstream(hlpPath);
        _helpFile   = new THelpFile(_helpStream);
    }

    public override THelpFile GetHelpFile()
    {
        EnsureHelpFile();
        return _helpFile;
    }

    public TVDemoApp() : base()
    {
        // Open a welcome window at startup.
        OpenWelcomeWindow();

        // Enable all demo commands unconditionally at startup.
        EnableCommand(DemoCmd.cmNewWindow);
        EnableCommand(DemoCmd.cmShowDialog);
        EnableCommand(DemoCmd.cmAbout);
        EnableCommand(DemoCmd.cmControlsShowcase);
        EnableCommand(DemoCmd.cmColorOptions);
        EnableCommand(DemoCmd.cmResourceDialog);
        EnableCommand(DemoCmd.cmSaveDesktop);
        EnableCommand(DemoCmd.cmLoadDesktop);
        EnableCommand(DemoCmd.cmNewEditor);
        EnableCommand(DemoCmd.cmHistoryDemo);
        EnableCommand(Views.cmOpen);

        // Install the real editor-dialog dispatcher so that TFileEditor's
        // Save / SaveAs / close-prompt flow shows actual UI.
        InstallEditorDialog();
    }

    // -----------------------------------------------------------------------
    // InstallEditorDialog — wire TEditor.editorDialog to real modal dialogs.
    // Delegates to the shared TEditorDialogHelper.
    // -----------------------------------------------------------------------
    private void InstallEditorDialog()
    {
        TEditorDialogHelper.Install(DeskTop);
    }

    // -----------------------------------------------------------------------
    // InitMenuBar
    // -----------------------------------------------------------------------
    public override TMenuBar InitMenuBar(TRect r)
    {
        r.b.y = r.a.y + 1;
        var mb = new TMenuBar(r,
            //new TSubMenu($"~{SharpVisionGlyphs.SystemMenu}~", Keys.kbAltSpace) +
            //    new TMenuItem("ASCII", DemoCmd.cmASCII, Keys.kbAltSpace) +
            new TSubMenu("~F~ile", Keys.kbAltF) +
                new TMenuItem("~N~ew Window",       DemoCmd.cmNewWindow, Keys.kbF4,  Views.hcNoContext, "F4") +
                new TMenuItem("~N~ew Editor...",    DemoCmd.cmNewEditor, Keys.kbF3,  Views.hcNoContext, "F3") +
                new TMenuItem("~O~pen File...",     Views.cmOpen,        Keys.kbNoKey, Views.hcNoContext) +
                TMenuItem.NewLine() +
                new TMenuItem("~S~ave",             Views.cmSave,        Keys.kbCtrlS, Views.hcNoContext, "Ctrl+S") +
                new TMenuItem("S~a~ve As...",       Views.cmSaveAs,      Keys.kbCtrlW, Views.hcNoContext, "Ctrl+W") +
                TMenuItem.NewLine() +
                new TMenuItem("E~x~it",             Views.cmQuit,        Keys.kbAltX, Views.hcNoContext, "Alt-X") +
            new TSubMenu("~E~dit", Keys.kbAltE) +
                new TMenuItem("~F~ind...",        Views.cmFind,         Keys.kbCtrlF, Views.hcNoContext, "Ctrl+F") +
                new TMenuItem("~R~eplace...",     Views.cmReplace,      Keys.kbCtrlR, Views.hcNoContext, "Ctrl+R") +
                new TMenuItem("Search ~A~gain",   Views.cmSearchAgain,  Keys.kbNoKey) +
                TMenuItem.NewLine() +
                new TMenuItem("~S~how Dialog",          DemoCmd.cmShowDialog,       Keys.kbNoKey) +
                new TMenuItem("~C~ontrols Showcase...", DemoCmd.cmControlsShowcase, Keys.kbNoKey) +
                new TMenuItem("~H~istory Demo...",      DemoCmd.cmHistoryDemo,      Keys.kbNoKey) +
            new TSubMenu("~O~ptions", Keys.kbAltO) +
                new TMenuItem("~C~olors...", DemoCmd.cmColorOptions, Keys.kbNoKey) +
            new TSubMenu("~W~indow", Keys.kbAltW) +
                new TMenuItem("~N~ext",     Views.cmNext, Keys.kbF6, Views.hcNoContext, "F6") +
                new TMenuItem("~P~revious", Views.cmPrev, Keys.kbF5, Views.hcNoContext, "F5") +
                TMenuItem.NewLine() +
                new TMenuItem("~C~ascade",  Views.cmCascade, Keys.kbNoKey) +
                new TMenuItem("~T~ile",     Views.cmTile,    Keys.kbNoKey) +
                TMenuItem.NewLine() +
                new TMenuItem("~S~ave Desktop State", DemoCmd.cmSaveDesktop, Keys.kbNoKey) +
                new TMenuItem("~L~oad Desktop State", DemoCmd.cmLoadDesktop, Keys.kbNoKey) +
            new TSubMenu("~H~elp", Keys.kbAltH) +
                new TMenuItem("~I~ndex",              Views.cmHelpIndex,       Keys.kbNoKey) +
                TMenuItem.NewLine() +
                new TMenuItem("~A~bout...", DemoCmd.cmAbout, Keys.kbNoKey) +
                TMenuItem.NewLine() +
                new TMenuItem("~R~esource Dialog...", DemoCmd.cmResourceDialog, Keys.kbNoKey)
        );
        mb.helpCtx = DemoHelpCtx.MainMenu;
        return mb;
    }

    // -----------------------------------------------------------------------
    // InitStatusLine
    // -----------------------------------------------------------------------
    public override TStatusLine InitStatusLine(TRect r)
    {
        r.a.y = r.b.y - 1;
        return new TStatusLine(r,
            new TStatusDef(0, 0xFFFF) +
            new TStatusItem("~F1~ Help",    Keys.kbF1,   Views.cmHelp) +
            new TStatusItem("~F10~ Menu",   Keys.kbF10,  Views.cmMenu) +
            new TStatusItem("~Alt+X~ Exit", Keys.kbAltX, Views.cmQuit) +
            new TStatusItem(null, Keys.kbAltF3,  Views.cmClose) +
            new TStatusItem(null, Keys.kbF5,     Views.cmZoom) +
            new TStatusItem(null, Keys.kbCtrlF5, Views.cmResize) +
            new TStatusItem(null, Keys.kbF6,     Views.cmNext) +
            new TStatusItem(null, Keys.kbCtrlS,  Views.cmSave) +
            new TStatusItem(null, Keys.kbCtrlW,  Views.cmSaveAs)
        );
    }

    // -----------------------------------------------------------------------
    // HandleEvent — dispatch demo commands
    // -----------------------------------------------------------------------
    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);

        if (ev.What != Events.evCommand) return;

        if (InputTrace.Enabled)
            InputTrace.LogEvent("Stage10-Demo01.HandleEvent(evCommand)", ev);

        switch (ev.message.command)
        {
            case DemoCmd.cmNewWindow:
                OpenNewWindow();
                ClearEvent(ref ev);
                break;

            case DemoCmd.cmNewEditor:
                OpenEditorWindow();
                ClearEvent(ref ev);
                break;

            case Views.cmOpen:
                OpenFileDialog();
                ClearEvent(ref ev);
                break;

            case DemoCmd.cmShowDialog:
                ShowSampleDialog();
                ClearEvent(ref ev);
                break;

            case DemoCmd.cmControlsShowcase:
                OpenControlsShowcaseDialog();
                ClearEvent(ref ev);
                break;

            case DemoCmd.cmHistoryDemo:
                OpenHistoryDemoDialog();
                ClearEvent(ref ev);
                break;

            case DemoCmd.cmAbout:
                ShowAbout();
                ClearEvent(ref ev);
                break;

            case Views.cmHelpIndex:
                {
                    var hf = GetHelpFile();
                    if (hf != null)
                    {
                        var win = new THelpWindow(hf, DemoHelpCtx.Index);
                        if (ValidView(win) != null)
                            ExecuteHelp(win);
                    }
                    ClearEvent(ref ev);
                    break;
                }

            case DemoCmd.cmColorOptions:
                OpenColorDialog();
                ClearEvent(ref ev);
                break;

            case DemoCmd.cmResourceDialog:
                LoadResourceDialog();
                ClearEvent(ref ev);
                break;

            case DemoCmd.cmSaveDesktop:
                SaveDesktopState();
                ClearEvent(ref ev);
                break;

            case DemoCmd.cmLoadDesktop:
                LoadDesktopState();
                ClearEvent(ref ev);
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Options / Colors — open the color dialog with a sample palette.
    // -----------------------------------------------------------------------
    private void OpenColorDialog()
    {
        if (DeskTop == null) return;

        // ── Demo palette ── 7 entries representing a tiny app color scheme ──
        // Data[0] = 7 (count), Data[1..7] = color attributes.
        //  1: Desktop bg    0x17 = bright-white on blue
        //  2: Win frame act 0x70 = black on white
        //  3: Win frame inact 0x08 = dark-gray on black
        //  4: Dialog frame  0x70 = black on white
        //  5: Dialog title  0x0F = bright-white on black
        //  6: Button normal 0x20 = black on green
        //  7: Button default 0x2E = yellow on green
        var demoPalette = new TPalette("\x17\x70\x08\x70\x0F\x20\x2E", 7);

        // ── Demo groups / items chain (matches palette indices above) ──────
        var groups =
            new TColorGroup("Desktop",
                new TColorItem("Background", 1)) +
            new TColorGroup("Window",
                new TColorItem("Frame Active",   2) +
                new TColorItem("Frame Inactive", 3)) +
            new TColorGroup("Dialog",
                new TColorItem("Frame", 4) +
                new TColorItem("Title", 5)) +
            new TColorGroup("Button",
                new TColorItem("Normal",  6) +
                new TColorItem("Default", 7));

        var dlg = new TColorDialog(demoPalette, groups);
        if (ValidView(dlg) != null)
        {
            dlg.helpCtx = DemoHelpCtx.ColorDialog;
            DeskTop.ExecView(dlg);
        }
    }

    // -----------------------------------------------------------------------
    // Welcome window shown at startup
    // -----------------------------------------------------------------------
    private void OpenWelcomeWindow()
    {
        var r = new TRect(2, 2, 60, 18);
        var win = new TWindow(r, "SharpVision Demo", _nextWinNum++);
        win.Insert(new TStaticText(
            new TRect(2, 1, win.size.x - 4, win.size.y - 4),
            "Welcome to SharpVision!\n\n" +
            "SharpVision is a C#/.NET 8 reimplementation\n" +
            "of Borland Turbo Vision.\n\n" +
            "Use Alt+F to open the File menu.\n" +
            "Use F10 to activate the menu bar.\n" +
            "Use Alt+X or File/Exit to quit.\n\n" +
            "Arrows navigate menus; Enter selects;\n" +
            "Esc closes menus and dialogs."));
        win.helpCtx = DemoHelpCtx.WelcomeWindow;
        if (DeskTop != null)
            DeskTop.Insert(win);
    }

    // -----------------------------------------------------------------------
    // File / New Window
    // -----------------------------------------------------------------------
    private void OpenNewWindow()
    {
        if (DeskTop == null) return;
        int n = _nextWinNum++;
        // Tile windows slightly so they don't stack exactly.
        int ox = (n % 6) * 3 + 2;
        int oy = (n % 4) * 2 + 2;
        var r = new TRect(ox, oy, ox + 40, oy + 12);
        var win = new TWindow(r, $"Window {n}", (ushort)n);
        win.Insert(new TStaticText(
            new TRect(2, 1, win.size.x - 4, win.size.y - 4),
            $"This is window #{n}.\n\nPress F6 to cycle windows.\nF5 zooms; Ctrl+F5 resizes."));
        win.helpCtx = DemoHelpCtx.WindowManagement;
        DeskTop.Insert(win);
    }

    // -----------------------------------------------------------------------
    // File / Open... (shows file dialog or a deferred notice)
    // -----------------------------------------------------------------------
    private void OpenFileDialog()
    {
        if (DeskTop == null) return;
        try
        {
            var dlg = new TFileDialog("*.*", "Open File",
                "~N~ame", FileDialogOptions.fdOpenButton, 100);
            if (ValidView(dlg) != null)
            {
                ushort result = DeskTop.ExecView(dlg);
                if (result == Views.cmOK)
                {
                    dlg.GetData(out string chosen);
                    if (!string.IsNullOrEmpty(chosen))
                        OpenEditorWindow(chosen);
                }
            }
        }
        catch
        {
            MsgBox.MessageBox(DeskTop,
                "File dialog manual polish is deferred.\nUse File/New Editor... to open an editor.",
                MsgBox.mfInformation | MsgBox.mfOKButton);
        }
    }

    // -----------------------------------------------------------------------
    // File / New Editor Window
    // Opens a TEditWindow with an optional file.  When fileName is empty/null
    // the editor starts with pre-loaded sample text so the user can type
    // immediately.
    // -----------------------------------------------------------------------
    private void OpenEditorWindow(string fileName = null)
    {
        if (DeskTop == null) return;

        int n = _nextWinNum++;
        int ox = (n % 6) * 2 + 4;
        int oy = (n % 4) * 2 + 2;
        // Minimum TEditWindow size is 24×6; use a generous default.
        var r = new TRect(ox, oy,
                          Math.Min(ox + 68, DeskTop.size.x - 2),
                          Math.Min(oy + 16, DeskTop.size.y - 3));
        // Enforce minimum
        if (r.b.x - r.a.x < TEditWindow.MinEditWinSize.x)
            r.b.x = r.a.x + TEditWindow.MinEditWinSize.x;
        if (r.b.y - r.a.y < TEditWindow.MinEditWinSize.y)
            r.b.y = r.a.y + TEditWindow.MinEditWinSize.y;

        var ew = new TEditWindow(r, fileName ?? string.Empty, n);
        if (ValidView(ew) == null) return;

        ew.helpCtx = DemoHelpCtx.EditorPlaceholder;

        // Pre-load sample text when opening a blank editor.
        if (string.IsNullOrEmpty(fileName) && ew.editor != null && ew.editor.isValid)
        {
            string sample =
                "SharpVision Editor Sample\n" +
                "--------------------------------------\n" +
                "\n" +
                "Type here.  Arrow keys, Backspace, Delete, Enter all work.\n" +
                "F5 zooms the window; Ctrl+F5 resizes.\n" +
                "F6 cycles among open windows.\n" +
                "\n" +
                "This editor is a TEditWindow wrapping TFileEditor + scrollbars.\n";
            byte[] bytes = System.Text.Encoding.Latin1.GetBytes(sample);
            ew.editor.InsertText(bytes, (uint)bytes.Length, false);
            // Move cursor to top-left after inserting.
            ew.editor.SetCurPtr(0, 0);
        }

        DeskTop.Insert(ew);
    }

    // -----------------------------------------------------------------------
    // Edit / Controls Showcase — modal dialog showing all implemented controls
    // -----------------------------------------------------------------------
    private void OpenControlsShowcaseDialog()
    {
        if (DeskTop == null) return;

        // Keep references to the interactive controls so we can read their
        // values after the user presses OK.
        TInputLine    nameInput = null;
        TCheckBoxes   checkBoxes = null;
        TRadioButtons radioButtons = null;
        TListBox      listBox = null;

        TDialog dlg = BuildControlsShowcaseDialog(
            out nameInput, out checkBoxes, out radioButtons, out listBox);
        if (ValidView(dlg) == null) return;

        ushort result = DeskTop.ExecView(dlg);

        if (result == Views.cmOK)
        {
            // Collect results from each control.
            string name = nameInput?.Data ?? string.Empty;

            string[] checkLabels = { "Bold", "Italic", "Wide output", "Verbose" };
            uint chkVal = checkBoxes?.value ?? 0u;
            var chosen = new System.Collections.Generic.List<string>();
            for (int i = 0; i < checkLabels.Length; i++)
                if ((chkVal & (1u << i)) != 0) chosen.Add(checkLabels[i]);
            string checks = chosen.Count > 0 ? string.Join(", ", chosen) : "(none)";

            string[] radioLabels = { "Normal", "Verbose", "Silent" };
            uint radioVal = radioButtons?.value ?? 0u;
            string mode = radioVal < (uint)radioLabels.Length ? radioLabels[radioVal] : "?";

            string[] langs = { "C#", "C++", "Pascal", "Python", "Delphi" };
            int langIdx = listBox?.focused ?? 0;
            string lang = (langIdx >= 0 && langIdx < langs.Length) ? langs[langIdx] : "?";

            MsgBox.MessageBox(DeskTop,
                $"Name    : {name}\n" +
                $"Mode    : {mode}\n" +
                $"Checks  : {checks}\n" +
                $"Language: {lang}",
                MsgBox.mfInformation | MsgBox.mfOKButton);
        }
    }

    // Public so smoke tests can call it without running the full app.
    public TDialog BuildControlsShowcaseDialog(
        out TInputLine    nameInput,
        out TCheckBoxes   checkBoxes,
        out TRadioButtons radioButtons,
        out TListBox      listBox)
    {
        // Dialog: 68 wide × 20 tall — fits in any normal 80×24 console.
        var dlg = new TDialog(new TRect(0, 0, 68, 20), "Dialog Controls");

        // Center on the desktop.
        if (DeskTop != null)
            dlg.MoveTo(
                (DeskTop.size.x - 68) / 2,
                (DeskTop.size.y - 20) / 2);

        // ── Row 1: header description ─────────────────────────────────────
        dlg.Insert(new TStaticText(
            new TRect(2, 1, 65, 2),
            "Visual showcase of implemented Turbo Vision dialog controls."));

        // ── Row 2: Name label + input line ────────────────────────────────
        nameInput = new TInputLine(new TRect(10, 2, 50, 3), 40);
        nameInput.SetData("Enter name here");
        dlg.Insert(nameInput);
        dlg.Insert(new TLabel(new TRect(2, 2, 10, 3), "~N~ame:", nameInput));

        // ── Row 4: Checkboxes header label ────────────────────────────────
        dlg.Insert(new TStaticText(new TRect(2, 4, 22, 5), "Checkboxes:"));

        // ── Rows 5-8: TCheckBoxes (4 items, left column) ─────────────────
        checkBoxes = new TCheckBoxes(
            new TRect(2, 5, 24, 9),
            new TSItem("~B~old",
            new TSItem("~I~talic",
            new TSItem("~W~ide output",
            new TSItem("~V~erbose", null)))));
        checkBoxes.value = 0b0001u;     // Bold pre-checked
        dlg.Insert(checkBoxes);

        // ── Row 4: Options/Radio header label ─────────────────────────────
        dlg.Insert(new TStaticText(new TRect(36, 4, 55, 5), "Radio buttons:"));

        // ── Rows 5-7: TRadioButtons (3 items, right column) ───────────────
        radioButtons = new TRadioButtons(
            new TRect(36, 5, 60, 8),
            new TSItem("~N~ormal",
            new TSItem("~S~ilent",
            new TSItem("~D~ebug", null))));
        radioButtons.value = 0u;        // Normal pre-selected
        dlg.Insert(radioButtons);

        // ── Row 9: Language list label ────────────────────────────────────
        dlg.Insert(new TStaticText(new TRect(2, 9, 22, 10), "Language:"));

        // ── Rows 10-13: TListBox (no scrollbar) ──────────────────────────
        listBox = new TListBox(new TRect(2, 10, 24, 15), 1, null);
        var langList = new TStringCollection();
        langList.Insert("C#");
        langList.Insert("C++");
        langList.Insert("Pascal");
        langList.Insert("Python");
        langList.Insert("Delphi");
        listBox.NewList(langList);
        dlg.Insert(listBox);

        // ── Rows 9-13: TParamText (right column, info area) ───────────────
        var infoText = new TParamText(new TRect(36, 9, 65, 15), "", 0);
        infoText.SetText(
            "Use Tab/Shift+Tab to move\n" +
            "between controls.\n" +
            "\n" +
            "Arrow keys work inside\n" +
            "radio/check groups and\n" +
            "the language list.");
        dlg.Insert(infoText);

        // ── Rows 16-17: OK / Cancel buttons ──────────────────────────────
        dlg.Insert(new TButton(
            new TRect(9,  16, 21, 18), "~O~K",     Views.cmOK,     ButtonConstants.bfDefault));
        dlg.Insert(new TButton(
            new TRect(25, 16, 40, 18), "~C~ancel", Views.cmCancel, ButtonConstants.bfNormal));

        dlg.SelectNext(false);  // focus first selectable control
        dlg.helpCtx = DemoHelpCtx.ControlsShowcase;
        return dlg;
    }

    // -----------------------------------------------------------------------
    // Help / Resource Dialog — load a dialog from a TResourceFile.
    //
    // The resource file is generated automatically in the executable output
    // directory the first time this command is run, then reloaded on
    // subsequent calls. Resource key: "dialog.resourceSample".
    //
    // Resource file location: <AppContext.BaseDirectory>/demo-resources.tvr
    // -----------------------------------------------------------------------

    // Ensure all streamable types are registered before any stream or resource
    // operation.  Delegates to the central StreamableRegistration.RegisterAll()
    // helper which covers all 45+ types including validators and help types.
    // Safe to call multiple times and safe after Pstream.DeInitTypes().
    private static void EnsureResourceTypes()
    {
        StreamableRegistration.RegisterAll();
    }

    // Build the canonical "resource sample" dialog.
    // Kept separate so Demo and Demo01 share the exact same layout.
    private static TDialog BuildResourceSampleDialog()
    {
        var dlg = new TDialog(new TRect(0, 0, 56, 13), "Resource Dialog");

        var st = new TStaticText(new TRect(2, 2, 52, 5),
            "This dialog was loaded from a TResourceFile.\n" +
            "The resource was generated at runtime by\n" +
            "SharpVision and persisted to disk.");
        dlg.Insert(st);

        var il = new TInputLine(new TRect(10, 6, 50, 7), 40);
        il.Data = "type something here";
        dlg.Insert(il);
        dlg.Insert(new TLabel(new TRect(2, 6, 10, 7), "~I~nput:", il));

        dlg.Insert(new TButton(new TRect(14, 10, 26, 12), "~O~K",     Views.cmOK,     ButtonConstants.bfDefault));
        dlg.Insert(new TButton(new TRect(28, 10, 40, 12), "~C~ancel", Views.cmCancel, ButtonConstants.bfNormal));

        dlg.SelectNext(false);
        return dlg;
    }

    private static readonly string _resourceFilePath =
        System.IO.Path.Combine(AppContext.BaseDirectory, "demo-resources.tvr");

    private const string ResourceKeyDialog = "dialog.resourceSample";

    // Ensure the sample resource file exists; create it if missing.
    private static void EnsureResourceSampleFile()
    {
        EnsureResourceTypes();

        if (System.IO.File.Exists(_resourceFilePath)) return;

        var dlg = BuildResourceSampleDialog();
        var fp  = new Fpstream(_resourceFilePath);
        var rf  = new TResourceFile(fp);
        rf.Put(dlg, ResourceKeyDialog);
        rf.Flush();
        fp.Close();
    }

    private void LoadResourceDialog()
    {
        if (DeskTop == null) return;

        EnsureResourceSampleFile();

        var fp = new Fpstream(_resourceFilePath);
        var rf = new TResourceFile(fp);
        var dlg = rf.Get(ResourceKeyDialog) as TDialog;
        fp.Close();

        if (dlg == null)
        {
            MsgBox.MessageBox(DeskTop,
                "Could not load the resource dialog.\n" +
                "Try deleting demo-resources.tvr and retrying.",
                MsgBox.mfError | MsgBox.mfOKButton);
            return;
        }

        if (DeskTop.size.x > 0)
            dlg.MoveTo(
                (DeskTop.size.x - dlg.size.x) / 2,
                (DeskTop.size.y - dlg.size.y) / 2);

        dlg.helpCtx = DemoHelpCtx.ResourceDialog;
        DeskTop.ExecView(dlg);
    }

    // -----------------------------------------------------------------------
    // Window / Save Desktop State  &  Window / Load Desktop State
    //
    // Windows are persisted into demo-resources.tvr under keys:
    //   "desktop.win.00", "desktop.win.01", ...
    // Keys are zero-padded to two digits so lexicographic sort preserves
    // stacking order (topmost = 00, bottom-most = NN).
    // TBackground is intentionally not persisted; a fresh one is always
    // created by TDeskTop on startup.
    // -----------------------------------------------------------------------

    private void SaveDesktopState()
    {
        if (DeskTop == null) return;
        EnsureResourceTypes();

        // Collect TWindows in top-to-bottom stacking order.
        var windows = new System.Collections.Generic.List<TWindow>();
        DeskTop.ForEachView(p => { if (p is TWindow w) windows.Add(w); });

        if (windows.Count == 0)
        {
            MsgBox.MessageBox(DeskTop, "No windows open to save.",
                MsgBox.mfInformation | MsgBox.mfOKButton);
            return;
        }

        // Ensure the resource file exists.
        EnsureResourceSampleFile();

        var fp = new Fpstream(_resourceFilePath);
        var rf = new TResourceFile(fp);

        // Remove any previously saved desktop windows.
        var toRemove = new System.Collections.Generic.List<string>();
        for (short i = 0; i < rf.Count(); i++)
        {
            string k = rf.KeyAt(i);
            if (k.StartsWith("desktop.win.")) toRemove.Add(k);
        }
        foreach (var k in toRemove) rf.Remove(k);

        // Persist each TWindow (top-to-bottom = 00, 01, 02, ...).
        for (int i = 0; i < windows.Count; i++)
            rf.Put(windows[i], $"desktop.win.{i:D2}");

        rf.Pack();
        fp.Close();

        MsgBox.MessageBox(DeskTop,
            $"Desktop state saved ({windows.Count} window{(windows.Count == 1 ? "" : "s")}).\n" +
            $"File: {_resourceFilePath}",
            MsgBox.mfInformation | MsgBox.mfOKButton);
    }

    private void LoadDesktopState()
    {
        if (DeskTop == null) return;
        EnsureResourceTypes();

        if (!System.IO.File.Exists(_resourceFilePath))
        {
            MsgBox.MessageBox(DeskTop,
                "No saved desktop state found.\n" +
                "Use Window / Save Desktop State first.",
                MsgBox.mfInformation | MsgBox.mfOKButton);
            return;
        }

        // Collect the saved window keys.
        Fpstream fp = null;
        try
        {
            fp = new Fpstream(_resourceFilePath);
            var rf = new TResourceFile(fp);

            var winKeys = new System.Collections.Generic.List<string>();
            for (short i = 0; i < rf.Count(); i++)
            {
                string k = rf.KeyAt(i);
                if (k.StartsWith("desktop.win.")) winKeys.Add(k);
            }

            if (winKeys.Count == 0)
            {
                fp.Close();
                MsgBox.MessageBox(DeskTop,
                    "No saved desktop windows found in resource file.\n" +
                    "Use Window / Save Desktop State first.",
                    MsgBox.mfInformation | MsgBox.mfOKButton);
                return;
            }

            // Remove existing desktop windows (not background, not dialogs).
            var existing = new System.Collections.Generic.List<TWindow>();
            DeskTop.ForEachView(p => { if (p is TWindow w) existing.Add(w); });
            foreach (var w in existing) DeskTop.Remove(w);

            // Insert loaded windows in reverse key order so that key "00"
            // (original topmost) ends up on top after all insertions.
            // (Each Insert() places the view at the TOP of the Z-order.)
            int loaded = 0;
            for (int i = winKeys.Count - 1; i >= 0; i--)
            {
                var win = rf.Get(winKeys[i]) as TWindow;
                if (win != null) { DeskTop.Insert(win); loaded++; }
            }

            fp.Close();

            // Update the next window number to avoid collisions.
            ushort maxNum = 0;
            DeskTop.ForEachView(p =>
            {
                if (p is TWindow w && w.number > maxNum) maxNum = w.number;
            });
            _nextWinNum = (ushort)(maxNum + 1);

            DeskTop.DrawView();

            MsgBox.MessageBox(DeskTop,
                $"Desktop state loaded ({loaded} window{(loaded == 1 ? "" : "s")}).",
                MsgBox.mfInformation | MsgBox.mfOKButton);
        }
        catch (Exception ex)
        {
            fp?.Close();
            MsgBox.MessageBox(DeskTop,
                $"Error loading desktop state:\n{ex.Message}",
                MsgBox.mfError | MsgBox.mfOKButton);
        }
    }

    // -----------------------------------------------------------------------
    // Edit / Show Dialog — sample dialog with static text, input, buttons
    // -----------------------------------------------------------------------
    private void ShowSampleDialog()
    {
        if (DeskTop == null) return;

        var dlg = new TDialog(new TRect(0, 0, 50, 13), "Sample Dialog");
        // Center it
        dlg.MoveTo((DeskTop.size.x - 50) / 2, (DeskTop.size.y - 13) / 2);

        dlg.Insert(new TStaticText(new TRect(2, 2, 46, 4),
            "Enter your name below:"));

        var input = new TInputLine(new TRect(2, 4, 46, 5), 60);
        input.SetData("World");
        dlg.Insert(input);

        dlg.Insert(new TLabel(new TRect(2, 3, 10, 4), "~N~ame:", input));

        dlg.Insert(new TButton(new TRect(9,  9, 19, 11), "~O~K",     Views.cmOK,     ButtonConstants.bfDefault));
        dlg.Insert(new TButton(new TRect(21, 9, 31, 11), "~C~ancel", Views.cmCancel, ButtonConstants.bfNormal));

        dlg.SelectNext(false);

        ushort result = DeskTop.ExecView(dlg);
        if (result == Views.cmOK)
        {
            string name = input.Data ?? "World";
            MsgBox.MessageBox(DeskTop, $"Hello, {name}!",
                MsgBox.mfInformation | MsgBox.mfOKButton);
        }
    }

    // -----------------------------------------------------------------------
    // Edit / History Demo — modal dialog with TInputLine + THistory
    // -----------------------------------------------------------------------
    // Public so smoke tests can build the dialog without running the full app.
    public TDialog BuildHistoryDemoDialog()
    {
        // History id 1 is dedicated to this demo (FileDialog uses 100+).
        const ushort HistDemoId = 1;

        // 52 wide × 8 tall — compact but comfortable.
        var dlg = new TDialog(new TRect(0, 0, 52, 8), "Search History");
        if (DeskTop != null)
            dlg.MoveTo((DeskTop.size.x - 52) / 2, (DeskTop.size.y - 8) / 2);

        // ── Row 2: search label + input line + history button ────────────
        var input = new TInputLine(new TRect(11, 2, 47, 3), 80);
        dlg.Insert(input);
        dlg.Insert(new TLabel(new TRect(2, 2, 11, 3), "~S~earch:", input));

        // THistory: 3 cols wide, same row as input, just to its right.
        var hist = new THistory(new TRect(47, 2, 50, 3), input, HistDemoId);
        dlg.Insert(hist);

        // ── Row 4-6: OK + Cancel buttons ─────────────────────────────────
        dlg.Insert(new TButton(new TRect(10, 4, 21, 6), "~O~K",     Views.cmOK,     ButtonConstants.bfDefault));
        dlg.Insert(new TButton(new TRect(23, 4, 34, 6), "~C~ancel", Views.cmCancel, ButtonConstants.bfNormal));

        dlg.SelectNext(false);
        return dlg;
    }

    private void OpenHistoryDemoDialog()
    {
        if (DeskTop == null) return;
        var dlg = BuildHistoryDemoDialog();
        if (ValidView(dlg) == null) return;
        // cmRecordHistory is broadcast by TButton.Press() when OK is clicked,
        // so THistory records the current input value automatically.
        DeskTop.ExecView(dlg);
    }

    // -----------------------------------------------------------------------
    // Help / About
    // -----------------------------------------------------------------------
    private void ShowAbout()
    {
        if (DeskTop == null) return;
        MsgBox.MessageBox(DeskTop,
            "SharpVision\n\n" +
            "A C#/.NET 8 reimplementation of\n" +
            "Borland Turbo Vision.\n\n" +
            "Driver: Win32ConsoleDriver (Windows)\n" +
            "973/973 smoke checks pass.",
            MsgBox.mfInformation | MsgBox.mfOKButton);
    }
}

// ---------------------------------------------------------------------------
// Entry point
// ---------------------------------------------------------------------------
internal class Program
{
    static int Main(string[] args)
    {
        // StreamableRegistration ensures all streamable types are available if
        // any stream/resource code path is exercised (defensive, same pattern as Demo01).
        StreamableRegistration.RegisterAll();

        // Load configuration before the driver is initialized.
        var config = SharpVisionConfigurationLoader.Load();
        ScreenDriverFactory.ConfiguredDriverName = config.DriverName;
        ScreenDriverFactory.ConfiguredSdlFontName = config.SdlFontName;

        return AppLifecycleGuard.Run(new TVDemoApp());
    }
}
