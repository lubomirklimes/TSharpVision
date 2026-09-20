using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>Popup window owning a history viewer and its standard scrollbars.</summary>
public class THistoryWindow : TWindow
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "THistoryWindow";

    /// <summary>Owned history-list view, or null when no viewer was created.</summary>
    public THistoryViewer? Viewer;

    private static readonly TPalette _palette = new TPalette(
        "\x13\x13\x15\x18\x17\x13\x14", 7);
    /// <inheritdoc />
    public override TPalette GetPalette() => _palette;

    /// <summary>Creates a closable, unnumbered history popup for the supplied history ID at owner-relative cell bounds.</summary>
    public THistoryWindow(TRect bounds, ushort historyId)
        : base(bounds, null, Views.wnNoNumber)
    {
        flags = Views.wfClose;
        Viewer = InitViewer(GetExtent(), this, historyId);
        if (Viewer != null) Insert(Viewer);
    }

    /// <summary>Returns the focused history text truncated to 255 characters, or empty text without a viewer.</summary>
    public string GetSelection()
    {
        if (Viewer == null) return string.Empty;
        return Viewer.GetText(Viewer.focused, 255);
    }

    /// <summary>Creates a history viewer inside the supplied bounds and inserts standard scrollbars into the supplied window.</summary>
    public virtual THistoryViewer InitViewer(TRect r, TWindow win, ushort historyId)
    {
        r.Grow(-1, -1);
        return new THistoryViewer(r,
            win.StandardScrollBar((ushort)(Views.sbHorizontal | Views.sbHandleKeyboard)),
            win.StandardScrollBar((ushort)(Views.sbVertical | Views.sbHandleKeyboard)),
            historyId);
    }
}
