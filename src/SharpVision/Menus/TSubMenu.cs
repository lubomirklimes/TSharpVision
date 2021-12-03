using SharpVision.Constants;

namespace SharpVision.Menus;

// TSubMenu dědí z TMenuItem.
public class TSubMenu : TMenuItem
{
    // Konstruktor TSubMenu – předává parametry rodičovské třídě.
    public TSubMenu(string aName, ushort aCommand, ushort aHelpCtx = Views.hcNoContext)
         //: base(aName, aCommand,
         : base(aName, aCommand, 0, aHelpCtx, null, null)
    {
        // Další inicializace, pokud je potřeba.
    }

    // Přetížení operátoru + pro sloučení dvou submenu
    public static TSubMenu operator +(TSubMenu s1, TSubMenu s2)
    {
        if (s1 == null)
            throw new ArgumentNullException(nameof(s1));
        if (s2 == null)
            throw new ArgumentNullException(nameof(s2));

        TMenuItem cur = s1;
        while (cur.Next != null)
            cur = cur.Next;
        cur.Next = s2;
        return s1;
    }
}
