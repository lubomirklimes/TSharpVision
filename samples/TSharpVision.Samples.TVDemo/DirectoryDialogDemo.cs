using TSharpVision.Constants;

namespace TSharpVision.Samples.TVDemo;

internal sealed class DemoDirectoryDialog : TChDirDialog
{
    internal const int Width = 74, Height = 19;

    internal DemoDirectoryDialog() : base(ChDirDialogOptions.cdNormal, 113)
    {
        // Keep the established outer size and centering policy; the core dialog's
        // growth rules distribute the extra width between tree and action column.
        ChangeBounds(new TRect(0, 0, Width, Height));
        helpCtx = DemoHelpCtx.MainMenu; // Includes Change Directory semantics.
    }
}

public partial class TVDemoApp
{
    private void OpenChangeDirectoryDemo()
    {
        if (DeskTop == null) return;
        string previous = Directory.GetCurrentDirectory();
        var dialog = new DemoDirectoryDialog();
        ushort result;
        try
        {
            if (ValidView(dialog) == null) return;
            result = DeskTop.ExecView(dialog);
        }
        finally { dialog.ShutDown(); }
        string current = Directory.GetCurrentDirectory();
        ShowDemoResult(DeskTop,
            (result == Views.cmOK ? "Directory accepted: process CWD changed." : "Directory cancelled: CWD unchanged.") +
            $"\nPrevious: {previous}\nCurrent: {current}");
    }
}
