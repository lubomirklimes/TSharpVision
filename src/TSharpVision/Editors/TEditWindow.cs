using TSharpVision.Constants;

namespace TSharpVision;

// TEditWindow: a TWindow shell wrapping a TFileEditor, two
// TScrollBars and a TIndicator. Owns the title-update broadcast and the
// 24x6 minimum size limit.
public class TEditWindow : TWindow
{
    // Converted to get-only properties so the active
    // TSharpVisionIntl provider is consulted on each read.
    public static string clipboardTitle
        => TSharpVisionIntl.Get("Edit_Clipboard", "Clipboard");
    public static string untitled
        => TSharpVisionIntl.Get("Edit_Untitled", "Untitled");

    public static readonly TPoint MinEditWinSize = new TPoint(24, 6);

    public TFileEditor editor;

    public TEditWindow(TRect bounds, string fileName, int aNumber)
        : this(bounds, fileName, aNumber, null)
    {
    }

    public TEditWindow(
        TRect bounds,
        string fileName,
        int aNumber,
        TFileEditorOpenOptions openOptions)
        : base(bounds, null, (ushort)aNumber)
    {
        options |= Views.ofTileable;

        var hScrollBar = new TScrollBar(
            new TRect(18, size.y - 1, size.x - 2, size.y));
        hScrollBar.Hide();
        Insert(hScrollBar);

        var vScrollBar = new TScrollBar(
            new TRect(size.x - 1, 1, size.x, size.y - 1));
        vScrollBar.Hide();
        Insert(vScrollBar);

        var indicator = new TIndicator(
            new TRect(2, size.y - 1, 16, size.y));
        indicator.Hide();
        Insert(indicator);

        TRect r = GetExtent();
        r.Grow(-1, -1);
        editor = new TFileEditor(
            r,
            hScrollBar,
            vScrollBar,
            indicator,
            fileName,
            openOptions);
        Insert(editor);
    }

    public override void Close()
    {
        if (editor != null && editor.IsClipboard())
            Hide();
        else
            base.Close();
    }

    public override string GetTitle(short maxSize)
    {
        if (editor == null) return untitled;
        if (editor.IsClipboard()) return clipboardTitle;
        if (string.IsNullOrEmpty(editor.fileName)) return untitled;
        return editor.fileName;
    }

    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);
        if (ev.What == Events.evBroadcast
            && ev.message.command == Views.cmUpdateTitle)
        {
            frame?.DrawView();
            ClearEvent(ref ev);
        }
    }

    public override void SizeLimits(ref TPoint min, ref TPoint max)
    {
        base.SizeLimits(ref min, ref max);
        min = MinEditWinSize;
    }

    protected TEditWindow(StreamableInit init) :
        base(init)
    {
    }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WritePointer(editor);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        editor = (TFileEditor)isStream.ReadPointer();
        return this;
    }

    public new static TStreamable Build() => new TEditWindow(StreamableInit.streamableInit);
    public static readonly TStreamableClass StreamableClassTEditWindow =
        new TStreamableClass("TEditWindow", () => new TEditWindow(StreamableInit.streamableInit), 0);
}
