using System;
using System.Text;
using TSharpVision.Constants;

namespace TSharpVision;

// TEditorReplaceDialog — builds and executes the editor Replace dialog.
//
// Static factory that mirrors the upstream layout.
public static class TEditorReplaceDialog
{
    /// <summary>
    /// Build a Replace dialog pre-populated from <paramref name="rec"/>.
    /// Returns the dialog and exposes the key controls via out-parameters.
    /// </summary>
    public static TDialog Build(TReplaceDialogRec rec,
                                out TInputLine findInput,
                                out TInputLine replaceInput,
                                out TCheckBoxes checkBoxes)
    {
        var dlg = new TDialog(new TRect(0, 0, 40, 16),
            TSharpVisionIntl.Get("Edit_ReplaceTitle", "Replace"));
        dlg.options |= Views.ofCentered;

        findInput = new TInputLine(new TRect(3, 3, 34, 4), 80);
        findInput.Data = TEditorFindDialog.BytesToString(rec?.Find);
        dlg.Insert(findInput);
        dlg.Insert(new TLabel(new TRect(2, 2, 34, 3),
            TSharpVisionIntl.Get("Edit_FindText", "~T~ext to find"), findInput));

        replaceInput = new TInputLine(new TRect(3, 6, 34, 7), 80);
        replaceInput.Data = TEditorFindDialog.BytesToString(rec?.Replace);
        dlg.Insert(replaceInput);
        dlg.Insert(new TLabel(new TRect(2, 5, 34, 6),
            TSharpVisionIntl.Get("Edit_ReplaceText", "~R~eplace with"), replaceInput));

        // Checkboxes: bit 0 = case-sensitive, bit 1 = whole words only,
        //             bit 2 = prompt on replace, bit 3 = replace all.
        // These map directly to efCaseSensitive (0x01) .. efReplaceAll (0x08).
        const ushort replaceOptionsMask =
            Views.efCaseSensitive | Views.efWholeWordsOnly
            | Views.efPromptOnReplace | Views.efReplaceAll;

        checkBoxes = new TCheckBoxes(new TRect(3, 8, 37, 12),
            new TSItem(TSharpVisionIntl.Get("Edit_Chk_CaseSensitive",   "~C~ase sensitive"),
            new TSItem(TSharpVisionIntl.Get("Edit_Chk_WholeWords",      "~W~hole words only"),
            new TSItem(TSharpVisionIntl.Get("Edit_Chk_PromptOnReplace", "~P~rompt on replace"),
            new TSItem(TSharpVisionIntl.Get("Edit_Chk_ReplaceAll",      "Replace ~A~ll"), null)))));
        checkBoxes.value = (uint)((rec?.Options ?? 0) & replaceOptionsMask);
        dlg.Insert(checkBoxes);

        dlg.Insert(new TButton(new TRect(17, 13, 27, 15),
            TSharpVisionIntl.Get("Btn_OK", "~O~K"), Views.cmOK, ButtonConstants.bfDefault));
        dlg.Insert(new TButton(new TRect(28, 13, 38, 15),
            TSharpVisionIntl.Get("Btn_Cancel", "Cancel"), Views.cmCancel, ButtonConstants.bfNormal));

        dlg.SelectNext(false);
        return dlg;
    }

    /// <summary>
    /// Write dialog control values back into <paramref name="rec"/>.
    /// Call this after ExecView returns a non-cancel result.
    /// </summary>
    public static void ReadBack(TReplaceDialogRec rec,
                                TInputLine findInput,
                                TInputLine replaceInput,
                                TCheckBoxes checkBoxes)
    {
        if (rec == null) return;
        const ushort replaceOptionsMask =
            Views.efCaseSensitive | Views.efWholeWordsOnly
            | Views.efPromptOnReplace | Views.efReplaceAll;

        TEditorFindDialog.StringToBytes(findInput?.Data ?? string.Empty, rec.Find);
        TEditorFindDialog.StringToBytes(replaceInput?.Data ?? string.Empty, rec.Replace);
        rec.Options = (ushort)(
            (rec.Options & ~replaceOptionsMask)
            | ((checkBoxes?.value ?? 0) & replaceOptionsMask));
    }

    /// <summary>
    /// Show the Replace dialog modally on <paramref name="host"/> and populate
    /// <paramref name="rec"/> on success.
    /// Returns <c>cmOK</c> on success, <c>cmCancel</c> if dismissed.
    /// </summary>
    public static ushort Execute(TGroup host, TReplaceDialogRec rec)
    {
        if (host == null || rec == null) return Views.cmCancel;

        var dlg = Build(rec, out var findInput, out var replaceInput, out var checkBoxes);
        ushort result = host.ExecView(dlg);
        if (result != Views.cmCancel)
            ReadBack(rec, findInput, replaceInput, checkBoxes);
        return result;
    }
}
