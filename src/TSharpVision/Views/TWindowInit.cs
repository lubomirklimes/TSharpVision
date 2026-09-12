namespace TSharpVision;

/// <summary>Stores a customizable factory for creating a window frame.</summary>
public class TWindowInit
{
    /// <summary>Factory receiving character-cell bounds for the frame it creates.</summary>
    protected Func<TRect, TFrame> createFrame;

    /// <summary>Stores the supplied frame factory without invoking it.</summary>
    public TWindowInit(Func<TRect, TFrame> cFrame)
    {
        createFrame = cFrame;
    }
}
