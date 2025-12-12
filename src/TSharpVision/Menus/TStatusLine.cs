using TSharpVision.Constants;
namespace TSharpVision;

public class TStatusLine : TView
{
    public const string cpStatusLine = "\x02\x03\x04\x05\x06\x07";

    static TPalette palette = new TPalette(cpStatusLine, (ushort)(cpStatusLine.Length - 1 ));

    public static readonly string Name = "TStatusLine";

    public TStatusItem Items { get; set; }
    public TStatusDef Defs { get; set; }

    public TStatusLine(TRect bounds, TStatusDef aDefs)
        : base(bounds)
    {
        Defs = aDefs;

        options |= Views.ofPreProcess;
        eventMask |= Events.evBroadcast;
        growMode = Views.gfGrowLoY | Views.gfGrowHiX | Views.gfGrowHiY;
        FindItems();        
    }

    ~TStatusLine()
    {
    }

    public override void Draw()
    {
        DrawSelect(null);
    }

    public override TPalette GetPalette()
    {
        return palette;
    }

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);

        if (InputTrace.Enabled && @event.What != Events.evNothing)
            InputTrace.LogEvent("Stage8-TStatusLine.HandleEvent(entry)", @event);

        switch (@event.What)
        {
            case Events.evMouseDown:
            {
                // Click an enabled status item to dispatch its command, then consume.
                var hit = ItemMouseIsIn(@event.mouse.where);
                if (hit != null && CommandEnabled(hit.Command))
                {
                    @event.What = Events.evCommand;
                    @event.message.command = hit.Command;
                    @event.message.infoPtr = null;
                }
                else ClearEvent(ref @event);
                break;
            }

            case Events.evKeyDown:
                // Printable characters cannot match a status-line accelerator.
                // Guard prevents an uppercase letter's ASCII value from coinciding
                // with a special-key constant (e.g. 'B' == kbF10 -> cmMenu).
                if (@event.keyDown.charScan.charCode < 32 || @event.keyDown.charScan.charCode >= 127)
                {
                    for (TStatusItem t = Items; t != null; t = t.Next)
                    {
                        if (@event.keyDown.keyCode == t.KeyCode &&
                            CommandEnabled(t.Command))
                        {
                            @event.What = Events.evCommand;
                            @event.message.command = t.Command;
                            @event.message.infoPtr = null;
                            return;
                        }
                    }
                }
                break;
            
            case Events.evBroadcast:
                if (@event.message.command == Views.cmCommandSetChanged)
                    DrawView();
                break;
        }
    }

    public virtual string Hint(ushort aHelpCtx)
    {
        return "";
    }

    // Polled from TProgram::idle(). Pulls the current help context from
    // the modal TopView and, when it changed, rebuilds Items from the
    // matching TStatusDef and repaints.
    public void Update()
    {
        TView p = TopView();
        ushort h = (p != null) ? p.GetHelpCtx() : Views.hcNoContext;
        if (helpCtx != h)
        {
            helpCtx = h;
            FindItems();
            DrawView();
        }
    }

    // Count visible chars, excluding the ~ delimiters used by moveCStr for hotkey markup.
    private static int CStrLen(string s)
    {
        if (s == null) return 0;
        int n = 0;
        for (int k = 0; k < s.Length; k++)
            if (s[k] != '~') n++;
        return n;
    }

    private void DrawSelect(TStatusItem selected) 
    {
        TDrawBuffer b = new TDrawBuffer();
        ushort color;

        ushort cNormal = GetColor(0x0301);
        ushort cSelect = GetColor(0x0604);
        ushort cNormDisabled = GetColor(0x0202);
        ushort cSelDisabled = GetColor(0x0505);
        b.moveChar(0, ' ', cNormal, size.x);
        TStatusItem T = Items;
        int i = 0;

        while (T != null)
        {
            if (T.Text != null)
            {
                // Use display length (tildes stripped) to match upstream
                // cstrlen behaviour and keep draw/hit-test consistent.
                var l = CStrLen(T.Text);
                if (i + l + 2 <= size.x)
                {
                    if (CommandEnabled(T.Command))
                        if (T == selected)
                            color = cSelect;
                        else
                            color = cNormal;
                    else
                        if (T == selected)
                        color = cSelDisabled;
                    else
                        color = cNormDisabled;

                    b.moveChar(i, ' ', color, 1);
                    b.moveCStr((ushort)(i + 1), T.Text, color);
                    b.moveChar(i + l + 1, ' ', color, 1);
                }
                i += l + 2;
            }
            T = T.Next;
        }

        if (i < size.x - 2)
        {
            // Render the hint separator and the help-context hint after the last item.
            string hintText = Hint(helpCtx);
            if (hintText.Length > 0)
            {
                b.moveStr(i, hintSeparator, cNormal);
                i += 2;
                b.moveStr(i, hintText, cNormal, size.x - i, 0);
            }
        }
        WriteLine(0, 0, size.x, 1, b);
    }

    private void FindItems() 
    {
        TStatusDef p = Defs;
        while (p != null && (helpCtx < p.Min || helpCtx > p.Max))
        {
            p = p.Next;
        }
        Items = (p == null) ? null : p.Items;
    }
    private TStatusItem ItemMouseIsIn(TPoint p)
    {
        // Convert the absolute screen position to local view coordinates first,
        // exactly as the C++ port does with makeLocal(mouse). Without this,
        // clicking on a status line that is not at origin (0,0) — e.g. the
        // bottom row at y=24 — always returns null because the absolute y
        // coordinate is never 0.
        p = MakeLocal(p);
        if (p.y != 0) return null;
        int i = 0;
        for (TStatusItem t = Items; t != null; t = t.Next)
        {
            if (string.IsNullOrEmpty(t.Text)) continue;
            int len = CStrLen(t.Text);
            if (p.x >= i && p.x < i + len + 2) return t;
            i += len + 2;
        }
        return null;
    }
    private void DisposeItems(TStatusItem item) { /* GC handles cleanup */ }

    // "\xB3 " (CP437 vertical bar followed by a space).
    // Latin-1 0xB3 is mapped at the driver
    // layer; here we use the Unicode equivalent.
    // U+2502 '│' separates status items from hint text.
    // See TSharpVisionGlyphs.StatusHintSeparator.
    private static readonly string hintSeparator = "\u2502 ";

    // ── Streaming ─────────────────────────────────────────────────────────

    protected TStatusLine(StreamableInit init) : base(init) { }

    // Upstream writeItems: WriteInt(count) + foreach item: WriteString(text) + WriteShort(keyCode) + WriteShort(command).
    private static void WriteItems(Opstream os, TStatusItem ts)
    {
        int count = 0;
        for (TStatusItem t = ts; t != null; t = t.Next) count++;
        os.WriteInt((uint)count);
        for (; ts != null; ts = ts.Next)
        {
            os.WriteString(ts.Text);
            os.WriteShort(ts.KeyCode);
            os.WriteShort(ts.Command);
        }
    }

    // Upstream writeDefs: WriteInt(count) + foreach def: WriteShort(min) + WriteShort(max) + WriteItems(items).
    private static void WriteDefs(Opstream os, TStatusDef td)
    {
        int count = 0;
        for (TStatusDef t = td; t != null; t = t.Next) count++;
        os.WriteInt((uint)count);
        for (; td != null; td = td.Next)
        {
            os.WriteShort(td.Min);
            os.WriteShort(td.Max);
            WriteItems(os, td.Items);
        }
    }

    public override void Write(Opstream os)
    {
        base.Write(os);
        WriteDefs(os, Defs);
    }

    private static TStatusItem ReadItems(Ipstream isStream)
    {
        TStatusItem first = null, last = null;
        int count = (int)isStream.ReadInt();
        while (count-- > 0)
        {
            string text   = isStream.ReadString();
            ushort key    = isStream.ReadShort();
            ushort cmd    = isStream.ReadShort();
            var item = new TStatusItem(text, key, cmd);
            if (last == null) first = item; else last.Next = item;
            last = item;
        }
        return first;
    }

    private static TStatusDef ReadDefs(Ipstream isStream)
    {
        TStatusDef first = null, last = null;
        int count = (int)isStream.ReadInt();
        while (count-- > 0)
        {
            ushort min = isStream.ReadShort();
            ushort max = isStream.ReadShort();
            var def = new TStatusDef(min, max, ReadItems(isStream));
            if (last == null) first = def; else last.Next = def;
            last = def;
        }
        return first;
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        Defs = ReadDefs(isStream);
        FindItems();
        return this;
    }

    public static TStreamable Build() => new TStatusLine(StreamableInit.streamableInit);

    public static readonly TStreamableClass StreamableClassTStatusLine =
        new TStreamableClass("TStatusLine", () => new TStatusLine(StreamableInit.streamableInit), 0);

    public override string ToString() { return Name; }
}
