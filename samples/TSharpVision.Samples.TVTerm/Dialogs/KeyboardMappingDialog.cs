using TSharpVision.Constants;

namespace TSharpVision.Samples.TVTerm;

/// <summary>
/// Options → Keyboard Mapping…
///
/// Shows the fixed key bindings built into TTerminal and lets the user
/// configure the one ambiguous binding: Ctrl+C priority (Copy vs Interrupt).
/// The choice is stored in <see cref="TVTermConfig.KeyBindings"/> under the
/// key "CtrlC" → "copy" | "interrupt".
///
/// All other bindings listed are informational (hardcoded in TTerminal core).
/// </summary>
public static class KeyboardMappingDialog
{
    private const string KeyCtrlC = "CtrlC";
    private const string ValCopy      = "copy";
    private const string ValInterrupt = "interrupt";

    // Documented fixed bindings shown in the list.
    private static readonly string[] FixedBindings =
    {
        "Ctrl+C          Copy selection (if text selected); else interrupt session",
        "Ctrl+V          Paste clipboard text into terminal input",
        "Ctrl+A          Move input cursor to start of line",
        "Ctrl+E          Move input cursor to end of line",
        "Ctrl+M / Enter  Submit input line (Command mode)",
        "Up / Down       Navigate command history (Command mode)",
        "PgUp / PgDn     Scroll terminal output",
        "Home / End      Scroll to top / bottom of scrollback",
        "Mouse wheel     Scroll terminal output",
        "Left drag       Select text",
        "Escape          Clear selection (or forward ESC in Raw mode)",
        "Alt+F3          Close terminal window",
        "F9              Interrupt active session (via Session menu)",
    };

    public static void ShowDialog(TGroup parent, TVTermConfig cfg)
    {
        // Read current Ctrl+C preference from config.
        cfg.KeyBindings.TryGetValue(KeyCtrlC, out string ctrlCVal);
        bool interruptFirst = ctrlCVal == ValInterrupt;

        // ── Dialog: 68 × 22 ─────────────────────────────────────────────────
        var dlg = new TDialog(new TRect(0, 0, 68, 22), "Keyboard Mapping");
        dlg.options |= Views.ofCentered;

        dlg.Insert(new TStaticText(new TRect(2, 1, 66, 2),
            "Fixed key bindings in TTerminal (informational):"));

        // Scrollable list of fixed bindings.
        var sb = new TScrollBar(new TRect(64, 2, 65, 15));
        dlg.Insert(sb);
        var lb = new TListBox(new TRect(2, 2, 64, 15), 1, sb);
        var col = new TStringCollection();
        foreach (string s in FixedBindings) col.Insert(s);
        lb.NewList(col);
        dlg.Insert(lb);

        // Configurable: Ctrl+C priority.
        dlg.Insert(new TStaticText(new TRect(2, 15, 66, 16),
            "Ctrl+C preference (stored only; not applied to input):"));

        var ctrlCMode = new TRadioButtons(
            new TRect(2, 16, 66, 18),
            new TSItem("~C~opy selection first, then interrupt (default)",
            new TSItem("~I~nterrupt session first (selection is lost)", null)));
        ctrlCMode.value = interruptFirst ? 1u : 0u;
        dlg.Insert(ctrlCMode);

        dlg.Insert(new TButton(new TRect(12, 19, 24, 21), "O~K~",
            Views.cmOK, ButtonConstants.bfDefault));
        dlg.Insert(new TButton(new TRect(26, 19, 38, 21), "Cancel",
            Views.cmCancel, ButtonConstants.bfNormal));

        dlg.SelectNext(false);

        ushort res = parent.ExecView(dlg);
        if (res != Views.cmOK) return;

        cfg.KeyBindings[KeyCtrlC] = ctrlCMode.value == 1u ? ValInterrupt : ValCopy;
        cfg.Save();

        MsgBox.MessageBox(parent,
            "Keyboard mapping saved.\n" +
            "This preference is not yet applied to terminal input.",
            MsgBox.mfInformation | MsgBox.mfOKButton);
    }
}
