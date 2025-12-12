namespace TSharpVision;

public class THistInit
{
    protected Func<TRect, TWindow, ushort, TListViewer> createListViewer;

    public THistInit(Func<TRect, TWindow, ushort, TListViewer> cListViewer)
    {
        createListViewer = cListViewer;
    }
}
