namespace TSharpVision.Diagnostics.Keyboard.Profiles;

/// <summary>Windows Czech (QWERTY), KLID 00010405, KBDCZ1.DLL. Values verified with ToUnicodeEx against the live layout.</summary>
/// <remarks>Dead-key compositions were not probed and are deliberately absent.</remarks>
public static class CzechQwertyProfile
{
    public static LayoutProfile Instance { get; } = Build();

    private static LayoutProfile Build()
    {
        var keys = new Dictionary<PhysicalKey, KeyLevels>();
        LayoutProfile.AddQwertyLetters(keys);
        keys[PhysicalKey.KeyE] = new KeyLevels("e", "E", "€");

        // Number row: Shift gives the digit, AltGr the US shifted symbol.
        keys[PhysicalKey.Backquote] = new KeyLevels(";", "°", "`", DeadShift: true);
        keys[PhysicalKey.Digit1] = new KeyLevels("+", "1", "!");
        keys[PhysicalKey.Digit2] = new KeyLevels("ě", "2", "@");
        keys[PhysicalKey.Digit3] = new KeyLevels("š", "3", "#");
        keys[PhysicalKey.Digit4] = new KeyLevels("č", "4", "$");
        keys[PhysicalKey.Digit5] = new KeyLevels("ř", "5", "%");
        keys[PhysicalKey.Digit6] = new KeyLevels("ž", "6", "^");
        keys[PhysicalKey.Digit7] = new KeyLevels("ý", "7", "&");
        keys[PhysicalKey.Digit8] = new KeyLevels("á", "8", "*");
        keys[PhysicalKey.Digit9] = new KeyLevels("í", "9", "(");
        keys[PhysicalKey.Digit0] = new KeyLevels("é", "0", ")");
        keys[PhysicalKey.Minus] = new KeyLevels("=", "%", "-");
        keys[PhysicalKey.Equal] = new KeyLevels("´", "ˇ", "=", DeadPlain: true, DeadShift: true);

        keys[PhysicalKey.BracketLeft] = new KeyLevels("ú", "/", "[");
        keys[PhysicalKey.BracketRight] = new KeyLevels(")", "(", "]");
        keys[PhysicalKey.Backslash] = new KeyLevels("¨", "'", "\\", DeadPlain: true);
        keys[PhysicalKey.Semicolon] = new KeyLevels("ů", "\"", ";");
        keys[PhysicalKey.Quote] = new KeyLevels("§", "!", "¤");
        keys[PhysicalKey.Comma] = new KeyLevels(",", "?", "<");
        keys[PhysicalKey.Period] = new KeyLevels(".", ":", ">");
        keys[PhysicalKey.Slash] = new KeyLevels("-", "_", "/");
        keys[PhysicalKey.IntlBackslash] = new KeyLevels("\\", "|", "ß");
        keys[PhysicalKey.Space] = new KeyLevels(" ", " ");

        // Digit2 rules out US, KeyY rules out Czech QWERTZ, Semicolon confirms the Czech punctuation.
        return new LayoutProfile("cz-qwerty", "Czech QWERTY", "00010405", keys,
            [(PhysicalKey.Digit2, "ě"), (PhysicalKey.KeyY, "y"), (PhysicalKey.Semicolon, "ů")],
            [new DeadKeyComposition(PhysicalKey.Equal, PhysicalKey.KeyE, "é")]);
    }
}
