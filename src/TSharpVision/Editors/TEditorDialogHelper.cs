using System.IO;
using TSharpVision.Constants;

namespace TSharpVision;

// TEditorDialogHelper — installs the standard TEditor.editorDialog callback.
public static class TEditorDialogHelper
{
    /// <summary>
    /// Injectable delegate for Save-As overwrite confirmation.
    /// Receives the desktop (may be null) and the target file path.
    /// Returns <see cref="Views.cmYes"/> to overwrite, anything else to cancel.
    /// Replace in tests to simulate Yes/No without interactive UI.
    /// </summary>
    public static Func<TDeskTop, string, ushort> OverwriteConfirm = DefaultOverwriteConfirm;

    /// <summary>
    /// The built-in (non-injected) overwrite confirmation implementation.
    /// Exposed publicly so smoke tests can restore the default without
    /// capturing a private delegate reference.
    /// </summary>
    public static ushort OverwriteConfirmDefault(TDeskTop deskTop, string path)
        => DefaultOverwriteConfirm(deskTop, path);

    private static ushort DefaultOverwriteConfirm(TDeskTop deskTop, string path)
    {
        if (deskTop == null) return Views.cmCancel;
        return MsgBox.MessageBox(deskTop,
            string.Format(
                TSharpVisionIntl.Get("File_ConfirmOverwrite",
                    "File '{0}' already exists. Overwrite?"),
                System.IO.Path.GetFileName(path)),
            MsgBox.mfWarning | MsgBox.mfYesNoCancel);
    }

    /// <summary>
    /// Installs the standard editor-dialog dispatcher into
    /// <see cref="TEditor.editorDialog"/>.  All modal UI is routed through
    /// <paramref name="deskTop"/>; if <paramref name="deskTop"/> is null,
    /// dialog paths return <see cref="Views.cmCancel"/> without throwing.
    /// </summary>
    public static void Install(TDeskTop deskTop)
    {
        TEditor.editorDialog = (int dialog, object info) =>
        {
            switch (dialog)
            {
                case Views.edSaveAs:
                {
                    var fe = info as TFileEditor;
                    string initial = fe?.fileName ?? string.Empty;
                    var dlg = new TFileDialog("*.*",
                        TSharpVisionIntl.Get("File_Title_SaveAs", "Save File As"),
                        "~N~ame", FileDialogOptions.fdOKButton, 101);
                    dlg.SetData(initial);
                    if (deskTop == null) return Views.cmCancel;
                    ushort r = deskTop.ExecView(dlg);
                    if (r != Views.cmCancel && r != 0)
                    {
                        dlg.GetData(out string chosen);
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
