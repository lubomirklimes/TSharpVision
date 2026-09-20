using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>Scrollable list of entries from a shared input history.</summary>
public class THistoryViewer : TListViewer
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "THistoryViewer";

    /// <summary>Identifier selecting the history whose entries are displayed.</summary>
    public ushort HistoryId;

    /// <summary>Creates a history list at owner-relative cell bounds with scrollbars, initially focusing the second entry when available.</summary>
    public THistoryViewer(TRect bounds,
                          TScrollBar? aHScrollBar,
                          TScrollBar? aVScrollBar,
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
    /// <inheritdoc />
    public override TPalette GetPalette() => _palette;

    /// <inheritdoc />
    public override string GetText(int item, int maxChars)
    {
        string s = THistoryList.Str(HistoryId, item) ?? string.Empty;
        if (s.Length > maxChars) s = s.Substring(0, maxChars);
        return s;
    }

    /// <inheritdoc />
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

    /// <summary>Returns the longest stored history string length in UTF-16 code units.</summary>
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
