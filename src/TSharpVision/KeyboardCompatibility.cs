using TSharpVision.Constants;

namespace TSharpVision;

/// <summary>Lookup helpers for the historical DOS scan-code key table.</summary>
internal static class KeyboardCompatibility
{
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

    public static ushort AltCode(char character)
    {
        char upper = char.ToUpperInvariant(character);
        return upper is >= 'A' and <= 'Z' ? AltLetters[upper - 'A'] : Keys.kbNoKey;
    }

    public static char AltCharacter(ushort keyCode)
    {
        int letter = Array.IndexOf(AltLetters, keyCode);
        if (letter >= 0) return (char)('A' + letter);
        int digit = Array.IndexOf(AltDigits, keyCode);
        return digit >= 0 ? (char)('0' + digit) : '\0';
    }
}
