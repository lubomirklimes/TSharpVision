using System.Globalization;

namespace TSharpVision.Text;

/// <summary>
/// Per-scalar width and emoji properties pinned to <see cref="UnicodeVersion"/>, generated from the Unicode
/// Character Database into <c>UnicodeWidthData.g.cs</c> (a two-level table, 256 code points per block).
/// </summary>
internal static partial class UnicodeWidthData
{
    /// <summary>East Asian Width W or F.</summary>
    public const byte EawWide = 0x01;
    /// <summary>East Asian Width A (ambiguous).</summary>
    public const byte EawAmbiguous = 0x02;
    /// <summary>General_Category Mn, Me or Cf, except U+00AD SOFT HYPHEN.</summary>
    public const byte ZeroWidth = 0x04;
    /// <summary>Emoji_Presentation.</summary>
    public const byte EmojiPresentation = 0x08;
    /// <summary>Extended_Pictographic.</summary>
    public const byte ExtendedPictographic = 0x10;
    /// <summary>Base of a standardized text (VS15) or emoji (VS16) variation sequence.</summary>
    public const byte EmojiVariationBase = 0x20;
    /// <summary>Regional indicator U+1F1E6..U+1F1FF.</summary>
    public const byte RegionalIndicator = 0x40;
    /// <summary>Hangul medial vowel or final consonant jamo (zero advance in per-code-point width).</summary>
    public const byte HangulMedialFinal = 0x80;

    /// <summary>Property byte of <paramref name="codePoint"/>; 0 outside 0..10FFFF.</summary>
    public static byte Get(int codePoint)
    {
        if ((uint)codePoint > 0x10FFFF) return 0;
        return Stage2[(Stage1[codePoint >> 8] << 8) | (codePoint & 0xFF)];
    }

    /// <summary>
    /// True when the running runtime's Unicode data matches <see cref="UnicodeVersion"/> on code points whose
    /// properties changed between Unicode 15.0, 16.0 and 17.0. A mismatch is a diagnostic only: segmentation then
    /// follows the runtime while widths follow the pinned table.
    /// </summary>
    public static bool RuntimeMatchesPinnedVersion => s_runtimeMatches.Value;

    private static readonly Lazy<bool> s_runtimeMatches = new(() =>
        // New in 16.0: U+0897 ARABIC PEPPER (Mn) and U+1FAE9 FACE WITH BAGS UNDER EYES (So).
        CharUnicodeInfo.GetUnicodeCategory(0x0897) == UnicodeCategory.NonSpacingMark
        && CharUnicodeInfo.GetUnicodeCategory(0x1FAE9) == UnicodeCategory.OtherSymbol
        // New in 17.0, so still unassigned in 16.0: U+1FAEA DISTORTED FACE.
        && CharUnicodeInfo.GetUnicodeCategory(0x1FAEA) == UnicodeCategory.OtherNotAssigned);
}
