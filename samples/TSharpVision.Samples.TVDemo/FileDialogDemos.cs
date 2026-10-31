using TSharpVision.Constants;

namespace TSharpVision.Samples.TVDemo;

// The framework chooser accepts syntactically valid new names. Open adds existence
// validation here; Save deliberately remains a filename selection, never a writer.
internal sealed class DemoFileDialog : TFileDialog
{
    private readonly bool _save;

    internal DemoFileDialog(bool save, bool viewer = false)
        : base("*.*", viewer ? "View File" : save ? "Save filename (no write)" : "Open file dialog",
            "~N~ame / mask", (save ? FileDialogOptions.fdOKButton : FileDialogOptions.fdOpenButton), viewer ? (byte)112 : save ? (byte)111 : (byte)110)
    {
        _save = save;
        // Match the fixed Change Directory presentation; reflow every chooser part.
        ChangeBounds(new TRect(0, 0, DemoDirectoryDialog.Width, DemoDirectoryDialog.Height));
        growMode = 0;
        flags = Views.wfMove | Views.wfClose;
        fileName!.ChangeBounds(new TRect(3, 3, 56, 4));
        fileList!.ChangeBounds(new TRect(3, 6, 58, 14));
        fileList.vScrollBar!.ChangeBounds(new TRect(58, 6, 59, 14));
        ForEachView(view =>
        {
            switch (view)
            {
                case THistory history: history.ChangeBounds(new TRect(56, 3, 59, 4)); break;
                case TLabel label:
                    label.MoveTo(2, ReferenceEquals(label.Link, fileName) ? 2 : 5);
                    break;
                case TFileInfoPane info: info.ChangeBounds(new TRect(1, 15, 73, 18)); break;
                case TButton button:
                    int y = button.Command == Views.cmCancel ? 6 : 3;
                    button.ChangeBounds(new TRect(61, y, 71, y + 2));
                    break;
            }
        });
        helpCtx = DemoHelpCtx.MainMenu;
    }

    public override void SizeLimits(ref TPoint min, ref TPoint max)
    {
        // TFileDialog's legacy minimum height is 20; this explicitly reflowed sample is 19.
        min = max = new TPoint(DemoDirectoryDialog.Width, DemoDirectoryDialog.Height);
    }

    public override bool Valid(ushort command)
    {
        if (!base.Valid(command)) return false;
        if (!_save && TVDemoApp.IsFileAcceptance(command))
        {
            GetData(out string path);
            if (!File.Exists(path))
            {
                LastError = "Open requires an existing file.";
                TVDemoApp.ShowDemoResult(owner, LastError + "\n" + path, MsgBox.mfError);
                fileName?.Select();
                return false;
            }
        }
        return true;
    }
}

public partial class TVDemoApp
{
    internal static bool IsFileAcceptance(ushort result) =>
        result is Views.cmFileOpen or Views.cmOK;

    private (ushort Code, string? Path) ChooseDemoFile(bool save, bool viewer = false)
    {
        var dialog = new DemoFileDialog(save, viewer);
        try
        {
            if (ValidView(dialog) == null) return (Views.cmCancel, null);
            ushort result = DeskTop!.ExecView(dialog);
            if (!IsFileAcceptance(result)) return (result, null);
            dialog.GetData(out string path);
            return (result, path);
        }
        finally { dialog.ShutDown(); }
    }

    private void OpenFileDialogDemo() => ShowFileSelection(save: false);
    private void OpenFileSaveDialogDemo() => ShowFileSelection(save: true);

    private void ShowFileSelection(bool save)
    {
        if (DeskTop == null) return;
        var result = ChooseDemoFile(save);
        string mode = save ? "Save filename" : "Open filename";
        ShowDemoResult(DeskTop, result.Path == null
            ? mode + ": cancelled.\nNo file was written."
            : $"{mode}: {DemoResultName(result.Code)}\nAbsolute path: {result.Path}\nNo file was written.");
    }
}
