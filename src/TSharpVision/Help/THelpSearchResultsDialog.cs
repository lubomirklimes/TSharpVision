using TSharpVision.Constants;

namespace TSharpVision;

// Lists help search results as "title  [source]" and returns the chosen one. Enter, the Open button or a
// double-click accepts the focused row; Escape or Cancel closes without a choice.
internal sealed class THelpSearchResultsDialog : TDialog
{
    private readonly IReadOnlyList<THelpSearchResult> _results;
    private readonly TListBox _list;

    internal THelpSearchResultsDialog(TGroup host, IReadOnlyList<THelpSearchResult> results)
        : base(Bounds(host), TSharpVisionIntl.Get("Help_Search_ResultsTitle", "Help Topics Found"))
    {
        _results = results;
        options |= Views.ofCentered;

        var sb = new TScrollBar(new TRect(size.x - 4, 3, size.x - 3, size.y - 4));
        Insert(sb);
        _list = new TListBox(new TRect(3, 3, size.x - 4, size.y - 4), 1, sb);
        var lines = new TStringCollection();
        foreach (var r in results)
            lines.Insert(r.Title.Length == 0 ? $"[{r.Source}]" : $"{r.Title}  [{r.Source}]");
        _list.NewList(lines);
        Insert(_list);
        Insert(new TLabel(new TRect(2, 2, size.x - 3, 3),
            string.Format(TSharpVisionIntl.Get("Help_Search_Topics", "~T~opics ({0})"), results.Count), _list));

        int x = size.x / 2 - 12;
        Insert(new TButton(new TRect(x, size.y - 3, x + 11, size.y - 1),
            TSharpVisionIntl.Get("File_Btn_Open", "~O~pen"), Views.cmOK, ButtonConstants.bfDefault));
        Insert(new TButton(new TRect(x + 13, size.y - 3, x + 24, size.y - 1),
            TSharpVisionIntl.Get("Btn_Cancel", "~C~ancel"), Views.cmCancel, ButtonConstants.bfNormal));
        _list.Select();
    }

    // The result focused in the list, or null when there is none.
    internal THelpSearchResult? Selected
        => _list.focused >= 0 && _list.focused < _results.Count ? _results[_list.focused] : null;

    internal TListBox List => _list;

    private static TRect Bounds(TGroup host)
    {
        int w = Math.Clamp(host.size.x - 4, 40, 72), h = Math.Clamp(host.size.y - 2, 10, 18);
        return new TRect(0, 0, w, h);
    }

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        // A double-click on a row selects it.
        if (@event.What == Events.evBroadcast && @event.message.command == Views.cmListItemSelected
            && ReferenceEquals(@event.message.infoPtr, _list))
        {
            EndModal(Views.cmOK);
            ClearEvent(ref @event);
        }
    }
}
