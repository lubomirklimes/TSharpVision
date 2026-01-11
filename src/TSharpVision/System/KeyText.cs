namespace TSharpVision;

/// <summary>
/// Helpers for extracting printable text from a key event.
/// </summary>
public static class KeyText
{
    public static string PrintableText(in KeyDownEvent keyDown, bool includeTab = false, bool extendedLegacy = true)
    {
        if (!string.IsNullOrEmpty(keyDown.text))
            return keyDown.text;

        byte ch = keyDown.charScan.charCode;
        if (includeTab && ch == '\t')
            return "\t";

        if (ch >= 32 && (extendedLegacy ? ch < 255 : ch < 127))
            return ((char)ch).ToString();

        return string.Empty;
    }
}

