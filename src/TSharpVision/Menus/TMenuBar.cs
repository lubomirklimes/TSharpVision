using TSharpVision.Constants;
namespace TSharpVision;

public class TMenuBar : TMenuView
{
    public static readonly string Name = "TMenuBar";

    public TMenuBar(TRect bounds, TMenu aMenu) : base(bounds, aMenu, null)
    {
        growMode = Views.gfGrowHiX;
        options |= Views.ofPreProcess;
    }

    public TMenuBar(TRect bounds, TSubMenu aMenu) : base(bounds, new TMenu(aMenu), null)
    {
        growMode = Views.gfGrowHiX;
        options |= Views.ofPreProcess;
    }

    ~TMenuBar()
    {
    }

    // Count visible chars in a '~'-hotkey string (same as TMenuBox.CStrLen).
    private static int CStrLen(string s)
    {
        if (s == null) return 0;
        int n = 0;
        foreach (char c in s) if (c != '~') n++;
        return n;
    }

    public override void Draw()
    {
        var b = new TDrawBuffer();
        ushort color;

        ushort cNormal = GetColor(0x0301);
        ushort cSelect = GetColor(0x0604);
        ushort cNormDisabled = GetColor(0x0202);
        ushort cSelDisabled = GetColor(0x0505);

        b.moveChar(0, ' ', cNormal, size.x);

        if (Menu != null)
        {
            int x = 1;
            var p = Menu.Items;

            while (p != null)
            {
                if (!string.IsNullOrEmpty(p.Name))
                {
                    int l = CStrLen(p.Name);
                    if (x + l < size.x)
                    {
                        if (p.Disabled)
                        {
                            color = (p == Current)
                                ? cSelDisabled
                                : cNormDisabled;
                        }
                        else
                        {
                            color = (p == Current)
                                ? cSelect
                                : cNormal;
                        }

                        b.moveChar((ushort)x, ' ', color, 1);
                        b.moveCStr((ushort)(x + 1), p.Name, color);
                        b.moveChar((ushort)(x + l + 1), ' ', color, 1);
                    }
                    x += l + 2;
                }
                p = p.Next;
            }
        }

        WriteBuf(0, 0, size.x, 1, b);
    }

    public override TRect GetItemRect(TMenuItem item)
    {
        if (Menu == null || item == null) return new TRect(0, 0, 0, 0);
        int x = 1;
        for (TMenuItem p = Menu.Items; p != null; p = p.Next)
        {
            if (string.IsNullOrEmpty(p.Name))
            {
                if (p == item) return new TRect(x, 0, x, 1);
                continue;
            }
            int len = CStrLen(p.Name);
            if (p == item) return new TRect(x, 0, x + len + 2, 1);
            x += len + 2;
        }
        return new TRect(0, 0, 0, 0);
    }

    protected TMenuBar(StreamableInit init) : base(init) { }

    // TMenuBar adds no fields beyond TMenuView — Write/Read are inherited.
    // Build() must return a TMenuBar instance so the registry creates the
    // correct concrete type.
    public static new TStreamable Build() => new TMenuBar(StreamableInit.streamableInit);

    public static readonly TStreamableClass StreamableClassTMenuBar =
        new TStreamableClass("TMenuBar", () => new TMenuBar(StreamableInit.streamableInit), 0);
}
