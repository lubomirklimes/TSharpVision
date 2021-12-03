using SharpVision.Constants;

namespace SharpVision.Dialogs;

// ------------------------------------------------------------------------
// THistoryWindow
// ------------------------------------------------------------------------
public class THistoryWindow : TWindow // v C# nelze dědit z více tříd, proto virtuální dědičnost nahrazujeme kompozicí
{
    public static readonly string Name = "THistoryWindow";

    // Pomocí kompozice – zahrnujeme instanci THistInit
    protected THistInit HistInitHelper;
    protected TListViewer Viewer;

    // Konstruktor THistoryWindow(const TRect& bounds, ushort historyId)
    public THistoryWindow(TRect bounds, ushort historyId)
        : base(bounds, /*0*/ null, Views.wnNoNumber)
    {
        //this.Bounds = bounds;
        // Inicializace pomocí tovární metody; předpokládáme, že initViewer je dostupná
        HistInitHelper = new THistInit(InitViewer);
        //Viewer = HistInitHelper.createListViewer(bounds, this, historyId);
    }

    /*
    // TODO: THistoryWindow::THistoryWindow( const TRect& bounds,
                                ushort historyId ) :
    THistInit( &THistoryWindow::initViewer ),
    TWindow( bounds, 0, wnNoNumber),
    TWindowInit( &THistoryWindow::initFrame ) { ... }
     */

    public virtual TPalette GetPalette()
    {
        throw new NotImplementedException("THistoryWindow.GetPalette() není implementováno.");
    }

    public virtual void GetSelection(char[] dest)
    {
        throw new NotImplementedException("THistoryWindow.GetSelection() není implementováno.");
    }

    // Statická metoda initViewer
    public static TListViewer InitViewer(TRect bounds, TWindow owner, ushort historyId)
    {
        throw new NotImplementedException("THistoryWindow.InitViewer() není implementováno.");
    }
}
