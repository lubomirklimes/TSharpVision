using System.Text;
using TSharpVision.Constants;
using TSharpVision.Text;

namespace TSharpVision.Samples.TVDemo;

// The code editor's Open and Save As run the framework TFileDialog with both the Help button and the
// encoding selector, in the framework's own layout. Help is the framework's modal help for DemoHelpCtx.EditorFiles.
internal sealed class DemoEditorFileDialog : TFileDialog
{
    internal const ushort OpenOptions = FileDialogOptions.fdOpenButton | FileDialogOptions.fdHelpButton
        | FileDialogOptions.fdEncodingSelector;
    internal const ushort SaveAsOptions = FileDialogOptions.fdOKButton | FileDialogOptions.fdHelpButton
        | FileDialogOptions.fdEncodingSelector;

    // Open only: Save As is the framework editor chooser (TEditorDialogHelper with SaveAsOptions).
    internal DemoEditorFileDialog() : base("*.*", "Open File", "~N~ame", OpenOptions, 100)
    {
        helpCtx = DemoHelpCtx.EditorFiles;
    }

    public override bool Valid(ushort command)
    {
        if (!base.Valid(command)) return false;
        if (TVDemoApp.IsFileAcceptance(command))
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
    private void OpenEditorFile()
    {
        if (DeskTop == null) return;
        var dialog = new DemoEditorFileDialog();
        string path;
        EditorTextEncoding encoding;
        try
        {
            if (ValidView(dialog) == null) return;
            if (!IsFileAcceptance(DeskTop.ExecView(dialog))) return;
            dialog.GetData(out path);
            encoding = dialog.SelectedEncoding;
        }
        finally { dialog.ShutDown(); }

        DemoCodeWindow window;
        try
        {
            window = new DemoCodeWindow(DemoBounds(72, 19), _nextWinNum++, path,
                new TFileEditorOpenOptions { Encoding = encoding });
        }
        catch (DecoderFallbackException)
        {
            ShowDemoResult(DeskTop, $"The file could not be decoded using the selected encoding.\n{path}", MsgBox.mfError);
            return;
        }
        if (Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase))
            window.CodeEditor.SetSyntaxScope("source.cs");
        InsertDemoWindow(window);
    }

    // Index in EditorEncodingChoices.BuiltIn of the encoding the editor will write.
    internal static int EncodingChoiceIndex(TFileEditor editor)
    {
        var choices = EditorEncodingChoices.BuiltIn;
        for (int i = 1; i < choices.Count; i++)
        {
            var encoding = choices[i].Encoding;
            bool match = editor.OriginalLegacyEncoding != null
                ? ReferenceEquals(encoding.LegacyEncoding, editor.OriginalLegacyEncoding)
                : editor.OriginalEncoding == EditorEncodingKind.Latin1
                    ? ReferenceEquals(encoding.LegacyEncoding, LegacyTextEncodings.Latin1)
                    : encoding.Mode == EditorTextEncodingMode.Utf8;
            if (match) return i;
        }
        return 0;
    }

    internal static string EncodingLabel(TFileEditor editor)
    {
        string label = EditorEncodingChoices.BuiltIn[EncodingChoiceIndex(editor)].Label;
        return editor.OriginalEncoding == EditorEncodingKind.Utf8Bom ? label + " BOM" : label;
    }
}
