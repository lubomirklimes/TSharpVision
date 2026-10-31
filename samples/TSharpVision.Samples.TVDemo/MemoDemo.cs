using TSharpVision.Constants;

namespace TSharpVision.Samples.TVDemo;

internal sealed class MemoDemoDialog : TDialog
{
    internal const uint Capacity = 1024;
    internal TMemo Memo { get; }

    internal MemoDemoDialog(TRect bounds, TMemoData original) : base(bounds, "Multiline memo")
    {
        Insert(new TStaticText(new TRect(2, 1, 67, 3),
            "Edit multiple lines (1024 UTF-16 units). Enter inserts a line.\nTab leaves the memo; OK transfers text; Cancel keeps the original."));
        var horizontal = new TScrollBar(new TRect(2, 14, 66, 15));
        var vertical = new TScrollBar(new TRect(66, 4, 67, 14));
        var indicator = new TIndicator(new TRect(2, 15, 25, 16));
        Insert(horizontal);
        Insert(vertical);
        Insert(indicator);
        Memo = new TMemo(new TRect(2, 4, 66, 14), horizontal, vertical, indicator, Capacity);
        Memo.SetData(original);
        Memo.SetSelect(0, 0, false);
        Memo.modified = false;
        Insert(Memo);
        Insert(new TButton(new TRect(21, 17, 33, 19), "~O~K", Views.cmOK, ButtonConstants.bfDefault));
        Insert(new TButton(new TRect(37, 17, 49, 19), "~C~ancel", Views.cmCancel, ButtonConstants.bfNormal));
        Memo.Select();
    }

    internal void ReadAccepted(ushort result, TMemoData target)
    {
        if (result == Views.cmOK) Memo.GetData(target);
    }

    // Esc and its frame close are intentional discard operations. In particular,
    // do not invoke the file editor's save prompts for this data-bound memo.
}

public partial class TVDemoApp
{
    internal static TMemoData CreateMemoDemoData()
    {
        const string text = "A memo is data, not a file.\nEdit this second line.\nTab reaches OK / Cancel.";
        var data = new TMemoData(MemoDemoDialog.Capacity) { length = (uint)text.Length };
        text.CopyTo(0, data.buffer, 0, text.Length);
        return data;
    }

    private void OpenMemoDemo()
    {
        if (DeskTop == null) return;
        var data = CreateMemoDemoData();
        var dialog = new MemoDemoDialog(DemoBounds(70, 21), data);
        ushort result;
        try
        {
            if (ValidView(dialog) == null) return;
            result = DeskTop.ExecView(dialog);
            dialog.ReadAccepted(result, data);
        }
        finally { dialog.ShutDown(); }
        // Keep feedback inside the screen even when the memo is at capacity.
        string text = new(data.buffer, 0, (int)data.length);
        string preview = string.Join("\n", text.Split('\n').Take(4).Select(line =>
            line.Length > 65 ? line[..65] + "..." : line));
        ShowDemoResult(DeskTop,
            $"Memo: {DemoResultName(result)}; {data.length}/{MemoDemoDialog.Capacity} UTF-16 units.\n" +
            (result == Views.cmOK ? "Accepted TMemoData (first four lines):\n" : "Cancelled: original TMemoData preserved.\n") +
            preview + "\nNo file was written.");
    }
}
