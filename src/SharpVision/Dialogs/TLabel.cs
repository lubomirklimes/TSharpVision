namespace SharpVision.Dialogs;

// ------------------------------------------------------------------------
// TLabel
// ------------------------------------------------------------------------
public class TLabel : TStaticText
{
    public static readonly string Name = "TLabel";

    protected TView Link;
    protected bool Light;

    // Konstruktor TLabel(const TRect& bounds, const char *aText, TView *aLink)
    public TLabel(TRect bounds, string aText, TView aLink)
        : base(bounds, aText)
    {
        Link = aLink;
    }

    public override void Draw()
    {
        throw new NotImplementedException("TLabel.Draw() není implementováno.");
    }

    public override TPalette GetPalette()
    {
        throw new NotImplementedException("TLabel.GetPalette() není implementováno.");
    }

    public override void HandleEvent(ref TEvent @event)
    {
        throw new NotImplementedException("TLabel.HandleEvent() není implementováno.");
    }

    public virtual void ShutDown()
    {
        throw new NotImplementedException("TLabel.ShutDown() není implementováno.");
    }

    // Konstruktor pro streamable inicializaci
    protected TLabel(object streamableInit)
        : base(streamableInit)
    {
        throw new NotImplementedException("TLabel(streamableInit) není implementováno.");
    }

    protected override void Write(Opstream os)
    {
        throw new NotImplementedException("TLabel.Write() není implementováno.");
    }
    protected override object Read(Ipstream isStream)
    {
        throw new NotImplementedException("TLabel.Read() není implementováno.");
    }

    public static new TStreamable Build()
    {
        throw new NotImplementedException("TLabel.Build() není implementováno.");
    }

    protected override string StreamableName()
    {
        return Name;
    }
}
