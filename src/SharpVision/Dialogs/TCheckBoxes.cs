namespace SharpVision;

public class TCheckBoxes : TCluster
{
    public new static readonly string Name = "TCheckBoxes";

    // TCheckBoxes::button[] = " [ ] ".
    // 5 chars: leading space, open-bracket, marker-slot (space/X), close-bracket, trailing space.
    // DrawBox writes the marker at col+2 (the slot), label at col+5.
    private const string Button = " [ ] ";

    public TCheckBoxes(TRect bounds, TSItem aStrings)
        : base(bounds, aStrings)
    {
    }

    public override void Draw() => DrawBox(Button, SharpVisionGlyphs.CheckBoxChecked);

    public override bool Mark(int item) => (value & (1u << item)) != 0;

    public override void Press(int item)
    {
        value ^= (1u << item);
        base.Press(item);
    }

    // ── Streaming ────────────────────────────────────────────────────────
    public static readonly TStreamableClass StreamableClassTCheckBoxes =
        new TStreamableClass("TCheckBoxes", () => new TCheckBoxes(StreamableInit.streamableInit), 0);

    protected TCheckBoxes(StreamableInit init) : base(init) { }

    public new static TStreamable Build() => new TCheckBoxes(StreamableInit.streamableInit);
}
