namespace SharpVision.Dialogs;

// ------------------------------------------------------------------------
// TStaticText
// ------------------------------------------------------------------------
public class TStaticText : TView
{
    public static readonly string Name = "TStaticText";

    protected string Text;

    // Konstruktor TStaticText(const TRect& bounds, const char *aText)
    public TStaticText(TRect bounds, string aText)
        : base(bounds)
    {
        //this.Bounds = bounds;
        Text = aText;
    }

    ~TStaticText()
    {
        // Úklid zdrojů, pokud je třeba
    }

    public override void Draw()
    {
        throw new NotImplementedException("TStaticText.Draw() není implementováno.");
    }

    public virtual TPalette GetPalette()
    {
        throw new NotImplementedException("TStaticText.GetPalette() není implementováno.");
    }

    public virtual void GetText(out string result)
    {
        result = Text;
    }

    // Konstruktor pro streamable inicializaci
    protected TStaticText(object streamableInit)
        : base(streamableInit)
    {
        throw new NotImplementedException("TStaticText(streamableInit) není implementováno.");
    }

    protected virtual void Write(Opstream os)
    {
        throw new NotImplementedException("TStaticText.Write() není implementováno.");
    }
    protected virtual object Read(Ipstream isStream)
    {
        throw new NotImplementedException("TStaticText.Read() není implementováno.");
    }

    public static TStreamable Build()
    {
        throw new NotImplementedException("TStaticText.Build() není implementováno.");
    }

    protected virtual string StreamableName()
    {
        return Name;
    }
}
