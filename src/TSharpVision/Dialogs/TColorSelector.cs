using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>A grid for choosing foreground or background palette indices and broadcasting color changes.</summary>
public class TColorSelector : TView
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TColorSelector";

    /// <summary>Chooses which component of a packed color attribute the grid edits.</summary>
    public enum ColorSel
    {
        /// <summary>Select a background palette index.</summary>
        csBackground,
        /// <summary>Select a foreground palette index.</summary>
        csForeground
    }

    /// <summary>Glyph filling each displayed color swatch.</summary>
    public static char Icon = '\u2588';   // full block
    // cp437 code 8 = ◘ (U+25D8 INVERSE BULLET); we use ● (U+25CF) for clarity.
    /// <summary>Glyph marking the currently selected color swatch.</summary>
    public static char Mark = '\u25CF';   // bullet ●

    private byte    _color;
    private ColorSel _selType;

    /// <summary>Creates a color grid at owner-relative cell bounds for foreground or background selection, initially selecting index zero.</summary>
    public TColorSelector(TRect bounds, ColorSel aSelType) : base(bounds)
    {
        options   |= (ushort)(Views.ofSelectable | Views.ofFirstClick | Views.ofFramed);
        eventMask |= Events.evBroadcast;
        _selType = aSelType;
        _color   = 0;
    }

    // Rows 0..size.y-1, columns 0..3, each cell 3 chars wide.
    // icon character filled with raw color attribute c; mark shown in middle.
    /// <inheritdoc />
    public override void Draw()
    {
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
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

    /// <inheritdoc />
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
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTColorSelector =
        new TStreamableClass("TColorSelector", () => new TColorSelector(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TColorSelector(StreamableInit init) : base(init) { }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteByte(_color);
        os.WriteShort((ushort)_selType);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        _color   = (byte)isStream.ReadByte();
        _selType = (ColorSel)isStream.ReadShort();
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TColorSelector(StreamableInit.streamableInit);
}

/// <summary>Four monochrome attribute choices synchronized with color-setting broadcasts.</summary>
public class TMonoSelector : TCluster
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TMonoSelector";

    /// <summary>Icon template used to draw each monochrome option around its selection marker.</summary>
    public static string Button = " ( ) ";

    // Indices: 0=Normal(0x07), 1=Highlight(0x0F), 2=Underline(0x01), 3=Inverse(0x70).
    private static readonly byte[] MonoColors = { 0x07, 0x0F, 0x01, 0x70, 0x09 };

    /// <summary>Creates Normal, Highlight, Underline, and Inverse choices at owner-relative cell bounds.</summary>
    public TMonoSelector(TRect bounds)
        : base(bounds, new TSItem("Normal",
                       new TSItem("Highlight",
                       new TSItem("Underline",
                       new TSItem("Inverse", null)))))
    {
        eventMask |= Events.evBroadcast;
    }

    /// <inheritdoc />
    public override void Draw()
    {
        DrawBox(Button, '\x07');   // marker = char(7) = bell — shows as bullet in DOS
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public override bool Mark(int item)
        => item < MonoColors.Length && MonoColors[item] == (byte)value;

    /// <inheritdoc />
    public override void Press(int item)
    {
        if (item < MonoColors.Length)
        {
            value = MonoColors[item];
            NewColor();
        }
    }

    /// <inheritdoc />
    public override void MovedTo(int item)
    {
        if (item < MonoColors.Length)
        {
            value = MonoColors[item];
            NewColor();
        }
    }

    /// <summary>Broadcasts the current attribute's foreground and background components to the owner.</summary>
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
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTMonoSelector =
        new TStreamableClass("TMonoSelector", () => new TMonoSelector(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TMonoSelector(StreamableInit init) : base(init) { }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TMonoSelector(StreamableInit.streamableInit);
}
