using System.IO;
using TSharpVision.Constants;

namespace TSharpVision;

/// <summary>Installs and supports the standard prompts used by editor operations.</summary>
public static class TEditorDialogHelper
{
    /// <summary>
    /// Injectable delegate for Save-As overwrite confirmation.
    /// Receives the desktop (may be null) and the target file path.
    /// Returns <see cref="Views.cmYes"/> to overwrite, anything else to cancel.
    /// Replace in tests to simulate Yes/No without interactive UI.
    /// </summary>
    public static Func<TDeskTop?, string, ushort> OverwriteConfirm = DefaultOverwriteConfirm;

    /// <summary>
    /// The built-in (non-injected) overwrite confirmation implementation.
    /// Exposed publicly so smoke tests can restore the default without
    /// capturing a private delegate reference.
    /// </summary>
    public static ushort OverwriteConfirmDefault(TDeskTop? deskTop, string path)
        => DefaultOverwriteConfirm(deskTop, path);

    private static ushort DefaultOverwriteConfirm(TDeskTop? deskTop, string path)
    {
        if (deskTop == null) return Views.cmCancel;
        return MsgBox.MessageBox(deskTop,
            string.Format(
                TSharpVisionIntl.Get("File_ConfirmOverwrite",
                    "File '{0}' already exists. Overwrite?"),
                System.IO.Path.GetFileName(path)),
            MsgBox.mfWarning | MsgBox.mfYesNoCancel);
    }

    // Index in EditorEncodingChoices.BuiltIn of the encoding the editor writes, or 0 (Auto) when none matches.
    internal static int EncodingChoiceIndex(TFileEditor editor)
    {
        var choices = EditorEncodingChoices.BuiltIn;
        for (int i = 1; i < choices.Count; i++)
        {
            var encoding = choices[i].Encoding;
            bool match = editor.OriginalLegacyEncoding != null
                ? ReferenceEquals(encoding.LegacyEncoding, editor.OriginalLegacyEncoding)
                : editor.OriginalEncoding == EditorEncodingKind.Latin1
                    ? ReferenceEquals(encoding.LegacyEncoding, TSharpVision.Text.LegacyTextEncodings.Latin1)
                    : encoding.Mode == EditorTextEncodingMode.Utf8;
            if (match) return i;
        }
        return 0;
    }

    /// <summary>
    /// Installs the standard editor-dialog dispatcher into
    /// <see cref="TEditor.editorDialog"/>.  All modal UI is routed through
    /// <paramref name="deskTop"/>; if <paramref name="deskTop"/> is null,
    /// dialog paths return <see cref="Views.cmCancel"/> without throwing.
    /// </summary>
    public static void Install(TDeskTop? deskTop)
        => Install(deskTop, FileDialogOptions.fdOKButton, Views.hcNoContext);

    /// <summary>
    /// Installs the standard editor-dialog dispatcher with a configurable Save As chooser. <paramref name="saveAsOptions"/>
    /// are <see cref="FileDialogOptions"/> flags (OK is added when neither OK nor Open is given), for example
    /// <see cref="FileDialogOptions.fdHelpButton"/> and <see cref="FileDialogOptions.fdEncodingSelector"/>; the chooser
    /// uses <paramref name="saveAsHelpContext"/> as its help context. With the encoding selector the chooser starts on
    /// the document's encoding, and the accepted choice is the encoding written (<see cref="TFileEditor.SaveAsEncoding"/>;
    /// Auto keeps the document's encoding).
    /// </summary>
    public static void Install(TDeskTop? deskTop, ushort saveAsOptions, ushort saveAsHelpContext)
    {
        if ((saveAsOptions & (FileDialogOptions.fdOKButton | FileDialogOptions.fdOpenButton)) == 0)
            saveAsOptions |= FileDialogOptions.fdOKButton;
        TEditor.editorDialog = (int dialog, object? info) =>
        {
            switch (dialog)
            {
                case Views.edSaveAs:
                {
                    var fe = info as TFileEditor;
                    string initial = fe?.fileName ?? string.Empty;
                    var dlg = new TFileDialog("*.*",
                        TSharpVisionIntl.Get("File_Title_SaveAs", "Save File As"),
                        TSharpVisionIntl.Get("File_Label_Name", "~N~ame"), saveAsOptions, 101)
                        { helpCtx = saveAsHelpContext };
                    dlg.SetData(initial);
                    if (fe != null)
                        dlg.encodingSelector?.SetData((ushort)EncodingChoiceIndex(fe));
                    if (deskTop == null) return Views.cmCancel;
                    ushort r = deskTop.ExecView(dlg);
                    if (r != Views.cmCancel && r != 0)
                    {
                        dlg.GetData(out string chosen);
                        if (fe != null && dlg.encodingSelector != null)
                            fe.SaveAsEncoding = dlg.SelectedEncoding;
                        if (!string.IsNullOrEmpty(chosen) && fe != null)
                        {
                            fe.fileName = chosen;
                            // Overwrite prompt: ask before clobbering an
                            // existing file.  OverwriteConfirm is injectable
                            // for headless testing.
                            if (System.IO.File.Exists(fe.fileName))
                            {
                                ushort confirm = OverwriteConfirm(deskTop, fe.fileName);
                                if (confirm != Views.cmYes)
                                {
                                    fe.fileName = initial; // restore original
                                    return Views.cmCancel;
                                }
                            }
                        }
                        return r;
                    }
                    return Views.cmCancel;
                }

                case Views.edSaveModify:
                {
                    if (deskTop == null) return Views.cmCancel;
                    string name = (info as string) ?? "file";
                    return MsgBox.MessageBox(deskTop,
                        string.Format(
                            TSharpVisionIntl.Get("Edit_SaveModify", "{0} has been modified. Save?"),
                            Path.GetFileName(name)),
                        MsgBox.mfInformation | MsgBox.mfYesNoCancel);
                }

                case Views.edSaveUntitled:
                    if (deskTop == null) return Views.cmCancel;
                    return MsgBox.MessageBox(deskTop,
                        TSharpVisionIntl.Get("Edit_SaveUntitled", "Save untitled file?"),
                        MsgBox.mfInformation | MsgBox.mfYesNoCancel);

                case Views.edCreateError:
                    if (deskTop == null) return Views.cmOK;
                    MsgBox.MessageBox(deskTop,
                        string.Format(
                            TSharpVisionIntl.Get("Edit_Err_Create", "Error creating file '{0}'."),
                            info as string),
                        MsgBox.mfError | MsgBox.mfOKButton);
                    return Views.cmOK;

                case Views.edReadError:
                    if (deskTop == null) return Views.cmOK;
                    MsgBox.MessageBox(deskTop,
                        string.Format(
                            TSharpVisionIntl.Get("Edit_Err_Read", "Error reading file '{0}'."),
                            info as string),
                        MsgBox.mfError | MsgBox.mfOKButton);
                    return Views.cmOK;

                case Views.edWriteError:
                    if (deskTop == null) return Views.cmOK;
                    MsgBox.MessageBox(deskTop,
                        string.Format(
                            TSharpVisionIntl.Get("Edit_Err_Write", "Error writing file '{0}'."),
                            info as string),
                        MsgBox.mfError | MsgBox.mfOKButton);
                    return Views.cmOK;

                case Views.edEncodingWriteError:
                    if (deskTop == null) return Views.cmOK;
                    MsgBox.MessageBox(deskTop,
                        TSharpVisionIntl.Get(
                            "File_Err_EncodingEncode",
                            "The file contains characters that cannot be saved using the selected encoding."),
                        MsgBox.mfError | MsgBox.mfOKButton);
                    return Views.cmOK;

                case Views.edOutOfMemory:
                    if (deskTop == null) return Views.cmOK;
                    MsgBox.MessageBox(deskTop,
                        TSharpVisionIntl.Get("Edit_Err_OOM", "Not enough memory for editor buffer."),
                        MsgBox.mfError | MsgBox.mfOKButton);
                    return Views.cmOK;

                case Views.edFind:
                {
                    if (info is not TFindDialogRec findRec) return Views.cmCancel;
                    if (deskTop == null) return Views.cmCancel;
                    return TEditorFindDialog.Execute(deskTop, findRec);
                }

                case Views.edReplace:
                {
                    if (info is not TReplaceDialogRec replaceRec) return Views.cmCancel;
                    if (deskTop == null) return Views.cmCancel;
                    return TEditorReplaceDialog.Execute(deskTop, replaceRec);
                }

                case Views.edSearchFailed:
                    if (deskTop == null) return Views.cmOK;
                    MsgBox.MessageBox(deskTop,
                        TSharpVisionIntl.Get("Edit_SearchFailed", "Search string not found."),
                        MsgBox.mfError | MsgBox.mfOKButton);
                    return Views.cmOK;

                case Views.edReplacePrompt:
                {
                    if (deskTop == null) return Views.cmCancel;
                    var r = new TRect(0, 1, 40, 8);
                    r.Move((deskTop.size.x - r.b.x) / 2, 0);
                    if (info is TPoint cursorPt)
                    {
                        var dialogBottom = deskTop.MakeGlobal(new TPoint(r.b.x, r.b.y));
                        if (cursorPt.y <= dialogBottom.y + 1)
                            r.Move(0, deskTop.size.y - r.b.y - 2);
                    }
                    return MsgBox.MessageBoxRect(deskTop, r,
                        TSharpVisionIntl.Get("Edit_ReplacePrompt", "Replace this occurrence?"),
                        MsgBox.mfYesNoCancel | MsgBox.mfInformation);
                }

                default:
                    return Views.cmCancel;
            }
        };
    }
}
