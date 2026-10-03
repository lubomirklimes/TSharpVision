using Xunit;
using static TSharpVision.Terminal.Tests.Emulation.EmulatorTestKit;

namespace TSharpVision.Terminal.Tests.Emulation;

/// <summary>U-1b golden screens: cursor addressing, erasing, insert/delete, scrolling regions, wrapping and tabs.</summary>
public sealed class ScreenOperationTests
{
    // ── prompt, carriage return, cursor addressing ───────────────────────────

    [Fact]
    public void ASimplePrompt()
    {
        TerminalEmulator e = Fed("user@host:~$ ", 20, 3);
        e.AssertScreen("user@host:~$", "", "");
        e.AssertCursor(0, 13);
    }

    [Fact]
    public void AProgressLineRewrittenWithCarriageReturn()
    {
        TerminalEmulator e = Fed("Downloading  10%\rDownloading  55%\rDownloading 100%\r\ndone", 20, 3);
        e.AssertScreen("Downloading 100%", "done", "");
    }

    [Fact]
    public void CursorAddressedUpdatesLandWhereAddressed()
    {
        TerminalEmulator e = Fed("\x1b[2;3HX\x1b[1;1HA\x1b[4;10HZ\x1b[HB", 10, 4);
        e.AssertScreen("B", "  X", "", "         Z");
    }

    [Theory]
    [InlineData("\x1b[5;5H\x1b[A", 3, 4)]      // CUU default 1
    [InlineData("\x1b[5;5H\x1b[0A", 3, 4)]     // 0 means 1
    [InlineData("\x1b[5;5H\x1b[2B", 6, 4)]
    [InlineData("\x1b[5;5H\x1b[3C", 4, 7)]
    [InlineData("\x1b[5;5H\x1b[9D", 4, 0)]     // clamped at the left margin
    [InlineData("\x1b[5;5H\x1b[99A", 0, 4)]    // clamped at the top
    [InlineData("\x1b[5;5H\x1b[2E", 6, 0)]     // CNL
    [InlineData("\x1b[5;5H\x1b[2F", 2, 0)]     // CPL
    [InlineData("\x1b[5;5H\x1b[7G", 4, 6)]     // CHA
    [InlineData("\x1b[5;5H\x1b[2`", 4, 1)]     // HPA
    [InlineData("\x1b[5;5H\x1b[3d", 2, 4)]     // VPA
    [InlineData("\x1b[5;5H\x1b[2a", 4, 6)]     // HPR
    [InlineData("\x1b[5;5H\x1b[2e", 6, 4)]     // VPR
    [InlineData("\x1b[;7H", 0, 6)]             // omitted row
    [InlineData("\x1b[3H", 2, 0)]              // omitted column
    [InlineData("\x1b[3;4f", 2, 3)]            // HVP
    [InlineData("\x1b[99;99H", 7, 9)]          // clamped to the grid
    public void CursorMovement(string stream, int row, int column)
        => Fed(stream, 10, 8).AssertCursor(row, column);

    [Fact]
    public void SaveAndRestoreCursorKeepPositionAndRendition()
    {
        TerminalEmulator e = Fed("\x1b[3;4H\x1b[31m\u001b7\x1b[H\x1b[0mA\u001b8B\x1b[2;2H\x1b[s\x1b[5;5H\x1b[uC", 10, 6);
        Assert.Equal("B", e.GetCell(2, 3).Text);
        Assert.Equal(TerminalColor.FromIndex(1), e.GetCell(2, 3).Style.Foreground);
        Assert.Equal("C", e.GetCell(1, 1).Text);
    }

    [Fact]
    public void BackspaceStopsAtTheLeftMarginAndDoesNotEraseAnything()
        => Fed("ab\b\b\b\bX", 10, 2).AssertScreen("Xb", "");

    [Fact]
    public void LineFeedKeepsTheColumnUnlessNewLineModeIsOn()
    {
        Fed("ab\ncd", 10, 3).AssertScreen("ab", "  cd", "");
        Fed("\x1b[20hab\ncd", 10, 3).AssertScreen("ab", "cd", "");
    }

    // ── wrapping ─────────────────────────────────────────────────────────────

    [Fact]
    public void PrintingInTheLastColumnLeavesTheCursorThereUntilTheNextCharacter()
    {
        TerminalEmulator e = Fed("abcde", 5, 3);
        e.AssertCursor(0, 4);
        e.AssertScreen("abcde", "", "");
        Assert.False(e.IsRowWrapped(0));

        e.Feed("f"u8);
        e.AssertScreen("abcde", "f", "");
        e.AssertCursor(1, 1);
        Assert.True(e.IsRowWrapped(0));
    }

    [Fact]
    public void CarriageReturnAfterAFullRowStaysOnThatRow()
        => Fed("abcde\rX", 5, 3).AssertScreen("Xbcde", "", "");

    [Fact]
    public void CarriageReturnLineFeedAfterAFullRowGivesNoBlankRow()
        => Fed("abcde\r\nf", 5, 3).AssertScreen("abcde", "f", "");

    [Fact]
    public void BackspaceAfterAFullRowMovesLeftFromTheLastColumn()
        => Fed("abcde\bX", 5, 2).AssertScreen("abcXe", "");

    [Fact]
    public void WithoutAutoWrapTheLastColumnIsOverwritten()
    {
        TerminalEmulator e = Fed("\x1b[?7labcdefg", 5, 2);
        e.AssertScreen("abcdg", "");
        e.AssertCursor(0, 4);
    }

    [Fact]
    public void WrappingAtTheBottomScrollsIntoScrollback()
    {
        TerminalEmulator e = Fed("aaaaabbbbbccccc", 5, 2);
        e.AssertScreen("bbbbb", "ccccc");
        Assert.Equal(1, e.ScrollbackCount);
    }

    // ── tabs ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TabsStopEveryEightColumnsAndNeverPassTheMargin()
    {
        TerminalEmulator e = Fed("a\tb\tc\td", 20, 2);
        Assert.Equal("b", e.GetCell(0, 8).Text);
        Assert.Equal("c", e.GetCell(0, 16).Text);
        Assert.Equal("d", e.GetCell(0, 19).Text);   // the next stop is past the margin: the last column
    }

    [Fact]
    public void TabDoesNotWriteSpacesOverExistingText()
        => Fed("abcdefghij\r\tX", 20, 2).AssertScreen("abcdefghXj", "");

    [Fact]
    public void TabStopsCanBeSetClearedAndWalkedBackwards()
    {
        TerminalEmulator e = Fed("\x1b[3g\x1b[1;4H\x1bH\x1b[1;11H\x1bH\x1b[H\tA\tB\x1b[ZC\x1b[1;11H\x1b[0g\x1b[H\x1b[2IX", 20, 2);
        Assert.Equal("A", e.GetCell(0, 3).Text);
        Assert.Equal("C", e.GetCell(0, 10).Text);    // back tab from after B returns to its stop
        Assert.Equal("X", e.GetCell(0, 19).Text);    // stop at 10 cleared: CHT 2 goes 3, then to the margin
    }

    // ── erasing ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("\x1b[K", "abc", "fghij")]
    [InlineData("\x1b[0K", "abc", "fghij")]
    [InlineData("\x1b[1K", "    e", "fghij")]
    [InlineData("\x1b[2K", "", "fghij")]
    public void EraseInLine(string erase, string first, string second)
        => Fed("abcde\r\nfghij\x1b[1;4H" + erase, 5, 2).AssertScreen(first, second);

    [Theory]
    [InlineData("\x1b[J", "aaa", "b", "")]
    [InlineData("\x1b[1J", "", "  b", "ccc")]
    [InlineData("\x1b[2J", "", "", "")]
    public void EraseInDisplay(string erase, string r0, string r1, string r2)
        => Fed("aaa\r\nbbb\r\nccc\x1b[2;2H" + erase, 5, 3).AssertScreen(r0, r1, r2);

    [Fact]
    public void EraseScrollbackKeepsTheScreen()
    {
        TerminalEmulator e = Fed("1\r\n2\r\n3\r\n4\x1b[3J", 5, 2);
        Assert.Equal(0, e.ScrollbackCount);
        e.AssertScreen("3", "4");
    }

    [Fact]
    public void EraseCharactersBlanksWithoutShifting()
        => Fed("abcdef\x1b[1;2H\x1b[3X", 10, 1).AssertScreen("a   ef");

    [Fact]
    public void ErasedCellsTakeTheCurrentBackground()
    {
        TerminalEmulator e = Fed("\x1b[44m\x1b[2J\x1b[0m", 4, 2);
        Assert.Equal(TerminalColor.FromIndex(4), e.GetCell(1, 3).Style.Background);
        Assert.True(e.GetCell(1, 3).IsEmpty);
    }

    // ── insert and delete ────────────────────────────────────────────────────

    [Fact]
    public void InsertAndDeleteCharactersShiftTheRow()
    {
        Fed("abcdef\x1b[1;3H\x1b[2@", 8, 1).AssertScreen("ab  cdef");
        Fed("abcdefgh\x1b[1;3H\x1b[2@", 8, 1).AssertScreen("ab  cdef");   // pushed past the margin: lost
        Fed("abcdef\x1b[1;2H\x1b[2P", 8, 1).AssertScreen("adef");
    }

    [Fact]
    public void InsertModeInsertsPrintedCharacters()
        => Fed("abcd\x1b[1;2H\x1b[4hXY\x1b[4lZ", 8, 1).AssertScreen("aXYZcd");

    [Fact]
    public void InsertAndDeleteLinesWorkBelowTheCursor()
    {
        TerminalEmulator e = Fed("1\r\n2\r\n3\r\n4\x1b[2;3H\x1b[L", 5, 4);
        e.AssertScreen("1", "", "2", "3");
        e.AssertCursor(1, 0);

        Fed("1\r\n2\r\n3\r\n4\x1b[2;1H\x1b[2M", 5, 4).AssertScreen("1", "4", "", "");
    }

    // ── scrolling regions ────────────────────────────────────────────────────

    [Fact]
    public void LineFeedAtTheBottomMarginScrollsOnlyTheRegion()
    {
        TerminalEmulator e = Fed("top\r\nr1\r\nr2\r\nr3\r\nbottom\x1b[2;4r\x1b[4;1H\nnew", 8, 5);
        e.AssertScreen("top", "r2", "r3", "new", "bottom");
        Assert.Equal(0, e.ScrollbackCount);   // an inner region keeps no history
    }

    [Fact]
    public void ReverseIndexAtTheTopMarginScrollsTheRegionDown()
        => Fed("top\r\nr1\r\nr2\r\nr3\r\nbottom\x1b[2;4r\x1b[2;1H\x1bMnew", 8, 5)
            .AssertScreen("top", "new", "r1", "r2", "bottom");

    [Fact]
    public void ScrollUpAndDownMoveOnlyTheRegion()
    {
        Fed("a\r\nb\r\nc\r\nd\x1b[2;3r\x1b[S", 4, 4).AssertScreen("a", "c", "", "d");
        Fed("a\r\nb\r\nc\r\nd\x1b[2;3r\x1b[T", 4, 4).AssertScreen("a", "", "b", "d");
    }

    [Fact]
    public void InsertLineOutsideTheRegionDoesNothing()
        => Fed("a\r\nb\r\nc\r\nd\x1b[2;3r\x1b[4;1H\x1b[L", 4, 4).AssertScreen("a", "b", "c", "d");

    [Fact]
    public void OriginModeAddressesRelativeToTheRegion()
    {
        TerminalEmulator e = Fed("\x1b[2;4r\x1b[?6h\x1b[1;1HA\x1b[9;1HB\x1b[6n", 6, 5);
        Assert.Equal("A", e.GetCell(1, 0).Text);
        Assert.Equal("B", e.GetCell(3, 0).Text);   // clamped to the bottom margin
        Assert.Equal("\x1b[3;2R", e.TakeResponses()); // reported relative to the region
    }

    [Fact]
    public void AnInvalidRegionMeansTheWholeScreen()
    {
        TerminalEmulator e = Fed("\x1b[4;2r1\r\n2\r\n3\r\n4\r\n5", 4, 4);
        e.AssertScreen("2", "3", "4", "5");
        Assert.Equal(1, e.ScrollbackCount);
    }

    // ── clearing, repeat, alignment ──────────────────────────────────────────

    [Fact]
    public void ClearScreenAndHome()
        => Fed("junk\r\nmore\x1b[H\x1b[2Jclean", 6, 2).AssertScreen("clean", "");

    [Fact]
    public void RepeatRepeatsTheLastPrintedCharacter()
        => Fed("-\x1b[4bx", 10, 1).AssertScreen("-----x");

    [Fact]
    public void AlignmentPatternFillsTheScreen()
        => Fed("\x1b#8", 3, 2).AssertScreen("EEE", "EEE");

    [Fact]
    public void DecSpecialGraphicsDrawBoxes()
        => Fed("\x1b(0lqk\r\nx x\r\nmqj\x1b(Bq", 4, 3).AssertScreen("┌─┐", "│ │", "└─┘q");
}
