namespace SharpVision.Dialogs;

// ------------------------------------------------------------------------
// TCheckBoxes
// ------------------------------------------------------------------------
public class TCheckBoxes : TCluster
{
    public static readonly string Name = "TCheckBoxes";

    // Konstruktor TCheckBoxes(const TRect& bounds, TSItem *aStrings)
    public TCheckBoxes(TRect bounds, TSItem aStrings)
        : base(bounds, aStrings)
    {
    }

    public override void Draw()
    {
        throw new NotImplementedException("TCheckBoxes.Draw() není implementováno.");
    }

    public override bool Mark(int item)
    {
        throw new NotImplementedException("TCheckBoxes.Mark() není implementováno.");
    }

    public override void Press(int item)
    {
        throw new NotImplementedException("TCheckBoxes.Press() není implementováno.");
    }

    // Konstruktor pro streamable inicializaci
    protected TCheckBoxes(object streamableInit)
        : base(streamableInit)
    {
        throw new NotImplementedException("TCheckBoxes(streamableInit) není implementováno.");
    }

    public static new TStreamable Build()
    {
        throw new NotImplementedException("TCheckBoxes.Build() není implementováno.");
    }

    protected override string StreamableName()
    {
        return Name;
    }
}
