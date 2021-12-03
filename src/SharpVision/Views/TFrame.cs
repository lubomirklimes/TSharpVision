using SharpVision.Constants;
namespace SharpVision;

public class TFrame : TView
{
    public new static readonly string Name = "TFrame";

    // Encodes the bitmask seeds used by frameLine() for {top, mid, bot} of {inactive,active} frames.
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
    public static readonly char[] FrameChars =
        {
            ' ', ' ', ' ', '└', ' ', '│', '┌', '├',
            ' ', '┘', '─', '┴', '┐', '┤', '┬', '┼',
            ' ', ' ', ' ', '╚', ' ', '║', '╔', '╠',
            ' ', '╝', '═', '╩', '╗', '╣', '╦', '╬',
            ' '
        };

    // Hot-key markers wrap inner glyph.
    public static string CloseIcon  = "[~■~]";
    public static string ZoomIcon   = "[~↑~]";
    public static string UnZoomIcon = "[~↕~]";
    public static string DragIcon   = "──┘";
    public static string AnimIcon   = "[~+~]";

    public static bool DoAnimation = true;

    private const int ciClose = 0;
    private const int ciZoom  = 1;

    public TFrame(TRect bounds) : base(bounds)
    {
        growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
        eventMask |= Events.evBroadcast | Events.evMouseUp;
    }

    public override void Draw()
    {
        ushort cFrame, cTitle;
        int f, i, l, width;
        TDrawBuffer b = new TDrawBuffer();

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
            string title = win?.GetTitle((short)l);
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
                owner.SizeLimits(ref minSize, ref maxSize);
                b.moveCStr(width - 5,
                    owner.size == maxSize ? UnZoomIcon : ZoomIcon, cFrame);
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
    public override TPalette GetPalette() => _palette;

    public void DragWindow(ref TEvent ev, byte mode)
    {
        if (owner == null || owner.owner == null) return;
        TRect limits = owner.owner.GetExtent();
        TPoint min = default, max = default;
        owner.SizeLimits(ref min, ref max);
        owner.DragView(ev, (byte)(owner.dragMode | mode), ref limits, min, max);
        ClearEvent(ref ev);
    }

    public void DrawIcon(int bNormal, int ciType)
    {
        ushort cFrame;
        if ((state & Views.sfActive) == 0) cFrame = 0x0101;
        else if ((state & Views.sfDragging) != 0) cFrame = 0x0505;
        else cFrame = 0x0503;
        cFrame = GetColor(cFrame);

        TDrawBuffer drawBuf = new TDrawBuffer();
        if (ciType == ciClose)
        {
            drawBuf.moveCStr(0, bNormal != 0 ? CloseIcon : AnimIcon, cFrame);
            WriteLine(2, 0, 3, 1, drawBuf);
        }
        else
        {
            TPoint minSize = default, maxSize = default;
            owner.SizeLimits(ref minSize, ref maxSize);
            string icon = bNormal != 0
                ? (owner.size == maxSize ? UnZoomIcon : ZoomIcon)
                : AnimIcon;
            drawBuf.moveCStr(0, icon, cFrame);
            WriteLine(size.x - 5, 0, 3, 1, drawBuf);
        }
    }

    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);

        if ((@event.What & (Events.evMouseDown | Events.evMouseUp)) != 0
            && (state & Views.sfActive) != 0)
        {
            TPoint mouse = MakeLocal(@event.mouse.where);
            var win = owner as TWindow;
            byte flags = win?.flags ?? 0;

            if (mouse.y == 0)
            {
                bool overClose = mouse.y == 0 && mouse.x >= 2 && mouse.x <= 4;
                bool overZoom  = mouse.y == 0 && mouse.x >= size.x - 5 && mouse.x <= size.x - 3;

                if ((flags & Views.wfClose) != 0 && overClose)
                {
                    if (@event.What == Events.evMouseUp)
                        PutEvent(Events.evCommand, Views.cmClose, owner as IInfo);
                    ClearEvent(ref @event);
                }
                else if (@event.mouse.doubleClick
                         || ((flags & Views.wfZoom) != 0 && overZoom))
                {
                    if (@event.mouse.doubleClick)
                    {
                        PutEvent(Events.evCommand, Views.cmZoom, owner as IInfo);
                        ClearEvent(ref @event);
                    }
                    else
                    {
                        if (@event.What == Events.evMouseUp)
                            PutEvent(Events.evCommand, Views.cmZoom, owner as IInfo);
                        ClearEvent(ref @event);
                    }
                }
                else if ((flags & Views.wfMove) != 0
                         && (@event.What & Events.evMouseDown) != 0)
                    DragWindow(ref @event, Views.dmDragMove);
            }
            else if ((@event.What & Events.evMouseDown) != 0
                     && mouse.x >= size.x - 2 && mouse.y >= size.y - 1)
            {
                if ((flags & Views.wfGrow) != 0)
                    DragWindow(ref @event, Views.dmDragGrow);
            }
        }
    }

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

    public static readonly TStreamableClass StreamableClassTFrame =
        new TStreamableClass("TFrame", () => new TFrame(StreamableInit.streamableInit), 0);

    protected TFrame(StreamableInit init) : base(init) { }

    public override void Write(Opstream os) { base.Write(os); }

    public override object Read(Ipstream isStream) { base.Read(isStream); return this; }

    public new static TStreamable Build() { return new TFrame(StreamableInit.streamableInit); }
    public override string StreamableName() { return Name; }
}
