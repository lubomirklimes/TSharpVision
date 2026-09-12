using TSharpVision.Constants;

namespace TSharpVision;

/// <summary>Window owning a file editor, two scrollbars, and a caret/modification indicator.</summary>
public class TEditWindow : TWindow
{
    // Converted to get-only properties so the active
    // TSharpVisionIntl provider is consulted on each read.
    /// <summary>Clipboard-window title resolved from the current localization provider.</summary>
    public static string clipboardTitle
        => TSharpVisionIntl.Get("Edit_Clipboard", "Clipboard");
    /// <summary>Unnamed-document title resolved from the current localization provider.</summary>
    public static string untitled
        => TSharpVisionIntl.Get("Edit_Untitled", "Untitled");

    /// <summary>Minimum editor-window size in cells: 24 columns by 6 rows.</summary>
    public static readonly TPoint MinEditWinSize = new TPoint(24, 6);

    /// <summary>Owned file editor displaying the document.</summary>
    public TFileEditor editor;

    /// <summary>Creates an editor window at owner-relative cell bounds with default decoding and a window-selection number.</summary>
    public TEditWindow(TRect bounds, string fileName, int aNumber)
        : this(bounds, fileName, aNumber, null)
    {
    }

    /// <summary>Creates an editor window and its controls using the supplied bounds, path, window number, and optional decoding policy.</summary>
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

    /// <inheritdoc />
    public override void Close()
    {
        if (editor != null && editor.IsClipboard())
            Hide();
        else
            base.Close();
    }

    /// <inheritdoc />
    public override string GetTitle(short maxSize)
    {
        if (editor == null) return untitled;
        if (editor.IsClipboard()) return clipboardTitle;
        if (string.IsNullOrEmpty(editor.fileName)) return untitled;
        return editor.fileName;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public override void SizeLimits(ref TPoint min, ref TPoint max)
    {
        base.SizeLimits(ref min, ref max);
        min = MinEditWinSize;
    }

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TEditWindow(StreamableInit init) :
        base(init)
    {
    }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WritePointer(editor);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        editor = (TFileEditor)isStream.ReadPointer();
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TEditWindow(StreamableInit.streamableInit);
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTEditWindow =
        new TStreamableClass("TEditWindow", () => new TEditWindow(StreamableInit.streamableInit), 0);
}
