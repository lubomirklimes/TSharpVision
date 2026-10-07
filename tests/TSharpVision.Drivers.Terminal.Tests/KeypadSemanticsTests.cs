using System.Text;
using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.Drivers.Terminal.Tests;

/// <summary>
/// KEYPAD-CLOSURE through the terminal driver. Kitty reports a keypad key with a code of its own, the modifiers with
/// the NumLock bit, and — when associated text is reported — the text the key typed. The decoder's output is pinned by
/// <see cref="KeypadConformanceTests"/>; these tests are about what it means once
/// <see cref="KeypadKeys.Normalize"/> has seen it. Plain ANSI has no keypad to normalize.
/// </summary>
public sealed class KeypadSemanticsTests
{
    private static List<TEvent> Dispatched(string input, bool kitty = true)
    {
        var decoder = new TerminalInputDecoder();
        if (kitty)
        {
            decoder.BeginKeyboardNegotiation();
            decoder.Feed("\u001b[?0u\u001b[?31u"u8, _ => { });
        }

        decoder.Feed(Encoding.UTF8.GetBytes(input), _ => { });
        var events = new List<TEvent>();
        while (decoder.TryRead(out TEvent ev))
        {
            KeypadKeys.Normalize(ref ev);
            events.Add(ev);
        }

        return events;
    }

    public static IEnumerable<object[]> NavigationCases()
    {
        // KP_LEFT … KP_DELETE and the report of the main key: CSI 1;m <letter> or CSI <number>;m ~.
        (int Keypad, string Main, ushort Identity)[] keys =
        {
            (57417, "D", Keys.kbKeypad4), (57418, "C", Keys.kbKeypad6), (57419, "A", Keys.kbKeypad8),
            (57420, "B", Keys.kbKeypad2), (57421, "5~", Keys.kbKeypad9), (57422, "6~", Keys.kbKeypad3),
            (57423, "H", Keys.kbKeypad7), (57424, "F", Keys.kbKeypad1), (57425, "2~", Keys.kbKeypad0),
            (57426, "3~", Keys.kbKeypadDecimal),
        };
        foreach (var key in keys)
            foreach (int modifiers in new[] { 1, 2, 5, 6, 3 })      // none, Shift, Ctrl, Ctrl+Shift, Alt
                yield return new object[] { key.Keypad, key.Main, key.Identity, modifiers };
    }

    private static string MainReport(string main, int modifiers, int type) =>
        main.EndsWith('~') ? $"\u001b[{main[..^1]};{modifiers}:{type}~" : $"\u001b[1;{modifiers}:{type}{main}";

    /// <summary>The keypad's navigation codes are the main cursor keys, with the same code and state under the same modifiers.</summary>
    [Theory, MemberData(nameof(NavigationCases))]
    public void ANavigationCodeIsWhatTheMainCursorKeyIs(int keypad, string main, ushort identity, int modifiers)
    {
        List<TEvent> expected = Dispatched(MainReport(main, modifiers, 1) + MainReport(main, modifiers, 3));
        List<TEvent> actual = Dispatched($"\u001b[{keypad};{modifiers}:1u\u001b[{keypad};{modifiers}:3u");

        Assert.Equal(new[] { Events.evKeyDown, Events.evKeyUp }, actual.Select(e => e.What));
        Assert.Equal(expected.Select(e => e.keyDown.keyCode), actual.Select(e => e.keyDown.keyCode));
        Assert.Equal(expected.Select(e => e.keyDown.controlKeyState), actual.Select(e => e.keyDown.controlKeyState));
        Assert.All(actual, e => Assert.Equal(identity, e.keyDown.keypadKey));
        Assert.All(actual, e => Assert.Equal(string.Empty, e.keyDown.text));
    }

    /// <summary>A numeric code with its associated text (NumLock bit 128 set): the text, once, with the character as the code.</summary>
    [Theory]
    [InlineData(57399, '0', Keys.kbKeypad0)]
    [InlineData(57400, '1', Keys.kbKeypad1)]
    [InlineData(57404, '5', Keys.kbKeypad5)]
    [InlineData(57408, '9', Keys.kbKeypad9)]
    [InlineData(57409, '.', Keys.kbKeypadDecimal)]
    [InlineData(57409, ',', Keys.kbKeypadDecimal)]
    [InlineData(57410, '/', Keys.kbKeypadDivide)]
    [InlineData(57411, '*', Keys.kbKeypadMultiply)]
    public void ANumericCodeWithAssociatedTextTypesThatText(int keypad, char character, ushort identity)
    {
        TEvent ev = Assert.Single(Dispatched($"\u001b[{keypad};129;{(int)character}u"));

        Assert.Equal(character, ev.keyDown.keyCode);
        Assert.Equal(character.ToString(), ev.keyDown.text);
        Assert.Equal(character.ToString(), KeyText.PrintableText(ev.keyDown));
        Assert.Equal(identity, ev.keyDown.keypadKey);
        Assert.Equal(Keys.kbNumState, ev.keyDown.controlKeyState);
    }

    [Theory]
    [InlineData(57412, '-', Keys.kbGrayMinus)]
    [InlineData(57413, '+', Keys.kbGrayPlus)]
    public void MinusAndPlusKeepTheirGrayCodesAndTypeTheirText(int keypad, char character, ushort identity)
    {
        TEvent ev = Assert.Single(Dispatched($"\u001b[{keypad};129;{(int)character}u"));

        Assert.Equal(identity, ev.keyDown.keyCode);
        Assert.Equal(character.ToString(), KeyText.PrintableText(ev.keyDown));
        Assert.Equal(identity, ev.keyDown.keypadKey);
    }

    /// <summary>
    /// Protocol limit. A terminal that does not report associated text sends the numeric code alone. The driver never
    /// makes text of a key identity, so the key types nothing; and with the NumLock bit set it is not navigation.
    /// </summary>
    [Theory]
    [InlineData(57403, Keys.kbKeypad4)]
    [InlineData(57409, Keys.kbKeypadDecimal)]
    [InlineData(57411, Keys.kbKeypadMultiply)]
    [InlineData(57413, Keys.kbGrayPlus)]
    public void ANumericCodeWithoutAssociatedTextTypesNothing(int keypad, ushort identity)
    {
        TEvent ev = Assert.Single(Dispatched($"\u001b[{keypad};129u"));

        Assert.Equal(identity, ev.keyDown.keyCode);
        Assert.Equal(identity, ev.keyDown.keypadKey);
        Assert.Equal(string.Empty, KeyText.PrintableText(ev.keyDown));
    }

    /// <summary>KP_BEGIN, the keypad 5 without NumLock, is no key — as CSI E is none in plain ANSI.</summary>
    [Fact]
    public void KeypadBeginIsNoKey()
    {
        TEvent ev = Assert.Single(Dispatched("\u001b[57427u"));

        Assert.Equal(Keys.kbKeypad5, ev.keyDown.keyCode);
        Assert.Equal(Keys.kbKeypad5, ev.keyDown.keypadKey);
    }

    /// <summary>
    /// Known limit, pinned so that a change is deliberate. With NumLock on, Shift makes the terminal send the
    /// navigation code with the NumLock bit still set. The decoder reports KP_LEFT and KP_4 as the same identity, so
    /// the event is treated like every other text-less key under NumLock: not navigation.
    /// </summary>
    [Fact]
    public void ANavigationCodeWhileNumLockIsOnIsNotNavigation()
    {
        TEvent ev = Assert.Single(Dispatched("\u001b[57417;130u"));     // Shift + NumLock

        Assert.Equal(Keys.kbKeypad4, ev.keyDown.keyCode);
        Assert.Equal(Keys.kbKeypad4, ev.keyDown.keypadKey);
    }

    [Fact]
    public void KeypadEnterIsStillEnterAndCtrlEnter()
    {
        List<TEvent> events = Dispatched("\u001b[57414u\u001b[57414;5u\u001b[57414;130u");

        Assert.Equal(new[] { Keys.kbEnter, Keys.kbCtrlEnter, Keys.kbEnter }, events.Select(e => e.keyDown.keyCode));
        Assert.All(events, e => Assert.Equal(Keys.kbKeypadEnter, e.keyDown.keypadKey));
    }

    /// <summary>Plain ANSI sends what the main keys send. There is no identity, nothing to normalize and nothing invented.</summary>
    [Theory]
    [InlineData("0123456789.,/*-+")]
    [InlineData("\r")]
    [InlineData("\u001b[A\u001b[B\u001b[C\u001b[D\u001b[H\u001b[F\u001b[2~\u001b[3~\u001b[5~\u001b[6~")]
    [InlineData("\u001bOA\u001bOB\u001bOC\u001bOD\u001bOH\u001bOF")]
    public void PlainAnsiIsLeftExactlyAsDecoded(string input)
    {
        var decoder = new TerminalInputDecoder();
        decoder.Feed(Encoding.ASCII.GetBytes(input), _ => { });
        while (decoder.TryRead(out TEvent reported))
        {
            TEvent ev = reported;
            Assert.False(KeypadKeys.Normalize(ref ev));
            Assert.Equal(reported.keyDown.keyCode, ev.keyDown.keyCode);
            Assert.Equal(reported.keyDown.charScan.ToUShort(), ev.keyDown.charScan.ToUShort());
            Assert.Equal(0, ev.keyDown.keypadKey);
        }
    }
}
