using TSharpVision.Constants;
namespace TSharpVision;

public class TMenuItem
{
    public TMenuItem Next { get; set; }
    public string Name { get; set; }
    public ushort Command { get; set; }
    public bool Disabled { get; set; }
    public ushort KeyCode { get; set; }
    public ushort HelpCtx { get; set; }
    public string Param { get; set; }
    public TMenu SubMenu { get; set; }

    public TMenuItem(string aName, ushort aCommand, ushort aKeyCode, ushort aHelpCtx = Views.hcNoContext, string p = null, TMenuItem aNext = null)
    {
        Name = aName;
        Command = aCommand;
        KeyCode = aKeyCode;
        HelpCtx = aHelpCtx;
        Param = p;
        Next = aNext;
        Disabled = false;
        SubMenu = new TMenu();
    }

    public TMenuItem(string aName, ushort aKeyCode, TMenu aSubMenu, ushort aHelpCtx = Views.hcNoContext, TMenuItem aNext = null)
    {
        Name = aName;
        KeyCode = aKeyCode;
        SubMenu = aSubMenu;
        HelpCtx = aHelpCtx;
        Next = aNext;
        Disabled = false;
        Command = 0;
        Param = null;
    }

    public void Append(TMenuItem aNext)
    {
        Next = aNext;
    }

    public static TMenuItem NewLine()
    {
        return new TMenuItem(null, 0, 0, Views.hcNoContext, null, null);
    }

    // The upstream code looks for the character after the first '~' in the localized name and
    // compares it case-insensitively against the user keystroke.
    /// <summary>Returns the upper-case hotkey character marked with '~',
    /// or '\0' if the name has no marker.</summary>
    public char HotChar()
    {
        if (string.IsNullOrEmpty(Name)) return '\0';
        int i = Name.IndexOf('~');
        if (i < 0 || i + 1 >= Name.Length) return '\0';
        return char.ToUpperInvariant(Name[i + 1]);
    }

    /// <summary>True when this entry is a submenu reference (no command).</summary>
    public bool IsSubMenu => Command == 0 && SubMenu != null && SubMenu.Items != null;

    public static TSubMenu operator +(TSubMenu s, TMenuItem i)
    {
        if (s == null)
            throw new ArgumentNullException(nameof(s));
        if (i == null)
            throw new ArgumentNullException(nameof(i));

        // Find the last submenu in the chain
        TSubMenu sub = s;
        while (sub.Next is TSubMenu nextSub)
            sub = nextSub;

        if (sub.SubMenu == null)
            throw new InvalidOperationException("SubMenu is null in TSubMenu.");

        if (sub.SubMenu.Items == null)
        {
            sub.SubMenu.Items = i;
            sub.SubMenu.Default = i;
        }
        else
        {
            TMenuItem cur = sub.SubMenu.Items;
            while (cur.Next != null)
                cur = cur.Next;
            cur.Next = i;
        }
        return s;
    }
}
