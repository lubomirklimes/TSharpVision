using SharpVision.Constants;
namespace SharpVision;

public class TSubMenu : TMenuItem
{
    // Submenu ctor sets command=0, keyCode=aKeyCode, subMenu=new TMenu().
    // Upstream C++ uses the second TMenuItem ctor (name, keyCode, *subMenu, helpCtx, next).
    public TSubMenu(string aName, ushort aKeyCode, ushort aHelpCtx = Views.hcNoContext)
        : base(aName, aKeyCode, new TMenu(), aHelpCtx, null)
    {
        // Command = 0 (set by submenu ctor), SubMenu initialized to new TMenu().
        // The + operator fills SubMenu.Items when items are appended.
    }

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
