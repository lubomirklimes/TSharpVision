using TSharpVision.Constants;
namespace TSharpVision;

public class TButton : TView
{
    public new static readonly string Name = "TButton";

    public string Title { get; protected set; }
    public ushort Command { get; protected set; }
    public byte Flags { get; protected set; }
    public bool AmDefault { get; protected set; }

    public TButton(TRect bounds, string aTitle, ushort aCommand, ushort aFlags)
        : base(bounds)
    {
        Title = aTitle;
        Command = aCommand;
        Flags = (byte)aFlags;
        AmDefault = (aFlags & ButtonConstants.bfDefault) != 0;
        options |= (ushort)(Views.ofSelectable | Views.ofFirstClick
                          | Views.ofPreProcess | Views.ofPostProcess);
        eventMask |= Events.evBroadcast;
        if (!CommandEnabled(aCommand))
            state |= Views.sfDisabled;
    }

    ~TButton()
    {
    }

    public override void Draw() => DrawState(false);

    private void DrawTitle(TDrawBuffer b, int s, int i, ushort cButton, bool down)
    {
        string theTitle = Title ?? string.Empty;
        int l;
        if ((Flags & ButtonConstants.bfLeftJust) != 0)
            l = 1;
        else
        {
            int titleLen = 0;
            for (int k = 0; k < theTitle.Length; k++) if (theTitle[k] != '~') titleLen++;
            l = (s - titleLen - 1) / 2;
            if (l < 1) l = 1;
        }
        b.moveCStr(i + l, theTitle, cButton);
    }

    // TButton::shadows[] = "\xDC\xDB\xDF".
    // cp437→Unicode:  0xDC=▄ (U+2584 lower half block),
    //                 0xDB=█ (U+2588 full block),
    //                 0xDF=▀ (U+2580 upper half block).
    // shadows[0] — right edge, first body row
    // shadows[1] — right edge, subsequent body rows
    // shadows[2] — fill char for the bottom shadow row
    private static readonly char[] Shadows = { '\u2584', '\u2588', '\u2580' };

    public void DrawState(bool down)
    {
        ushort cButton, cShadow;
        char ch = ' ';
        int i;
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);

        if ((state & Views.sfDisabled) != 0)
            cButton = GetColor(0x0404);
        else
        {
            cButton = GetColor(0x0501);
            if ((state & Views.sfActive) != 0)
            {
                if ((state & Views.sfSelected) != 0) cButton = GetColor(0x0703);
                else if (AmDefault) cButton = GetColor(0x0602);
            }
        }
        cShadow = GetColor(8);
        int s = size.x - 1;
        int T = size.y / 2 - 1;

        for (int y = 0; y <= size.y - 2; y++)
        {
            b.moveChar(0, ' ', cButton, size.x);
            b.putAttribute(0, cShadow);
            if (down)
            {
                b.putAttribute(1, cShadow);
                i = 2;
            }
            else
            {
                b.putAttribute(s, cShadow);
                // Stamp shadow block chars on the right edge (▄ on first row, █ on subsequent rows);
                // bottom row will use ▀ (set via ch below).
                b.putChar(s, y == 0 ? Shadows[0] : Shadows[1]);
                ch = Shadows[2];
                i = 1;
            }
            if (y == T && Title != null)
                DrawTitle(b, s, i, cButton, down);
            WriteLine(0, y, size.x, 1, b);
        }
        b.moveChar(0, ' ', cShadow, 2);
        b.moveChar(2, ch, cShadow, s - 1);
        WriteLine(0, size.y - 1, size.x, 1, b);
    }

    private static readonly TPalette _palette = new TPalette(
        "\x0A\x0B\x0C\x0D\x0E\x0E\x0E\x0F", 8);
    public override TPalette GetPalette() => _palette;

    private static char ExtractHotKey(string s)
    {
        if (string.IsNullOrEmpty(s)) return '\0';
        for (int i = 0; i < s.Length - 1; i++)
            if (s[i] == '~') return char.ToUpperInvariant(s[i + 1]);
        return '\0';
    }

    private static ushort GetAltCode(char c)
    {
        if (c >= 'A' && c <= 'Z') return (ushort)(Keys.kbAltA + (c - 'A'));
        return Keys.kbNoKey;
    }

    public override void HandleEvent(ref TEvent @event)
    {
        TPoint mouse;
        TRect clickRect = GetExtent();
        clickRect.a.x++;
        clickRect.b.x--;
        clickRect.b.y--;
        char c = ExtractHotKey(Title);
        bool down = false;

        if (@event.What == Events.evMouseDown)
        {
            mouse = MakeLocal(@event.mouse.where);
            if (!clickRect.Contains(mouse)) ClearEvent(ref @event);
        }
        base.HandleEvent(ref @event);

        switch (@event.What)
        {
            case Events.evMouseDown:
                clickRect.b.x++;
                do
                {
                    mouse = MakeLocal(@event.mouse.where);
                    if (down != clickRect.Contains(mouse))
                    {
                        down = !down;
                        DrawState(down);
                    }
                } while (MouseEvent(ref @event, Events.evMouseMove));
                if (down) { Press(); DrawState(false); }
                ClearEvent(ref @event);
                break;

            case Events.evKeyDown:
            {
                ushort altCode = GetAltCode(c);
                bool altMatch = altCode != Keys.kbNoKey
                                && @event.keyDown.keyCode == altCode;
                bool postProcMatch = owner != null
                                     && owner.phase == TView.phaseType.phPostProcess
                                     && c != '\0'
                                     && char.ToUpperInvariant(
                                         (char)@event.keyDown.charScan.charCode) == c;
                // kbEnter or Space on a focused button activates it.
                bool focusedActivate = (state & Views.sfFocused) != 0
                                       && (@event.keyDown.charScan.charCode == ' '
                                           || @event.keyDown.keyCode == Keys.kbEnter);
                if (altMatch || postProcMatch || focusedActivate)
                {
                    Press();
                    ClearEvent(ref @event);
                }
                break;
            }

            case Events.evBroadcast:
                switch (@event.message.command)
                {
                    case Views.cmDefault:
                        if (AmDefault && (state & Views.sfDisabled) == 0)
                        {
                            Press();
                            ClearEvent(ref @event);
                        }
                        break;
                    case Views.cmGrabDefault:
                    case Views.cmReleaseDefault:
                        if ((Flags & ButtonConstants.bfDefault) != 0)
                        {
                            AmDefault = @event.message.command == Views.cmReleaseDefault;
                            DrawView();
                        }
                        break;
                    case Views.cmCommandSetChanged:
                        bool isDisabled = (state & Views.sfDisabled) != 0;
                        bool enabled = CommandEnabled(Command);
                        if (isDisabled == enabled)
                        {
                            SetState(Views.sfDisabled, !enabled);
                            DrawView();
                        }
                        break;
                }
                break;
        }
    }

    public void MakeDefault(bool enable)
    {
        if ((Flags & ButtonConstants.bfDefault) == 0)
        {
            owner?.Message(
                Events.evBroadcast,
                enable ? Views.cmGrabDefault : Views.cmReleaseDefault,
                this as IInfo);
            AmDefault = enable;
            DrawView();
        }
    }

    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if ((aState & (Views.sfSelected | Views.sfActive)) != 0)
        {
            if (!enable)
            {
                state &= unchecked((ushort)~Views.sfFocused);
                MakeDefault(false);
            }
            DrawView();
        }
        if ((aState & Views.sfFocused) != 0)
            MakeDefault(enable);
    }

    public virtual void Press()
    {
        owner?.Message(Events.evBroadcast, Views.cmRecordHistory, null);
        if ((Flags & ButtonConstants.bfBroadcast) != 0)
            owner?.Message(Events.evBroadcast, Command, this as IInfo);
        else
        {
            TEvent e = default;
            e.What = Events.evCommand;
            e.message.command = Command;
            e.message.infoPtr = this as IInfo;
            PutEvent(ref e);
        }
    }

    private TRect GetActiveRect()
    {
        TRect r = GetExtent();
        r.b.x--;
        return r;
    }

    public static readonly TStreamableClass StreamableClassTButton =
        new TStreamableClass("TButton", () => new TButton(StreamableInit.streamableInit), 0);

    protected TButton(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteString(Title);
        os.WriteShort(Command);
        os.WriteByte(Flags);
        os.WriteInt(AmDefault ? 1u : 0u);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        Title = isStream.ReadString();
        Command = isStream.ReadShort();
        Flags = (byte)isStream.ReadByte();
        AmDefault = isStream.ReadInt() != 0;
        return this;
    }

    public new static TStreamable Build() => new TButton(StreamableInit.streamableInit);
}
