using TSharpVision.Constants;
namespace TSharpVision;

public class TMenuView : TView
{
    public const string cpMenuView = "\x02\x03\x04\x05\x06\x07";

    static TPalette palette = new TPalette(cpMenuView, (ushort)(cpMenuView.Length - 1));

    public static readonly string Name = "TMenuView";
    public TMenuView ParentMenu { get; protected set; }
    public TMenu Menu { get; protected set; }
    public TMenuItem Current { get; protected set; }

    public TMenuView(TRect bounds, TMenu aMenu, TMenuView aParent)
        : base(bounds)
    {
        ParentMenu = aParent;
        Menu = aMenu;
        Current = null;
    }

    public TMenuView(TRect bounds) : base(bounds)
    {
        ParentMenu = null;
        Menu = null;
        Current = null;
    }

    public override void SetBounds(TRect bounds)
    {
        base.SetBounds(bounds);
    }

    public override ushort Execute()
    {
        if (InputTrace.Enabled)
            InputTrace.Log("Stage9-TMenuView.Execute(start)", $"view={GetType().Name}");

        bool autoSelect = false;
        ushort result = 0;
        TMenuItem itemShown = null;
        TMenuView target;
        TRect r;
        TEvent e = default;

        Current = Menu?.Default;
        bool running = true;
        while (running)
        {
            GetEvent(ref e);
            bool doSelect = false;
            bool doReturn = false;

            switch (e.What)
            {
                case Events.evMouseDown:
                    if (MouseInView(e.mouse.where) || MouseInOwner(e))
                    {
                        TrackMouse(e);
                        if (size.y == 1) autoSelect = true;
                    }
                    else doReturn = true;
                    break;
                case Events.evMouseUp:
                    TrackMouse(e);
                    if (MouseInOwner(e)) Current = Menu?.Default;
                    else if (Current != null && !string.IsNullOrEmpty(Current.Name))
                        doSelect = true;
                    else doReturn = true;
                    break;
                case Events.evMouseMove:
                    if (e.mouse.buttons != 0)
                    {
                        TrackMouse(e);
                        if (!MouseInView(e.mouse.where) && !MouseInOwner(e) && MouseInMenus(e))
                            doReturn = true;
                    }
                    break;
                case Events.evKeyDown:
                    switch (e.keyDown.keyCode)
                    {
                        case Keys.kbUp:
                        case Keys.kbDown:
                            if (size.y != 1) TrackKey(e.keyDown.keyCode == Keys.kbDown);
                            else if (e.keyDown.keyCode == Keys.kbDown) autoSelect = true;
                            break;
                        case Keys.kbLeft:
                        case Keys.kbRight:
                            if (ParentMenu != null) doReturn = true;
                            else TrackKey(e.keyDown.keyCode == Keys.kbRight);
                            break;
                        case Keys.kbHome:
                        case Keys.kbEnd:
                            if (size.y != 1)
                            {
                                Current = Menu?.Items;
                                if (e.keyDown.keyCode == Keys.kbEnd) TrackKey(false);
                            }
                            break;
                        case Keys.kbEnter:
                            if (size.y == 1) autoSelect = true;
                            doSelect = true;
                            break;
                        case Keys.kbEsc:
                            doReturn = true;
                            if (ParentMenu == null || ParentMenu.size.y != 1)
                                ClearEvent(ref e);
                            break;
                        default:
                        {
                            target = this;
                            char altCh = GetAltChar(e.keyDown.keyCode, e.keyDown.charScan.charCode, e.keyDown.shiftState);
                            bool isAlt = altCh != '\0';
                            if (isAlt && ParentMenu != null) target = TopMenu();
                            char ch = isAlt ? altCh : (char)e.keyDown.charScan.charCode;
                            TMenuItem found = (ch != '\0') ? target.FindItem(ch) : null;
                            if (found == null)
                            {
                                if (!isAlt && (e.keyDown.charScan.charCode < 32 || e.keyDown.charScan.charCode >= 127))
                                {
                                    found = TopMenu().HotKey(e.keyDown.keyCode);
                                    if (found != null && CommandEnabled(found.Command))
                                    { result = found.Command; doReturn = true; }
                                }
                            }
                            else if (target == this)
                            {
                                if (size.y == 1) autoSelect = true;
                                Current = found;
                                doSelect = true;
                            }
                            else if (ParentMenu != target || ParentMenu.Current != found)
                                doReturn = true;
                            break;
                        }
                    }
                    break;
                case Events.evCommand:
                    if (e.message.command == Views.cmMenu)
                    {
                        autoSelect = false;
                        if (ParentMenu != null) doReturn = true;
                    }
                    else doReturn = true;
                    break;
            }

            if (itemShown != Current) { itemShown = Current; DrawView(); }

            if ((doSelect || (!doReturn && autoSelect))
                && Current != null && !string.IsNullOrEmpty(Current.Name))
            {
                if (Current.Command == 0)
                {
                    // Submenu
                    if ((e.What & (Events.evMouseDown | Events.evMouseMove)) != 0)
                        PutEvent(ref e);
                    r = GetItemRect(Current);
                    r.a.x += origin.x;
                    r.a.y = r.b.y + origin.y;
                    r.b = owner.size;
                    target = TopMenu().NewSubView(r, Current.SubMenu, this);
                    result = owner.ExecView(target);
                }
                else if (doSelect)
                    result = Current.Command;
            }

            if (result != 0 && CommandEnabled(result))
            { doReturn = true; ClearEvent(ref e); }
            else result = 0;

            if (doReturn) running = false;
        }

        if (e.What != Events.evNothing && (ParentMenu != null || e.What == Events.evCommand))
            PutEvent(ref e);
        if (Current != null)
        {
            if (Menu != null) Menu.Default = Current;
            Current = null;
            DrawView();
        }
        if (InputTrace.Enabled)
            InputTrace.Log("Stage9-TMenuView.Execute(exit)", $"view={GetType().Name} result={result}");
        return result;
    }

    // Case-insensitive match against the '~'-marked hotkey letter of every enabled named item.
    public virtual TMenuItem FindItem(char ch)
    {
        if (ch == '\0') return null;
        ch = char.ToUpperInvariant(ch);
        for (TMenuItem p = Menu?.Items; p != null; p = p.Next)
        {
            if (string.IsNullOrEmpty(p.Name) || p.Disabled) continue;
            if (p.HotChar() == ch) return p;
        }
        return null;
    }

    // Recursively walks the menu tree looking for an enabled item whose accelerator equals keyCode.
    public static TMenuItem FindHotKey(TMenuItem p, ushort keyCode)
    {
        while (p != null)
        {
            if (!string.IsNullOrEmpty(p.Name))
            {
                if (p.Command == 0)
                {
                    var sub = p.SubMenu?.Items;
                    if (sub != null)
                    {
                        var t = FindHotKey(sub, keyCode);
                        if (t != null) return t;
                    }
                }
                else if (!p.Disabled
                      && p.KeyCode != Keys.kbNoKey
                      && p.KeyCode == keyCode)
                    return p;
            }
            p = p.Next;
        }
        return null;
    }

    public virtual TMenuItem HotKey(ushort keyCode)
    {
        if (Menu == null) return null;
        return FindHotKey(Menu.Items, keyCode);
    }

    // keyToHotKey: route an accelerator key onto the command stream as evCommand.
    public bool KeyToHotKey(ref TEvent ev)
    {
        // Printable characters are not menu accelerators; skip to prevent an
        // uppercase letter's ASCII value from matching a special-key constant
        // (e.g. 'G' == kbPgUp, 'L' == kbPgDn, 'B' == kbF10).
        if (ev.keyDown.charScan.charCode >= 32 && ev.keyDown.charScan.charCode < 127)
            return false;
        var p = HotKey(ev.keyDown.keyCode);
        if (p != null && CommandEnabled(p.Command))
        {
            ev.What = Events.evCommand;
            ev.message.command = p.Command;
            ev.message.infoPtr = null;
            PutEvent(ref ev);
            ClearEvent(ref ev);
            return true;
        }
        return p != null;
    }

    // keyToItem: when any menu item matches the Alt+letter hotkey, put the event
    // back and enter the pulldown modal loop via DoASelect (matches upstream
    // keyToItem which calls putEvent + do_a_select for both command and submenu items).
    public bool KeyToItem(ref TEvent ev)
    {
        char ch = GetAltChar(ev.keyDown.keyCode, ev.keyDown.charScan.charCode,
                              ev.keyDown.shiftState);
        if (ch == '\0') return false;
        if (FindItem(ch) == null) return false;
        PutEvent(ref ev);
        DoASelect(ref ev);
        return true;
    }

    // GetAltChar. tvision encodes Alt+letters as 0x0201..0x021A and Alt+digits as 0x0220..0x0229.
    //
    // The function must ONLY return a non-null char for genuine Alt+key codes.
    // The previous implementation had a spurious fallthrough that returned the plain charCode payload when Alt was
    // not held, causing KeyToItem() (called from the non-modal HandleEvent) to match top-level menu hotkeys on any
    // plain letter keypress (e.g. F opening the File menu).
    //
    // The plain-char fallback belongs exclusively in the Execute() modal
    // loop, which already handles it independently:
    //   char ch = isAlt ? altCh : (char)e.keyDown.charScan.charCode;
    public static char GetAltChar(ushort keyCode, byte charCode, ushort shiftState)
    {
        if (keyCode >= Keys.kbAltA && keyCode <= Keys.kbAltZ)
            return (char)('A' + (keyCode - Keys.kbAltA));
        if (keyCode >= Keys.kbAlt1 && keyCode <= Keys.kbAlt9)
            return (char)('1' + (keyCode - Keys.kbAlt1));
        if (keyCode == Keys.kbAlt0)
            return '0';
        return '\0';
    }

    public virtual TRect GetItemRect(TMenuItem item) => new TRect(0, 0, 0, 0);

    protected void TrackMouse(TEvent e)
    {
        TPoint mouse = MakeLocal(e.mouse.where);
        Current = null;
        for (TMenuItem p = Menu?.Items; p != null; p = p.Next)
            if (GetItemRect(p).Contains(mouse)) { Current = p; return; }
    }

    protected void NextItem()
    {
        if (Menu == null) return;
        if ((Current = Current?.Next) == null) Current = Menu.Items;
    }

    protected void PrevItem()
    {
        if (Menu == null) return;
        TMenuItem p = Current;
        if (p == Menu.Items) p = null;
        do { NextItem(); } while (Current?.Next != p);
    }

    protected void TrackKey(bool findNext)
    {
        if (Current == null) Current = Menu?.Items;
        if (Current == null) return;
        do {
            if (findNext) NextItem(); else PrevItem();
        } while (Current != null && string.IsNullOrEmpty(Current.Name));
    }

    protected bool MouseInOwner(TEvent e)
    {
        if (ParentMenu == null || ParentMenu.size.y != 1) return false;
        TPoint mouse = ParentMenu.MakeLocal(e.mouse.where);
        TMenuItem cur = ParentMenu.Current;
        return cur != null && ParentMenu.GetItemRect(cur).Contains(mouse);
    }

    protected bool MouseInMenus(TEvent e)
    {
        TMenuView p = ParentMenu;
        while (p != null && !p.MouseInView(e.mouse.where)) p = p.ParentMenu;
        return p != null;
    }

    protected TMenuView TopMenu()
    {
        TMenuView p = this;
        while (p.ParentMenu != null) p = p.ParentMenu;
        return p;
    }

    protected void DoASelect(ref TEvent ev)
    {
        if (owner == null) { ClearEvent(ref ev); return; }
        if (InputTrace.Enabled)
            InputTrace.LogEvent($"Stage9-DoASelect(entry,{GetType().Name})", ev);
        PutEvent(ref ev);
        ev.message.command = owner.ExecView(this);
        if (InputTrace.Enabled)
            InputTrace.Log($"Stage9-DoASelect(exit,{GetType().Name})", $"result={ev.message.command}");
        if (ev.message.command != 0 && CommandEnabled(ev.message.command))
        {
            ev.What = Events.evCommand;
            ev.message.infoPtr = null;
            PutEvent(ref ev);
        }
        ClearEvent(ref ev);
    }

    public override ushort GetHelpCtx()
    {
        var c = this;
        while (c != null
            && (c.Current == null
                || c.Current.HelpCtx == Views.hcNoContext
                || string.IsNullOrEmpty(c.Current.Name)))
            c = c.ParentMenu;
        return c != null ? c.Current.HelpCtx : Views.hcNoContext;
    }

    public override TPalette GetPalette() => palette;

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (InputTrace.Enabled && @event.What != Events.evNothing)
            InputTrace.LogEvent($"Stage7-{GetType().Name}.HandleEvent(entry)", @event);
        if (Menu != null)
        {
            switch (@event.What)
            {
                case Events.evMouseDown:
                    if (MouseInView(@event.mouse.where) || (owner?.MouseInView(@event.mouse.where) == true))
                    {
                        if (CommandEnabled(Views.cmMenu))
                            DoASelect(ref @event);
                    }
                    else ClearEvent(ref @event);
                    break;
                case Events.evKeyDown:
                    if (!KeyToItem(ref @event))
                        KeyToHotKey(ref @event);
                    break;
                case Events.evCommand:
                    if (@event.message.command == Views.cmMenu)
                    {
                        if (CommandEnabled(Views.cmMenu))
                            DoASelect(ref @event);
                        else ClearEvent(ref @event);
                    }
                    break;
                case Events.evBroadcast:
                    if (@event.message.command == Views.cmCommandSetChanged)
                    {
                        if (UpdateMenu(Menu)) DrawView();
                    }
                    break;
            }
        }
    }

    public static bool UpdateMenu(TMenu menu)
    {
        if (menu == null) return false;
        bool res = false;
        for (TMenuItem p = menu.Items; p != null; p = p.Next)
        {
            if (string.IsNullOrEmpty(p.Name)) continue;
            if (p.Command == 0)
            {
                if (UpdateMenu(p.SubMenu)) res = true;
            }
            else
            {
                bool enabled = CommandEnabled(p.Command);
                if (p.Disabled == enabled)
                {
                    p.Disabled = !enabled;
                    res = true;
                }
            }
        }
        return res;
    }

    public virtual TMenuView NewSubView(TRect bounds, TMenu aMenu, TMenuView aParentMenu)
    {
        return new TMenuBox(bounds, aMenu, aParentMenu);
    }

    public override void Draw() { }


    protected TMenuView(StreamableInit init) : base(init) { }

    // Writes the menu item linked list recursively (upstream writeMenu).
    // Wire per item: 0xFF sentinel + WriteString(name) + WriteShort(command)
    // + WriteShort(disabled) + WriteShort(keyCode) + WriteShort(helpCtx).
    // If name != null && command == 0: recurse into subMenu.
    // If name != null && command != 0: WriteString(param).
    // After all items: 0x00 sentinel.
    private static void WriteMenu(Opstream os, TMenu menu)
    {
        if (menu == null) { os.WriteByte(0x00); return; }
        for (TMenuItem item = menu.Items; item != null; item = item.Next)
        {
            os.WriteByte(0xFF);
            os.WriteString(item.Name);
            os.WriteShort(item.Command);
            os.WriteShort((ushort)(item.Disabled ? 1 : 0));
            os.WriteShort(item.KeyCode);
            os.WriteShort(item.HelpCtx);
            if (item.Name != null)
            {
                if (item.Command == 0)
                    WriteMenu(os, item.SubMenu);
                else
                    os.WriteString(item.Param);
            }
        }
        os.WriteByte(0x00);
    }

    public override void Write(Opstream os)
    {
        base.Write(os);
        WriteMenu(os, Menu);
    }

    // Reads the menu item linked list recursively (upstream readMenu).
    private static TMenu ReadMenu(Ipstream isStream)
    {
        var menu = new TMenu();
        TMenuItem last = null;
        byte tok = (byte)isStream.ReadByte();
        while (tok != 0)
        {
            var item = new TMenuItem((string)null, (ushort)0, (ushort)0);
            // Append to the end of the linked list (preserve order).
            if (last == null) { menu.Items = item; }
            else              { last.Next  = item; }
            last = item;

            item.Name    = isStream.ReadString();
            item.Command = isStream.ReadShort();
            item.Disabled = isStream.ReadShort() != 0;
            item.KeyCode  = isStream.ReadShort();
            item.HelpCtx  = isStream.ReadShort();

            if (item.Name != null)
            {
                if (item.Command == 0)
                    item.SubMenu = ReadMenu(isStream);
                else
                    item.Param = isStream.ReadString();
            }
            tok = (byte)isStream.ReadByte();
        }
        menu.Default = menu.Items;   // upstream: menu->deflt = menu->items
        return menu;
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        Menu = ReadMenu(isStream);
        ParentMenu = null;
        Current = null;
        return this;
    }

    public static TStreamable Build() => new TMenuView(StreamableInit.streamableInit);

    public static readonly TStreamableClass StreamableClassTMenuView =
        new TStreamableClass("TMenuView", () => new TMenuView(StreamableInit.streamableInit), 0);

    public override string ToString() { return Name; }
}
