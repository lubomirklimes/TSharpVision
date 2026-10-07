using TSharpVision.Constants;

namespace TSharpVision;

/// <summary>
/// Helpers for extracting printable text from a key event.
/// </summary>
public static class KeyText
{
    private static readonly string[] CharTextCache = CreateCharTextCache();

    private static string[] CreateCharTextCache()
    {
        var cache = new string[256];
        for (int i = 0; i < cache.Length; i++)
            cache[i] = ((char)i).ToString();
        return cache;
    }

    /// <summary>Returns explicit event text when present; otherwise converts printable legacy bytes, optionally including Tab and bytes 128 through 254 (DEL, 127, is never text), or returns empty text.</summary>
    public static string PrintableText(in KeyDownEvent keyDown, bool includeTab = false, bool extendedLegacy = true)
    {
        if (!string.IsNullOrEmpty(keyDown.text))
            return keyDown.text;

        // A named special key is not text, whatever its legacy low byte happens to be.
        if (IsNamedSpecialKey(keyDown.keyCode))
            return string.Empty;

        byte ch = keyDown.charScan.charCode;
        if (includeTab && ch == '\t')
            return "\t";

        // DEL (0x7F) is a control character, not text: it is the low byte of kbCtrlBack.
        if (ch >= 32 && ch != 0x7F && (extendedLegacy ? ch < 255 : ch < 127))
            return CharTextCache[ch];

        return string.Empty;
    }

    /// <summary>
    /// Returns whether <paramref name="keyCode"/> is a named non-character key whose legacy low byte lies in the
    /// printable range. Such a code identifies a key, not a character: its low byte must never be read as text.
    /// </summary>
    /// <remarks>
    /// Almost every special key has a zero low byte and needs no entry here. The exceptions are the synthetic codes
    /// that were given a non-zero low byte to stay distinct from their neighbours: <see cref="Keys.kbCtrlShiftIns"/>
    /// (0x01CD) and <see cref="Keys.kbCtrlShiftDel"/> (0x01CE). The keypad's Gray +/- are deliberately not listed:
    /// their low byte is the character the key types.
    /// </remarks>
    public static bool IsNamedSpecialKey(ushort keyCode)
        => keyCode is Keys.kbCtrlShiftIns or Keys.kbCtrlShiftDel;
}
