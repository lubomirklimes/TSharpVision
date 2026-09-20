using TSharpVision.Constants;
namespace TSharpVision.Samples.TVDemo;

public partial class TVDemoApp
{
    private void OpenWelcomeWindow()
    {
        var r = new TRect(2, 2, 60, 18);
        var win = new TWindow(r, "TSharpVision Demo", _nextWinNum++);
        win.Insert(new TStaticText(
            new TRect(2, 1, win.size.x - 4, win.size.y - 4),
            "Welcome to TSharpVision!\n\n" +
            "TSharpVision is a C#/.NET reimplementation\n" +
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

    private void OpenControlsShowcaseDialog()
    {
        if (DeskTop == null) return;

        // Keep references to the interactive controls so we can read their
        // values after the user presses OK.
        TInputLine nameInput;
        TCheckBoxes checkBoxes;
        TRadioButtons radioButtons;
        TListBox listBox;

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

            string[] radioLabels = { "Normal", "Silent", "Debug" };
            uint radioVal = radioButtons?.value ?? 0u;
            string mode = radioVal < (uint)radioLabels.Length ? radioLabels[radioVal] : "?";

            string lang = listBox?.GetText(listBox.focused, 256) ?? "?";

            MsgBox.MessageBox(DeskTop,
                $"Name    : {name}\n" +
                $"Mode    : {mode}\n" +
                $"Checks  : {checks}\n" +
                $"Language: {lang}",
                MsgBox.mfInformation | MsgBox.mfOKButton);
        }
    }

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
}

