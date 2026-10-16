// Source: xterm SGR 1006 mouse protocol. Reference:
// `xterm.terminfo`, https://invisible-island.net/xterm/ctlseqs/ctlseqs.html
//
// SGR 1006 frame: ESC [ < <button> ; <x> ; <y> (M|m)
//   M = press / move-while-pressed
//   m = release
//   button low 2 bits: 0=left, 1=middle, 2=right
//   bit 5 (0x20): motion (no-button move)
//   bit 6 (0x40): wheel events
//   bits 2..4: shift/alt/ctrl modifiers
using TSharpVision;
using TSharpVision.Constants;

namespace TSharpVision.Drivers.Terminal;

/// <summary>Decodes xterm SGR mouse sequences into framework mouse events.</summary>
internal static class AnsiMouseDecoder
{
    /// <summary>
    /// Try to decode an SGR mouse event. Returns the bytes consumed (0 when
    /// the buffer doesn't start with a recognized prefix; bytes may still
    /// be advanced when complete=false meaning "wait for more").
    /// </summary>
    public static int TryDecode(ReadOnlySpan<byte> buf, out TEvent ev, out bool complete)
    {
        ev = default;
        complete = true;

        if (buf.IsEmpty || buf[0] != 0x1B)
            return 0;
        if (buf.Length < 2) { complete = false; return 0; }
        if (buf[1] != (byte)'[')
            return 0;
        if (buf.Length < 3) { complete = false; return 0; }
        if (buf[2] != (byte)'<')
            return 0;

        // Need the full SGR mouse prefix "ESC [ < <button>...".
        if (buf.Length < 4) { complete = false; return 0; }

        // Three nonempty decimal fields, bounded before multiplication. A rejected prefix is
        // consumed without swallowing the offending byte, which may start the next input event.
        const int maxFrameLength = 32;
        const int maxValue = 65535;
        Span<int> values = stackalloc int[3];
        values.Clear();
        int field = 0, digits = 0, i = 3;
        byte term;
        for (; ; i++)
        {
            if (i >= maxFrameLength) return i;
            if (i >= buf.Length) { complete = false; return 0; }
            byte ch = buf[i];
            if (ch >= '0' && ch <= '9')
            {
                int digit = ch - '0';
                if (++digits > 5 || values[field] > (maxValue - digit) / 10) return i;
                values[field] = values[field] * 10 + digit;
            }
            else if (ch == ';' && field < 2 && digits != 0)
            {
                field++;
                digits = 0;
            }
            else if ((ch == 'M' || ch == 'm') && field == 2 && digits != 0)
            {
                term = ch;
                break;
            }
            else return i;
        }
        int b = values[0], x = values[1], y = values[2];
        if (b > 255 || x == 0 || y == 0) return i + 1;

        // Coordinates are 1-based.
        ev.mouse.where = new TPoint(x - 1, y - 1);

        bool wheel     = (b & 0x40) != 0;  // buttons 4-7: vertical wheel / horizontal tilt
        bool extraButton = (b & 0x80) != 0; // xterm buttons 8-11
        bool motion    = (b & 0x20) != 0;  // bit 5: mouse motion (move or drag)
        int  buttonIdx = b & 0x03;

        byte buttons;
        if (wheel)
        {
            // Wheel/tilt sequences do not truthfully report simultaneously held buttons.
            buttons = 0;
            ev.mouse.eventFlags |= buttonIdx switch
            {
                0 => Events.meWheelUp,
                1 => Events.meWheelDown,
                2 => Events.meWheelRight,
                _ => Events.meWheelLeft,
            };
        }
        else if (extraButton && term == (byte)'M')
        {
            // xterm's first two additional physical buttons (8 and 9).
            buttons = buttonIdx switch
            {
                0 => (byte)Events.mbButton4,
                1 => (byte)Events.mbButton5,
                _ => 0,
            };
        }
        else if (motion)
        {
            // Drag: preserve the held button from the low two bits.
            // xterm encodes a pure move (no button held) with buttonIdx = 3.
            buttons = buttonIdx switch
            {
                0 => (byte)Events.mbLeftButton,   // 0x01 — left drag
                2 => (byte)Events.mbRightButton,  // 0x02 — right drag
                1 => (byte)Events.mbMiddleButton, // middle drag
                _ => 0,                           // pure move (buttonIdx == 3)
            };
        }
        else if (term == (byte)'M')
        {
            // Press: encode the active button into the bitmask.
            buttons = buttonIdx switch
            {
                0 => (byte)Events.mbLeftButton,   // 0x01 — left
                2 => (byte)Events.mbRightButton,  // 0x02 — right
                1 => (byte)Events.mbMiddleButton, // middle
                _ => 0,
            };
        }
        else
        {
            // Release (term == 'm'): all buttons released.
            buttons = 0;
        }

        ev.mouse.buttons    = buttons;
        if ((b & 0x04) != 0) ev.mouse.controlKeyState |= Keys.kbShift;
        if ((b & 0x08) != 0) ev.mouse.controlKeyState |= Keys.kbAltShift;
        if ((b & 0x10) != 0) ev.mouse.controlKeyState |= Keys.kbCtrlShift;
        if (motion) ev.mouse.eventFlags |= Events.meMouseMoved;

        if (wheel)                  ev.What = Events.evMouseWheel;
        else if (motion)            ev.What = Events.evMouseMove;
        else if (term == (byte)'M') ev.What = Events.evMouseDown;
        else                        ev.What = Events.evMouseUp;
        return i + 1;
    }
}
