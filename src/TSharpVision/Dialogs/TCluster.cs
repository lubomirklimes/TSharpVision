using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>Base for grouped labeled options with keyboard and mouse selection, used by checkboxes and radio buttons.</summary>
public class TCluster : TView
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TCluster";

    /// <summary>Enables broadcasts when focus moves between options or an option is activated.</summary>
    public const ushort ofBeVerbose = 0x800;
    /// <summary>Additional view options copied into newly constructed clusters.</summary>
    public static ushort ExtraOptions = ofBeVerbose;

    /// <summary>Option value; derived controls interpret it as a bit mask or selected-option index.</summary>
    public uint value;
    /// <summary>Zero-based index of the option with the cluster's keyboard focus.</summary>
    public int sel;
    /// <summary>Option labels in display order, including any tilde mnemonic markers.</summary>
    public List<string> Strings;
    /// <summary>Per-option enabled bits; bits zero through 31 correspond to option indices.</summary>
    protected uint enableMask;

    /// <summary>Creates a selectable cluster at owner-relative cell bounds and copies labels from the linked list.</summary>
    public TCluster(TRect bounds, TSItem aStrings)
        : base(bounds)
    {
        options |= (ushort)(Views.ofSelectable | Views.ofFirstClick
                          | Views.ofPreProcess | Views.ofPostProcess
                          | ExtraOptions);
        Strings = new List<string>();
        enableMask = uint.MaxValue;
        for (TSItem? p = aStrings; p != null; p = p.Next)
            Strings.Add(p.Value);
        SetCursor(2, 0);
        ShowCursor();
    }

    /// <summary>Finalizer hook; performs no resource cleanup.</summary>
    ~TCluster() { }

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TCluster(StreamableInit init) : base(init)
    {
        Strings = new List<string>();
        enableMask = uint.MaxValue;
    }

    /// <summary>Returns the label at a valid zero-based option index, substituting an empty string for a null label.</summary>
    public string GetItemText(int item) => Strings[item] ?? string.Empty;

    /// <inheritdoc />
    public override ushort DataSize() => sizeof(ushort);

    private static int CStrLen(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int n = 0;
        for (int i = 0; i < s.Length; i++) if (s[i] != '~') n++;
        return n;
    }

    /// <summary>Draws option labels and icons, applying the marker to options selected by Mark.</summary>
    public void DrawBox(string icon, char marker) => DrawMultiBox(icon, string.Concat(' ', marker));

    /// <summary>Draws option labels and icons using a marker character selected separately for each option.</summary>
    public void DrawMultiBox(string icon, string markers)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentNullException.ThrowIfNull(markers);
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
        ushort cNorm = GetColor(0x0301);
        ushort cSel = GetColor(0x0402);
        ushort cDis = GetColor(0x0505);
        for (int i = 0; i <= size.y; i++)
        {
            b.moveChar(0, ' ', cNorm, size.x);
            for (int j = 0; j <= (Strings.Count - 1) / size.y + 1; j++)
            {
                int cur = j * size.y + i;
                int col = Column(cur);
                if (cur < Strings.Count
                    && col + CStrLen(GetItemText(cur)) + 5 < TDrawBuffer.MaxViewWidth
                    && col < size.x)
                {
                    ushort color = (state & Views.sfDisabled) != 0 || !ButtonState(cur)
                        ? cDis
                        : (cur == sel && (state & Views.sfSelected) != 0) ? cSel : cNorm;
                    b.moveChar(col, ' ', color, size.x - col);
                    b.moveCStr(col, icon, color);
                    byte marker = MultiMark(cur);
                    b.putChar(col + 2, marker < markers.Length ? markers[marker] : ' ');
                    b.moveCStr(col + 5, GetItemText(cur), color);
                }
            }
            WriteBuf(0, i, size.x, 1, b);
        }
        SetCursor(Column(sel) + 2, Row(sel));
    }

    /// <inheritdoc />
    public override void GetData(ref object rec)
    {
        rec = (ushort)value;
    }

    /// <inheritdoc />
    public override ushort GetHelpCtx()
    {
        if (helpCtx == Views.hcNoContext) return Views.hcNoContext;
        return (ushort)(helpCtx + sel);
    }

    private static readonly TPalette _palette = new TPalette(
        "\x10\x11\x12\x12\x1F", 5);
    /// <inheritdoc />
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
        return KeyboardCompatibility.AltCode(c);
    }

    private static ushort CtrlToArrow(ushort code) => code;

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if ((options & Views.ofSelectable) == 0)
            return;
        if (@event.What == Events.evMouseDown)
        {
            TPoint mouse = MakeLocal(@event.mouse.where);
            int i = FindSel(mouse);
            if (i != -1 && ButtonState(i)) sel = i;
            DrawView();
            do
            {
                mouse = MakeLocal(@event.mouse.where);
                if (FindSel(mouse) == sel && ButtonState(sel)) ShowCursor();
                else HideCursor();
            } while (MouseEvent(ref @event, Events.evMouseMove));
            ShowCursor();
            mouse = MakeLocal(@event.mouse.where);
            if (FindSel(mouse) == sel && ButtonState(sel))
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
                        MoveToEnabled(Keys.kbUp);
                        ClearEvent(ref @event);
                    }
                    break;
                case Keys.kbDown:
                    if ((state & Views.sfFocused) != 0)
                    {
                        MoveToEnabled(Keys.kbDown);
                        ClearEvent(ref @event);
                    }
                    break;
                case Keys.kbRight:
                    if ((state & Views.sfFocused) != 0)
                    {
                        MoveToEnabled(Keys.kbRight);
                        ClearEvent(ref @event);
                    }
                    break;
                case Keys.kbLeft:
                    if ((state & Views.sfFocused) != 0)
                    {
                        MoveToEnabled(Keys.kbLeft);
                        ClearEvent(ref @event);
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
                            if (!ButtonState(i))
                                return;
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

    /// <inheritdoc />
    public override void SetData(object rec)
    {
        if (rec is ushort u) value = u;
        else if (rec is uint ui) value = ui;
        else if (rec is int ii) value = (uint)ii;
        DrawView();
    }

    /// <inheritdoc />
    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if (aState == Views.sfSelected || aState == Views.sfDisabled)
            DrawView();
    }

    /// <summary>Reports whether an option should display its selected marker; the base cluster marks none.</summary>
    public virtual bool Mark(int item) => false;

    /// <summary>Returns the marker-table index for an option; the base maps <see cref="Mark"/> to zero or one.</summary>
    public virtual byte MultiMark(int item) => Mark(item) ? (byte)1 : (byte)0;

    /// <summary>Returns whether the option at an index from zero through 31 is enabled.</summary>
    public bool ButtonState(int item) => item is >= 0 and < 32 && (enableMask & (1u << item)) != 0;

    /// <summary>Enables or disables options selected by a 32-bit mask and updates cluster selectability.</summary>
    public virtual void SetButtonState(uint aMask, bool enable)
    {
        if (enable) enableMask |= aMask;
        else enableMask &= ~aMask;
        int count = Strings.Count;
        if (count < 32)
        {
            uint itemMask = count == 0 ? 0 : (1u << count) - 1;
            if ((enableMask & itemMask) != 0) options |= Views.ofSelectable;
            else options &= unchecked((ushort)~Views.ofSelectable);
        }
    }

    private void MoveToEnabled(ushort keyCode)
    {
        if (Strings.Count == 0)
            return;

        int candidate = sel;
        for (int attempts = 0; attempts <= Strings.Count; attempts++)
        {
            candidate = keyCode switch
            {
                Keys.kbUp => candidate > 0 ? candidate - 1 : Strings.Count - 1,
                Keys.kbDown => candidate + 1 < Strings.Count ? candidate + 1 : 0,
                Keys.kbRight => candidate + size.y < Strings.Count ? candidate + size.y : 0,
                Keys.kbLeft => MoveLeft(candidate),
                _ => candidate
            };
            if (ButtonState(candidate))
            {
                sel = candidate;
                MovedTo(sel);
                DrawView();
                return;
            }
        }
    }

    private int MoveLeft(int candidate)
    {
        if (candidate <= 0)
            return Strings.Count - 1;

        candidate -= size.y;
        if (candidate >= 0)
            return candidate;

        candidate = ((Strings.Count + size.y - 1) / size.y) * size.y + candidate - 1;
        return candidate < Strings.Count ? candidate : Strings.Count - 1;
    }

    /// <summary>Notifies the owner of option focus movement when verbose broadcasts are enabled.</summary>
    public virtual void MovedTo(int item)
    {
        if (owner != null && (options & ofBeVerbose) != 0)
            owner.Message(Events.evBroadcast, Views.cmClusterMovedTo, this);
    }

    /// <summary>Notifies the owner of option activation when verbose broadcasts are enabled.</summary>
    public virtual void Press(int item)
    {
        if (owner != null && (options & ofBeVerbose) != 0)
            owner.Message(Events.evBroadcast, Views.cmClusterPress, this);
    }

    /// <summary>Returns the local character-cell column where an option's icon begins.</summary>
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

    /// <summary>Returns the option index at a local cell position, or minus one outside the options.</summary>
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

    /// <summary>Returns the local character-cell row for an option index.</summary>
    protected int Row(int item) => size.y == 0 ? 0 : item % size.y;

    // ── Streaming ────────────────────────────────────────────────────────
    // Wire: TView base + WriteShort(value) + WriteInt(sel) + WritePointer(strings as TStringCollection).
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTCluster =
        new TStreamableClass("TCluster", () => new TCluster(StreamableInit.streamableInit), 0);

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteShort((ushort)value);
        os.WriteInt((uint)sel);
        var sc = new TStringCollection();
        foreach (var s in Strings) sc.Insert(s);
        os.WritePointer(sc);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        value = isStream.ReadShort();
        sel   = (int)isStream.ReadInt();
        Strings = new List<string>();
        enableMask = uint.MaxValue;
        if (isStream.ReadPointer() is TStringCollection sc)
            Strings.AddRange(sc.Items);
        SetCursor(2, 0);
        ShowCursor();
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TCluster(StreamableInit.streamableInit);
}
