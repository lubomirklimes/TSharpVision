namespace SharpVision.Dialogs;

// ------------------------------------------------------------------------
// TParamText
// ------------------------------------------------------------------------
public class TParamText : TStaticText
{
    public static readonly string Name = "TParamText";

    protected short ParamCount;
    protected object ParamList; // Může jít o libovolnou strukturu (pole, seznam) parametrů

    // Konstruktor TParamText(const TRect& bounds, const char *aText, int aParamCount)
    public TParamText(TRect bounds, string aText, int aParamCount)
        : base(bounds, aText)
    {
        ParamCount = (short)aParamCount;
        // Inicializujte ParamList dle potřeby
    }

    public virtual ushort DataSize()
    {
        throw new NotImplementedException("TParamText.DataSize() není implementováno.");
    }

    public override void GetText(out string result)
    {
        // Přidejte logiku pro formátování s ohledem na parametry
        result = Text; // Stub verze
    }

    public override void SetData(object rec)
    {
        throw new NotImplementedException("TParamText.SetData() není implementováno.");
    }

    // Konstruktor pro streamable inicializaci
    protected TParamText(object streamableInit) : base(streamableInit)
    {
        throw new NotImplementedException("TParamText(streamableInit) není implementováno.");
    }

    protected override void Write(Opstream os)
    {
        throw new NotImplementedException("TParamText.Write() není implementováno.");
    }
    protected override object Read(Ipstream isStream)
    {
        throw new NotImplementedException("TParamText.Read() není implementováno.");
    }

    public static new TStreamable Build()
    {
        throw new NotImplementedException("TParamText.Build() není implementováno.");
    }

    protected override string StreamableName()
    {
        return Name;
    }
}
