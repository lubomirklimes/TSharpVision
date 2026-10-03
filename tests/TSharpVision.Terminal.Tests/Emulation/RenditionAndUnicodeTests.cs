using Xunit;
using static TSharpVision.Terminal.Tests.Emulation.EmulatorTestKit;

namespace TSharpVision.Terminal.Tests.Emulation;

/// <summary>U-1b: SGR in all its forms, colour intent kept in the model, and the Unicode cell-width model.</summary>
public sealed class RenditionAndUnicodeTests
{
    private static TerminalStyle StyleOf(string sgr) => Fed(sgr + "X").GetCell(0, 0).Style;

    // ── SGR ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("\x1b[31m", 1)]
    [InlineData("\x1b[37m", 7)]
    [InlineData("\x1b[91m", 9)]
    [InlineData("\x1b[97m", 15)]
    [InlineData("\x1b[38;5;196m", 196)]
    [InlineData("\x1b[38:5:21m", 21)]
    public void ForegroundColoursKeepTheirIndex(string sgr, int index)
        => Assert.Equal(TerminalColor.FromIndex((byte)index), StyleOf(sgr).Foreground);

    [Theory]
    [InlineData("\x1b[44m", 4)]
    [InlineData("\x1b[103m", 11)]
    [InlineData("\x1b[48;5;232m", 232)]
    public void BackgroundColoursKeepTheirIndex(string sgr, int index)
        => Assert.Equal(TerminalColor.FromIndex((byte)index), StyleOf(sgr).Background);

    [Theory]
    [InlineData("\x1b[38;2;10;20;30m")]
    [InlineData("\x1b[38:2:10:20:30m")]
    [InlineData("\x1b[38:2::10:20:30m")]
    [InlineData("\x1b[38:2:0:10:20:30m")]
    public void TrueColourInEveryFormIsKeptExactly(string sgr)
        => Assert.Equal(TerminalColor.FromRgb(10, 20, 30), StyleOf(sgr).Foreground);

    [Fact]
    public void ExtendedColourParametersAreNotMistakenForOtherAttributes()
    {
        // 38;5;1 is "colour 1", not "…;1 = bold"; 48;2;4;5;7 is a colour, not underline, blink and reverse.
        TerminalStyle indexed = StyleOf("\x1b[38;5;1;4m");
        Assert.Equal(TerminalColor.FromIndex(1), indexed.Foreground);
        Assert.Equal(TerminalCellAttributes.Underline, indexed.Attributes);

        TerminalStyle rgb = StyleOf("\x1b[48;2;4;5;7;1m");
        Assert.Equal(TerminalColor.FromRgb(4, 5, 7), rgb.Background);
        Assert.Equal(TerminalCellAttributes.Bold, rgb.Attributes);

        TerminalStyle colon = StyleOf("\x1b[38:2::1:2:3;9m");
        Assert.Equal(TerminalColor.FromRgb(1, 2, 3), colon.Foreground);
        Assert.Equal(TerminalCellAttributes.Strikethrough, colon.Attributes);
    }

    [Fact]
    public void TruncatedExtendedColoursAreIgnoredSafely()
    {
        Assert.Equal(TerminalStyle.Default, StyleOf("\x1b[38;5m"));
        Assert.Equal(TerminalStyle.Default, StyleOf("\x1b[48;2;1;2m"));
        Assert.Equal(TerminalColor.FromIndex(255), StyleOf("\x1b[38;5;999m").Foreground);   // clamped
    }

    [Fact]
    public void AttributesSetAndResetIndependently()
    {
        TerminalStyle on = StyleOf("\x1b[1;2;3;4;5;7;8;9m");
        Assert.Equal(TerminalCellAttributes.Bold | TerminalCellAttributes.Dim | TerminalCellAttributes.Italic
                     | TerminalCellAttributes.Underline | TerminalCellAttributes.Blink | TerminalCellAttributes.Inverse
                     | TerminalCellAttributes.Hidden | TerminalCellAttributes.Strikethrough, on.Attributes);

        Assert.Equal(TerminalCellAttributes.None, StyleOf("\x1b[1;2;3;4;5;7;8;9m\x1b[22;23;24;25;27;28;29m").Attributes);
        Assert.Equal(TerminalCellAttributes.None, StyleOf("\x1b[4m\x1b[4:0m").Attributes);
        Assert.Equal(TerminalCellAttributes.Underline, StyleOf("\x1b[4:3m").Attributes);
        Assert.Equal(TerminalStyle.Default, StyleOf("\x1b[1;31;44m\x1b[m"));
        Assert.Equal(TerminalStyle.Default, StyleOf("\x1b[1;31;44m\x1b[0m"));
        Assert.Equal(TerminalColor.Default, StyleOf("\x1b[31;44m\x1b[39;49m").Foreground);
    }

    [Fact]
    public void UnknownSgrParametersAreIgnored()
        => Assert.Equal(TerminalColor.FromIndex(2), StyleOf("\x1b[999;32;1000m").Foreground);

    [Fact]
    public void ColouredTextKeepsItsColoursCellByCell()
    {
        TerminalEmulator e = Fed("\x1b[31mR\x1b[32mG\x1b[0mN");
        Assert.Equal(TerminalColor.FromIndex(1), e.GetCell(0, 0).Style.Foreground);
        Assert.Equal(TerminalColor.FromIndex(2), e.GetCell(0, 1).Style.Foreground);
        Assert.Equal(TerminalStyle.Default, e.GetCell(0, 2).Style);
    }

    // ── colour mapping at the rendering boundary ─────────────────────────────

    [Theory]
    [InlineData("\x1b[31m", 0x04)]            // ANSI red → VGA red
    [InlineData("\x1b[34m", 0x01)]            // ANSI blue → VGA blue
    [InlineData("\x1b[33m", 0x06)]            // ANSI yellow → VGA brown
    [InlineData("\x1b[1;31m", 0x0C)]          // bold red → bright red
    [InlineData("\x1b[91m", 0x0C)]
    [InlineData("\x1b[1m", 0x0F)]             // bold default → white
    [InlineData("", 0x07)]                    // default → light grey on black
    [InlineData("\x1b[38;5;196m", 0x04)]      // cube (255,0,0): nearer VGA red (170,0,0) than light red (255,85,85)
    [InlineData("\x1b[38;5;21m", 0x01)]       // cube (0,0,255): nearer VGA blue than light blue
    [InlineData("\x1b[38;5;232m", 0x00)]      // darkest grey → black
    [InlineData("\x1b[38;5;255m", 0x0F)]      // lightest grey → white
    [InlineData("\x1b[38;2;250;250;80m", 0x0E)]   // yellow-ish → yellow
    [InlineData("\x1b[44;33m", 0x16)]          // brown on blue
    [InlineData("\x1b[7;31m", 0x40)]           // inverse: red background, black text
    [InlineData("\x1b[8;31;42m", 0x22)]        // concealed: text in the background colour
    public void ColoursMapDeterministicallyToVga(string sgr, int attribute)
    {
        var mapper = new TerminalColorMapper();
        Assert.Equal((byte)attribute, mapper.ToAttribute(StyleOf(sgr), reverseVideo: false));
    }

    [Fact]
    public void ReverseVideoSwapsEveryCellAndAColourMapReplacesTheAnsiTable()
    {
        var mapper = new TerminalColorMapper();
        Assert.Equal((byte)0x70, mapper.ToAttribute(TerminalStyle.Default, reverseVideo: true));

        byte[] map = Enumerable.Range(0, 16).Select(i => (byte)(15 - i)).ToArray();
        mapper.ApplyColorMap(map);
        Assert.Equal((byte)0x0E, mapper.ToAttribute(StyleOf("\x1b[31m"), false));   // ANSI 1 → 14 by the map
    }

    // ── Unicode and widths ───────────────────────────────────────────────────

    [Fact]
    public void NonAsciiCharactersTakeOneCellEach()
    {
        TerminalEmulator e = Fed("čüΩ€", 10, 1);
        Assert.Equal(new[] { "č", "ü", "Ω", "€" }, Enumerable.Range(0, 4).Select(c => e.GetCell(0, c).Text));
        e.AssertCursor(0, 4);
    }

    [Fact]
    public void ASupplementaryCharacterIsOneCellNotTwoSurrogates()
    {
        TerminalEmulator e = Fed("a𝄞b", 10, 1);
        Assert.Equal("𝄞", e.GetCell(0, 1).Text);
        Assert.Equal(1, e.GetCell(0, 1).Width);
        Assert.Equal("b", e.GetCell(0, 2).Text);
        Assert.Equal('�', e.GetCell(0, 1).DisplayChar);   // one UTF-16 unit cannot show it
    }

    [Fact]
    public void WideCharactersTakeTwoCells()
    {
        TerminalEmulator e = Fed("中文😀x", 10, 1);
        Assert.Equal((2, "中"), (e.GetCell(0, 0).Width, e.GetCell(0, 0).Text));
        Assert.True(e.GetCell(0, 1).IsContinuation);
        Assert.Equal("文", e.GetCell(0, 2).Text);
        Assert.Equal((2, "😀"), (e.GetCell(0, 4).Width, e.GetCell(0, 4).Text));
        Assert.Equal("x", e.GetCell(0, 6).Text);
        e.AssertCursor(0, 7);
        Assert.Equal("中文😀x", e.GetRowText(0));
    }

    [Fact]
    public void CombiningMarksJoinThePrecedingCell()
    {
        TerminalEmulator e = Fed("éạ̈!", 10, 1);
        Assert.Equal("é", e.GetCell(0, 0).Text);
        Assert.Equal("ạ̈", e.GetCell(0, 1).Text);
        Assert.Equal("!", e.GetCell(0, 2).Text);
        e.AssertCursor(0, 3);
    }

    [Fact]
    public void ACombiningMarkWithNothingBeforeItIsDropped()
        => Fed("́x", 5, 1).AssertScreen("x");

    [Fact]
    public void AFloodOfCombiningMarksStaysBounded()
    {
        TerminalEmulator e = Fed("a" + string.Concat(Enumerable.Repeat("́", 10_000)), 5, 1);
        Assert.True(e.GetCell(0, 0).Text.Length <= TerminalCell.MaximumClusterLength);
    }

    [Fact]
    public void AWideCharacterThatDoesNotFitWrapsAndLeavesTheLastColumnEmpty()
    {
        TerminalEmulator e = Fed("abcd中", 5, 2);
        e.AssertScreen("abcd", "中");
        Assert.True(e.GetCell(0, 4).IsEmpty);
        Assert.True(e.IsRowWrapped(0));
    }

    [Fact]
    public void OverwritingHalfAWideCharacterBlanksTheOtherHalf()
    {
        TerminalEmulator left = Fed("中文\x1b[1;1Hx", 6, 1);
        Assert.Equal("x", left.GetCell(0, 0).Text);
        Assert.True(left.GetCell(0, 1).IsEmpty && !left.GetCell(0, 1).IsContinuation);

        TerminalEmulator right = Fed("中文\x1b[1;2Hx", 6, 1);
        Assert.True(right.GetCell(0, 0).IsEmpty);
        Assert.Equal("x", right.GetCell(0, 1).Text);
        Assert.Equal("文", right.GetCell(0, 2).Text);
    }

    [Fact]
    public void DeletingAndInsertingAroundWideCharactersNeverLeavesAHalf()
    {
        TerminalEmulator e = Fed("a中b\x1b[1;3H\x1b[P", 6, 1);   // delete the continuation cell
        Assert.Equal("a b", e.GetRowText(0));
        Assert.DoesNotContain(Enumerable.Range(0, 6), c => e.GetCell(0, c).Width == 2);

        TerminalEmulator pushed = Fed("abcd中\x1b[1;1H\x1b[@", 6, 1);   // the wide character is pushed half off
        Assert.Equal(" abcd", pushed.GetRowText(0));
    }

    [Theory]
    [InlineData(0x41, 1)]
    [InlineData(0x10D, 1)]     // č
    [InlineData(0x301, 0)]     // combining acute
    [InlineData(0x200D, 0)]    // zero-width joiner
    [InlineData(0x200B, 0)]    // zero-width space
    [InlineData(0xAD, 1)]      // soft hyphen is shown
    [InlineData(0x4E2D, 2)]    // 中
    [InlineData(0xAC00, 2)]    // 가
    [InlineData(0xFF21, 2)]    // Ａ full width
    [InlineData(0x1F600, 2)]   // 😀
    [InlineData(0x1D11E, 1)]   // 𝄞
    [InlineData(0x2500, 1)]    // ─
    [InlineData(0x1B, -1)]
    [InlineData(0x85, -1)]
    public void CharacterWidths(int codePoint, int width) => Assert.Equal(width, TerminalCharWidth.GetWidth(codePoint));
}
