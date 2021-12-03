namespace SharpVision.Menus;

// TMenuBox – varianta menuview, např. v dialogu
public class TMenuBox : TMenuView
{
    public static readonly string Name = "TMenuBox";
    // Statická konstanta pro znaky rámu
    protected static readonly string frameChars = "FRAMECHARS"; // Stub

    public TMenuBox(TRect bounds, TMenu aMenu) : base(bounds, aMenu, null) { }
    public TMenuBox(TRect bounds, TMenu aMenu, TMenuView aParentMenu) : base(bounds, aMenu, aParentMenu) { }

    public override void Draw()
    {
        throw new NotImplementedException("TMenuBox.Draw() není implementováno.");
    }

    public override TRect GetItemRect(TMenuItem item)
    {
        throw new NotImplementedException("TMenuBox.GetItemRect() není implementováno.");
    }

    // Soukromé metody pro kreslení rámu – stub
    private void FrameLine(object drawBuffer, short n)
    {
        throw new NotImplementedException("TMenuBox.FrameLine() není implementováno.");
    }
    private void DrawLine(object drawBuffer)
    {
        throw new NotImplementedException("TMenuBox.DrawLine() není implementováno.");
    }

    protected TMenuBox(object streamableInit) : base(null) { throw new NotImplementedException("TMenuBox(streamableInit) není implementováno."); }

    public static new TStreamable Build()
    {
        throw new NotImplementedException("TMenuBox.Build() není implementováno.");
    }
}
