using System.Text;
using Xunit;
using static TSharpVision.Terminal.Tests.Emulation.EmulatorTestKit;

namespace TSharpVision.Terminal.Tests.Emulation;

/// <summary>U-1b: primary/alternate screens, modes, replies to queries, titles, resize and bounded scrollback.</summary>
public sealed class ModesScreensAndResizeTests
{
    // ── alternate screen ─────────────────────────────────────────────────────

    [Fact]
    public void Mode1049SavesTheCursorClearsTheAlternateScreenAndRestoresEverything()
    {
        TerminalEmulator e = Fed("shell$ vim\x1b[?1049h", 12, 4);
        Assert.True(e.IsAlternateScreenActive);
        e.AssertScreen("", "", "", "");

        e.Feed("\x1b[H~\r\n~\r\n\x1b[4;1H:q"u8.ToArray());
        e.AssertScreen("~", "~", "", ":q");

        e.Feed("\x1b[?1049l"u8.ToArray());
        Assert.False(e.IsAlternateScreenActive);
        e.AssertScreen("shell$ vim", "", "", "");
        e.AssertCursor(0, 10);
    }

    [Fact]
    public void TheAlternateScreenKeepsNoScrollbackAndLeavesThePrimaryScrollbackAlone()
    {
        TerminalEmulator e = Fed("1\r\n2\r\n3", 5, 2);
        Assert.Equal(1, e.ScrollbackCount);

        e.Feed(Encoding.ASCII.GetBytes("\x1b[?1049h" + string.Concat(Enumerable.Range(0, 50).Select(i => $"{i}\r\n"))));
        Assert.Equal(e.Rows, e.BufferRowCount);   // no scrollback while the alternate screen is shown

        e.Feed("\x1b[?1049l"u8.ToArray());
        Assert.Equal(1, e.ScrollbackCount);
        e.AssertScreen("2", "3");
    }

    [Fact]
    public void Mode47And1047SwitchScreensWithoutTouchingTheCursorSave()
    {
        TerminalEmulator e = Fed("main\x1b[?47hALT\x1b[?47l", 10, 2);
        e.AssertScreen("main", "");

        TerminalEmulator cleared = Fed("\x1b[?1047hjunk\x1b[?1047l\x1b[?1047h", 10, 2);
        cleared.AssertScreen("", "");   // 1047 clears the alternate screen when leaving it
    }

    [Fact]
    public void ResizingWhileTheAlternateScreenIsShownResizesBoth()
    {
        TerminalEmulator e = Fed("primary\x1b[?1049h\x1b[HALT", 10, 3);   // the cursor carries over, so a program homes it
        e.Resize(6, 2);
        Assert.Equal((6, 2), (e.Columns, e.Rows));
        e.AssertScreen("ALT", "");

        e.Feed("\x1b[?1049l"u8.ToArray());
        Assert.Equal((6, 2), (e.Columns, e.Rows));
        Assert.Equal("primar", e.GetRowText(0));
    }

    // ── modes ────────────────────────────────────────────────────────────────

    [Fact]
    public void ModesFollowSetAndReset()
    {
        TerminalEmulator e = EmulatorTestKit.New();
        e.Feed("\x1b[?1h\x1b[?2004h\x1b[?25l\x1b=\x1b[?1000h\x1b[?1006h"u8.ToArray());
        Assert.True(e.ApplicationCursorKeys);
        Assert.True(e.BracketedPaste);
        Assert.False(e.CursorVisible);
        Assert.True(e.ApplicationKeypad);
        Assert.Equal(TerminalMouseTracking.Normal, e.MouseTracking);
        Assert.True(e.SgrMouseEncoding);

        e.Feed("\x1b[?1l\x1b[?2004l\x1b[?25h\x1b>\x1b[?1000l"u8.ToArray());
        Assert.False(e.ApplicationCursorKeys);
        Assert.False(e.BracketedPaste);
        Assert.True(e.CursorVisible);
        Assert.False(e.ApplicationKeypad);
        Assert.Equal(TerminalMouseTracking.None, e.MouseTracking);
    }

    [Fact]
    public void FullResetRestoresDefaultsButKeepsScrollback()
    {
        TerminalEmulator e = Fed("1\r\n2\r\n3\x1b[?1h\x1b[?25l\x1b[31m\u001bc", 5, 2);
        Assert.False(e.ApplicationCursorKeys);
        Assert.True(e.CursorVisible);
        Assert.Equal(TerminalStyle.Default, e.CurrentStyle);
        e.AssertScreen("", "");
        e.AssertCursor(0, 0);
        Assert.Equal(1, e.ScrollbackCount);
    }

    [Fact]
    public void SoftResetRestoresModesButNotTheScreen()
    {
        TerminalEmulator e = Fed("keep\x1b[?25l\x1b[4h\x1b[?1h\x1b[!p", 6, 2);
        Assert.True(e.CursorVisible);
        Assert.False(e.InsertMode);
        Assert.False(e.ApplicationCursorKeys);
        e.AssertScreen("keep", "");
    }

    // ── queries and titles ───────────────────────────────────────────────────

    [Theory]
    [InlineData("\x1b[5n", "\x1b[0n")]
    [InlineData("\x1b[3;4H\x1b[6n", "\x1b[3;4R")]
    [InlineData("\x1b[3;4H\x1b[?6n", "\x1b[?3;4R")]
    [InlineData("\x1b[c", "\x1b[?62;22c")]
    [InlineData("\x1b[0c", "\x1b[?62;22c")]
    [InlineData("\x1b[>c", "\x1b[>1;10;0c")]
    [InlineData("\x1b[18t", "\x1b[8;4;10t")]
    [InlineData("\x1b[?2004$p", "\x1b[?2004;2$y")]
    [InlineData("\x1b[?2004h\x1b[?2004$p", "\x1b[?2004;1$y")]
    [InlineData("\x1b[?9999$p", "\x1b[?9999;0$y")]
    [InlineData("\x1b]11;?\x07", "\x1b]11;rgb:0000/0000/0000\x1b\\")]
    public void QueriesAreAnswered(string query, string reply)
        => Assert.Equal(reply, Fed(query).TakeResponses());

    [Fact]
    public void TitlesAreSetSanitizedPushedAndPopped()
    {
        TerminalEmulator e = Fed("\x1b]0;first\x07");
        Assert.Equal("first", e.Title);
        Assert.Equal(TerminalChanges.Title, e.TakeChanges() & TerminalChanges.Title);

        e.Feed("\x1b[22t\x1b]2;sec\tond\x1b\\"u8.ToArray());
        Assert.Equal("second", e.Title);   // controls are removed

        e.Feed("\x1b[23t"u8.ToArray());
        Assert.Equal("first", e.Title);

        e.Feed("\x1b]1;icon only\x07"u8.ToArray());
        Assert.Equal("first", e.Title);   // OSC 1 is the icon name
    }

    // ── resize ───────────────────────────────────────────────────────────────

    [Fact]
    public void WideningKeepsContentAndNarrowingCutsIt()
    {
        TerminalEmulator e = Fed("abcdef\r\nxy", 6, 2);
        e.Resize(10, 2);
        e.AssertScreen("abcdef", "xy");

        e.Resize(3, 2);
        e.AssertScreen("abc", "xy");
        e.AssertCursor(1, 2);   // clamped into the narrower grid
    }

    [Fact]
    public void NarrowingThroughAWideCharacterDropsItsHalf()
    {
        TerminalEmulator e = Fed("ab中", 5, 1);
        e.Resize(3, 1);
        Assert.Equal("ab", e.GetRowText(0));
        Assert.True(e.GetCell(0, 2).IsEmpty);
    }

    [Fact]
    public void ShrinkingTheHeightDropsBlankRowsBelowTheCursorFirstThenScrollsTheTopIntoScrollback()
    {
        TerminalEmulator blankBelow = Fed("1\r\n2", 5, 5);
        blankBelow.Resize(5, 2);
        blankBelow.AssertScreen("1", "2");
        Assert.Equal(0, blankBelow.ScrollbackCount);

        TerminalEmulator full = Fed("1\r\n2\r\n3\r\n4\r\n5", 5, 5);
        full.Resize(5, 2);
        full.AssertScreen("4", "5");
        full.AssertCursor(1, 1);
        Assert.Equal(3, full.ScrollbackCount);
    }

    [Fact]
    public void GrowingTheHeightTakesRowsBackFromScrollback()
    {
        TerminalEmulator e = Fed("1\r\n2\r\n3\r\n4", 5, 2);
        Assert.Equal(2, e.ScrollbackCount);
        e.Resize(5, 4);
        e.AssertScreen("1", "2", "3", "4");
        e.AssertCursor(3, 1);
        Assert.Equal(0, e.ScrollbackCount);
    }

    [Fact]
    public void ResizeResetsTheRegionAndKeepsDefaultTabStops()
    {
        TerminalEmulator e = Fed("\x1b[2;3r", 10, 4);
        e.Resize(20, 6);
        e.Feed("\x1b[6;1H\n\tX"u8.ToArray());   // LF at the last row scrolls the whole screen
        Assert.Equal(1, e.ScrollbackCount);
        Assert.Equal("X", e.GetCell(5, 8).Text);
    }

    [Fact]
    public void DegenerateSizesAreClampedToOneCell()
    {
        TerminalEmulator e = EmulatorTestKit.New(0, -5);
        Assert.Equal((1, 1), (e.Columns, e.Rows));
        e.Feed("abc中\r\nd"u8.ToArray());
        e.Resize(-3, 0);
        Assert.Equal((1, 1), (e.Columns, e.Rows));
    }

    // ── scrollback ───────────────────────────────────────────────────────────

    [Fact]
    public void ScrollbackIsBounded()
    {
        TerminalEmulator e = EmulatorTestKit.New(8, 3, scrollback: 100);
        for (int i = 0; i < 5000; i++) e.Feed(Encoding.ASCII.GetBytes($"line{i}\r\n"));

        Assert.Equal(100, e.ScrollbackCount);
        Assert.Equal("line4898", TerminalText.RowText(e.GetBufferRow(0, out _)));   // oldest kept
        Assert.Equal(5000 - 2, e.ScrollbackTotalAdded);

        e.MaxScrollbackLines = 10;
        Assert.Equal(10, e.ScrollbackCount);
        Assert.Equal("line4988", TerminalText.RowText(e.GetBufferRow(0, out _)));
    }

    [Fact]
    public void ALongLineWithSplitEscapesAndUtf8IsNeitherAlteredNorBroken()
    {
        // Regression for the removed BoundLongLines: a very long line is parsed exactly as sent, however it is split.
        string line = new string('x', 20_000) + "\x1b[31mč" + new string('y', 9_000) + "\x1b[0m!";
        byte[] bytes = Encoding.UTF8.GetBytes(line);
        TerminalEmulator e = EmulatorTestKit.New(80, 5, scrollback: 1000);
        for (int at = 0; at < bytes.Length; at += 4093)   // an odd chunk size lands inside the escape and the 'č'
            e.Feed(bytes.AsSpan(at, Math.Min(4093, bytes.Length - at)));

        string text = string.Concat(Enumerable.Range(0, e.BufferRowCount).Select(r => TerminalText.RowText(e.GetBufferRow(r, out _))));
        Assert.Equal(new string('x', 20_000) + "č" + new string('y', 9_000) + "!", text);
        Assert.DoesNotContain("long terminal line", text, StringComparison.Ordinal);

        int cRow = 20_000 / 80;   // 'č' is the 20 001st character: the first column of its row
        ReadOnlySpan<TerminalCell> row = e.GetBufferRow(cRow, out bool wrapped);
        Assert.True(wrapped);
        Assert.Equal("č", row[0].Text);
        Assert.Equal(TerminalColor.FromIndex(1), row[0].Style.Foreground);
    }
}
