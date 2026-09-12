namespace TSharpVision;

/// <summary>Stores a factory for constructing a history-list viewer.</summary>
public class THistInit
{
    /// <summary>Factory receiving viewer bounds, containing window, and history ID.</summary>
    protected Func<TRect, TWindow, ushort, TListViewer> createListViewer;

    /// <summary>Stores the supplied history-viewer factory for use by derived initialization code.</summary>
    public THistInit(Func<TRect, TWindow, ushort, TListViewer> cListViewer)
    {
        createListViewer = cListViewer;
    }
}
