using System.Text;
using Xunit;

namespace TSharpVision.Terminal.Tests.Emulation;

/// <summary>
/// U-1b: the byte stream is parsed statefully — any split, down to single bytes, gives the same screen — and malformed
/// input never throws, never grows state without bound and always recovers.
/// </summary>
public sealed class ParserStreamingTests
{
    [Fact]
    public void AnSgrSequenceInFiveBuffersEqualsOneBuffer()
    {
        TerminalEmulator one = EmulatorTestKit.New();
        one.Feed(Encoding.ASCII.GetBytes("\x1b[31mX"));

        TerminalEmulator five = EmulatorTestKit.New();
        foreach (string piece in new[] { "\x1b", "[", "3", "1", "mX" }) five.Feed(Encoding.ASCII.GetBytes(piece));

        Assert.Equal(one.Snapshot(), five.Snapshot());
        Assert.Equal(TerminalColor.FromIndex(1), five.GetCell(0, 0).Style.Foreground);
        Assert.Equal("X", five.GetCell(0, 0).Text);
    }

    [Theory]
    [InlineData("prompt$ ls\r\nčeský € Ω 𝄞 中文 😀 é\r\n")]                              // UTF-8 of every length
    [InlineData("\x1b[1;31mred\x1b[0m \x1b[38;5;196mindexed\x1b[48;2;10;20;30mtrue\x1b[m")]      // SGR forms
    [InlineData("\x1b]0;my title\u0007after\x1b]2;second\x1b\\done")]                              // OSC, BEL and ST
    [InlineData("\x1b[38:2::1:2:3mcolon\x1b[4:0mnounder\x1b[58:5:9mul")]                         // colon sub-parameters
    [InlineData("main\x1b[?1049hALT\x1b[2J\x1b[5;5Hx\x1b[?1049lback")]                           // alternate screen
    [InlineData("\x1b[3;5r\x1b[5;1H\n\n\nscroll\x1bM\x1b[r\x1b[2@\x1b[P\x1b[L\x1b[M")]            // regions, insert, delete
    [InlineData("\x1b(0lqqk\x1b(B\x1b)0\x0ex\x0f\u001b7\x1b[H\u001b8end")]                             // charsets, DECSC
    [InlineData("\x1bPq#0;2;0;0;0#0!10~-\x1b\\visible\x1b_apc\x1b\\\x1b^pm\x1b\\\x1bXsos\x1b\\ok")] // strings ignored
    public void AnySplitGivesTheSameState(string stream)
        => EmulatorTestKit.AssertChunkingIndependent(stream);

    [Fact]
    public void Utf8SplitAcrossReadsDecodesAsOneCharacter()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("€");   // E2 82 AC
        TerminalEmulator emulator = EmulatorTestKit.New();
        emulator.Feed(bytes.AsSpan(0, 1));
        emulator.Feed(bytes.AsSpan(1, 1));
        Assert.Equal(0, emulator.CursorColumn);          // nothing printed for a partial character
        emulator.Feed(bytes.AsSpan(2, 1));

        Assert.Equal("€", emulator.GetCell(0, 0).Text);
        Assert.Equal(1, emulator.CursorColumn);
    }

    [Fact]
    public void InvalidUtf8BecomesReplacementCharactersAndDecodingRecovers()
    {
        TerminalEmulator emulator = EmulatorTestKit.New(20, 2);
        emulator.Feed(new byte[] { (byte)'a', 0xC3, (byte)'b', 0xFF, 0xE2, 0x82, (byte)'c', 0xF0, 0x9F, 0x98, 0x80 });

        Assert.Equal("a�b��c😀", emulator.GetRowText(0));
    }

    [Fact]
    public void AnOscTitleSplitAtEveryByteStillSetsTheTitle()
    {
        TerminalEmulator emulator = EmulatorTestKit.AssertChunkingIndependent("\x1b]2;Build ✓\x1b\\$ ");
        Assert.Equal("Build ✓", emulator.Title);
        Assert.Equal("$", emulator.GetRowText(0));
    }

    [Fact]
    public void TrueColourSplitInsideItsParametersIsStillTrueColour()
    {
        TerminalEmulator emulator = EmulatorTestKit.New();
        foreach (string piece in new[] { "\x1b[38;2;", "12", "0;", "34;5", "6mZ" }) emulator.Feed(Encoding.ASCII.GetBytes(piece));

        Assert.Equal(TerminalColor.FromRgb(120, 34, 56), emulator.GetCell(0, 0).Style.Foreground);
    }

    [Fact]
    public void AnAlternateScreenSwitchSplitAcrossReadsSwitchesOnce()
    {
        TerminalEmulator emulator = EmulatorTestKit.New();
        emulator.Feed(Encoding.ASCII.GetBytes("keep"));
        foreach (string piece in new[] { "\x1b[?10", "49h" }) emulator.Feed(Encoding.ASCII.GetBytes(piece));
        Assert.True(emulator.IsAlternateScreenActive);
        Assert.Equal(string.Empty, emulator.GetRowText(0));

        foreach (string piece in new[] { "\x1b", "[?1049", "l" }) emulator.Feed(Encoding.ASCII.GetBytes(piece));
        Assert.False(emulator.IsAlternateScreenActive);
        Assert.Equal("keep", emulator.GetRowText(0));
    }

    // ── robustness ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("\x1b[")]                          // unterminated CSI
    [InlineData("\x1b[?")]
    [InlineData("\x1b[1;2;3;4;5;6;7;8;9;10;11;12;13;14;15;16;17;18;19;20;21;22;23;24;25;26;27;28;29;30;31;32;33;34;35m")]
    [InlineData("\x1b[99999999999999999999999H")]   // a parameter far beyond int
    [InlineData("\x1b[1$")]                          // intermediate, then the text below finishes it
    [InlineData("\x1b[1;2?3m")]                      // a private marker in the middle: ignored
    [InlineData("\x1b]0;unterminated")]            // an OSC that never ends before ESC
    [InlineData("\x1bP$qm")]                         // DCS request
    [InlineData("\x1b(\x1b)")]                       // charset designations cut short
    [InlineData("\x1b#")]
    [InlineData("\x1b[\x18")]                        // CAN aborts
    [InlineData("\x1b[31\x1a")]                      // SUB aborts
    [InlineData("\u0085\u009b31m")]                  // C1 controls as code points are not interpreted or printed
    public void MalformedSequencesRecoverAndTheNextTextPrints(string garbage)
    {
        TerminalEmulator emulator = EmulatorTestKit.New(40, 3);
        emulator.Feed(Encoding.UTF8.GetBytes(garbage));
        emulator.Feed(Encoding.ASCII.GetBytes("\x1b\\\x1b[0m\x1b[HOK"));

        Assert.StartsWith("OK", emulator.GetRowText(0), StringComparison.Ordinal);
        Assert.InRange(emulator.CursorRow, 0, emulator.Rows - 1);
        Assert.InRange(emulator.CursorColumn, 0, emulator.Columns - 1);
    }

    [Fact]
    public void AnOverlongOscIsBoundedAndDoesNotEndUpOnScreen()
    {
        TerminalEmulator emulator = EmulatorTestKit.New(20, 2);
        emulator.Feed(Encoding.ASCII.GetBytes("\x1b]2;" + new string('t', 1_000_000) + "\x07visible"));

        Assert.True(emulator.Title.Length <= 1024);
        Assert.Equal("visible", emulator.GetRowText(0));
    }

    [Fact]
    public void AnEndlessDcsPayloadIsDiscardedWithoutGrowing()
    {
        TerminalEmulator emulator = EmulatorTestKit.New(20, 2);
        byte[] payload = Encoding.ASCII.GetBytes(new string('z', 64 * 1024));
        emulator.Feed(Encoding.ASCII.GetBytes("\x1bP"));
        for (int i = 0; i < 64; i++) emulator.Feed(payload);   // 4 MiB of payload, never stored
        emulator.Feed(Encoding.ASCII.GetBytes("\x1b\\after"));

        Assert.Equal("after", emulator.GetRowText(0));
    }

    [Fact]
    public void HugeCountsAreClampedToTheGrid()
    {
        TerminalEmulator emulator = EmulatorTestKit.New(10, 4);
        emulator.Feed(Encoding.ASCII.GetBytes(
            "\x1b[65535A\x1b[65535B\x1b[65535C\x1b[65535D\x1b[65535;65535H\x1b[65535@\x1b[65535P\x1b[65535X" +
            "\x1b[65535L\x1b[65535M\x1b[65535S\x1b[65535T\x1b[65535I\x1b[65535Z\x1bX\x1b[65535b\x1b[0;0r"));

        emulator.AssertCursor(0, 0);   // DECSTBM with defaults homes the cursor
        Assert.Equal(10, emulator.Columns);
        Assert.Equal(4, emulator.Rows);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RandomBytesNeverThrowOrLeaveTheGrid(int seed)
    {
        var random = new Random(seed);
        TerminalEmulator emulator = EmulatorTestKit.New(17, 5, scrollback: 50);
        byte[] alphabet = Encoding.UTF8.GetBytes("\x1b[]();:?>!$#\"' 0123456789ABCDHJKLMPSTXZabcdfghlmnqrstu@`\x07\x08\t\n\r\x0e\x0f\x18\x1a\\PX^_čĂ€😀中");
        var chunk = new byte[512];
        for (int round = 0; round < 400; round++)
        {
            for (int i = 0; i < chunk.Length; i++)
                chunk[i] = random.Next(4) == 0 ? (byte)random.Next(256) : alphabet[random.Next(alphabet.Length)];
            emulator.Feed(chunk);
            if (round % 50 == 0) emulator.Resize(random.Next(1, 30), random.Next(1, 10));

            Assert.InRange(emulator.CursorRow, 0, emulator.Rows - 1);
            Assert.InRange(emulator.CursorColumn, 0, emulator.Columns - 1);
            Assert.InRange(emulator.ScrollbackCount, 0, 50);
            emulator.TakeResponses();
        }

        _ = emulator.Snapshot();   // every cell is readable
    }

    [Fact]
    public void RepliesToQueriesAreBoundedEvenWhenNobodyTakesThem()
    {
        TerminalEmulator emulator = EmulatorTestKit.New();
        byte[] query = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("\x1b[6n\x1b[c", 20_000)));
        emulator.Feed(query);

        Assert.True(emulator.TakeResponses()!.Length <= 64 * 1024);
    }
}
