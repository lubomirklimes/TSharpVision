// Source: SDL3 SDL_keycode.h and SDL_mouse.h. Reference values:
// https://wiki.libsdl.org/SDL3/SDL_Keycode
//
// This is a *pure* translator: it takes the integer keycode + modifier
// bitmask + optional textinput character and produces a tvision TEvent.
// Tests can call it directly without any SDL window/runtime.
using TSharpVision;
using TSharpVision.Constants;

namespace TSharpVision.Drivers.SDL;

/// <summary>Converts SDL key codes and modifier masks to framework keyboard events without requiring an SDL window.</summary>
internal static class SdlKeyTranslator
{
    private static readonly ushort[] ControlLetters =
    [
        Keys.kbCtrlA, Keys.kbCtrlB, Keys.kbCtrlC, Keys.kbCtrlD, Keys.kbCtrlE, Keys.kbCtrlF,
        Keys.kbCtrlG, Keys.kbCtrlH, Keys.kbCtrlI, Keys.kbCtrlJ, Keys.kbCtrlK, Keys.kbCtrlL,
        Keys.kbCtrlM, Keys.kbCtrlN, Keys.kbCtrlO, Keys.kbCtrlP, Keys.kbCtrlQ, Keys.kbCtrlR,
        Keys.kbCtrlS, Keys.kbCtrlT, Keys.kbCtrlU, Keys.kbCtrlV, Keys.kbCtrlW, Keys.kbCtrlX,
        Keys.kbCtrlY, Keys.kbCtrlZ
    ];
    private static readonly ushort[] AltLetters =
    [
        Keys.kbAltA, Keys.kbAltB, Keys.kbAltC, Keys.kbAltD, Keys.kbAltE, Keys.kbAltF,
        Keys.kbAltG, Keys.kbAltH, Keys.kbAltI, Keys.kbAltJ, Keys.kbAltK, Keys.kbAltL,
        Keys.kbAltM, Keys.kbAltN, Keys.kbAltO, Keys.kbAltP, Keys.kbAltQ, Keys.kbAltR,
        Keys.kbAltS, Keys.kbAltT, Keys.kbAltU, Keys.kbAltV, Keys.kbAltW, Keys.kbAltX,
        Keys.kbAltY, Keys.kbAltZ
    ];
    private static readonly ushort[] AltDigits =
        [Keys.kbAlt0, Keys.kbAlt1, Keys.kbAlt2, Keys.kbAlt3, Keys.kbAlt4,
         Keys.kbAlt5, Keys.kbAlt6, Keys.kbAlt7, Keys.kbAlt8, Keys.kbAlt9];

    // SDL3 modifier flags (subset).
    /// <summary>SDL modifier mask for left Shift being pressed.</summary>
    public const ushort SDL_KMOD_LSHIFT = 0x0001;
    /// <summary>SDL modifier mask for right Shift being pressed.</summary>
    public const ushort SDL_KMOD_RSHIFT = 0x0002;
    /// <summary>SDL modifier mask for left Control being pressed.</summary>
    public const ushort SDL_KMOD_LCTRL  = 0x0040;
    /// <summary>SDL modifier mask for right Control being pressed.</summary>
    public const ushort SDL_KMOD_RCTRL  = 0x0080;
    /// <summary>SDL modifier mask for left Alt being pressed.</summary>
    public const ushort SDL_KMOD_LALT   = 0x0100;
    /// <summary>SDL modifier mask for right Alt being pressed.</summary>
    public const ushort SDL_KMOD_RALT   = 0x0200;

    /// <summary>SDL modifier mask for either Shift key being pressed.</summary>
    public const ushort SDL_KMOD_SHIFT = SDL_KMOD_LSHIFT | SDL_KMOD_RSHIFT;
    /// <summary>SDL modifier mask for either Control key being pressed.</summary>
    public const ushort SDL_KMOD_CTRL  = SDL_KMOD_LCTRL  | SDL_KMOD_RCTRL;
    /// <summary>SDL modifier mask for either Alt key being pressed.</summary>
    public const ushort SDL_KMOD_ALT   = SDL_KMOD_LALT   | SDL_KMOD_RALT;

    // SDL3 SDLK_* constants (only the keys we map). The high bit 0x40000000
    // is the SDL "scancode mask" that distinguishes named keys from ASCII.
    /// <summary>SDL key code for BACKSPACE; pass as the keycode argument to translation.</summary>
    public const uint SDLK_BACKSPACE = 0x08;
    /// <summary>SDL key code for TAB; pass as the keycode argument to translation.</summary>
    public const uint SDLK_TAB       = 0x09;
    /// <summary>SDL key code for RETURN; pass as the keycode argument to translation.</summary>
    public const uint SDLK_RETURN    = 0x0D;
    /// <summary>SDL key code for ESCAPE; pass as the keycode argument to translation.</summary>
    public const uint SDLK_ESCAPE    = 0x1B;
    /// <summary>SDL key code for DELETE; pass as the keycode argument to translation.</summary>
    public const uint SDLK_DELETE    = 0x7F;

    /// <summary>SDL key code for F1; pass as the keycode argument to translation.</summary>
    public const uint SDLK_F1   = 0x4000003A;
    /// <summary>SDL key code for F2; pass as the keycode argument to translation.</summary>
    public const uint SDLK_F2   = 0x4000003B;
    /// <summary>SDL key code for F3; pass as the keycode argument to translation.</summary>
    public const uint SDLK_F3   = 0x4000003C;
    /// <summary>SDL key code for F4; pass as the keycode argument to translation.</summary>
    public const uint SDLK_F4   = 0x4000003D;
    /// <summary>SDL key code for F5; pass as the keycode argument to translation.</summary>
    public const uint SDLK_F5   = 0x4000003E;
    /// <summary>SDL key code for F6; pass as the keycode argument to translation.</summary>
    public const uint SDLK_F6   = 0x4000003F;
    /// <summary>SDL key code for F7; pass as the keycode argument to translation.</summary>
    public const uint SDLK_F7   = 0x40000040;
    /// <summary>SDL key code for F8; pass as the keycode argument to translation.</summary>
    public const uint SDLK_F8   = 0x40000041;
    /// <summary>SDL key code for F9; pass as the keycode argument to translation.</summary>
    public const uint SDLK_F9   = 0x40000042;
    /// <summary>SDL key code for F10; pass as the keycode argument to translation.</summary>
    public const uint SDLK_F10  = 0x40000043;
    /// <summary>SDL key code for F11; pass as the keycode argument to translation.</summary>
    public const uint SDLK_F11  = 0x40000044;
    /// <summary>SDL key code for F12; pass as the keycode argument to translation.</summary>
    public const uint SDLK_F12  = 0x40000045;

    /// <summary>SDL key code for INSERT; pass as the keycode argument to translation.</summary>
    public const uint SDLK_INSERT   = 0x40000049;
    /// <summary>SDL key code for HOME; pass as the keycode argument to translation.</summary>
    public const uint SDLK_HOME     = 0x4000004A;
    /// <summary>SDL key code for Page Up; pass as the keycode argument to translation.</summary>
    public const uint SDLK_PAGEUP   = 0x4000004B;
    /// <summary>SDL key code for END; pass as the keycode argument to translation.</summary>
    public const uint SDLK_END      = 0x4000004D;
    /// <summary>SDL key code for Page Down; pass as the keycode argument to translation.</summary>
    public const uint SDLK_PAGEDOWN = 0x4000004E;
    /// <summary>SDL key code for RIGHT; pass as the keycode argument to translation.</summary>
    public const uint SDLK_RIGHT    = 0x4000004F;
    /// <summary>SDL key code for LEFT; pass as the keycode argument to translation.</summary>
    public const uint SDLK_LEFT     = 0x40000050;
    /// <summary>SDL key code for DOWN; pass as the keycode argument to translation.</summary>
    public const uint SDLK_DOWN     = 0x40000051;
    /// <summary>SDL key code for UP; pass as the keycode argument to translation.</summary>
    public const uint SDLK_UP       = 0x40000052;

    // SDL3 modifier-only keycodes — caller will see these on bare Shift/etc.
    /// <summary>SDL key code for left Control; pass as the keycode argument to translation.</summary>
    public const uint SDLK_LCTRL  = 0x400000E0;
    /// <summary>SDL key code for left Shift; pass as the keycode argument to translation.</summary>
    public const uint SDLK_LSHIFT = 0x400000E1;
    /// <summary>SDL key code for left Alt; pass as the keycode argument to translation.</summary>
    public const uint SDLK_LALT   = 0x400000E2;
    /// <summary>SDL key code for right Control; pass as the keycode argument to translation.</summary>
    public const uint SDLK_RCTRL  = 0x400000E4;
    /// <summary>SDL key code for right Shift; pass as the keycode argument to translation.</summary>
    public const uint SDLK_RSHIFT = 0x400000E5;
    /// <summary>SDL key code for right Alt; pass as the keycode argument to translation.</summary>
    public const uint SDLK_RALT   = 0x400000E6;

    /// <summary>
    /// Translate an SDL_KEYDOWN into a tvision <see cref="TEvent"/>.
    /// </summary>
    /// <param name="keycode">SDL_Keycode value (from event.key.key).</param>
    /// <param name="modState">SDL_Keymod bitmask (from event.key.mod).</param>
    /// <param name="textChar">
    /// Optional ASCII character associated with this keystroke
    /// (e.g., from SDL_TEXTINPUT). Pass '\0' if not available.
    /// </param>
    /// <param name="ev">Translated event when successful; otherwise the default event.</param>
    /// <returns>true if mapped to a useful event, false to ignore.</returns>
    public static bool TryTranslate(uint keycode, ushort modState, char textChar, out TEvent ev)
    {
        ev = default;

        // Drop bare modifier presses.
        if (keycode == SDLK_LCTRL || keycode == SDLK_RCTRL ||
            keycode == SDLK_LSHIFT || keycode == SDLK_RSHIFT ||
            keycode == SDLK_LALT || keycode == SDLK_RALT)
            return false;

        uint shift = ToShiftState(modState);
        bool ctrl  = (shift & Keys.kbCtrlShift) != 0;
        bool alt   = (shift & Keys.kbAltShift)  != 0;
        bool shf   = (shift & Keys.kbShift)     != 0;

        // ---- Special / navigation keys -----------------------------------
        ushort kc = keycode switch
        {
            SDLK_BACKSPACE => Keys.kbBack,
            SDLK_TAB       => shf ? Keys.kbShiftTab : Keys.kbTab,
            SDLK_RETURN    => Keys.kbEnter,
            SDLK_ESCAPE    => Keys.kbEsc,
            SDLK_DELETE    => ctrl && shf ? Keys.kbCtrlShiftDel : ctrl ? Keys.kbCtrlDel : (shf ? Keys.kbShiftDel : Keys.kbDel),
            SDLK_INSERT    => ctrl && shf ? Keys.kbCtrlShiftIns : ctrl ? Keys.kbCtrlIns : (shf ? Keys.kbShiftIns : Keys.kbIns),
            SDLK_HOME      => ctrl ? Keys.kbCtrlHome : Keys.kbHome,
            SDLK_END       => ctrl ? Keys.kbCtrlEnd  : Keys.kbEnd,
            SDLK_PAGEUP    => ctrl ? Keys.kbCtrlPgUp : Keys.kbPgUp,
            SDLK_PAGEDOWN  => ctrl ? Keys.kbCtrlPgDn : Keys.kbPgDn,
            SDLK_LEFT      => ctrl ? Keys.kbCtrlLeft  : Keys.kbLeft,
            SDLK_RIGHT     => ctrl ? Keys.kbCtrlRight : Keys.kbRight,
            SDLK_UP        => Keys.kbUp,
            SDLK_DOWN      => Keys.kbDown,
            SDLK_F1  => SelectFn(Keys.kbF1,  Keys.kbShiftF1,  Keys.kbCtrlF1,  Keys.kbAltF1,  shf, ctrl, alt),
            SDLK_F2  => SelectFn(Keys.kbF2,  Keys.kbShiftF2,  Keys.kbCtrlF2,  Keys.kbAltF2,  shf, ctrl, alt),
            SDLK_F3  => SelectFn(Keys.kbF3,  Keys.kbShiftF3,  Keys.kbCtrlF3,  Keys.kbAltF3,  shf, ctrl, alt),
            SDLK_F4  => SelectFn(Keys.kbF4,  Keys.kbShiftF4,  Keys.kbCtrlF4,  Keys.kbAltF4,  shf, ctrl, alt),
            SDLK_F5  => SelectFn(Keys.kbF5,  Keys.kbShiftF5,  Keys.kbCtrlF5,  Keys.kbAltF5,  shf, ctrl, alt),
            SDLK_F6  => SelectFn(Keys.kbF6,  Keys.kbShiftF6,  Keys.kbCtrlF6,  Keys.kbAltF6,  shf, ctrl, alt),
            SDLK_F7  => SelectFn(Keys.kbF7,  Keys.kbShiftF7,  Keys.kbCtrlF7,  Keys.kbAltF7,  shf, ctrl, alt),
            SDLK_F8  => SelectFn(Keys.kbF8,  Keys.kbShiftF8,  Keys.kbCtrlF8,  Keys.kbAltF8,  shf, ctrl, alt),
            SDLK_F9  => SelectFn(Keys.kbF9,  Keys.kbShiftF9,  Keys.kbCtrlF9,  Keys.kbAltF9,  shf, ctrl, alt),
            SDLK_F10 => SelectFn(Keys.kbF10, Keys.kbShiftF10, Keys.kbCtrlF10, Keys.kbAltF10, shf, ctrl, alt),
            SDLK_F11 => SelectFn(Keys.kbF11, Keys.kbShiftF11, Keys.kbCtrlF11, Keys.kbAltF11, shf, ctrl, alt),
            SDLK_F12 => SelectFn(Keys.kbF12, Keys.kbShiftF12, Keys.kbCtrlF12, Keys.kbAltF12, shf, ctrl, alt),
            _ => 0,
        };

        if (kc != 0)
        {
            ev = MakeKey(kc, shift);
            return true;
        }

        // ---- Letters: a..z -------------------------------------------------
        if (keycode >= 'a' && keycode <= 'z')
        {
            int letter = (int)keycode - 'a';
            if (alt)
            {
                ev = MakeKey(AltLetters[letter], shift);
                return true;
            }
            if (ctrl)
            {
                ev = MakeKey(ControlLetters[letter], shift);
                ev.keyDown.charScan.charCode = (byte)(letter + 1); // 0x01..0x1A
                return true;
            }
            // Plain or shifted letter — use textChar if available, else
            // synthesize from keycode.
            char ch = textChar != 0 ? textChar : (char)(shf ? char.ToUpper((char)keycode) : keycode);
            ev = MakeKey(ch, shift);
            ev.keyDown.charScan.charCode = (byte)ch;
            ev.keyDown.text = ch.ToString();
            return true;
        }

        // ---- Digits: 0..9 -------------------------------------------------
        if (keycode >= '0' && keycode <= '9')
        {
            int digit = (int)keycode - '0';
            if (alt)
            {
                ev = MakeKey(AltDigits[digit], shift);
                return true;
            }
            char ch = textChar != 0 ? textChar : (char)keycode;
            ev = MakeKey(ch, shift);
            ev.keyDown.charScan.charCode = (byte)ch;
            ev.keyDown.text = ch.ToString();
            return true;
        }

        // ---- Other ASCII printables --------------------------------------
        if (keycode >= 0x20 && keycode <= 0x7E)
        {
            char ch = textChar != 0 ? textChar : (char)keycode;
            ev = MakeKey(ch, shift);
            ev.keyDown.charScan.charCode = (byte)ch;
            ev.keyDown.text = ch.ToString();
            return true;
        }

        return false;
    }

    /// <summary>Maps SDL Shift, Control, and Alt modifier bits to framework keyboard-state masks.</summary>
    public static uint ToShiftState(ushort modState)
    {
        uint s = 0;
        if ((modState & SDL_KMOD_SHIFT) != 0) s |= Keys.kbShift;
        if ((modState & SDL_KMOD_CTRL)  != 0) s |= Keys.kbCtrlShift;
        if ((modState & SDL_KMOD_ALT)   != 0) s |= Keys.kbAltShift;
        return s;
    }

    private static ushort SelectFn(ushort plain, ushort shift, ushort ctrl, ushort alt,
                                    bool shf, bool ctrlMod, bool altMod)
    {
        if (altMod)  return alt;
        if (ctrlMod) return ctrl;
        if (shf)     return shift;
        return plain;
    }

    private static TEvent MakeKey(ushort kc, uint shift)
    {
        TEvent ev = default;
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = kc;
        ev.keyDown.charScan = new CharScanType(kc);
        ev.keyDown.controlKeyState = shift;
        return ev;
    }
}
