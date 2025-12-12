using TSharpVision.Constants;
namespace TSharpVision;

// TMenuBox – popup/dropdown menu box created by TMenuView.NewSubView.
public class TMenuBox : TMenuView
{
    public static readonly string Name = "TMenuBox";

    // CP437 box-drawing characters for the menu popup frame.
    // Layout (5 chars per row, accessed at offset n):
    //   n=0  top:        ' ','┌','─','┐',' '
    //   n=5  bottom:     ' ','└','─','┘',' '
    //   n=10 item row:   ' ','│',' ','│',' '
    //   n=15 separator:  ' ','├','─','┤',' '
    // frameLine(b, n): moveBuf(0, chars[n..n+1], 2) + moveChar(2, chars[n+2], size.x-4) + moveBuf(size.x-2, chars[n+3..n+4], 2)
    public static readonly char[] frameCharsArr = new char[]
    {
        ' ','┌','─','┐',' ',   // n=0  top border
        ' ','└','─','┘',' ',   // n=5  bottom border
        ' ','│',' ','│',' ',   // n=10 item row
        ' ','├','─','┤',' ',   // n=15 separator row
    };

    // CP437 0x10 '►' — right-pointing arrow drawn after the item name for submenus.
    // See TSharpVisionGlyphs.MenuSubmenuArrow.
    private static readonly char rightArrow = TSharpVisionGlyphs.MenuSubmenuArrow;

    // Port of upstream getRect(bounds, aMenu) from tmenubox.cc.
    // Takes a full TRect: bounds.a is the anchor (top-left of popup),
    // bounds.b is the owner/screen bottom-right boundary.
    private static TRect ComputeRect(TRect bounds, TMenu menu)
    {
        if (bounds is null) return new TRect(0, 0, 10, 3);
        int w = 10, h = 2;
        if (menu != null)
        {
            for (var p = menu.Items; p != null; p = p.Next)
            {
                if (!string.IsNullOrEmpty(p.Name))
                {
                    int l = CStrLen(p.Name) + 6;
                    if (p.Command == 0) l += 3;
                    else if (!string.IsNullOrEmpty(p.Param)) l += p.Param.Length + 2;
                    if (l > w) w = l;
                }
                h++;
            }
        }
        var r = new TRect(bounds.a.x, bounds.a.y, bounds.b.x, bounds.b.y);
        if (r.a.x + w < r.b.x) r.b.x = r.a.x + w;
        else r.a.x = r.b.x - w;
        if (r.a.y + h < r.b.y) r.b.y = r.a.y + h;
        else r.a.y = r.b.y - h;
        return r;
    }

    // Count visible chars, skipping '~' hotkey markers (cstrlen equivalent).
    private static int CStrLen(string s)
    {
        if (s == null) return 0;
        int n = 0;
        foreach (char c in s) if (c != '~') n++;
        return n;
    }

    public TMenuBox(TRect bounds, TMenu aMenu)
        : base(ComputeRect(bounds, aMenu), aMenu, null)
    {
        state |= Views.sfShadow;
        options |= Views.ofPreProcess;
    }

    public TMenuBox(TRect bounds, TMenu aMenu, TMenuView aParentMenu)
        : base(ComputeRect(bounds, aMenu), aMenu, aParentMenu)
    {
        state |= Views.sfShadow;
        options |= Views.ofPreProcess;
    }

    // Current line color used by FrameLine; set before each WriteBuf call.
    private ushort _lineColor;

    private void FrameLine(TDrawBuffer b, int n)
    {
        ushort cNormal = GetColor(0x0301);
        b.moveBuf(0, new System.ReadOnlySpan<char>(frameCharsArr, n, 2), cNormal, 2);
        b.moveChar(2, frameCharsArr[n + 2], _lineColor, size.x - 4);
        b.moveBuf(size.x - 2, new System.ReadOnlySpan<char>(frameCharsArr, n + 3, 2), cNormal, 2);
    }

    public override void Draw()
    {
        var b = new TDrawBuffer();
        ushort cNormal      = GetColor(0x0301);
        ushort cSelect      = GetColor(0x0604);
        ushort cNormDis     = GetColor(0x0202);
        ushort cSelDis      = GetColor(0x0505);
        int y = 0;

        // Top border
        _lineColor = cNormal;
        FrameLine(b, 0);
        WriteBuf(0, y++, size.x, 1, b);

        if (Menu != null)
        {
            for (var p = Menu.Items; p != null; p = p.Next)
            {
                if (string.IsNullOrEmpty(p.Name))
                {
                    // Separator line
                    _lineColor = cNormal;
                    FrameLine(b, 15);
                }
                else
                {
                    // Normal item or selected item
                    if (p.Disabled)
                        _lineColor = (p == Current) ? cSelDis : cNormDis;
                    else if (p == Current)
                        _lineColor = cSelect;
                    else
                        _lineColor = cNormal;

                    FrameLine(b, 10);
                    b.moveCStr(3, p.Name, _lineColor);
                    if (p.Command == 0)
                        b.putChar(size.x - 4, rightArrow);
                    else if (!string.IsNullOrEmpty(p.Param))
                        b.moveStr(size.x - 3 - p.Param.Length, p.Param, _lineColor);
                }
                WriteBuf(0, y++, size.x, 1, b);
            }
        }

        // Bottom border
        _lineColor = cNormal;
        FrameLine(b, 5);
        WriteBuf(0, y, size.x, 1, b);
    }

    public override TRect GetItemRect(TMenuItem item)
    {
        int y = 1;
        for (var p = Menu?.Items; p != null; p = p.Next)
        {
            if (p == item) return new TRect(2, y, size.x - 2, y + 1);
            y++;
        }
        return new TRect(0, 0, 0, 0);
    }

    protected TMenuBox(object streamableInit) : base((TRect)null) { }

    public static new TStreamable Build() => new TMenuBox(new object());
}
