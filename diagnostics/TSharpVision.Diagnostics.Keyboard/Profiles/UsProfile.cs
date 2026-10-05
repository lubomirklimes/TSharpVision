namespace TSharpVision.Diagnostics.Keyboard.Profiles;

/// <summary>Windows US English, KLID 00000409. Every printable key is ASCII, so every one has an identity.</summary>
public static class UsProfile
{
    public static LayoutProfile Instance { get; } = Build();

    private static LayoutProfile Build()
    {
        var keys = new Dictionary<PhysicalKey, KeyLevels>();
        LayoutProfile.AddQwertyLetters(keys);
        void Row(string plain, string shift, params PhysicalKey[] positions)
        {
            for (int i = 0; i < positions.Length; i++)
                keys[positions[i]] = new KeyLevels(plain[i].ToString(), shift[i].ToString());
        }
        Row("`1234567890-=", "~!@#$%^&*()_+",
            PhysicalKey.Backquote, PhysicalKey.Digit1, PhysicalKey.Digit2, PhysicalKey.Digit3, PhysicalKey.Digit4,
            PhysicalKey.Digit5, PhysicalKey.Digit6, PhysicalKey.Digit7, PhysicalKey.Digit8, PhysicalKey.Digit9,
            PhysicalKey.Digit0, PhysicalKey.Minus, PhysicalKey.Equal);
        Row("[]\\;',./\\ ", "{}|:\"<>?| ",
            PhysicalKey.BracketLeft, PhysicalKey.BracketRight, PhysicalKey.Backslash, PhysicalKey.Semicolon,
            PhysicalKey.Quote, PhysicalKey.Comma, PhysicalKey.Period, PhysicalKey.Slash, PhysicalKey.IntlBackslash,
            PhysicalKey.Space);

        return new LayoutProfile("us", "US English", "00000409", keys,
            [(PhysicalKey.Digit2, "2"), (PhysicalKey.KeyY, "y"), (PhysicalKey.Semicolon, ";")]);
    }
}
