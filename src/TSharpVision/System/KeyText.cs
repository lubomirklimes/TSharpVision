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

        byte ch = keyDown.charScan.charCode;
        if (includeTab && ch == '\t')
            return "\t";

        // DEL (0x7F) is a control character, not text: it is the low byte of kbCtrlBack.
        if (ch >= 32 && ch != 0x7F && (extendedLegacy ? ch < 255 : ch < 127))
            return CharTextCache[ch];

        return string.Empty;
    }
}

