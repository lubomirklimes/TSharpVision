using System.Text;
using TSharpVision.Text;

namespace TSharpVision.Terminal;

/// <summary>
/// How many terminal columns a character occupies: per-code-point <c>wcwidth</c> semantics, sourced from the core's
/// Unicode 16.0 width data (<see cref="TextLayout"/> with <see cref="TextWidthProfile.CodePoint"/>).
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>0: non-spacing and enclosing marks (Mn, Me), format characters (Cf, except U+00AD soft hyphen),
///   Hangul medial vowels and final consonants, zero-width space. They join the preceding cell's
///   grapheme.</description></item>
///   <item><description>2: East Asian Wide and Fullwidth characters and characters with a default emoji presentation
///   (Unicode 16.0 East_Asian_Width W/F and Emoji_Presentation).</description></item>
///   <item><description>1: everything else, including ambiguous-width characters (as xterm does by default).</description></item>
/// </list>
/// <para>
/// The width is decided one code point at a time, as a child program's <c>wcwidth</c> decides it, never per grapheme
/// cluster: a terminal grid has to agree with the program that positions the cursor in it. Full grapheme clustering
/// (emoji ZWJ sequences, skin-tone modifiers, regional-indicator pairs) is therefore not modelled: each code point
/// takes its own width, so such sequences may use more columns than a terminal that clusters them. The grid stays
/// consistent either way.
/// </para>
/// <para>
/// Terminal-specific override: regional indicators (U+1F1E6..U+1F1FF) are one column each, as in glibc, xterm and
/// most terminals' <c>wcwidth</c>, although Unicode gives them an emoji presentation (a flag is two of them: two
/// columns).
/// </para>
/// </remarks>
internal static class TerminalCharWidth
{
    private static readonly TextLayoutOptions CodePointOptions = new() { Profile = TextWidthProfile.CodePoint };

    // Basic Multilingual Plane cache: 0 = not computed yet, otherwise width + 1. Racing writers store the same value.
    private static readonly byte[] BmpCache = new byte[0x10000];

    /// <summary>
    /// Columns <paramref name="codePoint"/> occupies: 0, 1 or 2. C0/C1 controls and invalid code points answer -1; the
    /// emulator never prints them.
    /// </summary>
    public static int GetWidth(int codePoint)
    {
        if (codePoint < 0x20 || (codePoint >= 0x7F && codePoint < 0xA0) || codePoint > 0x10FFFF) return -1;
        if (codePoint < 0x300) return 1;   // Latin, including U+00AD, which terminals show
        if (!Rune.IsValid(codePoint)) return -1;

        if (codePoint <= 0xFFFF)
        {
            byte cached = BmpCache[codePoint];
            if (cached != 0) return cached - 1;
            int width = Compute(codePoint);
            BmpCache[codePoint] = (byte)(width + 1);
            return width;
        }

        return Compute(codePoint);
    }

    private static int Compute(int codePoint)
    {
        if (codePoint is >= 0x1F1E6 and <= 0x1F1FF) return 1;   // regional indicators: see remarks
        if (codePoint == 0x200B) return 0;                       // zero-width space

        Span<char> units = stackalloc char[2];
        int length = new Rune(codePoint).EncodeToUtf16(units);
        return Math.Clamp(TextLayout.Measure(units[..length], CodePointOptions), 0, 2);
    }
}
