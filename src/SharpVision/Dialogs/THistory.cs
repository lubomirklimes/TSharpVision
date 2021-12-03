namespace SharpVision.Dialogs;

// ------------------------------------------------------------------------
// THistory
// ------------------------------------------------------------------------
public class THistory : TView
{
    public static readonly string Name = "THistory";

    protected TInputLine Link;
    protected ushort HistoryId;

    // Konstruktor THistory(const TRect& bounds, TInputLine *aLink, ushort aHistoryId)
    public THistory(TRect bounds, TInputLine aLink, ushort aHistoryId)
        : base(bounds)
    {
        //this.Bounds = bounds;
        Link = aLink;
        HistoryId = aHistoryId;
    }

    public override void Draw()
    {
        throw new NotImplementedException("THistory.Draw() není implementováno.");
    }

    public override TPalette GetPalette()
    {
        throw new NotImplementedException("THistory.GetPalette() není implementováno.");
    }

    public override void HandleEvent(ref TEvent @event)
    {
        throw new NotImplementedException("THistory.HandleEvent() není implementováno.");
    }

    public virtual THistoryWindow InitHistoryWindow(TRect bounds)
    {
        throw new NotImplementedException("THistory.InitHistoryWindow() není implementováno.");
    }

    public virtual void ShutDown()
    {
        throw new NotImplementedException("THistory.ShutDown() není implementováno.");
    }

    // Soukromá statická konstanta icon
    protected static readonly string icon = "ICON"; // Stub hodnota

    // Konstruktor pro streamable inicializaci
    protected THistory(object streamableInit)
        : base(streamableInit)
    {
        throw new NotImplementedException("THistory(streamableInit) není implementováno.");
    }

    protected virtual void Write(Opstream os)
    {
        throw new NotImplementedException("THistory.Write() není implementováno.");
    }

    protected virtual object Read(Ipstream isStream)
    {
        throw new NotImplementedException("THistory.Read() není implementováno.");
    }

    public static TStreamable Build()
    {
        throw new NotImplementedException("THistory.Build() není implementováno.");
    }

    protected virtual string StreamableName()
    {
        return Name;
    }
}
