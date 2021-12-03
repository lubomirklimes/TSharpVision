namespace SharpVision;

// ========================================================================
// 8. TScroller – spojka posuvníků
// ========================================================================
public class TScroller : TView
{
    public static readonly string Name = "TScroller";

    protected byte drawLock;
    protected bool drawFlag;
    protected TScrollBar hScrollBar;
    protected TScrollBar vScrollBar;
    protected TPoint delta;
    protected TPoint limit;

    public TScroller(TRect bounds, TScrollBar hSB, TScrollBar vSB) : base(bounds)
    {
        hScrollBar = hSB;
        vScrollBar = vSB;
    }

    public override void ChangeBounds(TRect bounds) { throw new NotImplementedException("TScroller.ChangeBounds not implemented."); }
    public override TPalette GetPalette() { throw new NotImplementedException("TScroller.GetPalette not implemented."); }
    public override void HandleEvent(ref TEvent @event) { throw new NotImplementedException("TScroller.HandleEvent not implemented."); }
    public virtual void ScrollDraw() { throw new NotImplementedException("TScroller.ScrollDraw not implemented."); }
    public void ScrollTo(short x, short y) { throw new NotImplementedException("TScroller.ScrollTo not implemented."); }
    public void SetLimit(short x, short y) { limit = new TPoint(x, y); }
    public override void SetState(ushort aState, bool enable) { throw new NotImplementedException("TScroller.SetState not implemented."); }
    public void CheckDraw() { throw new NotImplementedException("TScroller.CheckDraw not implemented."); }
    public override void ShutDown() { throw new NotImplementedException("TScroller.ShutDown not implemented."); }

    protected TScroller(object streamableInit) : base(streamableInit) { throw new NotImplementedException("TScroller(streamableInit) not implemented."); }
    public override void Write(Opstream os) { throw new NotImplementedException("TScroller.Write not implemented."); }
    public override object Read(Ipstream isStream) { throw new NotImplementedException("TScroller.Read not implemented."); }
    public static TStreamable Build() { throw new NotImplementedException("TScroller.Build not implemented."); }
    public override string StreamableName() { return Name; }
}
