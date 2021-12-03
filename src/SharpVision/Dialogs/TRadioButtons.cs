using SharpVision.Dialogs;

namespace SharpVision;

// ------------------------------------------------------------------------
// TRadioButtons
// ------------------------------------------------------------------------
public class TRadioButtons : TCluster
{
    public static readonly string Name = "TRadioButtons";

    // Konstruktor TRadioButtons(const TRect& bounds, TSItem *aStrings)
    public TRadioButtons(TRect bounds, TSItem aStrings)
        : base(bounds, aStrings)
    {
    }

    // Přepis metody draw
    public override void Draw()
    {
        throw new NotImplementedException("TRadioButtons.Draw() není implementováno.");
    }

    public override bool Mark(int item)
    {
        throw new NotImplementedException("TRadioButtons.Mark() není implementováno.");
    }

    public override void MovedTo(int item)
    {
        throw new NotImplementedException("TRadioButtons.MovedTo() není implementováno.");
    }

    public override void Press(int item)
    {
        throw new NotImplementedException("TRadioButtons.Press() není implementováno.");
    }

    public override void SetData(object rec)
    {
        throw new NotImplementedException("TRadioButtons.SetData() není implementováno.");
    }

    // Konstruktor pro streamable inicializaci
    protected TRadioButtons(object streamableInit)
        : base(streamableInit)
    {
        throw new NotImplementedException("TRadioButtons(streamableInit) není implementováno.");
    }

    public static new TStreamable Build()
    {
        throw new NotImplementedException("TRadioButtons.Build() není implementováno.");
    }

    protected override string StreamableName()
    {
        return Name;
    }
}
