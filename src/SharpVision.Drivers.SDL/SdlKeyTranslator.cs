// Source: SDL3 SDL_keycode.h and SDL_mouse.h. Reference values:
// https://wiki.libsdl.org/SDL3/SDL_Keycode
//
// This is a *pure* translator: it takes the integer keycode + modifier
// bitmask + optional textinput character and produces a tvision TEvent.
// Tests can call it directly without any SDL window/runtime.
using SharpVision;
using SharpVision.Constants;

namespace SharpVision.Drivers.SDL;

public static class SdlKeyTranslator
{
    // SDL3 modifier flags (subset).
    public const ushort SDL_KMOD_LSHIFT = 0x0001;
    public const ushort SDL_KMOD_RSHIFT = 0x0002;
    public const ushort SDL_KMOD_LCTRL  = 0x0040;
    public const ushort SDL_KMOD_RCTRL  = 0x0080;
    public const ushort SDL_KMOD_LALT   = 0x0100;
    public const ushort SDL_KMOD_RALT   = 0x0200;

    public const ushort SDL_KMOD_SHIFT = SDL_KMOD_LSHIFT | SDL_KMOD_RSHIFT;
    public const ushort SDL_KMOD_CTRL  = SDL_KMOD_LCTRL  | SDL_KMOD_RCTRL;
    public const ushort SDL_KMOD_ALT   = SDL_KMOD_LALT   | SDL_KMOD_RALT;

    // SDL3 SDLK_* constants (only the keys we map). The high bit 0x40000000
    // is the SDL "scancode mask" that distinguishes named keys from ASCII.
    public const uint SDLK_BACKSPACE = 0x08;
    public const uint SDLK_TAB       = 0x09;
    public const uint SDLK_RETURN    = 0x0D;
    public const uint SDLK_ESCAPE    = 0x1B;
    public const uint SDLK_DELETE    = 0x7F;

    public const uint SDLK_F1   = 0x4000003A;
    public const uint SDLK_F2   = 0x4000003B;
    public const uint SDLK_F3   = 0x4000003C;
    public const uint SDLK_F4   = 0x4000003D;
    public const uint SDLK_F5   = 0x4000003E;
    public const uint SDLK_F6   = 0x4000003F;
    public const uint SDLK_F7   = 0x40000040;
    public const uint SDLK_F8   = 0x40000041;
    public const uint SDLK_F9   = 0x40000042;
    public const uint SDLK_F10  = 0x40000043;
    public const uint SDLK_F11  = 0x40000044;
    public const uint SDLK_F12  = 0x40000045;

    public const uint SDLK_INSERT   = 0x40000049;
    public const uint SDLK_HOME     = 0x4000004A;
    public const uint SDLK_PAGEUP   = 0x4000004B;
    public const uint SDLK_END      = 0x4000004D;
    public const uint SDLK_PAGEDOWN = 0x4000004E;
    public const uint SDLK_RIGHT    = 0x4000004F;
    public const uint SDLK_LEFT     = 0x40000050;
    public const uint SDLK_DOWN     = 0x40000051;
    public const uint SDLK_UP       = 0x40000052;

    // SDL3 modifier-only keycodes — caller will see these on bare Shift/etc.
    public const uint SDLK_LCTRL  = 0x400000E0;
    public const uint SDLK_LSHIFT = 0x400000E1;
    public const uint SDLK_LALT   = 0x400000E2;
    public const uint SDLK_RCTRL  = 0x400000E4;
    public const uint SDLK_RSHIFT = 0x400000E5;
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
    /// <returns>true if mapped to a useful event, false to ignore.</returns>
    public static bool TryTranslate(uint keycode, ushort modState, char textChar, out TEvent ev)
    {
        ev = default;

        // Drop bare modifier presses.
        if (keycode == SDLK_LCTRL || keycode == SDLK_RCTRL ||
            keycode == SDLK_LSHIFT || keycode == SDLK_RSHIFT ||
            keycode == SDLK_LALT || keycode == SDLK_RALT)
            return false;

        ushort shift = ToShiftState(modState);
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
            SDLK_DELETE    => ctrl ? Keys.kbCtrlDel : (shf ? Keys.kbShiftDel : Keys.kbDel),
            SDLK_INSERT    => ctrl ? Keys.kbCtrlIns : (shf ? Keys.kbShiftIns : Keys.kbIns),
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
                ev = MakeKey((ushort)(Keys.kbAltA + letter), shift);
                return true;
            }
            if (ctrl)
            {
                ev = MakeKey((ushort)(Keys.kbCtrlA + letter), shift);
                ev.keyDown.charScan.charCode = (byte)(letter + 1); // 0x01..0x1A
                return true;
            }
            // Plain or shifted letter — use textChar if available, else
            // synthesize from keycode.
            char ch = textChar != 0 ? textChar : (char)(shf ? char.ToUpper((char)keycode) : keycode);
            ev = MakeKey(ch, shift);
            ev.keyDown.charScan.charCode = (byte)ch;
            return true;
        }

        // ---- Digits: 0..9 -------------------------------------------------
        if (keycode >= '0' && keycode <= '9')
        {
            int digit = (int)keycode - '0';
            if (alt)
            {
                ushort altKc = digit == 0 ? Keys.kbAlt0 : (ushort)(Keys.kbAlt1 + digit - 1);
                ev = MakeKey(altKc, shift);
                return true;
            }
            char ch = textChar != 0 ? textChar : (char)keycode;
            ev = MakeKey(ch, shift);
            ev.keyDown.charScan.charCode = (byte)ch;
            return true;
        }

        // ---- Other ASCII printables --------------------------------------
        if (keycode >= 0x20 && keycode <= 0x7E)
        {
            char ch = textChar != 0 ? textChar : (char)keycode;
            ev = MakeKey(ch, shift);
            ev.keyDown.charScan.charCode = (byte)ch;
            return true;
        }

        return false;
    }

    public static ushort ToShiftState(ushort modState)
    {
        ushort s = 0;
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

    private static TEvent MakeKey(ushort kc, ushort shift)
    {
        TEvent ev = default;
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = kc;
        ev.keyDown.shiftState = shift;
        return ev;
    }
}
