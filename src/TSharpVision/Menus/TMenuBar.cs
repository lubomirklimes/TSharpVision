using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>Horizontal top-level menu view that opens submenus and routes accelerators.</summary>
public class TMenuBar : TMenuView
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TMenuBar";

    /// <summary>Creates a menu bar in owner-relative character-cell bounds, referencing the supplied menu.</summary>
    public TMenuBar(TRect bounds, TMenu? aMenu) : base(bounds, aMenu, null)
    {
        growMode = Views.gfGrowHiX;
        options |= Views.ofPreProcess;
    }

    /// <summary>Creates a menu bar in owner-relative character-cell bounds from a linked submenu chain.</summary>
    public TMenuBar(TRect bounds, TSubMenu aMenu) : base(bounds, new TMenu(aMenu), null)
    {
        growMode = Views.gfGrowHiX;
        options |= Views.ofPreProcess;
    }

    /// <summary>Finalizes the menu bar without additional resource cleanup.</summary>
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

    /// <inheritdoc />
    public override void Draw()
    {
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
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

    /// <inheritdoc />
    public override TRect GetItemRect(TMenuItem item)
    {
        if (Menu == null || item == null) return new TRect(0, 0, 0, 0);
        int x = 1;
        for (TMenuItem? p = Menu.Items; p != null; p = p.Next)
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

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TMenuBar(StreamableInit init) : base(init) { }

    // TMenuBar adds no fields beyond TMenuView — Write/Read are inherited.
    // Build() must return a TMenuBar instance so the registry creates the
    // correct concrete type.
    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public static new TStreamable Build() => new TMenuBar(StreamableInit.streamableInit);

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTMenuBar =
        new TStreamableClass("TMenuBar", () => new TMenuBar(StreamableInit.streamableInit), 0);
}
