using TSharpVision.Constants;
namespace TSharpVision;

// TColorSelector — 4×4 (foreground) or 2×4 (background) color grid.
// Each cell is 3 chars wide and shows a full-block ('█') in the cell's
// own color attribute; the selected cell is marked with a bullet '●'.
public class TColorSelector : TView
{
    public new static readonly string Name = "TColorSelector";

    public enum ColorSel { csBackground, csForeground }

    public static char Icon = '\u2588';   // full block
    // cp437 code 8 = ◘ (U+25D8 INVERSE BULLET); we use ● (U+25CF) for clarity.
    public static char Mark = '\u25CF';   // bullet ●

    private byte    _color;
    private ColorSel _selType;

    public TColorSelector(TRect bounds, ColorSel aSelType) : base(bounds)
    {
        options   |= (ushort)(Views.ofSelectable | Views.ofFirstClick | Views.ofFramed);
        eventMask |= Events.evBroadcast;
        _selType = aSelType;
        _color   = 0;
    }

    // Rows 0..size.y-1, columns 0..3, each cell 3 chars wide.
    // icon character filled with raw color attribute c; mark shown in middle.
    public override void Draw()
    {
        var b = new TDrawBuffer();
        b.moveChar(0, ' ', 0x70, size.x);   // initialize with gray background

        for (int i = 0; i < size.y; i++)
        {
            // Re-init row (upstream reuses same b; inside loop only rows < 4
            // get overwritten, but since size.y <= 4 all rows are filled).
            b.moveChar(0, ' ', 0x70, size.x);

            if (i < 4)
            {
                for (int j = 0; j < 4; j++)
                {
                    int c = i * 4 + j;
                    b.moveChar(j * 3, Icon, (ushort)c, 3);
                    if (c == _color)
                    {
                        b.putChar(j * 3 + 1, Mark);
                        if (c == 0)   // mark on black needs a visible attribute
                            b.putAttribute(j * 3 + 1, 0x70);
                    }
                }
            }
            WriteLine(0, i, size.x, 1, b);
        }
    }

    private void ColorChanged()
    {
        ushort cmd = (_selType == ColorSel.csForeground)
            ? Views.cmColorForegroundChanged
            : Views.cmColorBackgroundChanged;
        TEvent ev = default;
        ev.What = Events.evBroadcast;
        ev.message.command  = cmd;
        ev.message.infoLong = _color;
        owner?.HandleEvent(ref ev);
    }

    public override void HandleEvent(ref TEvent @event)
    {
        // maxCol depends on how many rows we display.
        int maxCol = (size.y == 2) ? 7 : 15;

        byte oldColor = _color;

        base.HandleEvent(ref @event);

        switch (@event.What)
        {
            case Events.evMouseDown:
                do {
                    if (MouseInView(@event.mouse.where))
                    {
                        TPoint mouse = MakeLocal(@event.mouse.where);
                        _color = (byte)(mouse.y * 4 + mouse.x / 3);
                        if (_color > maxCol) _color = (byte)maxCol;
                    }
                    else
                        _color = oldColor;
                    ColorChanged();
                    DrawView();
                } while (MouseEvent(ref @event, Events.evMouseMove));
                ClearEvent(ref @event);
                break;

            case Events.evKeyDown:
                switch (CtrlToArrow(@event.keyDown.keyCode))
                {
                    case Keys.kbLeft:
                        _color = (_color > 0) ? (byte)(_color - 1) : (byte)maxCol;
                        break;
                    case Keys.kbRight:
                        _color = (_color < maxCol) ? (byte)(_color + 1) : (byte)0;
                        break;
                    case Keys.kbUp:
                        if (_color > 3)
                            _color -= 4;
                        else if (_color == 0)
                            _color = (byte)maxCol;
                        else
                            _color = (byte)(_color + maxCol - 3);
                        break;
                    case Keys.kbDown:
                        if (_color < maxCol - 3)
                            _color += 4;
                        else if (_color == maxCol)
                            _color = 0;
                        else
                            _color = (byte)(_color - maxCol + 3);
                        break;
                    default:
                        return;
                }
                DrawView();
                ColorChanged();
                ClearEvent(ref @event);
                break;

            case Events.evBroadcast:
                if (@event.message.command == Views.cmColorSet)
                {
                    _color = (_selType == ColorSel.csBackground)
                        ? (byte)((@event.message.infoLong >> 4) & 0x0F)
                        : (byte)(@event.message.infoLong & 0x0F);
                    DrawView();
                }
                break;
        }
    }

    // Map Ctrl-key codes to arrow codes (minimal—just returns the key unchanged
    // since ctrlToArrow is mainly for Ctrl+H→Left etc.; full mapping deferred).
    private static ushort CtrlToArrow(ushort code) => code;

    // ── Streaming ────────────────────────────────────────────────────────
    // Wire: TView base + WriteByte(color) + WriteShort(selType).
    public static readonly TStreamableClass StreamableClassTColorSelector =
        new TStreamableClass("TColorSelector", () => new TColorSelector(StreamableInit.streamableInit), 0);

    protected TColorSelector(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteByte(_color);
        os.WriteShort((ushort)_selType);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        _color   = (byte)isStream.ReadByte();
        _selType = (ColorSel)isStream.ReadShort();
        return this;
    }

    public new static TStreamable Build() => new TColorSelector(StreamableInit.streamableInit);
}

// TMonoSelector — TCluster showing 4 mono-color preset options.
// Handles cmColorSet broadcast to sync the selected option with current color.
public class TMonoSelector : TCluster
{
    public new static readonly string Name = "TMonoSelector";

    public static string Button = " ( ) ";

    // Indices: 0=Normal(0x07), 1=Highlight(0x0F), 2=Underline(0x01), 3=Inverse(0x70).
    private static readonly byte[] MonoColors = { 0x07, 0x0F, 0x01, 0x70, 0x09 };

    public TMonoSelector(TRect bounds)
        : base(bounds, new TSItem("Normal",
                       new TSItem("Highlight",
                       new TSItem("Underline",
                       new TSItem("Inverse", null)))))
    {
        eventMask |= Events.evBroadcast;
    }

    public override void Draw()
    {
        DrawBox(Button, '\x07');   // marker = char(7) = bell — shows as bullet in DOS
    }

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What == Events.evBroadcast
            && @event.message.command == Views.cmColorSet)
        {
            value = (uint)@event.message.infoLong;
            DrawView();
        }
    }

    public override bool Mark(int item)
        => item < MonoColors.Length && MonoColors[item] == (byte)value;

    public override void Press(int item)
    {
        if (item < MonoColors.Length)
        {
            value = MonoColors[item];
            NewColor();
        }
    }

    public override void MovedTo(int item)
    {
        if (item < MonoColors.Length)
        {
            value = MonoColors[item];
            NewColor();
        }
    }

    public void NewColor()
    {
        TEvent fg = default;
        fg.What = Events.evBroadcast;
        fg.message.command  = Views.cmColorForegroundChanged;
        fg.message.infoLong = value & 0x0F;
        owner?.HandleEvent(ref fg);

        TEvent bg = default;
        bg.What = Events.evBroadcast;
        bg.message.command  = Views.cmColorBackgroundChanged;
        bg.message.infoLong = (value >> 4) & 0x0F;
        owner?.HandleEvent(ref bg);
    }

    // ── Streaming ────────────────────────────────────────────────────────
    public static readonly TStreamableClass StreamableClassTMonoSelector =
        new TStreamableClass("TMonoSelector", () => new TMonoSelector(StreamableInit.streamableInit), 0);

    protected TMonoSelector(StreamableInit init) : base(init) { }

    public new static TStreamable Build() => new TMonoSelector(StreamableInit.streamableInit);
}
