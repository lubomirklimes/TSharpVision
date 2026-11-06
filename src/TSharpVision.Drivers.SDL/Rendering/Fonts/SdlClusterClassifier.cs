using System.Text;

namespace TSharpVision.Drivers.SDL.Rendering.Fonts;

/// <summary>How a grapheme cluster asks to be presented, from its scalars (UTS #51 presentation selectors and emoji components).</summary>
internal enum SdlClusterPresentation
{
    /// <summary>No preference: the configured font chain order applies.</summary>
    Default,
    /// <summary>Emoji presentation (VS16, an emoji modifier, regional indicators, a keycap, or a wide pictograph): emoji fonts first.</summary>
    Emoji,
    /// <summary>Text presentation (VS15): emoji fonts last.</summary>
    Text,
}

/// <summary>
/// Pure scalar classification used to resolve a cluster to one primary font (TV13m). No SDL calls, so it is tested
/// headless. The pictograph ranges approximate Unicode 16 Extended_Pictographic; they only steer font order for wide
/// clusters (the core already decided the cluster's width from the Unicode 16 tables).
/// </summary>
internal static class SdlClusterClassifier
{
    internal const int ZeroWidthJoiner = 0x200D;
    internal const int TextSelector = 0xFE0E;   // VS15
    internal const int EmojiSelector = 0xFE0F;  // VS16
    internal const int CombiningKeycap = 0x20E3;

    /// <summary>
    /// Scalars a font need not have for the cluster to count as covered: they shape to nothing (or are consumed by a
    /// ligature). ZWJ/ZWNJ, variation selectors, tag characters, and other default-ignorable format characters.
    /// Combining marks are NOT ignorable: the font must have them, or the mark would be a .notdef box.
    /// </summary>
    internal static bool IsIgnorableForCoverage(int scalar) => scalar switch
    {
        0x00AD or 0x034F or 0x061C or 0x180E or 0xFEFF => true,
        >= 0x180B and <= 0x180F => true,       // Mongolian free variation selectors
        >= 0x200B and <= 0x200F => true,       // ZWSP, ZWNJ, ZWJ, LRM, RLM
        >= 0x202A and <= 0x202E => true,       // bidi embedding controls
        >= 0x2060 and <= 0x206F => true,       // word joiner, invisible operators, bidi isolates
        >= 0xFE00 and <= 0xFE0F => true,       // VS1-VS16
        >= 0xFFF0 and <= 0xFFF8 => true,
        >= 0x1BCA0 and <= 0x1BCA3 => true,     // shorthand format controls
        >= 0x1D173 and <= 0x1D17A => true,     // musical format controls
        >= 0xE0000 and <= 0xE0FFF => true,     // tags, VS17-VS256
        _ => false,
    };

    /// <summary>Emoji skin-tone modifiers U+1F3FB..U+1F3FF.</summary>
    internal static bool IsEmojiModifier(int scalar) => scalar is >= 0x1F3FB and <= 0x1F3FF;

    /// <summary>Regional indicator symbols U+1F1E6..U+1F1FF (flag letters).</summary>
    internal static bool IsRegionalIndicator(int scalar) => scalar is >= 0x1F1E6 and <= 0x1F1FF;

    /// <summary>Approximation of Extended_Pictographic (Unicode 16): the blocks emoji come from.</summary>
    internal static bool IsPictographic(int scalar) => scalar switch
    {
        0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139 or 0x2328 or 0x2388 or 0x23CF
            or 0x24C2 or 0x25B6 or 0x25C0 or 0x2934 or 0x2935 or 0x2B50 or 0x2B55
            or 0x3030 or 0x303D or 0x3297 or 0x3299 => true,
        >= 0x2194 and <= 0x2199 => true,
        >= 0x21A9 and <= 0x21AA => true,
        >= 0x231A and <= 0x231B => true,
        >= 0x23E9 and <= 0x23F3 => true,
        >= 0x23F8 and <= 0x23FA => true,
        >= 0x25AA and <= 0x25AB => true,
        >= 0x25FB and <= 0x25FE => true,
        >= 0x2600 and <= 0x27BF => true,
        >= 0x2B05 and <= 0x2B07 => true,
        >= 0x2B1B and <= 0x2B1C => true,
        >= 0x1F000 and <= 0x1FAFF => true,
        >= 0x1FC00 and <= 0x1FFFD => true,
        _ => false,
    };

    /// <summary>
    /// Presentation of <paramref name="cluster"/> occupying <paramref name="cells"/> cells. VS15 wins over everything
    /// (explicit text request); VS16, an emoji modifier, a regional indicator, a keycap, or a ZWJ sequence or 2-cell
    /// cluster led by a pictograph ask for emoji; anything else has no preference.
    /// </summary>
    internal static SdlClusterPresentation Classify(ReadOnlySpan<char> cluster, int cells)
    {
        bool vs15 = false, emoji = false, zwj = false;
        int first = -1;
        for (int i = 0; i < cluster.Length;)
        {
            int scalar = NextScalar(cluster, ref i);
            if (first < 0) first = scalar;
            switch (scalar)
            {
                case TextSelector: vs15 = true; break;
                case EmojiSelector: emoji = true; break;
                case CombiningKeycap: emoji = true; break;
                case ZeroWidthJoiner: zwj = true; break;
                default:
                    if (IsEmojiModifier(scalar) || IsRegionalIndicator(scalar)) emoji = true;
                    break;
            }
        }
        if (vs15) return SdlClusterPresentation.Text;
        if (emoji) return SdlClusterPresentation.Emoji;
        if (first >= 0 && IsPictographic(first) && (zwj || cells >= 2)) return SdlClusterPresentation.Emoji;
        return SdlClusterPresentation.Default;
    }

    /// <summary>The scalar at <paramref name="index"/> (U+FFFD for a lone surrogate); advances past it.</summary>
    internal static int NextScalar(ReadOnlySpan<char> text, ref int index)
    {
        if (Rune.DecodeFromUtf16(text[index..], out Rune rune, out int used) != System.Buffers.OperationStatus.Done)
        {
            index++;
            return 0xFFFD;
        }
        index += used;
        return rune.Value;
    }
}
