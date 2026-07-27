using TSharpVision.Constants;

namespace TSharpVision.Samples.TVTerm;

public static class TerminalSettingsDialog
{
    public static void ShowDialog(TGroup parent, TVTermConfig cfg)
    {
        var dlg = new TDialog(new TRect(0, 0, 56, 16), "Terminal Settings");
        dlg.options |= Views.ofCentered;

        var scrollback = new TInputLine(new TRect(22, 2, 32, 3), 8);
        scrollback.SetData(cfg.ScrollbackLines.ToString());
        dlg.Insert(scrollback);
        dlg.Insert(new TLabel(new TRect(2, 2, 21, 3), "~S~crollback lines:", scrollback));

        var shell = new TInputLine(new TRect(22, 4, 53, 5), 200);
        shell.SetData(cfg.DefaultShell ?? string.Empty);
        dlg.Insert(shell);
        dlg.Insert(new TLabel(new TRect(2, 4, 21, 5), "Default s~h~ell:", shell));

        var session = new TRadioButtons(
            new TRect(22, 6, 44, 10),
            new TSItem("~P~TY",
            new TSItem("Pi~p~e",
            new TSItem("~F~ake",
            new TSItem("~I~n-memory", null)))));
        session.value = (uint)cfg.DefaultSessionMode;
        dlg.Insert(session);
        dlg.Insert(new TLabel(new TRect(2, 6, 21, 7), "Default ~s~ession:", session));

        dlg.Insert(new TButton(new TRect(14, 12, 26, 14), "O~K~", Views.cmOK, ButtonConstants.bfDefault));
        dlg.Insert(new TButton(new TRect(28, 12, 40, 14), "Cancel", Views.cmCancel, ButtonConstants.bfNormal));

        dlg.SelectNext(false);
        ushort res = parent.ExecView(dlg);
        if (res != Views.cmOK) return;

        if (int.TryParse(scrollback.Data, out int n) && n >= 100 && n <= 1_000_000)
            cfg.ScrollbackLines = n;
        cfg.DefaultShell = shell.Data ?? string.Empty;
        cfg.DefaultSessionMode = (DefaultSessionMode)session.value;
        cfg.Save();
    }
}
