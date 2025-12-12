using TSharpVision.Constants;
namespace TSharpVision;

public class TCluster : TView
{
    public new static readonly string Name = "TCluster";

    // ofBeVerbose toggles the cmClusterMovedTo/cmClusterPress broadcasts.
    public const ushort ofBeVerbose = 0x800;
    public static ushort ExtraOptions = ofBeVerbose;

    public uint value;
    public int sel;
    public List<string> Strings;

    public TCluster(TRect bounds, TSItem aStrings)
        : base(bounds)
    {
        options |= (ushort)(Views.ofSelectable | Views.ofFirstClick
                          | Views.ofPreProcess | Views.ofPostProcess
                          | ExtraOptions);
        Strings = new List<string>();
        for (TSItem p = aStrings; p != null; p = p.Next)
            Strings.Add(p.Value);
        SetCursor(2, 0);
        ShowCursor();
    }

    ~TCluster() { }

    protected TCluster(StreamableInit init) : base(init)
    {
        Strings = new List<string>();
    }

    public string GetItemText(int item) => Strings[item] ?? string.Empty;

    public override ushort DataSize() => sizeof(ushort);

    private static int CStrLen(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int n = 0;
        for (int i = 0; i < s.Length; i++) if (s[i] != '~') n++;
        return n;
    }

    public void DrawBox(string icon, char marker)
    {
        var b = new TDrawBuffer();
        ushort cNorm = (state & Views.sfDisabled) != 0
            ? GetColor(0x0505) : GetColor(0x0301);
        ushort cSel = GetColor(0x0402);
        for (int i = 0; i <= size.y; i++)
        {
            for (int j = 0; j <= (Strings.Count - 1) / size.y + 1; j++)
            {
                int cur = j * size.y + i;
                int col = Column(cur);
                if (cur < Strings.Count
                    && col + CStrLen(GetItemText(cur)) + 5 < TDrawBuffer.MaxViewWidth
                    && col < size.x)
                {
                    ushort color = (cur == sel && (state & Views.sfSelected) != 0)
                        ? cSel : cNorm;
                    b.moveChar(col, ' ', color, size.x - col);
                    b.moveCStr(col, icon, color);
                    if (Mark(cur))
                        b.putChar(col + 2, marker);
                    b.moveCStr(col + 5, GetItemText(cur), color);
                }
            }
            WriteBuf(0, i, size.x, 1, b);
        }
        SetCursor(Column(sel) + 2, Row(sel));
    }

    public override void GetData(ref object rec)
    {
        rec = (ushort)value;
    }

    public override ushort GetHelpCtx()
    {
        if (helpCtx == Views.hcNoContext) return Views.hcNoContext;
        return (ushort)(helpCtx + sel);
    }

    private static readonly TPalette _palette = new TPalette(
        "\x10\x11\x12\x12\x1F", 5);
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

    private static ushort CtrlToArrow(ushort code) => code;

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What == Events.evMouseDown)
        {
            TPoint mouse = MakeLocal(@event.mouse.where);
            int i = FindSel(mouse);
            if (i != -1) sel = i;
            DrawView();
            do
            {
                mouse = MakeLocal(@event.mouse.where);
                if (FindSel(mouse) == sel) ShowCursor();
                else HideCursor();
            } while (MouseEvent(ref @event, Events.evMouseMove));
            ShowCursor();
            mouse = MakeLocal(@event.mouse.where);
            if (FindSel(mouse) == sel)
            {
                Press(sel);
                DrawView();
            }
            ClearEvent(ref @event);
        }
        else if (@event.What == Events.evKeyDown)
        {
            ushort code = CtrlToArrow(@event.keyDown.keyCode);
            switch (code)
            {
                case Keys.kbUp:
                    if ((state & Views.sfFocused) != 0)
                    {
                        if (--sel < 0) sel = Strings.Count - 1;
                        MovedTo(sel); DrawView(); ClearEvent(ref @event);
                    }
                    break;
                case Keys.kbDown:
                    if ((state & Views.sfFocused) != 0)
                    {
                        if (++sel >= Strings.Count) sel = 0;
                        MovedTo(sel); DrawView(); ClearEvent(ref @event);
                    }
                    break;
                case Keys.kbRight:
                    if ((state & Views.sfFocused) != 0)
                    {
                        sel += size.y;
                        if (sel >= Strings.Count)
                        {
                            sel = (sel + 1) % size.y;
                            if (sel >= Strings.Count) sel = 0;
                        }
                        MovedTo(sel); DrawView(); ClearEvent(ref @event);
                    }
                    break;
                case Keys.kbLeft:
                    if ((state & Views.sfFocused) != 0)
                    {
                        if (sel > 0)
                        {
                            sel -= size.y;
                            if (sel < 0)
                            {
                                sel = ((Strings.Count + size.y - 1) / size.y) * size.y + sel - 1;
                                if (sel >= Strings.Count) sel = Strings.Count - 1;
                            }
                        }
                        else sel = Strings.Count - 1;
                        MovedTo(sel); DrawView(); ClearEvent(ref @event);
                    }
                    break;
                default:
                {
                    for (int i = 0; i < Strings.Count; i++)
                    {
                        char c = ExtractHotKey(GetItemText(i));
                        bool altMatch = GetAltCode(c) == @event.keyDown.keyCode;
                        bool asciiMatch = c != '\0'
                            && (owner != null && owner.phase == TView.phaseType.phPostProcess
                                || (state & Views.sfFocused) != 0)
                            && char.ToUpperInvariant((char)@event.keyDown.charScan.charCode) == c;
                        if (altMatch || asciiMatch)
                        {
                            Select();
                            sel = i;
                            MovedTo(sel);
                            Press(sel);
                            DrawView();
                            ClearEvent(ref @event);
                            return;
                        }
                    }
                    if (@event.keyDown.charScan.charCode == ' '
                        && (state & Views.sfFocused) != 0)
                    {
                        Press(sel);
                        DrawView();
                        ClearEvent(ref @event);
                    }
                    break;
                }
            }
        }
    }

    public override void SetData(object rec)
    {
        if (rec is ushort u) value = u;
        else if (rec is uint ui) value = ui;
        else if (rec is int ii) value = (uint)ii;
        DrawView();
    }

    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if (aState == Views.sfSelected || aState == Views.sfDisabled)
            DrawView();
    }

    public virtual bool Mark(int item) => false;

    public virtual void MovedTo(int item)
    {
        if (owner != null && (options & ofBeVerbose) != 0)
            owner.Message(Events.evBroadcast, Views.cmClusterMovedTo, this);
    }

    public virtual void Press(int item)
    {
        if (owner != null && (options & ofBeVerbose) != 0)
            owner.Message(Events.evBroadcast, Views.cmClusterPress, this);
    }

    protected int Column(int item)
    {
        if (item < size.y) return 0;
        int width = 0;
        int col = -6;
        int l = 0;
        for (int i = 0; i <= item; i++)
        {
            if (i % size.y == 0)
            {
                col += width + 6;
                width = 0;
            }
            if (i < Strings.Count) l = CStrLen(GetItemText(i));
            if (l > width) width = l;
        }
        return col;
    }

    protected int FindSel(TPoint p)
    {
        TRect r = GetExtent();
        if (!r.Contains(p)) return -1;
        int i = 0;
        while (p.x >= Column(i + size.y)) i += size.y;
        int s = i + p.y;
        if (s >= Strings.Count) return -1;
        return s;
    }

    protected int Row(int item) => size.y == 0 ? 0 : item % size.y;

    // ── Streaming ────────────────────────────────────────────────────────
    // Wire: TView base + WriteShort(value) + WriteInt(sel) + WritePointer(strings as TStringCollection).
    public static readonly TStreamableClass StreamableClassTCluster =
        new TStreamableClass("TCluster", () => new TCluster(StreamableInit.streamableInit), 0);

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteShort((ushort)value);
        os.WriteInt((uint)sel);
        var sc = new TStringCollection();
        foreach (var s in Strings) sc.Insert(s);
        os.WritePointer(sc);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        value = isStream.ReadShort();
        sel   = (int)isStream.ReadInt();
        Strings = new List<string>();
        if (isStream.ReadPointer() is TStringCollection sc)
            Strings.AddRange(sc.Items);
        SetCursor(2, 0);
        ShowCursor();
        return this;
    }

    public new static TStreamable Build() => new TCluster(StreamableInit.streamableInit);
}
