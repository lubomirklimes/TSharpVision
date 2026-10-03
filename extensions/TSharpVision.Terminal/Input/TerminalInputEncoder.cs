using System.Text;
using TSharpVision.Constants;

namespace TSharpVision.Terminal;

/// <summary>
/// Translates TSharpVision key events and pasted text into the bytes an xterm-compatible terminal sends to the
/// program — the input half of the emulator, independent of any view.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>Text: any Unicode the driver reports in <see cref="KeyDownEvent.text"/>, sent as is. With Alt
///   (and not Ctrl, so AltGr text is unaffected) it is prefixed with ESC — the "meta sends escape" convention.</description></item>
///   <item><description>Ctrl+A…Z and Ctrl+@ [ \ ] ^ _ ?: the C0 control (and DEL for Ctrl+?).</description></item>
///   <item><description>Cursor keys, Home, End: CSI A…D/H/F, or SS3 in application cursor mode (DECCKM); with modifiers
///   CSI 1;m X, where m = 1 + Shift + 2·Alt + 4·Ctrl.</description></item>
///   <item><description>Insert, Delete, PageUp, PageDown, F5–F12: CSI n ~ (CSI n;m ~ modified); F1–F4: SS3 P…S
///   (CSI 1;m P…S modified).</description></item>
///   <item><description>Enter CR, Backspace DEL, Ctrl+Backspace BS, Tab HT, Shift+Tab CSI Z, Escape ESC.</description></item>
/// </list>
/// </remarks>
public static class TerminalInputEncoder
{
    private const string Csi = "\x1b[";
    private const string Ss3 = "\x1bO";

    private static readonly Dictionary<ushort, char> AltLetters = new()
    {
        [Keys.kbAltA] = 'a', [Keys.kbAltB] = 'b', [Keys.kbAltC] = 'c', [Keys.kbAltD] = 'd', [Keys.kbAltE] = 'e',
        [Keys.kbAltF] = 'f', [Keys.kbAltG] = 'g', [Keys.kbAltH] = 'h', [Keys.kbAltI] = 'i', [Keys.kbAltJ] = 'j',
        [Keys.kbAltK] = 'k', [Keys.kbAltL] = 'l', [Keys.kbAltM] = 'm', [Keys.kbAltN] = 'n', [Keys.kbAltO] = 'o',
        [Keys.kbAltP] = 'p', [Keys.kbAltQ] = 'q', [Keys.kbAltR] = 'r', [Keys.kbAltS] = 's', [Keys.kbAltT] = 't',
        [Keys.kbAltU] = 'u', [Keys.kbAltV] = 'v', [Keys.kbAltW] = 'w', [Keys.kbAltX] = 'x', [Keys.kbAltY] = 'y',
        [Keys.kbAltZ] = 'z', [Keys.kbAlt1] = '1', [Keys.kbAlt2] = '2', [Keys.kbAlt3] = '3', [Keys.kbAlt4] = '4',
        [Keys.kbAlt5] = '5', [Keys.kbAlt6] = '6', [Keys.kbAlt7] = '7', [Keys.kbAlt8] = '8', [Keys.kbAlt9] = '9',
        [Keys.kbAlt0] = '0', [Keys.kbAltMinus] = '-', [Keys.kbAltEqual] = '=',
    };

    [Flags]
    private enum Modifiers
    {
        None = 0,
        Shift = 1,
        Alt = 2,
        Ctrl = 4,
    }

    /// <summary>
    /// The bytes (as UTF-16 text, to be sent as UTF-8) for <paramref name="key"/>, or null when the key has no terminal
    /// meaning and should be left to the host.
    /// </summary>
    public static string? EncodeKey(KeyDownEvent key, bool applicationCursorKeys)
    {
        Modifiers held = HeldModifiers(key.controlKeyState);
        ushort code = key.keyCode;

        if (Special(code, held, applicationCursorKeys) is { } special) return special;

        string text = key.text;
        if (text.Length == 0 && key.charScan.charCode is >= 0x20 and < 0x7F) text = ((char)key.charScan.charCode).ToString();
        if (text.Length == 0 && code == Keys.kbSpace) text = " ";

        if (text.Length > 0 && text[0] >= 0x20 && text[0] != 0x7F)
        {
            bool ctrl = (held & Modifiers.Ctrl) != 0, alt = (held & Modifiers.Alt) != 0;
            if (ctrl && !alt && text.Length == 1 && ControlFor(text[0]) is { } control)
                return control.ToString();
            if (alt && !ctrl) return "\x1b" + text;
            return text;   // plain text, or AltGr (Ctrl+Alt) text such as '€'
        }

        // A control character the driver reports by code (kbCtrlA…kbCtrlZ) or in the character.
        int c0 = (code & 0xFF00) == 0 && (code & 0xFF) is > 0 and < 0x20 ? code & 0xFF
            : key.charScan.charCode is > 0 and < 0x20 ? key.charScan.charCode : -1;
        if (c0 > 0)
        {
            string control = ((char)c0).ToString();
            return (held & Modifiers.Alt) != 0 ? "\x1b" + control : control;
        }

        if (AltLetters.TryGetValue(code, out char letter))
            return "\x1b" + ((held & Modifiers.Shift) != 0 ? char.ToUpperInvariant(letter) : letter);

        return null;
    }

    /// <summary>
    /// The bytes for pasting <paramref name="text"/>: line breaks become CR (what Enter sends), and with
    /// <paramref name="bracketed"/> the text is wrapped in ESC [200~ … ESC [201~ with any end marker inside it removed,
    /// so pasted text can never end the bracket early.
    /// </summary>
    public static string EncodePaste(string text, bool bracketed)
    {
        ArgumentNullException.ThrowIfNull(text);
        string normalized = text.Replace("\r\n", "\r", StringComparison.Ordinal).Replace('\n', '\r');
        if (!bracketed) return normalized;
        string payload = normalized.Replace("\x1b[201~", string.Empty, StringComparison.Ordinal);
        return "\x1b[200~" + payload + "\x1b[201~";
    }

    private static Modifiers HeldModifiers(uint state)
    {
        Modifiers held = Modifiers.None;
        if ((state & Keys.kbShift) != 0) held |= Modifiers.Shift;
        if ((state & Keys.kbAltShift) != 0) held |= Modifiers.Alt;
        if ((state & Keys.kbCtrlShift) != 0) held |= Modifiers.Ctrl;
        return held;
    }

    private static char? ControlFor(char c) => c switch
    {
        >= 'a' and <= 'z' => (char)(c - 'a' + 1),
        >= '@' and <= '_' => (char)(c - '@'),   // @ A..Z [ \ ] ^ _
        ' ' or '2' => '\0',
        '?' => '\x7F',
        _ => null,
    };

    private static string? Special(ushort code, Modifiers held, bool applicationCursorKeys)
    {
        switch (code)
        {
            case Keys.kbEnter: return (held & Modifiers.Alt) != 0 ? "\x1b\r" : "\r";
            case Keys.kbCtrlEnter: return "\r";
            case Keys.kbBack: return (held & Modifiers.Alt) != 0 ? "\x1b\x7f" : (held & Modifiers.Ctrl) != 0 ? "\x08" : "\x7f";
            case Keys.kbCtrlBack: return "\x08";
            case Keys.kbAltBack: return "\x1b\x7f";
            case Keys.kbTab: return (held & Modifiers.Shift) != 0 ? Csi + "Z" : "\t";
            case Keys.kbShiftTab: return Csi + "Z";
            case Keys.kbEsc: return "\x1b";
            case Keys.kbAltSpace: return "\x1b ";

            case Keys.kbUp: return Cursor('A', held, applicationCursorKeys);
            case Keys.kbDown: return Cursor('B', held, applicationCursorKeys);
            case Keys.kbRight: return Cursor('C', held, applicationCursorKeys);
            case Keys.kbLeft: return Cursor('D', held, applicationCursorKeys);
            case Keys.kbHome: return Cursor('H', held, applicationCursorKeys);
            case Keys.kbEnd: return Cursor('F', held, applicationCursorKeys);
            case Keys.kbCtrlRight: return Cursor('C', held | Modifiers.Ctrl, applicationCursorKeys);
            case Keys.kbCtrlLeft: return Cursor('D', held | Modifiers.Ctrl, applicationCursorKeys);
            case Keys.kbCtrlHome: return Cursor('H', held | Modifiers.Ctrl, applicationCursorKeys);
            case Keys.kbCtrlEnd: return Cursor('F', held | Modifiers.Ctrl, applicationCursorKeys);

            case Keys.kbIns: return Tilde(2, held);
            case Keys.kbDel: return Tilde(3, held);
            case Keys.kbPgUp: return Tilde(5, held);
            case Keys.kbPgDn: return Tilde(6, held);
            case Keys.kbCtrlIns: return Tilde(2, held | Modifiers.Ctrl);
            case Keys.kbShiftIns: return Tilde(2, held | Modifiers.Shift);
            case Keys.kbCtrlDel: return Tilde(3, held | Modifiers.Ctrl);
            case Keys.kbShiftDel: return Tilde(3, held | Modifiers.Shift);
            case Keys.kbCtrlShiftIns: return Tilde(2, held | Modifiers.Ctrl | Modifiers.Shift);
            case Keys.kbCtrlShiftDel: return Tilde(3, held | Modifiers.Ctrl | Modifiers.Shift);
            case Keys.kbCtrlPgUp: return Tilde(5, held | Modifiers.Ctrl);
            case Keys.kbCtrlPgDn: return Tilde(6, held | Modifiers.Ctrl);
        }

        return FunctionKey(code, held);
    }

    private static string Cursor(char final, Modifiers held, bool application)
    {
        if (held == Modifiers.None) return (application ? Ss3 : Csi) + final;
        return $"{Csi}1;{1 + (int)held}{final}";
    }

    private static string Tilde(int number, Modifiers held)
        => held == Modifiers.None ? $"{Csi}{number}~" : $"{Csi}{number};{1 + (int)held}~";

    private static string? FunctionKey(ushort code, Modifiers held)
    {
        int number;
        Modifiers implied = Modifiers.None;
        int high = code >> 8;
        if ((code & 0xFF) != 0) return null;

        switch (high)
        {
            case >= 0x3B and <= 0x44: number = high - 0x3B + 1; break;                                  // F1..F10
            case 0x85 or 0x86: number = high - 0x85 + 11; break;                                         // F11/F12
            case >= 0x54 and <= 0x5D: number = high - 0x54 + 1; implied = Modifiers.Shift; break;       // Shift+F1..F10
            case >= 0x5E and <= 0x67: number = high - 0x5E + 1; implied = Modifiers.Ctrl; break;        // Ctrl+F1..F10
            case >= 0x68 and <= 0x71: number = high - 0x68 + 1; implied = Modifiers.Alt; break;         // Alt+F1..F10
            case 0x87 or 0x88: number = high - 0x87 + 11; implied = Modifiers.Shift; break;             // Shift+F11/F12
            case 0x89 or 0x8A: number = high - 0x89 + 11; implied = Modifiers.Ctrl; break;              // Ctrl+F11/F12
            case 0x8B or 0x8C: number = high - 0x8B + 11; implied = Modifiers.Alt; break;               // Alt+F11/F12
            default: return null;
        }

        Modifiers modifiers = held | implied;
        if (number <= 4)
        {
            char final = (char)('P' + number - 1);
            return modifiers == Modifiers.None ? Ss3 + final : $"{Csi}1;{1 + (int)modifiers}{final}";
        }

        int tilde = number switch { 5 => 15, 6 => 17, 7 => 18, 8 => 19, 9 => 20, 10 => 21, 11 => 23, _ => 24 };
        return Tilde(tilde, modifiers);
    }

    /// <summary>UTF-8 bytes of an encoded key or paste.</summary>
    internal static byte[] ToBytes(string encoded) => Encoding.UTF8.GetBytes(encoded);
}
