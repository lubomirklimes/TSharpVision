using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>Linked menu entry representing a command, submenu, or separator.</summary>
public class TMenuItem
{
    /// <summary>Next entry in the same menu, or null at the end of the chain.</summary>
    public TMenuItem Next { get; set; }
    /// <summary>Display label with optional tilde-marked mnemonic; null or empty denotes a separator.</summary>
    public string Name { get; set; }
    /// <summary>Command identifier dispatched on activation; zero identifies a submenu or separator.</summary>
    public ushort Command { get; set; }
    /// <summary>Whether this entry is unavailable for command activation and mnemonic lookup.</summary>
    public bool Disabled { get; set; }
    /// <summary>Accelerator key code; kbNoKey means no accelerator binding.</summary>
    public ushort KeyCode { get; set; }
    /// <summary>Help context identifier associated with this entry.</summary>
    public ushort HelpCtx { get; set; }
    /// <summary>Optional right-aligned text beside a command label, typically its shortcut description.</summary>
    public string Param { get; set; }
    /// <summary>Referenced child menu for an entry whose command is zero.</summary>
    public TMenu SubMenu { get; set; }

    /// <summary>Creates an enabled command entry with a label, accelerator, help context, optional right-hand text, and next node.</summary>
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

    /// <summary>Creates an enabled submenu entry referencing the supplied child menu and optional next node.</summary>
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

    /// <summary>Replaces the immediate next-node link with the supplied entry; null terminates the chain.</summary>
    public void Append(TMenuItem aNext)
    {
        Next = aNext;
    }

    /// <summary>Creates a separator entry with no label or command.</summary>
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

    /// <summary>Appends an item chain inside the last consecutive submenu node and returns the original submenu head; both operands must be non-null.</summary>
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
