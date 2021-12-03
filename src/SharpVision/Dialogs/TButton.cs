namespace SharpVision;

// ------------------------------------------------------------------------
// TButton
// ------------------------------------------------------------------------
public class TButton : TView
{
    public static readonly string Name = "TButton";

    public string Title { get; protected set; }
    public ushort Command { get; protected set; }
    public byte Flags { get; protected set; }
    public bool AmDefault { get; protected set; }

    // Konstruktor TButton(const TRect& bounds, const char *aTitle, ushort aCommand, ushort aFlags)
    public TButton(TRect bounds, string aTitle, ushort aCommand, ushort aFlags)
        : base(bounds)
    {
        //this.Bounds = bounds;
        Title = aTitle;
        Command = aCommand;
        Flags = (byte)aFlags;
    }

    // Destruktor – obvykle není třeba v C# speciálně implementovat
    ~TButton()
    {
        // Úklid dle potřeby
    }

    // Přepis metody draw
    public override void Draw()
    {
        throw new NotImplementedException("TButton.Draw() není implementováno.");
    }

    // Metoda drawState – vykreslí stav tlačítka (např. tlačítko stisknuto/uvolebně)
    public void DrawState(bool down)
    {
        throw new NotImplementedException("TButton.DrawState() není implementováno.");
    }

    // Přepis metody getPalette
    public virtual TPalette GetPalette()
    {
        throw new NotImplementedException("TButton.GetPalette() není implementováno.");
    }

    // Přepis metody handleEvent
    public override void HandleEvent(ref TEvent @event)
    {
        throw new NotImplementedException("TButton.HandleEvent() není implementováno.");
    }

    // Metoda makeDefault – nastaví, zda je tlačítko výchozí
    public void MakeDefault(bool enable)
    {
        AmDefault = enable;
        // Možná změna vzhledu dle toho, zda je default
    }

    // Metoda press – simuluje stisk tlačítka
    public virtual void Press()
    {
        throw new NotImplementedException("TButton.Press() není implementováno.");
    }

    // Přepis metody setState
    public virtual void SetState(ushort aState, bool enable)
    {
        throw new NotImplementedException("TButton.SetState() není implementováno.");
    }

    // Soukromé pomocné metody: drawTitle, pressButton, getActiveRect – implementujte dle potřeby
    private void DrawTitle(object drawBuffer, int x, int y, ushort aState, bool flag)
    {
        throw new NotImplementedException("TButton.DrawTitle() není implementováno.");
    }
    private void PressButton(TEvent ev)
    {
        throw new NotImplementedException("TButton.PressButton() není implementováno.");
    }
    private TRect GetActiveRect()
    {
        throw new NotImplementedException("TButton.GetActiveRect() není implementováno.");
    }

    // Konstruktor pro streamable inicializaci
    protected TButton(object streamableInit) : base(streamableInit)
    {
        throw new NotImplementedException("TButton(streamableInit) není implementováno.");
    }

    // Metody pro streamování
    protected virtual void Write(Opstream os)
    {
        throw new NotImplementedException("TButton.Write() není implementováno.");
    }
    protected virtual object Read(Ipstream isStream)
    {
        throw new NotImplementedException("TButton.Read() není implementováno.");
    }

    public static TStreamable Build()
    {
        throw new NotImplementedException("TButton.Build() není implementováno.");
    }

    protected virtual string StreamableName()
    {
        return Name;
    }
}
