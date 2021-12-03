using SharpVision;
using SharpVision.Constants;

namespace SharpVision.Menus;

// TMenuBar – speciální verze TMenuView pro horní lištu menu
public class TMenuBar : TMenuView
{
    public static readonly string Name = "TMenuBar";

    protected TMenu menu; 
    protected TMenuItem current;

    //public TMenuBar(TRect bounds, TMenu aMenu) : base(bounds, aMenu, null) { }
    public TMenuBar(TRect bounds, TMenu aMenu) : base(bounds) 
    {
        menu = aMenu;
        growMode = Views.gfGrowHiX;
        options |= Views.ofPreProcess;
    }

    //public TMenuBar(TRect bounds, TSubMenu aMenu) : base(bounds, new TMenu(aMenu), null) { }
    public TMenuBar(TRect bounds, TSubMenu aMenu) : base(bounds)
    {
        menu = new TMenu(aMenu);
        growMode = Views.gfGrowHiX;
        options |= Views.ofPreProcess;
    }

    ~TMenuBar()
    {
        // Uvolnění zdrojů, pokud je potřeba
    }

    public override void Draw()
    {
        var b = new TDrawBuffer();
        ushort color;

        // 1) připravíme čtyři základní barvy
        ushort cNormal = GetColor(0x0301);
        ushort cSelect = GetColor(0x0604);
        ushort cNormDisabled = GetColor(0x0202);
        ushort cSelDisabled = GetColor(0x0505);

        // 2) předvyplníme celý řádek mezerami s normální barvou
        b.moveChar(0, ' ', cNormal, size.x);

        // 3) pokud máme menu, vykreslíme položky
        if (menu != null)
        {
            int x = 1;
            var p = menu.Items;        // nebo menu.Items, podle vaší C# třídy

            while (p != null)
            {
                if (!string.IsNullOrEmpty(p.Name))   // nebo p.Name
                {
                    int l = p.Name.Length;           // délka textu
                    if (x + l < size.x)
                    {
                        // vybereme barvu podle stavu (disabled / vybraná / normální)
                        if (p.Disabled)             // nebo p.Disabled
                        {
                            color = p == current
                                ? cSelDisabled
                                : cNormDisabled;
                        }
                        else
                        {
                            color = p == current
                                ? cSelect
                                : cNormal;
                        }

                        // vykreslíme "okolo" textu mezeru, pak text, pak mezeru
                        b.moveChar((ushort)x, ' ', color, 1);
                        b.moveCStr((ushort)(x + 1), p.Name, color);
                        b.moveChar((ushort)(x + l + 1), ' ', color, 1);
                    }
                    x += l + 2;
                }
                p = p.Next;  // nebo p.Next
            }
        }

        // 4) napíšeme do bufferu (y=0, výška=1 řádek)
        WriteBuf(0, 0, size.x, 1, b);
    }

    public override TRect GetItemRect(TMenuItem item)
    {
        throw new NotImplementedException("TMenuBar.GetItemRect() není implementováno.");
    }

    protected TMenuBar(object streamableInit) : base(null) { throw new NotImplementedException("TMenuBar(streamableInit) není implementováno."); }

    public static new TStreamable Build()
    {
        throw new NotImplementedException("TMenuBar.Build() není implementováno.");
    }
}
