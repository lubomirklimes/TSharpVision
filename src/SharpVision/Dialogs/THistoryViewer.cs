using SharpVision.Constants;
namespace SharpVision;

public class THistoryViewer : TListViewer
{
    public new static readonly string Name = "THistoryViewer";

    public ushort HistoryId;

    public THistoryViewer(TRect bounds,
                          TScrollBar aHScrollBar,
                          TScrollBar aVScrollBar,
                          ushort aHistoryId)
        : base(bounds, 1, aHScrollBar, aVScrollBar)
    {
        HistoryId = aHistoryId;
        SetRange(THistoryList.Count(aHistoryId));
        if (range > 1) FocusItem(1);
        if (hScrollBar != null)
            hScrollBar.SetRange(0, Math.Max(0, HistoryWidth() - size.x + 3));
    }

    private static readonly TPalette _palette = new TPalette(
        "\x06\x06\x07\x06\x06", 5);
    public override TPalette GetPalette() => _palette;

    public override string GetText(int item, int maxChars)
    {
        string s = THistoryList.Str(HistoryId, item) ?? string.Empty;
        if (s.Length > maxChars) s = s.Substring(0, maxChars);
        return s;
    }

    public override void HandleEvent(ref TEvent @event)
    {
        if ((@event.What == Events.evMouseDown && @event.mouse.doubleClick)
            || (@event.What == Events.evKeyDown
                && @event.keyDown.keyCode == Keys.kbEnter))
        {
            EndModal(Views.cmOK);
            ClearEvent(ref @event);
        }
        else if ((@event.What == Events.evKeyDown
                  && @event.keyDown.keyCode == Keys.kbEsc)
                 || (@event.What == Events.evCommand
                  && @event.message.command == Views.cmCancel))
        {
            EndModal(Views.cmCancel);
            ClearEvent(ref @event);
        }
        else
        {
            base.HandleEvent(ref @event);
        }
    }

    public int HistoryWidth()
    {
        int width = 0;
        int count = THistoryList.Count(HistoryId);
        for (int i = 0; i < count; i++)
        {
            int t = (THistoryList.Str(HistoryId, i) ?? string.Empty).Length;
            if (t > width) width = t;
        }
        return width;
    }
}
