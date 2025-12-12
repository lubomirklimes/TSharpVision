using System;
using System.Text;
using TSharpVision.Constants;

namespace TSharpVision;

// TEditorFindDialog — builds and executes the editor Find dialog.
//
// Static factory that mirrors the upstream layout but works
// against the C# view hierarchy. Dialog data is transferred by manually
// reading/writing the TInputLine and TCheckBoxes controls rather than
// through the upstream raw-struct setData/getData protocol.
public static class TEditorFindDialog
{
    public static string BytesToString(byte[] b)
    {
        if (b == null || b.Length == 0) return string.Empty;
        int n = Array.IndexOf(b, (byte)0);
        if (n < 0) n = b.Length;
        return Encoding.ASCII.GetString(b, 0, n);
    }

    public static void StringToBytes(string s, byte[] dest)
    {
        Array.Clear(dest, 0, dest.Length);
        if (string.IsNullOrEmpty(s)) return;
        byte[] src = Encoding.ASCII.GetBytes(s);
        int n = Math.Min(src.Length, dest.Length - 1);
        Array.Copy(src, 0, dest, 0, n);
    }

    /// <summary>
    /// Build a Find dialog pre-populated from <paramref name="rec"/>.
    /// Returns the dialog and exposes the key controls via out-parameters
    /// so the caller can read them back after ExecView.
    /// </summary>
    public static TDialog Build(TFindDialogRec rec,
                                out TInputLine findInput,
                                out TCheckBoxes checkBoxes)
    {
        var dlg = new TDialog(new TRect(0, 0, 38, 12),
            TSharpVisionIntl.Get("Edit_FindTitle", "Find"));
        dlg.options |= Views.ofCentered;

        findInput = new TInputLine(new TRect(3, 3, 32, 4), 80);
        findInput.Data = BytesToString(rec?.Find);
        dlg.Insert(findInput);
        dlg.Insert(new TLabel(new TRect(2, 2, 32, 3),
            TSharpVisionIntl.Get("Edit_FindText", "~T~ext to find"), findInput));

        // Checkboxes: bit 0 = case-sensitive (efCaseSensitive = 0x01),
        //             bit 1 = whole words only (efWholeWordsOnly = 0x02).
        checkBoxes = new TCheckBoxes(new TRect(3, 5, 35, 7),
            new TSItem(TSharpVisionIntl.Get("Edit_Chk_CaseSensitive", "~C~ase sensitive"),
            new TSItem(TSharpVisionIntl.Get("Edit_Chk_WholeWords",    "~W~hole words only"), null)));
        checkBoxes.value = (uint)((rec?.Options ?? 0)
            & (Views.efCaseSensitive | Views.efWholeWordsOnly));
        dlg.Insert(checkBoxes);

        dlg.Insert(new TButton(new TRect(14, 9, 24, 11),
            "O~K~", Views.cmOK, ButtonConstants.bfDefault));
        dlg.Insert(new TButton(new TRect(26, 9, 36, 11),
            "Cancel", Views.cmCancel, ButtonConstants.bfNormal));

        dlg.SelectNext(false);
        return dlg;
    }

    /// <summary>
    /// Write dialog control values back into <paramref name="rec"/>.
    /// Call this after ExecView returns a non-cancel result.
    /// </summary>
    public static void ReadBack(TFindDialogRec rec,
                                TInputLine findInput,
                                TCheckBoxes checkBoxes)
    {
        if (rec == null) return;
        StringToBytes(findInput?.Data ?? string.Empty, rec.Find);
        const ushort mask = Views.efCaseSensitive | Views.efWholeWordsOnly;
        rec.Options = (ushort)(
            (rec.Options & ~mask)
            | ((checkBoxes?.value ?? 0) & mask));
    }

    /// <summary>
    /// Show the Find dialog modally on <paramref name="host"/> and populate
    /// <paramref name="rec"/> on success.
    /// Returns <c>cmOK</c> / other non-cancel on success, <c>cmCancel</c> if
    /// the user dismissed the dialog.
    /// </summary>
    public static ushort Execute(TGroup host, TFindDialogRec rec)
    {
        if (host == null || rec == null) return Views.cmCancel;

        var dlg = Build(rec, out var findInput, out var checkBoxes);
        ushort result = host.ExecView(dlg);
        if (result != Views.cmCancel)
            ReadBack(rec, findInput, checkBoxes);
        return result;
    }
}
