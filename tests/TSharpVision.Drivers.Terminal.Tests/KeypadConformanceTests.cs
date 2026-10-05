using System.Text;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using Xunit;
namespace TSharpVision.Drivers.Terminal.Tests;

public sealed class KeypadConformanceTests
{
    public static IEnumerable<object[]> Cases()
    {
        (int Native, ushort Code)[] keys =
        [
            (57399, Keys.kbKeypad0),
            (57400, Keys.kbKeypad1),
            (57401, Keys.kbKeypad2),
            (57402, Keys.kbKeypad3),
            (57403, Keys.kbKeypad4),
            (57404, Keys.kbKeypad5),
            (57405, Keys.kbKeypad6),
            (57406, Keys.kbKeypad7),
            (57407, Keys.kbKeypad8),
            (57408, Keys.kbKeypad9),
            (57409, Keys.kbKeypadDecimal),
            (57410, Keys.kbKeypadDivide),
            (57411, Keys.kbKeypadMultiply),
            (57412, Keys.kbGrayMinus),
            (57413, Keys.kbGrayPlus),
            (57414, Keys.kbKeypadEnter),
            (57360, Keys.kbNumLock),
            (57417, Keys.kbKeypad4), (57418, Keys.kbKeypad6),
            (57419, Keys.kbKeypad8), (57420, Keys.kbKeypad2),
            (57421, Keys.kbKeypad9), (57422, Keys.kbKeypad3),
            (57423, Keys.kbKeypad7), (57424, Keys.kbKeypad1),
            (57425, Keys.kbKeypad0), (57426, Keys.kbKeypadDecimal),
            (57427, Keys.kbKeypad5)
        ];
        foreach (var key in keys)
            foreach (int mod in new[] { 1, 134 }) // NumLock + Ctrl + Shift.
                yield return new object[] { key.Native, key.Code, mod };
    }
    [Theory, MemberData(nameof(Cases))]
    public void NegotiatedKittyKeepsDedicatedIdentityAndIndependentText(int native, ushort code, int mod)
    {
        var decoder = new TerminalInputDecoder();
        decoder.BeginKeyboardNegotiation();
        decoder.Feed("\u001b[?0u\u001b[?31u"u8, _ => { });
        // An alternate base-layout key must not replace a dedicated keypad identity.
        decoder.Feed(Encoding.ASCII.GetBytes($"\u001b[{native}::49;{mod}:1;44u\u001b[{native};{mod}:2;46u\u001b[{native};{mod}:3u"), _ => { });
        for (int i = 0; i < 3; i++)
        {
            Assert.True(decoder.TryRead(out var ev));
            Assert.Equal(i == 2 ? Events.evKeyUp : Events.evKeyDown, ev.What);
            Assert.Equal(code, ev.keyDown.keyCode);
            Assert.Equal(new CharScanType(code).ToUShort(), ev.keyDown.charScan.ToUShort());
            Assert.Equal(mod == 1 ? 0u : Keys.kbNumState | Keys.kbCtrlShift | Keys.kbShift, ev.keyDown.controlKeyState);
            Assert.Equal(i == 0 ? "," : i == 1 ? "." : "", ev.keyDown.text);
        }
        Assert.False(decoder.TryRead(out _));
    }
    [Theory]
    [InlineData("0", (ushort)'0')] [InlineData("1", (ushort)'1')]
    [InlineData("2", (ushort)'2')] [InlineData("3", (ushort)'3')]
    [InlineData("4", (ushort)'4')] [InlineData("5", (ushort)'5')]
    [InlineData("6", (ushort)'6')] [InlineData("7", (ushort)'7')]
    [InlineData("8", (ushort)'8')] [InlineData("9", (ushort)'9')]
    [InlineData(".", (ushort)'.')] [InlineData(",", (ushort)',')]
    [InlineData("/", (ushort)'/')] [InlineData("*", (ushort)'*')]
    [InlineData("-", (ushort)'-')] [InlineData("+", (ushort)'+')]
    [InlineData("\r", Keys.kbEnter)]
    public void LegacyAnsiHasTruthfulAliases(string input, ushort expected)
    {
        var decoder = new TerminalInputDecoder();
        decoder.Feed(Encoding.ASCII.GetBytes(input), _ => { });
        Assert.True(decoder.TryRead(out var ev));
        Assert.Equal(Events.evKeyDown, ev.What);
        Assert.Equal(expected, ev.keyDown.keyCode);
        Assert.False(decoder.TryRead(out _));
    }

    private static TerminalInputDecoder Kitty(string flags)
    {
        var decoder = new TerminalInputDecoder();
        decoder.BeginKeyboardNegotiation();
        decoder.Feed(Encoding.ASCII.GetBytes($"\u001b[?0u\u001b[?{flags}u"), _ => { });
        return decoder;
    }

    private static List<TEvent> Feed(TerminalInputDecoder decoder, string input)
    {
        decoder.Feed(Encoding.UTF8.GetBytes(input), _ => { });
        var events = new List<TEvent>();
        while (decoder.TryRead(out var ev)) events.Add(ev);
        return events;
    }

    private static bool IsKeypadIdentity(ushort code)
        => code is >= Keys.kbKeypad0 and <= Keys.kbNumLock or Keys.kbGrayMinus or Keys.kbGrayPlus;

    [Fact]
    public void PlainAnsiDoesNotAdvertiseDistinctNumericKeypad()
    {
        Assert.False(new AnsiTerminalDriver().KeyboardCapabilities.HasFlag(KeyboardCapabilities.DistinctNumericKeypad));
        var decoder = new TerminalInputDecoder();
        Assert.False(decoder.KeyboardCapabilities.HasFlag(KeyboardCapabilities.DistinctNumericKeypad));
        // Negotiation that the terminal declines stays plain ANSI.
        decoder.BeginKeyboardNegotiation();
        decoder.Feed("\u001b[?1;2c"u8, _ => { });
        Assert.Equal(KeyboardCapabilities.None, decoder.KeyboardCapabilities);
    }

    // Everything a keypad can send over plain ANSI is indistinguishable from the main keyboard.
    [Theory]
    [InlineData("0123456789.,/*-+")]
    [InlineData("\r")]
    [InlineData("\u001b[A\u001b[B\u001b[C\u001b[D\u001b[H\u001b[F")]
    [InlineData("\u001b[2~\u001b[3~\u001b[5~\u001b[6~\u001b[1~\u001b[4~")]
    [InlineData("\u001bOA\u001bOB\u001bOC\u001bOD\u001bOH\u001bOF")]
    [InlineData("\u001b[E")]
    public void PlainAnsiNeverInventsKeypadIdentity(string input)
    {
        var decoder = new TerminalInputDecoder();
        Assert.All(Feed(decoder, input), ev =>
        {
            Assert.Equal(Events.evKeyDown, ev.What);
            Assert.False(IsKeypadIdentity(ev.keyDown.keyCode));
            Assert.Equal(0u, ev.keyDown.controlKeyState & Keys.kbNumState);
        });
        Assert.Equal(KeyboardCapabilities.None, decoder.KeyboardCapabilities);
    }

    [Theory]
    [InlineData("\u001b", Keys.kbEsc, true)]
    [InlineData("\r", Keys.kbEnter, false)]
    [InlineData("\t", Keys.kbTab, false)]
    [InlineData("\u001b[Z", Keys.kbShiftTab, false)]
    [InlineData("\u007f", Keys.kbBack, false)]
    [InlineData("\u001bOP", Keys.kbF1, false)]
    [InlineData("\u001bOQ", Keys.kbF2, false)]
    [InlineData("\u001bOR", Keys.kbF3, false)]
    [InlineData("\u001bOS", Keys.kbF4, false)]
    [InlineData("\u001b[11~", Keys.kbF1, false)]
    [InlineData("\u001b[14~", Keys.kbF4, false)]
    [InlineData("\u001b[[A", Keys.kbF1, false)]
    [InlineData("\u001b[[B", Keys.kbF2, false)]
    [InlineData("\u001b[[C", Keys.kbF3, false)]
    [InlineData("\u001b[[D", Keys.kbF4, false)]
    [InlineData("\u001b[[E", Keys.kbF5, false)]
    [InlineData("\u001b[15~", Keys.kbF5, false)]
    [InlineData("\u001b[17~", Keys.kbF6, false)]
    [InlineData("\u001b[18~", Keys.kbF7, false)]
    [InlineData("\u001b[19~", Keys.kbF8, false)]
    [InlineData("\u001b[20~", Keys.kbF9, false)]
    [InlineData("\u001b[21~", Keys.kbF10, false)]
    [InlineData("\u001b[23~", Keys.kbF11, false)]
    [InlineData("\u001b[24~", Keys.kbF12, false)]
    [InlineData("\u001b[21;2~", Keys.kbShiftF10, false)]
    [InlineData("\u001b[23;5~", Keys.kbCtrlF11, false)]
    [InlineData("\u001b[24;3~", Keys.kbAltF12, false)]
    [InlineData("\u001b[2~", Keys.kbIns, false)]
    [InlineData("\u001b[H", Keys.kbHome, false)]
    [InlineData("\u001b[5~", Keys.kbPgUp, false)]
    [InlineData("\u001b[3~", Keys.kbDel, false)]
    [InlineData("\u001b[F", Keys.kbEnd, false)]
    [InlineData("\u001b[6~", Keys.kbPgDn, false)]
    [InlineData("\u001b[A", Keys.kbUp, false)]
    [InlineData("\u001b[D", Keys.kbLeft, false)]
    [InlineData("\u001b[B", Keys.kbDown, false)]
    [InlineData("\u001b[C", Keys.kbRight, false)]
    [InlineData("\u001b[1;5D", Keys.kbCtrlLeft, false)]
    [InlineData("1", (ushort)'1', false)]
    [InlineData("-", (ushort)'-', false)]
    [InlineData("=", (ushort)'=', false)]
    [InlineData("[", (ushort)'[', false)]
    [InlineData("]", (ushort)']', false)]
    [InlineData("\\", (ushort)'\\', false)]
    [InlineData(";", (ushort)';', false)]
    [InlineData("'", (ushort)'\'', false)]
    [InlineData(",", (ushort)',', false)]
    [InlineData(".", (ushort)'.', false)]
    [InlineData("/", (ushort)'/', false)]
    [InlineData("`", (ushort)'`', false)]
    [InlineData(" ", (ushort)' ', false)]
    [InlineData("\u0001", Keys.kbCtrlA, false)]
    [InlineData("\u001bx", Keys.kbAltX, false)]
    public void PlainAnsiStandardKeys(string input, ushort code, bool needsFollowingByte)
    {
        // A lone ESC is ambiguous until another byte arrives; a second ESC resolves it.
        var events = Feed(new TerminalInputDecoder(), needsFollowingByte ? input + "\u001b\u001b" : input);
        Assert.Equal(Events.evKeyDown, events[0].What);
        Assert.Equal(code, events[0].keyDown.keyCode);
        if (!needsFollowingByte) Assert.Single(events);
    }

    [Theory]
    [InlineData("\u001b[[", 0)]
    [InlineData("\u001b[[A\u001b[[", 1)]
    public void TruncatedLinuxConsoleFunctionKeyWaitsForItsFinalByte(string input, int expected)
        => Assert.Equal(expected, Feed(new TerminalInputDecoder(), input).Count);

    [Theory]
    [InlineData("ě")]
    [InlineData("š")]
    [InlineData("č")]
    [InlineData("ř")]
    [InlineData("😀")]
    public void UnicodeTextNeverFabricatesAKeyIdentity(string text)
    {
        var ev = Assert.Single(Feed(new TerminalInputDecoder(), text));
        Assert.Equal(0, ev.keyDown.keyCode);
        Assert.Equal(text, ev.keyDown.text);
        ev = Assert.Single(Feed(Kitty("31"), $"\u001b[{char.ConvertToUtf32(text, 0)};1:1;{char.ConvertToUtf32(text, 0)}u"));
        Assert.Equal(0, ev.keyDown.keyCode);
        Assert.Equal(text, ev.keyDown.text);
    }

    // Text-producing keypad keys get dedicated codes only with "report all keys as escape codes".
    [Theory]
    [InlineData("31", true)]
    [InlineData("9", true)]
    [InlineData("11", true)]
    [InlineData("1", false)]
    [InlineData("3", false)]
    [InlineData("8", true)]
    [InlineData("0", false)]
    public void KittyAdvertisesDistinctKeypadOnlyWhenAllKeysAreEscapeCodes(string flags, bool expected)
        => Assert.Equal(expected, Kitty(flags).KeyboardCapabilities.HasFlag(KeyboardCapabilities.DistinctNumericKeypad));

    [Fact]
    public void KittyCapabilityAppearsWithNegotiationAndEndsWithTheMode()
    {
        var driver = new AnsiTerminalDriver();
        driver.BeginKeyboardNegotiationForTesting();
        Assert.False(driver.KeyboardCapabilities.HasFlag(KeyboardCapabilities.DistinctNumericKeypad));
        driver.FeedInputForTesting("\u001b[?0u"u8, _ => { });
        Assert.False(driver.KeyboardCapabilities.HasFlag(KeyboardCapabilities.DistinctNumericKeypad));
        driver.FeedInputForTesting("\u001b[?31u"u8, _ => { });
        Assert.True(driver.KeyboardCapabilities.HasFlag(KeyboardCapabilities.DistinctNumericKeypad));

        var decoder = Kitty("31");
        decoder.EndKeyboardMode();
        Assert.Equal(KeyboardCapabilities.None, decoder.KeyboardCapabilities);
    }

    [Fact]
    public void KittyMainKeysAndKeypadKeysAreDistinct()
    {
        var events = Feed(Kitty("31"), "\u001b[49;129:1;49u\u001b[57400;129:1;49u\u001b[13u\u001b[57414u");
        Assert.Equal(new ushort[] { '1', Keys.kbKeypad1, Keys.kbEnter, Keys.kbKeypadEnter }, events.Select(e => e.keyDown.keyCode));
        Assert.Equal(new[] { "1", "1", "", "" }, events.Select(e => e.keyDown.text));
        Assert.All(events.Take(2), e => Assert.Equal(Keys.kbNumState, e.keyDown.controlKeyState));
    }

    [Theory]
    [InlineData("\u001b[27u", Keys.kbEsc)]
    [InlineData("\u001b[13u", Keys.kbEnter)]
    [InlineData("\u001b[9u", Keys.kbTab)]
    [InlineData("\u001b[9;2u", Keys.kbShiftTab)]
    [InlineData("\u001b[127u", Keys.kbBack)]
    [InlineData("\u001b[1;1:1P", Keys.kbF1)]
    [InlineData("\u001b[1;1:1Q", Keys.kbF2)]
    [InlineData("\u001b[13;1:1~", Keys.kbF3)]
    [InlineData("\u001b[1;1:1S", Keys.kbF4)]
    [InlineData("\u001b[15;1:1~", Keys.kbF5)]
    [InlineData("\u001b[17;1:1~", Keys.kbF6)]
    [InlineData("\u001b[18;1:1~", Keys.kbF7)]
    [InlineData("\u001b[19;1:1~", Keys.kbF8)]
    [InlineData("\u001b[20;1:1~", Keys.kbF9)]
    [InlineData("\u001b[21;1:1~", Keys.kbF10)]
    [InlineData("\u001b[23;1:1~", Keys.kbF11)]
    [InlineData("\u001b[24;1:1~", Keys.kbF12)]
    [InlineData("\u001b[21;2:1~", Keys.kbShiftF10)]
    [InlineData("\u001b[2;1:1~", Keys.kbIns)]
    [InlineData("\u001b[1;1:1H", Keys.kbHome)]
    [InlineData("\u001b[5;1:1~", Keys.kbPgUp)]
    [InlineData("\u001b[3;1:1~", Keys.kbDel)]
    [InlineData("\u001b[1;1:1F", Keys.kbEnd)]
    [InlineData("\u001b[6;1:1~", Keys.kbPgDn)]
    [InlineData("\u001b[1;1:1A", Keys.kbUp)]
    [InlineData("\u001b[1;1:1D", Keys.kbLeft)]
    [InlineData("\u001b[1;1:1B", Keys.kbDown)]
    [InlineData("\u001b[1;1:1C", Keys.kbRight)]
    [InlineData("\u001b[1;5:1D", Keys.kbCtrlLeft)]
    [InlineData("\u001b[97;5u", Keys.kbCtrlA)]
    [InlineData("\u001b[120;3u", Keys.kbAltX)]
    public void KittyStandardKeysPressRepeatAndReleaseKeepOneIdentity(string press, ushort code)
    {
        // Derive the repeat and release reports from the press report.
        char final = press[^1];
        string body = press[2..^1];
        string[] fields = body.Split(';');
        string modifier = fields.Length > 1 ? fields[1].Split(':')[0] : "1";
        string Report(int type) => $"\u001b[{fields[0]};{modifier}:{type}{final}";
        var events = Feed(Kitty("31"), press + Report(2) + Report(3));
        Assert.Equal(new[] { Events.evKeyDown, Events.evKeyDown, Events.evKeyUp }, events.Select(e => e.What));
        Assert.All(events, e =>
        {
            Assert.Equal(code, e.keyDown.keyCode);
            Assert.False(IsKeypadIdentity(e.keyDown.keyCode));
        });
    }
}
