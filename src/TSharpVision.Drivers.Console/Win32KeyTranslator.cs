using TSharpVision.Constants;

namespace TSharpVision.Drivers.Console;

/// <summary>
/// Maps documented Win32 console input to TSharpVision's public key contract.
/// </summary>
public static class Win32KeyTranslator
{
    public const uint LEFT_CTRL_PRESSED = 0x0008;
    public const uint RIGHT_CTRL_PRESSED = 0x0004;
    public const uint LEFT_ALT_PRESSED = 0x0002;
    public const uint RIGHT_ALT_PRESSED = 0x0001;
    public const uint SHIFT_PRESSED = 0x0010;
    public const uint NUMLOCK_ON = 0x0020;
    public const uint SCROLLLOCK_ON = 0x0040;
    public const uint CAPSLOCK_ON = 0x0080;
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
    private static readonly (uint Input, ushort Output)[] StateFlags =
    [
        (SHIFT_PRESSED, Keys.kbShift),
        (LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED, Keys.kbCtrlShift),
        (LEFT_ALT_PRESSED | RIGHT_ALT_PRESSED, Keys.kbAltShift),
        (CAPSLOCK_ON, Keys.kbCapsState), (NUMLOCK_ON, Keys.kbNumState),
        (SCROLLLOCK_ON, Keys.kbScrollState)
    ];

    public static bool TryTranslate(bool keyDown, ushort vk, char ch, uint ctrlState, out TEvent ev)
    {
        ev = default;
        if (!keyDown || vk is 0x10 or 0x11 or 0x12 or 0x14 or 0x5B or 0x5C
            or 0x90 or 0x91 or >= 0xA0 and <= 0xA5) return false;

        ushort state = 0;
        foreach (var flag in StateFlags)
            if ((ctrlState & flag.Input) != 0) state |= flag.Output;
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
            command = (ushort)(FunctionKeys[vk - 0x70] | (alt ? 0x0200 : control ? 0x0100 : shift ? 0x0080 : 0));
        else if (Navigation.TryGetValue(vk, out var navigation))
            command = (ushort)((control ? navigation.Ctrl : navigation.Normal)
                | (shift && vk is 0x2D or 0x2E ? 0x0080 : 0));
        else if (Editing.TryGetValue(vk, out var editing))
            command = alt && editing.Alt != editing.Normal ? editing.Alt
                : control && editing.Ctrl != editing.Normal ? editing.Ctrl : shift ? editing.Shift : editing.Normal;
        else if (control && vk == 0x2C) command = Keys.kbCtrlPrtSc;

        if (command == 0) return EmitText(ch, state, out ev);
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = command;
        ev.keyDown.shiftState = state;
        return true;
    }

    private static bool EmitText(char character, ushort state, out TEvent ev)
    {
        ev = default;
        if (character == '\0') return false;
        ev.What = Events.evKeyDown;
        ev.keyDown.shiftState = state;
        if (!char.IsControl(character)) ev.keyDown.text = character.ToString();
        if (character <= byte.MaxValue)
        {
            ev.keyDown.keyCode = character;
            ev.keyDown.charScan = new CharScanType((byte)character, 0);
        }
        return true;
    }
}
