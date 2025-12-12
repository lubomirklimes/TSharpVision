namespace TSharpVision;

public class TRadioButtons : TCluster
{
    public new static readonly string Name = "TRadioButtons";

    // TRadioButtons::button[] = " ( ) ".
    // 5 chars: leading space, open-paren, marker-slot (space/bullet), close-paren, trailing space.
    // DrawBox writes the marker at col+2 (the slot), label at col+5.
    private const string Button = " ( ) ";
    // CP437 0x07 maps to the bullet glyph in text-mode video. See TSharpVisionGlyphs.RadioChecked.
    private const char Check = TSharpVisionGlyphs.RadioChecked;

    public TRadioButtons(TRect bounds, TSItem aStrings)
        : base(bounds, aStrings)
    {
    }

    public override void Draw() => DrawBox(Button, Check);

    public override bool Mark(int item) => (uint)item == value;

    public override void MovedTo(int item)
    {
        value = (uint)item;
        base.MovedTo(item);
    }

    public override void Press(int item)
    {
        value = (uint)item;
        base.Press(item);
    }

    public override void SetData(object rec)
    {
        base.SetData(rec);
        sel = (int)value;
    }

    public static readonly TStreamableClass StreamableClassTRadioButtons =
        new TStreamableClass("TRadioButtons", () => new TRadioButtons(StreamableInit.streamableInit), 0);

    protected TRadioButtons(StreamableInit init) : base(init) { }

    public new static TStreamable Build() => new TRadioButtons(StreamableInit.streamableInit);
}
