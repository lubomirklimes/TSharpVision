using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>Window border view that renders title controls and handles move, resize, close, and zoom interactions.</summary>
public class TFrame : TView
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TFrame";

    /// <summary>Border-mask seeds for top, middle, and bottom rows of inactive and active frames.</summary>
    public static readonly byte[] InitFrame =
        { 0x06, 0x0A, 0x0C, 0x05, 0x00, 0x05, 0x03, 0x0A, 0x09,
          0x16, 0x1A, 0x1C, 0x15, 0x00, 0x15, 0x13, 0x1A, 0x19, 0x00 };

    // Box-drawing chars indexed by FrameMask byte. Bit encoding: bit0=UP, bit1=RIGHT, bit2=DOWN, bit3=LEFT,
    // bit4=DOUBLE (0x10). Indices 0-15: single-line; 16-31: double-line.
    //
    // Index derivation (single-line, bits 0-3 = UP,RIGHT,DOWN,LEFT):
    //  0=' '  1=' '  2=' '  3='└'(UP+RIGHT)   4=' '   5='│'(UP+DOWN)
    //  6='┌'(RIGHT+DOWN)  7='├'(U+R+D)  8=' '  9='┘'(UP+LEFT)
    // 10='─'(RIGHT+LEFT) 11='┴'(U+R+L) 12='┐'(DOWN+LEFT) 13='┤'(U+D+L)
    // 14='┬'(R+D+L) 15='┼'(all)
    // Double-line: add 16 → same pattern with ║═╔╚╝╗ etc.
    /// <summary>Box-drawing glyphs indexed by directional connection bits, with bit 4 selecting double lines.</summary>
    public static readonly char[] FrameChars =
        {
            ' ', ' ', ' ', '└', ' ', '│', '┌', '├',
            ' ', '┘', '─', '┴', '┐', '┤', '┬', '┼',
            ' ', ' ', ' ', '╚', ' ', '║', '╔', '╠',
            ' ', '╝', '═', '╩', '╗', '╣', '╦', '╬',
            ' '
        };

    /// <summary>Close-button label with tilde-delimited highlighting markers.</summary>
    public static string CloseIcon  = "[~■~]";
    /// <summary>Zoom-button label used when the window is not zoomed.</summary>
    public static string ZoomIcon   = "[~↑~]";
    /// <summary>Zoom-button label used to restore a zoomed window.</summary>
    public static string UnZoomIcon = "[~↕~]";
    /// <summary>Glyph sequence marking the frame's resize corner.</summary>
    public static string DragIcon   = "──┘";
    /// <summary>Temporary glyph shown when DrawIcon requests the non-normal button appearance.</summary>
    public static string AnimIcon   = "[~+~]";

    /// <summary>Compatibility animation preference; the current frame implementation does not consult it.</summary>
    public static bool DoAnimation = true;

    private const int ciClose = 0;
    private const int ciZoom  = 1;

    /// <summary>Creates a frame in owner-relative character-cell bounds that grows with its window and receives broadcasts and mouse-up events.</summary>
    public TFrame(TRect bounds) : base(bounds)
    {
        growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
        eventMask |= Events.evBroadcast | Events.evMouseUp;
    }

    /// <inheritdoc />
    public override void Draw()
    {
        ushort cFrame, cTitle;
        int f, i, l, width;
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        TDrawBuffer b = new TDrawBuffer(row);

        if ((state & Views.sfActive) == 0) { cFrame = 0x0101; cTitle = 0x0002; f = 0; }
        else if ((state & Views.sfDragging) != 0) { cFrame = 0x0505; cTitle = 0x0005; f = 0; }
        else { cFrame = 0x0503; cTitle = 0x0004; f = 9; }

        cFrame = GetColor(cFrame);
        cTitle = GetColor(cTitle);

        width = size.x;
        l = width - 10;

        var win = owner as TWindow;
        if (win != null && (win.flags & (Views.wfClose | Views.wfZoom)) != 0)
            l -= 6;

        FrameLine(b, 0, f, (byte)cFrame);

        if (win != null && win.number != Views.wnNoNumber)
        {
            l -= 4;
            i = ((win.flags & Views.wfZoom) != 0) ? 7 : 3;
            int n = win.number;
            if (n > 10)   i++;
            if (n > 100)  i++;
            if (n > 1000) i++;
            string num = n.ToString();
            for (int j = 0; j < num.Length; j++)
                b.putChar(width - i + j, num[j]);
        }

        if (owner != null)
        {
            string? title = win?.GetTitle((short)l);
            if (!string.IsNullOrEmpty(title))
            {
                int ls = title.Length;
                if (ls > l)
                {
                    i = (width - l) >> 1;
                    b.moveBuf(i - 1, " ..", cTitle, 3);
                    b.moveBuf(i + 2, title.AsSpan(ls - l + 2), cTitle, l);
                    b.putChar(i + l, ' ');
                    b.putChar(i + l + 1, ' ');
                }
                else
                {
                    l = ls;
                    i = (width - l) >> 1;
                    b.putChar(i - 1, ' ');
                    b.moveBuf(i, title.AsSpan(), cTitle, l);
                    b.putChar(i + l, ' ');
                }
            }
        }

        if ((state & Views.sfActive) != 0 && win != null)
        {
            if ((win.flags & Views.wfClose) != 0)
                b.moveCStr(2, CloseIcon, cFrame);
            if ((win.flags & Views.wfZoom) != 0)
            {
                TPoint minSize = default, maxSize = default;
                win.SizeLimits(ref minSize, ref maxSize);
                b.moveCStr(width - 5,
                    win.size == maxSize ? UnZoomIcon : ZoomIcon, cFrame);
            }
        }

        WriteLine(0, 0, size.x, 1, b);
        for (i = 1; i <= size.y - 2; i++)
        {
            FrameLine(b, i, f + 3, (byte)cFrame);
            WriteLine(0, i, size.x, 1, b);
        }
        FrameLine(b, size.y - 1, f + 6, (byte)cFrame);
        if ((state & Views.sfActive) != 0 && win != null
            && (win.flags & Views.wfGrow) != 0)
            // DragIcon is 3 chars; place at width-3 so the rightmost '┘'
            // lands at width-1 (the last column). Upstream C++ uses a 2-char
            // icon at width-2, but our C# DragIcon includes the corner glyph.
            b.moveCStr(width - 3, DragIcon, cFrame);
        WriteLine(0, size.y - 1, size.x, 1, b);
    }

    // Static palette for cpFrame.
    private static readonly TPalette _palette = new TPalette("\x01\x01\x02\x02\x03", 5);
    /// <inheritdoc />
    public override TPalette GetPalette() => _palette;

    /// <summary>Drags or resizes the owning window within its parent's cell extent and clears the triggering event; does nothing without both owners.</summary>
    public void DragWindow(ref TEvent ev, byte mode)
    {
        if (owner == null || owner.owner == null) return;
        TRect limits = owner.owner.GetExtent();
        TPoint min = default, max = default;
        owner.SizeLimits(ref min, ref max);
        owner.DragView(ev, (byte)(owner.dragMode | mode), ref limits, min, max);
        ClearEvent(ref ev);
    }

    /// <summary>Draws a frame button: nonzero bNormal selects the normal glyph, zero the animation glyph; ciType zero selects close, otherwise zoom.</summary>
    public void DrawIcon(int bNormal, int ciType)
    {
        int x = ciType == ciClose ? 2 : size.x - 5;
        if (x < 0 || x + 3 > size.x || size.y == 0) return;
        string glyph = AnimIcon;
        if (bNormal != 0)
        {
            glyph = CloseIcon;
            if (ciType != ciClose)
            {
                if (owner == null) return;
                TPoint minimum = default, maximum = default;
                owner.SizeLimits(ref minimum, ref maximum);
                glyph = owner.size == maximum ? UnZoomIcon : ZoomIcon;
            }
        }
        ushort palette = (state & Views.sfActive) == 0 ? (ushort)0x0101
            : (state & Views.sfDragging) != 0 ? (ushort)0x0505 : (ushort)0x0503;
        Span<TScreenChar> cells = stackalloc TScreenChar[3];
        cells.Clear();
        new TDrawBuffer(cells).moveCStr(0, glyph, GetColor(palette));
        WriteLine(x, 0, 3, 1, cells);
    }

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        bool pressed = @event.What == Events.evMouseDown;
        if ((!pressed && @event.What != Events.evMouseUp)
            || owner is not TWindow window
            || (state & Views.sfActive) == 0
            || ((state | window.state) & Views.sfDisabled) != 0) return;

        TPoint point = MakeLocal(@event.mouse.where);
        if (point.x < 0 || point.x >= size.x || point.y < 0 || point.y >= size.y) return;
        bool title = point.y == 0;
        bool close = title && point.x >= 2 && point.x < 5;
        bool zoom = title && point.x >= size.x - 5 && point.x < size.x - 2;
        ushort command = close && (window.flags & Views.wfClose) != 0 ? Views.cmClose
            : title && (zoom || @event.mouse.doubleClick) && (window.flags & Views.wfZoom) != 0
                ? Views.cmZoom : (ushort)0;
        if (command != 0)
        {
            if (!CommandEnabled(command)) return;
            if (!pressed || (command == Views.cmZoom && @event.mouse.doubleClick))
                PutEvent(Events.evCommand, command, window);
            ClearEvent(ref @event);
            return;
        }

        byte mode = title && (window.flags & Views.wfMove) != 0 ? Views.dmDragMove
            : point.y == size.y - 1 && point.x >= size.x - 2 && (window.flags & Views.wfGrow) != 0
                ? Views.dmDragGrow : (byte)0;
        if (pressed && mode != 0) DragWindow(ref @event, mode);
    }
    /// <inheritdoc />
    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if ((aState & (Views.sfActive | Views.sfDragging)) != 0)
            DrawView();
    }

    // Frame-line glyph computation.
    // NOTE: This is the simplified port — we ignore the FrameMask interleave
    // with overlapping ofFramed subviews.
    private void FrameLine(TDrawBuffer frameBuf, int y, int n, byte color)
    {
        int dx = size.x;
        int len = dx;
        char left  = FrameChars[InitFrame[n]];
        char mid   = FrameChars[InitFrame[n + 1]];
        char right = FrameChars[InitFrame[n + 2]];

        // Build the line into a transient char buffer then push via moveBuf.
        char[] row = new char[Math.Max(1, len)];
        row[0] = left;
        for (int i = 1; i < len - 1; i++) row[i] = mid;
        if (len > 1) row[len - 1] = right;
        frameBuf.moveBuf(0, row, color, len);
    }

    // TFrame has no own persistent fields beyond TView.
    // Only build() is defined upstream;
    // Write/Read delegate entirely to the TView base.

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTFrame =
        new TStreamableClass("TFrame", () => new TFrame(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TFrame(StreamableInit init) : base(init) { }

    /// <inheritdoc />
    public override void Write(Opstream os) { base.Write(os); }

    /// <inheritdoc />
    public override object Read(Ipstream isStream) { base.Read(isStream); return this; }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() { return new TFrame(StreamableInit.streamableInit); }
    /// <inheritdoc />
    public override string StreamableName() { return Name; }
}
