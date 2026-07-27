using TSharpVision.Constants;

namespace TSharpVision.Samples.TVTerm;

public sealed class FindResult
{
    public string Text { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; }
}

public static class FindDialog
{
    public static FindResult? ShowDialog(TGroup parent, string initial)
    {
        var dlg = new TDialog(new TRect(0, 0, 50, 10), "Find");
        dlg.options |= Views.ofCentered;

        var input = new TInputLine(new TRect(15, 2, 46, 3), 200);
        input.SetData(initial ?? string.Empty);
        dlg.Insert(input);
        dlg.Insert(new TLabel(new TRect(2, 2, 14, 3), "~T~ext:", input));

        var cb = new TCheckBoxes(
            new TRect(15, 4, 46, 5),
            new TSItem("~C~ase sensitive", null));
        cb.value = 0u;
        dlg.Insert(cb);

        dlg.Insert(new TButton(new TRect(10, 7, 22, 9), "O~K~",     Views.cmOK,     ButtonConstants.bfDefault));
        dlg.Insert(new TButton(new TRect(26, 7, 38, 9), "Cancel",    Views.cmCancel, ButtonConstants.bfNormal));

        dlg.SelectNext(false);

        ushort res = parent.ExecView(dlg);
        if (res != Views.cmOK) return null;
        string text = input.Data ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return null;
        return new FindResult { Text = text, CaseSensitive = (cb.value & 1u) != 0 };
    }
}
