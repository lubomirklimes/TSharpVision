namespace SharpVision;

// ========================================================================
// 9. TListViewer – zobrazení seznamu položek
// ========================================================================
public class TListViewer : TView
{
    public static readonly string Name = "TListViewer";

    public TScrollBar hScrollBar;
    public TScrollBar vScrollBar;
    public short numCols;
    public short topItem;
    public short focused;
    public short range;

    public TListViewer(TRect bounds, ushort aNumCols, TScrollBar hSB, TScrollBar vSB)
        : base(bounds)
    {
        numCols = (short)aNumCols;
        hScrollBar = hSB;
        vScrollBar = vSB;
    }

    public override void ChangeBounds(TRect bounds) { throw new NotImplementedException("TListViewer.ChangeBounds not implemented."); }
    public override void Draw() { throw new NotImplementedException("TListViewer.Draw not implemented."); }
    public virtual void FocusItem(short item) { throw new NotImplementedException("TListViewer.FocusItem not implemented."); }
    public override TPalette GetPalette() { throw new NotImplementedException("TListViewer.GetPalette not implemented."); }
    public virtual void GetText(char[] dest, short item, short maxLen) { throw new NotImplementedException("TListViewer.GetText not implemented."); }
    public virtual bool IsSelected(short item) { throw new NotImplementedException("TListViewer.IsSelected not implemented."); }
    public override void HandleEvent(ref TEvent @event) { throw new NotImplementedException("TListViewer.HandleEvent not implemented."); }
    public virtual void SelectItem(short item) { throw new NotImplementedException("TListViewer.SelectItem not implemented."); }
    public void SetRange(short aRange) { range = aRange; }
    public override void SetState(ushort aState, bool enable) { throw new NotImplementedException("TListViewer.SetState not implemented."); }
    public virtual void FocusItemNum(short item) { throw new NotImplementedException("TListViewer.FocusItemNum not implemented."); }
    public override void ShutDown() { throw new NotImplementedException("TListViewer.ShutDown not implemented."); }

    protected TListViewer(object streamableInit) : base(streamableInit) { throw new NotImplementedException("TListViewer(streamableInit) not implemented."); }
    public override void Write(Opstream os) { throw new NotImplementedException("TListViewer.Write not implemented."); }
    public override object Read(Ipstream isStream) { throw new NotImplementedException("TListViewer.Read not implemented."); }
    public static TStreamable Build() { throw new NotImplementedException("TListViewer.Build not implemented."); }
    public override string StreamableName() { return Name; }
}
