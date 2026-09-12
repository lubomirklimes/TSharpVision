namespace TSharpVision;

/// <summary>A cluster of independently checked options represented by bits in the cluster value.</summary>
public class TCheckBoxes : TCluster
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TCheckBoxes";

    // TCheckBoxes::button[] = " [ ] ".
    // 5 chars: leading space, open-bracket, marker-slot (space/X), close-bracket, trailing space.
    // DrawBox writes the marker at col+2 (the slot), label at col+5.
    private const string Button = " [ ] ";

    /// <summary>Creates checkboxes at owner-relative cell bounds, copying labels from the supplied linked list.</summary>
    public TCheckBoxes(TRect bounds, TSItem aStrings)
        : base(bounds, aStrings)
    {
    }

    /// <inheritdoc />
    public override void Draw() => DrawBox(Button, TSharpVisionGlyphs.CheckBoxChecked);

    /// <inheritdoc />
    public override bool Mark(int item) => (value & (1u << item)) != 0;

    /// <inheritdoc />
    public override void Press(int item)
    {
        value ^= (1u << item);
        base.Press(item);
    }

    // ── Streaming ────────────────────────────────────────────────────────
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTCheckBoxes =
        new TStreamableClass("TCheckBoxes", () => new TCheckBoxes(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TCheckBoxes(StreamableInit init) : base(init) { }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TCheckBoxes(StreamableInit.streamableInit);
}
