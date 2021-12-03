namespace SharpVision;

// ========================================================================
// 7. TScrollBar – posuvník
// ========================================================================
public class TScrollBar : TView
{
    public static readonly string Name = "TScrollBar";

    public short value;
    public char[] chars = new char[5];
    public short minVal;
    public short maxVal;
    public short pgStep;
    public short arStep;

    // Statické placeholdery
    protected static readonly char[] vChars = new char[5] { 'v', 'v', 'v', 'v', 'v' };
    protected static readonly char[] hChars = new char[5] { 'h', 'h', 'h', 'h', 'h' };

    public TScrollBar(TRect bounds) : base(bounds) { }

    public override void Draw() { throw new NotImplementedException("TScrollBar.Draw not implemented."); }
    public override TPalette GetPalette() { throw new NotImplementedException("TScrollBar.GetPalette not implemented."); }
    public override void HandleEvent(ref TEvent @event) { throw new NotImplementedException("TScrollBar.HandleEvent not implemented."); }
    public virtual void ScrollDraw() { throw new NotImplementedException("TScrollBar.ScrollDraw not implemented."); }
    public virtual short ScrollStep(short part) { throw new NotImplementedException("TScrollBar.ScrollStep not implemented."); }
    public void SetParams(short aValue, short aMin, short aMax, short aPgStep, short aArStep) { value = aValue; minVal = aMin; maxVal = aMax; pgStep = aPgStep; arStep = aArStep; }
    public void SetRange(short aMin, short aMax) { minVal = aMin; maxVal = aMax; }
    public void SetStep(short aPgStep, short aArStep) { pgStep = aPgStep; arStep = aArStep; }
    public void SetValue(short aValue) { value = aValue; }
    public void DrawPos(short pos) { throw new NotImplementedException("TScrollBar.DrawPos not implemented."); }
    public short GetPos() { throw new NotImplementedException("TScrollBar.GetPos not implemented."); }
    public short GetSize() { throw new NotImplementedException("TScrollBar.GetSize not implemented."); }
    private short GetPartCode() { throw new NotImplementedException("TScrollBar.GetPartCode not implemented."); }

    protected TScrollBar(object streamableInit) : base(streamableInit) { throw new NotImplementedException("TScrollBar(streamableInit) not implemented."); }
    public override void Write(Opstream os) { throw new NotImplementedException("TScrollBar.Write not implemented."); }
    public override object Read(Ipstream isStream) { throw new NotImplementedException("TScrollBar.Read not implemented."); }
    public static TStreamable Build() { throw new NotImplementedException("TScrollBar.Build not implemented."); }
    public override string StreamableName() { return Name; }
}
