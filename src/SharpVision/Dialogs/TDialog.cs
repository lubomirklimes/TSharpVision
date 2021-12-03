using SharpVision.Constants;

namespace SharpVision.Dialogs;

// ------------------------------------------------------------------------
// TDialog
// ------------------------------------------------------------------------
public class TDialog : TWindow
{
    public static readonly string Name = "TDialog";

    // Konstruktor odpovídající TDialog(const TRect& bounds, const char* aTitle)
    public TDialog(TRect bounds, string aTitle)
        : base(bounds, aTitle, Views.wnNoNumber)
    {
        //this.Bounds = bounds;
        // Uložení titulku, nastavení okna apod.
        // aTitle můžete uložit třeba do vlastnosti Title (není zde explicitně uvedena)
    }

    // Přepis metody getPalette – vrací paletu pro dialog
    public virtual TPalette GetPalette()
    {
        throw new NotImplementedException("TDialog.GetPalette() není implementováno.");
    }

    // Přepis metody handleEvent pro dialog
    public override void HandleEvent(ref TEvent @event)
    {
        throw new NotImplementedException("TDialog.HandleEvent() není implementováno.");
    }

    // Metoda valid – kontrola příkazu
    public virtual bool Valid(ushort command)
    {
        throw new NotImplementedException("TDialog.Valid() není implementováno.");
    }
    protected TDialog(StreamableInit init) : base(init) { }

    // Statická tovární metoda pro vytvoření instance přes stream
    public static TStreamable Build()
    {
        throw new NotImplementedException("TDialog.Build() není implementováno.");
    }

    // Pomocná metoda streamableName
    protected virtual string StreamableName()
    {
        return Name;
    }
}
