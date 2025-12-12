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

public static class AnsiMouseDecoder
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

        // Need at least "ESC [ < ".
        if (buf.Length < 4) { complete = false; return 0; }
        if (buf[0] != 0x1B || buf[1] != (byte)'[' || buf[2] != (byte)'<')
            return 0;

        // Walk to terminator (M or m).
        int i = 3;
        while (i < buf.Length && buf[i] != (byte)'M' && buf[i] != (byte)'m') i++;
        if (i >= buf.Length) { complete = false; return 0; }

        byte term = buf[i];
        ReadOnlySpan<byte> body = buf.Slice(3, i - 3);

        // Parse three semicolon-separated decimal integers.
        int p = 0; int field = 0;
        int b = 0, x = 0, y = 0;
        for (int k = 0; k < body.Length; k++)
        {
            byte ch = body[k];
            if (ch == ';') { field++; continue; }
            if (ch < '0' || ch > '9') continue;
            int d = ch - '0';
            switch (field)
            {
                case 0: b = b * 10 + d; break;
                case 1: x = x * 10 + d; break;
                case 2: y = y * 10 + d; break;
            }
            p++;
        }
        if (field < 2) return i + 1; // malformed but framed — discard

        // Coordinates are 1-based.
        ev.mouse.where = new TPoint(x - 1, y - 1);

        bool wheel     = (b & 0x40) != 0;  // bit 6: SGR 1006 wheel event
        bool motion    = (b & 0x20) != 0;  // bit 5: mouse motion (move or drag)
        int  buttonIdx = b & 0x03;

        byte buttons;
        if (wheel)
        {
            // b=64 (0x40, buttonIdx=0) → wheel up  → mbButton4
            // b=65 (0x41, buttonIdx=1) → wheel down → mbButton5
            buttons = (buttonIdx == 0)
                ? (byte)Events.mbButton4   // 0x04 — scroll up
                : (byte)Events.mbButton5;  // 0x08 — scroll down
        }
        else if (motion)
        {
            // Drag: preserve the held button from the low two bits.
            // xterm encodes a pure move (no button held) with buttonIdx = 3.
            buttons = buttonIdx switch
            {
                0 => (byte)Events.mbLeftButton,   // 0x01 — left drag
                2 => (byte)Events.mbRightButton,  // 0x02 — right drag
                1 => 0x04,                        // middle drag
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
                1 => 0x04,                        // middle
                _ => 0,
            };
        }
        else
        {
            // Release (term == 'm'): all buttons released.
            buttons = 0;
        }

        ev.mouse.buttons    = buttons;
        ev.mouse.doubleClick = false;

        if (wheel)                  ev.What = Events.evMouseWheel;
        else if (motion)            ev.What = Events.evMouseMove;
        else if (term == (byte)'M') ev.What = Events.evMouseDown;
        else                        ev.What = Events.evMouseUp;
        return i + 1;
    }
}
