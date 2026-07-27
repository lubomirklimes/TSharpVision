using TSharpVision.Constants;

namespace TSharpVision.Samples.TVTerm;

public static class AboutDialog
{
    public static void ShowDialog(TGroup parent)
    {
        var dlg = new TDialog(new TRect(0, 0, 50, 12), "About TVTerm");
        dlg.options |= Views.ofCentered;

        dlg.Insert(new TStaticText(new TRect(2, 2, 48, 8),
            "TVTerm — TSharpVision terminal demo\n\n" +
            "Showcases the TTerminal view and ITerminalSession\n" +
            "abstractions (in-memory, pipe, PTY, fake)."));

        dlg.Insert(new TButton(new TRect(19, 9, 31, 11),
            "O~K~", Views.cmOK, ButtonConstants.bfDefault));

        parent.ExecView(dlg);
    }
}
