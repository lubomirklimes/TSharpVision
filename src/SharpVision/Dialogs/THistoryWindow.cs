using SharpVision.Constants;
namespace SharpVision;

public class THistoryWindow : TWindow
{
    public new static readonly string Name = "THistoryWindow";

    public THistoryViewer Viewer;

    private static readonly TPalette _palette = new TPalette(
        "\x13\x13\x15\x18\x17\x13\x14", 7);
    public override TPalette GetPalette() => _palette;

    public THistoryWindow(TRect bounds, ushort historyId)
        : base(bounds, null, Views.wnNoNumber)
    {
        flags = Views.wfClose;
        Viewer = InitViewer(GetExtent(), this, historyId);
        if (Viewer != null) Insert(Viewer);
    }

    public string GetSelection()
    {
        if (Viewer == null) return string.Empty;
        return Viewer.GetText(Viewer.focused, 255);
    }

    public virtual THistoryViewer InitViewer(TRect r, TWindow win, ushort historyId)
    {
        r.Grow(-1, -1);
        return new THistoryViewer(r,
            win.StandardScrollBar((ushort)(Views.sbHorizontal | Views.sbHandleKeyboard)),
            win.StandardScrollBar((ushort)(Views.sbVertical | Views.sbHandleKeyboard)),
            historyId);
    }
}
