using System.Globalization;
using System.Text;
using TSharpVision.Text;
using Xunit;

namespace TSharpVision.Terminal.Tests.Emulation;

/// <summary>
/// TV13p: the emulator's per-code-point widths now come from the core Unicode 16.0 data. These tests pin the change
/// against the hand-written pre-16.0 table the emulator used before (kept here only as a reference), so any further
/// drift is deliberate.
/// </summary>
public sealed class TerminalWidthReconciliationTests
{
    // The pre-TV13p TerminalCharWidth table, verbatim.
    private static readonly (int First, int Last)[] OldWide =
    {
        (0x1100, 0x115F), (0x231A, 0x231B), (0x2329, 0x232A), (0x23E9, 0x23EC), (0x23F0, 0x23F0), (0x23F3, 0x23F3),
        (0x25FD, 0x25FE), (0x2614, 0x2615), (0x2648, 0x2653), (0x267F, 0x267F), (0x2693, 0x2693), (0x26A1, 0x26A1),
        (0x26AA, 0x26AB), (0x26BD, 0x26BE), (0x26C4, 0x26C5), (0x26CE, 0x26CE), (0x26D4, 0x26D4), (0x26EA, 0x26EA),
        (0x26F2, 0x26F3), (0x26F5, 0x26F5), (0x26FA, 0x26FA), (0x26FD, 0x26FD), (0x2705, 0x2705), (0x270A, 0x270B),
        (0x2728, 0x2728), (0x274C, 0x274C), (0x274E, 0x274E), (0x2753, 0x2755), (0x2757, 0x2757), (0x2795, 0x2797),
        (0x27B0, 0x27B0), (0x27BF, 0x27BF), (0x2B1B, 0x2B1C), (0x2B50, 0x2B50), (0x2B55, 0x2B55),
        (0x2E80, 0x303E), (0x3041, 0x33FF), (0x3400, 0x4DBF), (0x4E00, 0x9FFF), (0xA000, 0xA4CF), (0xA960, 0xA97F),
        (0xAC00, 0xD7A3), (0xF900, 0xFAFF), (0xFE10, 0xFE19), (0xFE30, 0xFE6F), (0xFF00, 0xFF60), (0xFFE0, 0xFFE6),
        (0x16FE0, 0x16FE4), (0x17000, 0x18AFF), (0x1B000, 0x1B2FF), (0x1F004, 0x1F004), (0x1F0CF, 0x1F0CF),
        (0x1F18E, 0x1F18E), (0x1F191, 0x1F19A), (0x1F200, 0x1F202), (0x1F210, 0x1F23B), (0x1F240, 0x1F248),
        (0x1F250, 0x1F251), (0x1F260, 0x1F265), (0x1F300, 0x1F320), (0x1F32D, 0x1F335), (0x1F337, 0x1F37C),
        (0x1F37E, 0x1F393), (0x1F3A0, 0x1F3CA), (0x1F3CF, 0x1F3D3), (0x1F3E0, 0x1F3F0), (0x1F3F4, 0x1F3F4),
        (0x1F3F8, 0x1F43E), (0x1F440, 0x1F440), (0x1F442, 0x1F4FC), (0x1F4FF, 0x1F53D), (0x1F54B, 0x1F54E),
        (0x1F550, 0x1F567), (0x1F57A, 0x1F57A), (0x1F595, 0x1F596), (0x1F5A4, 0x1F5A4), (0x1F5FB, 0x1F64F),
        (0x1F680, 0x1F6C5), (0x1F6CC, 0x1F6CC), (0x1F6D0, 0x1F6D2), (0x1F6D5, 0x1F6D7), (0x1F6DC, 0x1F6DF),
        (0x1F6EB, 0x1F6EC), (0x1F6F4, 0x1F6FC), (0x1F7E0, 0x1F7EB), (0x1F7F0, 0x1F7F0), (0x1F90C, 0x1F93A),
        (0x1F93C, 0x1F945), (0x1F947, 0x1F9FF), (0x1FA70, 0x1FAFF), (0x20000, 0x2FFFD), (0x30000, 0x3FFFD),
    };

    private static int OldWidth(int codePoint)
    {
        if (codePoint < 0x20 || (codePoint >= 0x7F && codePoint < 0xA0) || codePoint > 0x10FFFF) return -1;
        if (codePoint < 0x300) return 1;
        if (!Rune.IsValid(codePoint)) return -1;
        if (codePoint is >= 0x1160 and <= 0x11FF || codePoint == 0x200B) return 0;
        switch (Rune.GetUnicodeCategory(new Rune(codePoint)))
        {
            case UnicodeCategory.NonSpacingMark:
            case UnicodeCategory.EnclosingMark:
            case UnicodeCategory.Format:
                return 0;
        }

        foreach ((int first, int last) in OldWide)
            if (codePoint >= first && codePoint <= last) return 2;
        return 1;
    }

    private static List<(int CodePoint, int Old, int New)> Differences(bool assignedOnly)
    {
        var differences = new List<(int, int, int)>();
        for (int cp = 0; cp <= 0x10FFFF; cp++)
        {
            if (cp is >= 0xD800 and <= 0xDFFF) continue;
            if (assignedOnly && Rune.GetUnicodeCategory(new Rune(cp)) == UnicodeCategory.OtherNotAssigned) continue;
            int before = OldWidth(cp), after = TerminalCharWidth.GetWidth(cp);
            if (before != after) differences.Add((cp, before, after));
        }

        return differences;
    }

    [Fact]
    public void WidthsAgreeWithTheCoreCodePointProfileExceptTheDocumentedOverrides()
    {
        var options = new TextLayoutOptions { Profile = TextWidthProfile.CodePoint };
        for (int cp = 0x300; cp <= 0x10FFFF; cp++)
        {
            if (cp is >= 0xD800 and <= 0xDFFF) continue;
            if (cp is >= 0x1F1E6 and <= 0x1F1FF) continue;   // regional indicators: one column (override)
            if (cp is >= 0x7F and < 0xA0) continue;
            string text = char.ConvertFromUtf32(cp);
            int expected = TextLayout.Measure(text, options);
            Assert.True(expected == TerminalCharWidth.GetWidth(cp), $"U+{cp:X4}: core {expected}, terminal {TerminalCharWidth.GetWidth(cp)}");
        }
    }

    [Fact]
    public void TheChangeFromThePreUnicode16TableIsPinned()
    {
        List<(int CodePoint, int Old, int New)> assigned = Differences(assignedOnly: true);

        // Dumped for the report: TSV_DUMP_TERMINAL_WIDTH_DIFF=<path>.
        string? dump = Environment.GetEnvironmentVariable("TSV_DUMP_TERMINAL_WIDTH_DIFF");
        if (!string.IsNullOrEmpty(dump))
            File.WriteAllLines(dump, Differences(assignedOnly: false).Select(d => $"U+{d.CodePoint:X4}\t{d.Old}\t{d.New}\t{Rune.GetUnicodeCategory(new Rune(d.CodePoint))}"));

        Assert.Equal(ExpectedAssignedDifferences, assigned.Count);
        Assert.Contains(assigned, d => d.CodePoint == 0x2630 && d.Old == 1 && d.New == 2);   // trigram: wide in 16.0
        Assert.Contains(assigned, d => d.CodePoint == 0x3248 && d.Old == 2 && d.New == 1);   // EAW ambiguous
        Assert.Contains(assigned, d => d.CodePoint == 0xD7B0 && d.Old == 1 && d.New == 0);   // jamo vowel: joins
        Assert.DoesNotContain(assigned, d => d.CodePoint is >= 0x1F1E6 and <= 0x1F1FF);       // RI kept at 1
        Assert.DoesNotContain(assigned, d => d.Old == -1 || d.New == -1);                     // controls unchanged
    }

    // Assigned (per the runtime) scalars whose width changed:
    // * 683 narrow to wide: U+2630..2637, U+268A..268F, U+4DC0..4DFF, U+16FF0..16FF1, U+18B00..18CD5,
    //   U+18CFF..18D08, U+1AFF0..1AFFE (kana extended), U+1D300..1D376;
    // * 8 wide to narrow: U+3248..324F (East Asian Ambiguous);
    // * 72 one to zero: Hangul Jamo Extended-B vowels and finals U+D7B0..D7C6, U+D7CB..D7FB.
    // That is the 717 scalars of TV13j U0-C minus the 26 regional indicators kept at one column, plus the 72 jamo.
    private const int ExpectedAssignedDifferences = 763;

    [Theory]
    [InlineData(0x2630, 2)]    // \u2630 trigram: East Asian Wide since Unicode 16.0
    [InlineData(0x3248, 1)]    // circled number on black square: ambiguous, narrow
    [InlineData(0x1F1E8, 1)]   // regional indicator C: one column (terminal override)
    [InlineData(0x1F3FD, 2)]   // skin-tone modifier: its own wide cell (no clustering)
    [InlineData(0xFE0F, 0)]    // VS16 joins the preceding cell
    [InlineData(0x1160, 0)]    // Hangul medial vowel filler
    [InlineData(0x115F, 2)]    // Hangul choseong filler
    public void SelectedWidths(int codePoint, int width) => Assert.Equal(width, TerminalCharWidth.GetWidth(codePoint));
}
