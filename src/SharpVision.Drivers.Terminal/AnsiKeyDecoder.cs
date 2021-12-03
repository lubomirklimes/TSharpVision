// Source: xterm/VT220/Linux-console escape sequences. Reference:
// `man console_codes`, `xterm.terminfo`, and tvision/win32/winntcli.cc
// (which decodes the same sequences when stdin is redirected).
//
// The decoder is a pure state machine: callers feed bytes one at a time
// via TryConsume, and the decoder either reports "need more data",
// "skip" (caller can advance one byte and retry), or emits a TEvent and
// returns the number of bytes consumed.
using SharpVision;
using SharpVision.Constants;

namespace SharpVision.Drivers.Terminal;

/// <summary>
/// Stateless xterm key-escape decoder. Given a byte buffer, returns an
/// optional <see cref="TEvent"/> and the number of bytes consumed.
/// Returning (false, 0) means "need more data".
/// </summary>
public static class AnsiKeyDecoder
{
    /// <summary>
    /// Try to decode one keyboard event from the head of <paramref name="buf"/>.
    /// </summary>
    /// <returns>
    ///   bytes &gt; 0 — consumed that many bytes, ev populated.
    ///   bytes == 0 and complete=false — incomplete sequence, wait for more.
    ///   bytes &gt; 0 and ev.What == evNothing — bytes should be discarded
    ///   without emitting an event (e.g., a recognized control we don't map).
    /// </returns>
    public static int TryDecode(ReadOnlySpan<byte> buf, out TEvent ev, out bool complete)
    {
        ev = default;
        complete = true;

        if (buf.Length == 0) { complete = false; return 0; }

        byte b0 = buf[0];

        // ---- Plain ESC by itself or Alt-prefixed sequence -----------------
        if (b0 == 0x1B)
        {
            if (buf.Length == 1) { complete = false; return 0; }

            byte b1 = buf[1];

            // ESC [  → CSI sequence
            if (b1 == (byte)'[')
                return DecodeCsi(buf, out ev, out complete);

            // ESC O  → SS3 sequence (xterm uses for F1..F4 and arrow keys
            // when application-cursor mode is active).
            if (b1 == (byte)'O')
                return DecodeSs3(buf, out ev, out complete);

            // ESC <ch>  → Alt-<ch>. Filter ESC ESC (treat first ESC as kbEsc
            // to keep behaviour predictable for users hitting Esc twice).
            if (b1 == 0x1B)
            {
                ev = MakeKey(Keys.kbEsc);
                return 1;
            }

            // ESC + letter / digit → Alt-<letter>/Alt-<digit>.
            if (b1 >= 'a' && b1 <= 'z')
            {
                ev = MakeKey((ushort)(Keys.kbAltA + (b1 - 'a')), Keys.kbAltShift);
                return 2;
            }
            if (b1 >= 'A' && b1 <= 'Z')
            {
                ev = MakeKey((ushort)(Keys.kbAltA + (b1 - 'A')), Keys.kbAltShift | Keys.kbShift);
                return 2;
            }
            if (b1 >= '0' && b1 <= '9')
            {
                ushort kc = (b1 == '0')
                    ? Keys.kbAlt0
                    : (ushort)(Keys.kbAlt1 + (b1 - '1'));
                ev = MakeKey(kc, Keys.kbAltShift);
                return 2;
            }

            // Unknown ESC sequence — surface a bare Esc and let the caller
            // re-process the second byte on the next round.
            ev = MakeKey(Keys.kbEsc);
            return 1;
        }

        // ---- C0 controls: Ctrl-A..Ctrl-Z, Tab, Enter, Backspace ---------
        if (b0 == 0x09) { ev = MakeKey(Keys.kbTab); return 1; }
        if (b0 == 0x0D || b0 == 0x0A) { ev = MakeKey(Keys.kbEnter); return 1; }
        if (b0 == 0x08 || b0 == 0x7F) { ev = MakeKey(Keys.kbBack); return 1; }
        if (b0 >= 0x01 && b0 <= 0x1A)
        {
            // Ctrl-A..Ctrl-Z share the upstream layout 0x0101..0x011A.
            ushort kc = (ushort)(0x0100 | b0);
            ev = MakeKey(kc, Keys.kbCtrlShift);
            ev.keyDown.charScan.charCode = b0;
            return 1;
        }

        // ---- Plain printable ASCII --------------------------------------
        if (b0 >= 0x20 && b0 <= 0x7E)
        {
            ev = MakeKey(b0);
            ev.keyDown.charScan.charCode = b0;
            return 1;
        }

        // High-bit byte: treat as opaque char (caller should batch UTF-8
        // decoding; here we just emit the raw byte).
        ev = MakeKey(b0);
        ev.keyDown.charScan.charCode = b0;
        return 1;
    }

    // ESC O <letter>  — applies to F1..F4 and the arrow-key family in
    // application-cursor mode.
    private static int DecodeSs3(ReadOnlySpan<byte> buf, out TEvent ev, out bool complete)
    {
        ev = default;
        complete = true;
        if (buf.Length < 3) { complete = false; return 0; }
        byte c = buf[2];
        ushort kc = c switch
        {
            (byte)'P' => Keys.kbF1,
            (byte)'Q' => Keys.kbF2,
            (byte)'R' => Keys.kbF3,
            (byte)'S' => Keys.kbF4,
            (byte)'A' => Keys.kbUp,
            (byte)'B' => Keys.kbDown,
            (byte)'C' => Keys.kbRight,
            (byte)'D' => Keys.kbLeft,
            (byte)'H' => Keys.kbHome,
            (byte)'F' => Keys.kbEnd,
            _         => 0,
        };
        if (kc == 0)
        {
            // Unrecognized: emit kbEsc and consume only the ESC so we
            // re-attempt decoding from byte 1.
            ev = MakeKey(Keys.kbEsc);
            return 1;
        }
        ev = MakeKey(kc);
        return 3;
    }

    // ESC [ ... <final>  — full CSI parse. Supports arrow keys (`A`..`D`),
    // Home/End (`H`/`F`), and the numeric form `ESC [ <n> ; <mod> ~`.
    private static int DecodeCsi(ReadOnlySpan<byte> buf, out TEvent ev, out bool complete)
    {
        ev = default;
        complete = true;

        // Walk past parameters; final byte is in 0x40..0x7E. We bound the
        // search to avoid scanning the entire input forever.
        int i = 2;
        while (i < buf.Length && (buf[i] < 0x40 || buf[i] > 0x7E)) i++;
        if (i >= buf.Length) { complete = false; return 0; }

        byte final = buf[i];
        ReadOnlySpan<byte> param = buf.Slice(2, i - 2);

        // Parse up to two numeric parameters separated by ';'.
        int p1 = 0, p2 = 0;
        bool seenP1 = false, seenP2 = false;
        int idx = 0;
        bool inSecond = false;
        while (idx < param.Length)
        {
            byte ch = param[idx++];
            if (ch == ';') { inSecond = true; continue; }
            if (ch < '0' || ch > '9') continue;
            if (!inSecond) { p1 = p1 * 10 + (ch - '0'); seenP1 = true; }
            else           { p2 = p2 * 10 + (ch - '0'); seenP2 = true; }
        }

        // CSI <param=1..> ; <mod> <final>: arrow & home/end with modifier.
        ushort baseCode = final switch
        {
            (byte)'A' => Keys.kbUp,
            (byte)'B' => Keys.kbDown,
            (byte)'C' => Keys.kbRight,
            (byte)'D' => Keys.kbLeft,
            (byte)'H' => Keys.kbHome,
            (byte)'F' => Keys.kbEnd,
            _         => 0,
        };
        if (baseCode != 0)
        {
            ushort sh = ModToShift(seenP2 ? p2 : (seenP1 ? p1 : 1));
            ev = MakeKey(WithCtrlPrefix(baseCode, sh), sh);
            return i + 1;
        }

        // CSI <n> ~  — numeric forms.
        if (final == (byte)'~')
        {
            ushort kc = p1 switch
            {
                1  => Keys.kbHome,
                2  => Keys.kbIns,
                3  => Keys.kbDel,
                4  => Keys.kbEnd,
                5  => Keys.kbPgUp,
                6  => Keys.kbPgDn,
                7  => Keys.kbHome,
                8  => Keys.kbEnd,
                11 => Keys.kbF1,
                12 => Keys.kbF2,
                13 => Keys.kbF3,
                14 => Keys.kbF4,
                15 => Keys.kbF5,
                17 => Keys.kbF6,
                18 => Keys.kbF7,
                19 => Keys.kbF8,
                20 => Keys.kbF9,
                21 => Keys.kbF10,
                23 => Keys.kbF11,
                24 => Keys.kbF12,
                _  => 0,
            };
            if (kc == 0) return i + 1; // recognized framing, unknown payload
            ushort sh = ModToShift(seenP2 ? p2 : 1);
            ev = MakeKey(WithModifierFn(kc, sh), sh);
            return i + 1;
        }

        // Unknown CSI sequence — discard without emitting an event.
        return i + 1;
    }

    // xterm modifier parameter: 1 = none, 2 = Shift, 3 = Alt, 4 = Shift+Alt,
    // 5 = Ctrl, 6 = Ctrl+Shift, 7 = Ctrl+Alt, 8 = Ctrl+Alt+Shift.
    private static ushort ModToShift(int mod)
    {
        if (mod < 2) return 0;
        int m = mod - 1;
        ushort s = 0;
        if ((m & 0x01) != 0) s |= Keys.kbShift;
        if ((m & 0x02) != 0) s |= Keys.kbAltShift;
        if ((m & 0x04) != 0) s |= Keys.kbCtrlShift;
        return s;
    }

    // Promote a function key (F1..F12) to its modified sibling based on the
    // modifier bitmask. Ctrl takes precedence over Alt which takes precedence
    // over Shift, matching xterm modifier convention.
    private static ushort WithModifierFn(ushort baseCode, ushort sh)
    {
        bool ctrl  = (sh & Keys.kbCtrlShift) != 0;
        bool alt   = (sh & Keys.kbAltShift)  != 0;
        bool shift = (sh & Keys.kbShift)      != 0;
        if (ctrl) return WithCtrlPrefix(baseCode, sh);
        if (alt && !shift)
        {
            return baseCode switch
            {
                Keys.kbF1  => Keys.kbAltF1,  Keys.kbF2  => Keys.kbAltF2,
                Keys.kbF3  => Keys.kbAltF3,  Keys.kbF4  => Keys.kbAltF4,
                Keys.kbF5  => Keys.kbAltF5,  Keys.kbF6  => Keys.kbAltF6,
                Keys.kbF7  => Keys.kbAltF7,  Keys.kbF8  => Keys.kbAltF8,
                Keys.kbF9  => Keys.kbAltF9,  Keys.kbF10 => Keys.kbAltF10,
                Keys.kbF11 => Keys.kbAltF11, Keys.kbF12 => Keys.kbAltF12,
                _          => baseCode,
            };
        }
        if (shift && !alt)
        {
            return baseCode switch
            {
                Keys.kbF1  => Keys.kbShiftF1,  Keys.kbF2  => Keys.kbShiftF2,
                Keys.kbF3  => Keys.kbShiftF3,  Keys.kbF4  => Keys.kbShiftF4,
                Keys.kbF5  => Keys.kbShiftF5,  Keys.kbF6  => Keys.kbShiftF6,
                Keys.kbF7  => Keys.kbShiftF7,  Keys.kbF8  => Keys.kbShiftF8,
                Keys.kbF9  => Keys.kbShiftF9,  Keys.kbF10 => Keys.kbShiftF10,
                Keys.kbF11 => Keys.kbShiftF11, Keys.kbF12 => Keys.kbShiftF12,
                _          => baseCode,
            };
        }
        return baseCode;
    }

    // Promote a plain key to its Ctrl-prefixed sibling when the modifier
    // bitmask carries Ctrl. Mirrors `wn_kbCodes` upstream.
    private static ushort WithCtrlPrefix(ushort baseCode, ushort sh)
    {
        bool ctrl = (sh & Keys.kbCtrlShift) != 0;
        if (!ctrl) return baseCode;
        return baseCode switch
        {
            Keys.kbLeft  => Keys.kbCtrlLeft,
            Keys.kbRight => Keys.kbCtrlRight,
            Keys.kbHome  => Keys.kbCtrlHome,
            Keys.kbEnd   => Keys.kbCtrlEnd,
            Keys.kbPgUp  => Keys.kbCtrlPgUp,
            Keys.kbPgDn  => Keys.kbCtrlPgDn,
            Keys.kbIns   => Keys.kbCtrlIns,
            Keys.kbDel   => Keys.kbCtrlDel,
            Keys.kbF1    => Keys.kbCtrlF1,
            Keys.kbF2    => Keys.kbCtrlF2,
            Keys.kbF3    => Keys.kbCtrlF3,
            Keys.kbF4    => Keys.kbCtrlF4,
            Keys.kbF5    => Keys.kbCtrlF5,
            Keys.kbF6    => Keys.kbCtrlF6,
            Keys.kbF7    => Keys.kbCtrlF7,
            Keys.kbF8    => Keys.kbCtrlF8,
            Keys.kbF9    => Keys.kbCtrlF9,
            Keys.kbF10   => Keys.kbCtrlF10,
            Keys.kbF11   => Keys.kbCtrlF11,
            Keys.kbF12   => Keys.kbCtrlF12,
            _            => baseCode,
        };
    }

    private static TEvent MakeKey(ushort kc, ushort shiftState = 0)
    {
        TEvent ev = default;
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = kc;
        ev.keyDown.shiftState = shiftState;
        return ev;
    }
}
