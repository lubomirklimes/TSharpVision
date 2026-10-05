namespace TSharpVision.Diagnostics.Keyboard.Profiles;

/// <summary>A key position, named like the W3C <c>code</c> values. Independent of layout and of generated text.</summary>
public enum PhysicalKey
{
    Backquote, Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0, Minus, Equal,
    KeyA, KeyB, KeyC, KeyD, KeyE, KeyF, KeyG, KeyH, KeyI, KeyJ, KeyK, KeyL, KeyM,
    KeyN, KeyO, KeyP, KeyQ, KeyR, KeyS, KeyT, KeyU, KeyV, KeyW, KeyX, KeyY, KeyZ,
    BracketLeft, BracketRight, Backslash, Semicolon, Quote, Comma, Period, Slash, IntlBackslash, Space,
    Escape, Tab, Backspace, Enter,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Insert, Delete, Home, End, PageUp, PageDown, ArrowUp, ArrowDown, ArrowLeft, ArrowRight,
    Numpad0, Numpad1, Numpad2, Numpad3, Numpad4, Numpad5, Numpad6, Numpad7, Numpad8, Numpad9,
    NumpadDecimal, NumpadDivide, NumpadMultiply, NumpadSubtract, NumpadAdd, NumpadEnter, NumLock,
    ShiftLeft, ControlLeft, AltLeft, AltRight,
}

/// <summary>Fixed facts about key positions.</summary>
public static class PhysicalKeys
{
    public static readonly IReadOnlyList<PhysicalKey> All = Enum.GetValues<PhysicalKey>();

    public static readonly IReadOnlyList<PhysicalKey> Letters =
        All.Where(k => k is >= PhysicalKey.KeyA and <= PhysicalKey.KeyZ).ToArray();

    // PC set-1 make codes; the second value marks keys sent with the E0 prefix.
    private static readonly Dictionary<PhysicalKey, (byte Scan, bool Extended)> ScanCodes = BuildScanCodes();

    /// <summary>The PC set-1 make code, which is also what the Console driver reports as the raw scan.</summary>
    public static (byte Scan, bool Extended) ScanCode(PhysicalKey key) => ScanCodes[key];

    /// <summary>The lower-case ASCII letter of a letter key on a QWERTY arrangement.</summary>
    public static char Letter(PhysicalKey key) => (char)('a' + (key - PhysicalKey.KeyA));

    private static Dictionary<PhysicalKey, (byte, bool)> BuildScanCodes()
    {
        var map = new Dictionary<PhysicalKey, (byte, bool)>();
        void Row(byte first, IEnumerable<PhysicalKey> keys)
        {
            foreach (PhysicalKey key in keys) map[key] = (first++, false);
        }
        void Extended(PhysicalKey key, byte scan) => map[key] = (scan, true);
        static IEnumerable<PhysicalKey> K(string letters) => letters.Select(c => PhysicalKey.KeyA + (c - 'a'));

        map[PhysicalKey.Escape] = (0x01, false);
        Row(0x02, [PhysicalKey.Digit1, PhysicalKey.Digit2, PhysicalKey.Digit3, PhysicalKey.Digit4, PhysicalKey.Digit5,
            PhysicalKey.Digit6, PhysicalKey.Digit7, PhysicalKey.Digit8, PhysicalKey.Digit9, PhysicalKey.Digit0,
            PhysicalKey.Minus, PhysicalKey.Equal, PhysicalKey.Backspace, PhysicalKey.Tab]);
        Row(0x10, [.. K("qwertyuiop"), PhysicalKey.BracketLeft, PhysicalKey.BracketRight, PhysicalKey.Enter,
            PhysicalKey.ControlLeft]);
        Row(0x1E, [.. K("asdfghjkl"), PhysicalKey.Semicolon, PhysicalKey.Quote, PhysicalKey.Backquote,
            PhysicalKey.ShiftLeft, PhysicalKey.Backslash]);
        Row(0x2C, [.. K("zxcvbnm"), PhysicalKey.Comma, PhysicalKey.Period, PhysicalKey.Slash]);
        map[PhysicalKey.NumpadMultiply] = (0x37, false);
        map[PhysicalKey.AltLeft] = (0x38, false);
        map[PhysicalKey.Space] = (0x39, false);
        Row(0x3B, [PhysicalKey.F1, PhysicalKey.F2, PhysicalKey.F3, PhysicalKey.F4, PhysicalKey.F5,
            PhysicalKey.F6, PhysicalKey.F7, PhysicalKey.F8, PhysicalKey.F9, PhysicalKey.F10]);
        Row(0x47, [PhysicalKey.Numpad7, PhysicalKey.Numpad8, PhysicalKey.Numpad9, PhysicalKey.NumpadSubtract,
            PhysicalKey.Numpad4, PhysicalKey.Numpad5, PhysicalKey.Numpad6, PhysicalKey.NumpadAdd,
            PhysicalKey.Numpad1, PhysicalKey.Numpad2, PhysicalKey.Numpad3, PhysicalKey.Numpad0,
            PhysicalKey.NumpadDecimal]);
        map[PhysicalKey.IntlBackslash] = (0x56, false);
        map[PhysicalKey.F11] = (0x57, false);
        map[PhysicalKey.F12] = (0x58, false);

        Extended(PhysicalKey.NumpadEnter, 0x1C);
        Extended(PhysicalKey.NumpadDivide, 0x35);
        Extended(PhysicalKey.AltRight, 0x38);
        Extended(PhysicalKey.Home, 0x47);
        Extended(PhysicalKey.ArrowUp, 0x48);
        Extended(PhysicalKey.PageUp, 0x49);
        Extended(PhysicalKey.ArrowLeft, 0x4B);
        Extended(PhysicalKey.ArrowRight, 0x4D);
        Extended(PhysicalKey.End, 0x4F);
        Extended(PhysicalKey.ArrowDown, 0x50);
        Extended(PhysicalKey.PageDown, 0x51);
        Extended(PhysicalKey.Insert, 0x52);
        Extended(PhysicalKey.Delete, 0x53);
        // Num Lock is the plain make code 45: sent with the E0 prefix it is no key at all to Windows.
        map[PhysicalKey.NumLock] = (0x45, false);
        return map;
    }
}
