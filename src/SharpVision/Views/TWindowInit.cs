namespace SharpVision;

// ========================================================================
// 11. TWindowInit a TWindow
// ========================================================================
public class TWindowInit
{
    protected Func<TRect, TFrame> createFrame;

    public TWindowInit(Func<TRect, TFrame> cFrame)
    {
        createFrame = cFrame;
    }
}
