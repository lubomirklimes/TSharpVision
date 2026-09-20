using TSharpVision.Constants;

namespace TSharpVision.Drivers.Console;

/// <summary>
/// Maps documented Win32 console input to TSharpVision's public key contract.
/// </summary>
internal static class Win32KeyTranslator
{
    /// <summary>Win32 control-state bit indicating that left Control is held.</summary>
    public const uint LEFT_CTRL_PRESSED = 0x0008;
    /// <summary>Win32 control-state bit indicating that right Control is held.</summary>
    public const uint RIGHT_CTRL_PRESSED = 0x0004;
    /// <summary>Win32 control-state bit indicating that left Alt is held.</summary>
    public const uint LEFT_ALT_PRESSED = 0x0002;
    /// <summary>Win32 control-state bit indicating that right Alt is held.</summary>
    public const uint RIGHT_ALT_PRESSED = 0x0001;
    /// <summary>Win32 control-state bit indicating that Shift is held.</summary>
    public const uint SHIFT_PRESSED = 0x0010;
    /// <summary>Win32 control-state bit indicating that Num Lock is active.</summary>
    public const uint NUMLOCK_ON = 0x0020;
    /// <summary>Win32 control-state bit indicating that Scroll Lock is active.</summary>
    public const uint SCROLLLOCK_ON = 0x0040;
    /// <summary>Win32 control-state bit indicating that Caps Lock is active.</summary>
    public const uint CAPSLOCK_ON = 0x0080;
    /// <summary>Win32 control-state bit indicating that the key is an enhanced keyboard key.</summary>
    public const uint ENHANCED_KEY = 0x0100;

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
    private static readonly ushort[] FunctionKeys =
        [Keys.kbF1, Keys.kbF2, Keys.kbF3, Keys.kbF4, Keys.kbF5, Keys.kbF6,
         Keys.kbF7, Keys.kbF8, Keys.kbF9, Keys.kbF10, Keys.kbF11, Keys.kbF12];
    private static readonly ushort[] ShiftFunctionKeys =
        [Keys.kbShiftF1, Keys.kbShiftF2, Keys.kbShiftF3, Keys.kbShiftF4, Keys.kbShiftF5, Keys.kbShiftF6,
         Keys.kbShiftF7, Keys.kbShiftF8, Keys.kbShiftF9, Keys.kbShiftF10, Keys.kbShiftF11, Keys.kbShiftF12];
    private static readonly ushort[] ControlFunctionKeys =
        [Keys.kbCtrlF1, Keys.kbCtrlF2, Keys.kbCtrlF3, Keys.kbCtrlF4, Keys.kbCtrlF5, Keys.kbCtrlF6,
         Keys.kbCtrlF7, Keys.kbCtrlF8, Keys.kbCtrlF9, Keys.kbCtrlF10, Keys.kbCtrlF11, Keys.kbCtrlF12];
    private static readonly ushort[] AltFunctionKeys =
        [Keys.kbAltF1, Keys.kbAltF2, Keys.kbAltF3, Keys.kbAltF4, Keys.kbAltF5, Keys.kbAltF6,
         Keys.kbAltF7, Keys.kbAltF8, Keys.kbAltF9, Keys.kbAltF10, Keys.kbAltF11, Keys.kbAltF12];

    // Win32 VK identity -> target command identity. This is data from two public APIs.
    private static readonly Dictionary<ushort, (ushort Normal, ushort Ctrl)> Navigation = new()
    {
        [0x24] = (Keys.kbHome, Keys.kbCtrlHome), [0x26] = (Keys.kbUp, Keys.kbUp),
        [0x21] = (Keys.kbPgUp, Keys.kbCtrlPgUp),
        [0x25] = (Keys.kbLeft, Keys.kbCtrlLeft), [0x27] = (Keys.kbRight, Keys.kbCtrlRight),
        [0x23] = (Keys.kbEnd, Keys.kbCtrlEnd),
        [0x28] = (Keys.kbDown, Keys.kbDown), [0x22] = (Keys.kbPgDn, Keys.kbCtrlPgDn),
        [0x2D] = (Keys.kbIns, Keys.kbCtrlIns),
        [0x2E] = (Keys.kbDel, Keys.kbCtrlDel)
    };
    private static readonly Dictionary<ushort, (ushort Normal, ushort Shift, ushort Ctrl, ushort Alt)> Editing = new()
    {
        [0x08] = (Keys.kbBack, Keys.kbBack, Keys.kbCtrlBack, Keys.kbAltBack),
        [0x09] = (Keys.kbTab, Keys.kbShiftTab, Keys.kbTab, Keys.kbTab),
        [0x0D] = (Keys.kbEnter, Keys.kbEnter, Keys.kbCtrlEnter, Keys.kbEnter),
        [0x1B] = (Keys.kbEsc, Keys.kbEsc, Keys.kbEsc, Keys.kbEsc)
    };
    private static readonly (uint Input, uint Output)[] StateFlags =
    [
        (SHIFT_PRESSED, Keys.kbShift),
        (LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED, Keys.kbCtrlShift),
        (LEFT_ALT_PRESSED | RIGHT_ALT_PRESSED, Keys.kbAltShift),
        (CAPSLOCK_ON, Keys.kbCapsState), (NUMLOCK_ON, Keys.kbNumState),
        (SCROLLLOCK_ON, Keys.kbScrollState)
    ];

    /// <summary>Translates a Win32 key record; returns false for releases, modifier-only keys, and unmapped input.</summary>
    public static bool TryTranslate(bool keyDown, ushort vk, char ch, uint ctrlState, out TEvent ev)
    {
        ev = default;
        if (!keyDown || vk is 0x10 or 0x11 or 0x12 or 0x14 or 0x5B or 0x5C
            or 0x90 or 0x91 or >= 0xA0 and <= 0xA5) return false;

        uint state = ToControlKeyState(ctrlState);
        bool control = (state & Keys.kbCtrlShift) != 0;
        bool alt = (state & Keys.kbAltShift) != 0;
        bool shift = (state & Keys.kbShift) != 0;
        bool printable = ch != '\0' && !char.IsControl(ch);

        // The input signature has no layout identity. Prefer supplied text for Ctrl+Alt.
        if (printable && control && alt)
            return EmitText(ch, state, out ev);

        ushort command = 0;
        if (vk >= 'A' && vk <= 'Z' && (control || alt))
            command = (alt ? AltLetters : ControlLetters)[vk - 'A'];
        else if (alt && vk >= '0' && vk <= '9') command = AltDigits[vk - '0'];
        else if (alt && vk == 0x20) command = Keys.kbAltSpace;
        else if (alt && vk == 0xBD) command = Keys.kbAltMinus;
        else if (alt && vk == 0xBB) command = Keys.kbAltEqual;
        else if (vk >= 0x70 && vk <= 0x7B)
        {
            int index = vk - 0x70;
            command = alt ? AltFunctionKeys[index] : control ? ControlFunctionKeys[index]
                : shift ? ShiftFunctionKeys[index] : FunctionKeys[index];
        }
        else if (Navigation.TryGetValue(vk, out var navigation))
            command = control && shift && vk == 0x2D ? Keys.kbCtrlShiftIns
                : control && shift && vk == 0x2E ? Keys.kbCtrlShiftDel
                : control ? navigation.Ctrl
                : shift && vk == 0x2D ? Keys.kbShiftIns
                : shift && vk == 0x2E ? Keys.kbShiftDel
                : navigation.Normal;
        else if (Editing.TryGetValue(vk, out var editing))
            command = alt && editing.Alt != editing.Normal ? editing.Alt
                : control && editing.Ctrl != editing.Normal ? editing.Ctrl : shift ? editing.Shift : editing.Normal;
        else if (control && vk == 0x2C) command = Keys.kbCtrlPrtSc;

        if (command == 0) return EmitText(ch, state, out ev);
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = command;
        ev.keyDown.charScan = new CharScanType(command);
        ev.keyDown.controlKeyState = state;
        return true;
    }

    private static bool EmitText(char character, uint state, out TEvent ev)
    {
        ev = default;
        if (character == '\0') return false;
        ev.What = Events.evKeyDown;
        ev.keyDown.controlKeyState = state;
        if (!char.IsControl(character)) ev.keyDown.text = character.ToString();
        if (character <= byte.MaxValue)
        {
            ev.keyDown.keyCode = character;
            ev.keyDown.charScan = new CharScanType((byte)character, 0);
        }
        return true;
    }

    internal static uint ToControlKeyState(uint controlKeyState)
    {
        uint state = 0;
        foreach (var flag in StateFlags)
            if ((controlKeyState & flag.Input) != 0) state |= flag.Output;
        return state;
    }
}
