namespace TSharpVision;

/// <summary>A cluster allowing one selected option, stored as its zero-based index in value.</summary>
public class TRadioButtons : TCluster
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TRadioButtons";

    // TRadioButtons::button[] = " ( ) ".
    // 5 chars: leading space, open-paren, marker-slot (space/bullet), close-paren, trailing space.
    // DrawBox writes the marker at col+2 (the slot), label at col+5.
    private const string Button = " ( ) ";
    // CP437 0x07 maps to the bullet glyph in text-mode video. See TSharpVisionGlyphs.RadioChecked.
    private const char Check = TSharpVisionGlyphs.RadioChecked;

    /// <summary>Creates mutually exclusive options at owner-relative cell bounds by copying labels from the supplied chain.</summary>
    public TRadioButtons(TRect bounds, TSItem aStrings)
        : base(bounds, aStrings)
    {
    }

    /// <inheritdoc />
    public override void Draw() => DrawBox(Button, Check);

    /// <inheritdoc />
    public override bool Mark(int item) => (uint)item == value;

    /// <inheritdoc />
    public override void MovedTo(int item)
    {
        value = (uint)item;
        base.MovedTo(item);
    }

    /// <inheritdoc />
    public override void Press(int item)
    {
        value = (uint)item;
        base.Press(item);
    }

    /// <inheritdoc />
    public override void SetData(object rec)
    {
        base.SetData(rec);
        sel = (int)value;
    }

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTRadioButtons =
        new TStreamableClass("TRadioButtons", () => new TRadioButtons(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TRadioButtons(StreamableInit init) : base(init) { }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TRadioButtons(StreamableInit.streamableInit);
}
