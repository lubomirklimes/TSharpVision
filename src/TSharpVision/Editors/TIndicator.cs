using TSharpVision.Constants;

namespace TSharpVision;

/// <summary>Editor status view showing caret coordinates and whether the document is modified.</summary>
public class TIndicator : TView
{
    /// <summary>Caret column and line supplied by the editor, before the display adds one for human-readable numbering.</summary>
    public TPoint location;
    /// <summary>Whether the modification glyph is displayed.</summary>
    public bool modified;

    // See TSharpVisionGlyphs for the canonical names and CP437 byte values.
    /// <summary>Frame glyph used while the containing view is being dragged.</summary>
    public static char DragFrame    = TSharpVisionGlyphs.IndicatorDragFrame;   // '═' U+2550, was '\xCD' (CP437)
    /// <summary>Frame glyph used outside interactive dragging.</summary>
    public static char NormalFrame  = TSharpVisionGlyphs.IndicatorNormalFrame; // '─' U+2500, was '\xC4' (CP437)
    /// <summary>Glyph indicating unsaved document changes.</summary>
    public static char ModifiedStar = TSharpVisionGlyphs.IndicatorModified;    // '●' U+25CF, replaces (char)15

    private static readonly TPalette _palette = new TPalette("\x02\x03", 2);

    /// <summary>Creates a status indicator at owner-relative cell bounds that follows vertical owner resizing.</summary>
    public TIndicator(TRect bounds) : base(bounds)
    {
        growMode = Views.gfGrowLoY | Views.gfGrowHiY;
        location.x = 1;
        location.y = 1;
    }

    /// <inheritdoc />
    public override void Draw()
    {
        ushort color;
        char frame;
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);

        // NB: upstream really does select dragFrame when sfDragging is OFF
        // (and normalFrame when ON). The constants are named for the action
        // they advertise, not the current state.
        if ((state & Views.sfDragging) == 0)
        {
            color = GetColor(1);
            frame = DragFrame;
        }
        else
        {
            color = GetColor(2);
            frame = NormalFrame;
        }

        b.moveChar(0, frame, color, size.x);
        if (modified)
            b.putChar(0, ModifiedStar);

        // sprintf(s," %d:%d ",location.y+1,location.x+1)
        // moveCStr(8 - (strchr(s,':')-s), s, color)
        // i.e. align the ':' at column 8 of the indicator.
        string s = " " + (location.y + 1) + ":" + (location.x + 1) + " ";
        int colon = s.IndexOf(':');
        b.moveCStr(8 - colon, s, color);

        WriteLine(0, 0, size.x, 1, b);
    }

    /// <inheritdoc />
    public override TPalette GetPalette() => _palette;

    /// <inheritdoc />
    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if (aState == Views.sfDragging)
            DrawView();
    }

    /// <summary>Updates caret coordinates and modification state, redrawing only when either changes.</summary>
    public virtual void SetValue(TPoint aLocation, bool aModified)
    {
        if (location.x != aLocation.x || location.y != aLocation.y
            || modified != aModified)
        {
            location = aLocation;
            modified = aModified;
            DrawView();
        }
    }

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TIndicator(StreamableInit init) : base(init) { }
    /// <inheritdoc />
    public override object Read(Ipstream isStream) { base.Read(isStream); return this; }
    /// <inheritdoc />
    public override void Write(Opstream os) { base.Write(os); }
    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TIndicator(StreamableInit.streamableInit);
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTIndicator =
        new TStreamableClass("TIndicator", () => new TIndicator(StreamableInit.streamableInit), 0);
}
