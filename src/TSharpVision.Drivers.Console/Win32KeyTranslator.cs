// Win32 console key-event translation. Upstream tvision uses
// `KEY_EVENT_RECORD` from ReadConsoleInputW + a static lookup table
// (tvision/win32/winntsys.cc:wn_kbCodes) to fold (vk, modifier-state) into
// a tvision keycode. This file ports that table verbatim and exposes a
// pure function so it can be unit-tested without a real console.
using TSharpVision.Constants;

namespace TSharpVision.Drivers.Console;

/// <summary>
/// Pure translator from a Windows console key record into a tvision
/// <see cref="TEvent"/>. No P/Invoke — driver code marshals the
/// INPUT_RECORD into the four primitive parameters here.
/// </summary>
public static class Win32KeyTranslator
{
    // Upstream control-key flags (winntsys.cc:39).
    public const uint LEFT_CTRL_PRESSED  = 0x0008;
    public const uint RIGHT_CTRL_PRESSED = 0x0004;
    public const uint LEFT_ALT_PRESSED   = 0x0002;
    public const uint RIGHT_ALT_PRESSED  = 0x0001;
    public const uint SHIFT_PRESSED      = 0x0010;
    public const uint NUMLOCK_ON         = 0x0020;
    public const uint SCROLLLOCK_ON      = 0x0040;
    public const uint CAPSLOCK_ON        = 0x0080;
    public const uint ENHANCED_KEY       = 0x0100;

    /// <summary>
    /// Translate a Windows console key event into a TEvent. Returns false
    /// when the event isn't a meaningful keydown (key-up, dead modifier,
    /// pure shift/ctrl press without any character).
    /// </summary>
    /// <param name="keyDown">true when bKeyDown is non-zero.</param>
    /// <param name="vk">VirtualKeyCode (wVirtualKeyCode).</param>
    /// <param name="ch">UnicodeChar from the INPUT_RECORD.</param>
    /// <param name="ctrlState">dwControlKeyState bitmask.</param>
    public static bool TryTranslate(bool keyDown, ushort vk, char ch, uint ctrlState, out TEvent ev)
    {
        ev = default;
        if (!keyDown) return false;

        bool shift = (ctrlState & SHIFT_PRESSED) != 0;
        bool ctrl  = (ctrlState & (LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED)) != 0;
        bool alt   = (ctrlState & (LEFT_ALT_PRESSED  | RIGHT_ALT_PRESSED))  != 0;

        ushort kc = MapSpecial(vk, shift, ctrl, alt);
        if (kc == 0)
        {
            // Alt+letter / Alt+digit translate regardless of whether the
            // console delivered a character payload.
            if (alt && !ctrl && vk >= 'A' && vk <= 'Z')
            {
                kc = (ushort)(Keys.kbAltA + (vk - 'A'));
            }
            else if (alt && !ctrl && vk >= 0x30 && vk <= 0x39)
            {
                kc = vk == 0x30 ? Keys.kbAlt0 : (ushort)(Keys.kbAlt1 + (vk - 0x31));
            }
            else if (ch == 0)
            {
                // Bare modifier press (Shift/Ctrl/Alt by themselves).
                return false;
            }
            else if (ctrl && ch >= 1 && ch <= 26)
            {
                // Ctrl-A..Ctrl-Z share the upstream layout 0x0101..0x011A.
                kc = (ushort)(0x0100 | ch);
            }
            else
            {
                kc = ch;
            }
        }

        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = kc;
        ev.keyDown.charScan.charCode = (byte)(ch & 0xFF);
        ev.keyDown.charScan.scanCode = 0; // PC-BIOS scancode unavailable.
        ev.keyDown.shiftState = (ushort)(
            (shift ? Keys.kbShift   : 0) |
            (ctrl  ? Keys.kbCtrlShift : 0) |
            (alt   ? Keys.kbAltShift  : 0));
        if (ch >= 32 && ch != 0x7F && !ctrl && !alt)
            ev.keyDown.text = ch.ToString();
        return true;
    }

    /// <summary>
    /// Map a virtual-key + modifier into a tvision keycode for the keys
    /// that don't have a printable Unicode payload. Returns 0 when the
    /// caller should fall back to <c>ch</c>.
    /// </summary>
    private static ushort MapSpecial(ushort vk, bool shift, bool ctrl, bool alt)
    {
        switch (vk)
        {
            case 0x08: return alt ? Keys.kbAltBack : (ctrl ? Keys.kbCtrlBack : Keys.kbBack);
            case 0x09: return shift ? Keys.kbShiftTab : Keys.kbTab;
            case 0x0D: return ctrl ? Keys.kbCtrlEnter : Keys.kbEnter;
            case 0x1B: return Keys.kbEsc;

            case 0x21: // PgUp
                return ctrl ? Keys.kbCtrlPgUp : Keys.kbPgUp;
            case 0x22: // PgDn
                return ctrl ? Keys.kbCtrlPgDn : Keys.kbPgDn;
            case 0x23: // End
                return ctrl ? Keys.kbCtrlEnd : Keys.kbEnd;
            case 0x24: // Home
                return ctrl ? Keys.kbCtrlHome : Keys.kbHome;
            case 0x25: // Left
                return ctrl ? Keys.kbCtrlLeft : Keys.kbLeft;
            case 0x26: // Up
                return Keys.kbUp;
            case 0x27: // Right
                return ctrl ? Keys.kbCtrlRight : Keys.kbRight;
            case 0x28: // Down
                return Keys.kbDown;
            case 0x2D: // Insert
                return ctrl ? Keys.kbCtrlIns : (shift ? Keys.kbShiftIns : Keys.kbIns);
            case 0x2E: // Delete
                return ctrl ? Keys.kbCtrlDel : (shift ? Keys.kbShiftDel : Keys.kbDel);

            // F1..F12: vk 0x70..0x7B.
            case 0x70: case 0x71: case 0x72: case 0x73:
            case 0x74: case 0x75: case 0x76: case 0x77:
            case 0x78: case 0x79: case 0x7A: case 0x7B:
            {
                int n = vk - 0x70; // 0..11
                if (alt)   return (ushort)(Keys.kbAltF1   + n);
                if (ctrl)  return (ushort)(Keys.kbCtrlF1  + n);
                if (shift) return (ushort)(Keys.kbShiftF1 + n);
                return (ushort)(Keys.kbF1 + n);
            }
        }
        return 0;
    }
}
