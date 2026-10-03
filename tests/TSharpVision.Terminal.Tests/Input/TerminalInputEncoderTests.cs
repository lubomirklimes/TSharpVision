using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.Terminal.Tests.Input;

/// <summary>U-1b: key events and paste to the bytes an xterm sends.</summary>
public sealed class TerminalInputEncoderTests
{
    private const uint Shift = Keys.kbLeftShift, Ctrl = Keys.kbLeftCtrl, Alt = Keys.kbLeftAlt;

    private static KeyDownEvent Key(ushort code, uint modifiers = 0, string text = "", byte charCode = 0)
    {
        var key = new KeyDownEvent { keyCode = code, controlKeyState = modifiers, text = text };
        key.charScan.charCode = charCode;
        return key;
    }

    private static string? Encode(ushort code, uint modifiers = 0, string text = "", byte charCode = 0, bool application = false)
        => TerminalInputEncoder.EncodeKey(Key(code, modifiers, text, charCode), application);

    [Theory]
    [InlineData("a")]
    [InlineData("Z")]
    [InlineData("č")]
    [InlineData("ü")]
    [InlineData("€")]
    [InlineData("Ω")]
    [InlineData("𝄞")]
    [InlineData("中")]
    public void TextIsSentAsIs(string text) => Assert.Equal(text, Encode(0, text: text));

    [Fact]
    public void LegacyAsciiCharactersWithoutTextStillType()
        => Assert.Equal("q", Encode(0x1071, charCode: (byte)'q'));

    [Fact]
    public void AltGrTextIsNotTreatedAsCtrlOrAlt()
        => Assert.Equal("€", Encode(0, Ctrl | Alt, "€"));

    [Fact]
    public void CtrlAToZSendTheirControlCharacters()
    {
        ushort[] codes =
        {
            Keys.kbCtrlA, Keys.kbCtrlB, Keys.kbCtrlC, Keys.kbCtrlD, Keys.kbCtrlE, Keys.kbCtrlF, Keys.kbCtrlG, Keys.kbCtrlH,
            Keys.kbCtrlI, Keys.kbCtrlJ, Keys.kbCtrlK, Keys.kbCtrlL, Keys.kbCtrlM, Keys.kbCtrlN, Keys.kbCtrlO, Keys.kbCtrlP,
            Keys.kbCtrlQ, Keys.kbCtrlR, Keys.kbCtrlS, Keys.kbCtrlT, Keys.kbCtrlU, Keys.kbCtrlV, Keys.kbCtrlW, Keys.kbCtrlX,
            Keys.kbCtrlY, Keys.kbCtrlZ,
        };
        for (int i = 0; i < codes.Length; i++)
            Assert.Equal(((char)(i + 1)).ToString(), Encode(codes[i], Ctrl));
    }

    [Theory]
    [InlineData("a", "\x01")]
    [InlineData("z", "\x1a")]
    [InlineData("[", "\x1b")]
    [InlineData("\\", "\x1c")]
    [InlineData("]", "\x1d")]
    [InlineData(" ", "\0")]
    [InlineData("?", "\x7f")]
    public void CtrlWithTextSendsTheControlCharacter(string text, string expected)
        => Assert.Equal(expected, Encode(0, Ctrl, text));

    [Fact]
    public void AltPrefixesEscape()
    {
        Assert.Equal("\x1bx", Encode(0, Alt, "x"));
        Assert.Equal("\x1bč", Encode(0, Alt, "č"));
        Assert.Equal("\u001bf", Encode(Keys.kbAltF));
        Assert.Equal("\u001bF", Encode(Keys.kbAltF, Shift));
        Assert.Equal("\u001b1", Encode(Keys.kbAlt1));
        Assert.Equal("\x1b\x01", Encode(Keys.kbCtrlA, Ctrl | Alt));
        Assert.Equal("\x1b\r", Encode(Keys.kbEnter, Alt));
        Assert.Equal("\x1b\x7f", Encode(Keys.kbAltBack));
        Assert.Equal("\x1b ", Encode(Keys.kbAltSpace));
    }

    [Fact]
    public void CursorKeysFollowApplicationCursorMode()
    {
        Assert.Equal("\x1b[A", Encode(Keys.kbUp));
        Assert.Equal("\x1b[B", Encode(Keys.kbDown));
        Assert.Equal("\x1b[C", Encode(Keys.kbRight));
        Assert.Equal("\x1b[D", Encode(Keys.kbLeft));
        Assert.Equal("\x1b[H", Encode(Keys.kbHome));
        Assert.Equal("\x1b[F", Encode(Keys.kbEnd));

        Assert.Equal("\x1bOA", Encode(Keys.kbUp, application: true));
        Assert.Equal("\x1bOD", Encode(Keys.kbLeft, application: true));
        Assert.Equal("\x1bOH", Encode(Keys.kbHome, application: true));
    }

    [Fact]
    public void ModifiedCursorKeysCarryTheModifierParameter()
    {
        Assert.Equal("\x1b[1;5A", Encode(Keys.kbUp, Ctrl));
        Assert.Equal("\x1b[1;2B", Encode(Keys.kbDown, Shift));
        Assert.Equal("\x1b[1;3C", Encode(Keys.kbRight, Alt));
        Assert.Equal("\x1b[1;6D", Encode(Keys.kbLeft, Ctrl | Shift));
        Assert.Equal("\x1b[1;5C", Encode(Keys.kbCtrlRight));
        Assert.Equal("\x1b[1;5D", Encode(Keys.kbCtrlLeft));
        Assert.Equal("\x1b[1;5H", Encode(Keys.kbCtrlHome));
        Assert.Equal("\x1b[1;5F", Encode(Keys.kbCtrlEnd));
        Assert.Equal("\x1b[1;5A", Encode(Keys.kbUp, Ctrl, application: true));   // modified keys ignore DECCKM
    }

    [Fact]
    public void EditingAndPagingKeys()
    {
        Assert.Equal("\x1b[2~", Encode(Keys.kbIns));
        Assert.Equal("\x1b[3~", Encode(Keys.kbDel));
        Assert.Equal("\x1b[5~", Encode(Keys.kbPgUp));
        Assert.Equal("\x1b[6~", Encode(Keys.kbPgDn));
        Assert.Equal("\x1b[3;5~", Encode(Keys.kbCtrlDel));
        Assert.Equal("\x1b[2;2~", Encode(Keys.kbShiftIns));
        Assert.Equal("\x1b[5;5~", Encode(Keys.kbCtrlPgUp));
        Assert.Equal("\x1b[6;5~", Encode(Keys.kbCtrlPgDn));
        Assert.Equal("\x1b[5;2~", Encode(Keys.kbPgUp, Shift));
    }

    [Fact]
    public void FunctionKeysOneToTwelve()
    {
        ushort[] codes =
        {
            Keys.kbF1, Keys.kbF2, Keys.kbF3, Keys.kbF4, Keys.kbF5, Keys.kbF6, Keys.kbF7, Keys.kbF8, Keys.kbF9, Keys.kbF10,
            Keys.kbF11, Keys.kbF12,
        };
        string[] expected =
        {
            "\x1bOP", "\x1bOQ", "\x1bOR", "\x1bOS", "\x1b[15~", "\x1b[17~", "\x1b[18~", "\x1b[19~", "\x1b[20~", "\x1b[21~",
            "\x1b[23~", "\x1b[24~",
        };
        Assert.Equal(expected, codes.Select(code => Encode(code)).ToArray());
    }

    [Fact]
    public void ModifiedFunctionKeys()
    {
        Assert.Equal("\x1b[1;2P", Encode(Keys.kbShiftF1));
        Assert.Equal("\x1b[1;5S", Encode(Keys.kbCtrlF4));
        Assert.Equal("\x1b[15;3~", Encode(Keys.kbAltF5));
        Assert.Equal("\x1b[24;5~", Encode(Keys.kbCtrlF12));
        Assert.Equal("\x1b[23;2~", Encode(Keys.kbShiftF11));
        Assert.Equal("\x1b[21;2~", Encode(Keys.kbShiftF10));
    }

    [Fact]
    public void F11AndF12AreDistinctFromShiftF4AndShiftF5()
    {
        // Distinct key codes: the identity decides, whatever Shift state the driver reported with it.
        Assert.Equal("\x1b[23~", Encode(Keys.kbF11));
        Assert.Equal("\x1b[24~", Encode(Keys.kbF12));
        Assert.Equal("\x1b[1;2S", Encode(Keys.kbShiftF4, Shift));
        Assert.Equal("\x1b[1;2S", Encode(Keys.kbShiftF4));
        Assert.Equal("\x1b[15;2~", Encode(Keys.kbShiftF5, Shift));
        Assert.Equal("\x1b[15;2~", Encode(Keys.kbShiftF5));
        Assert.Equal("\x1b[23;2~", Encode(Keys.kbF11, Shift));
    }

    [Fact]
    public void BasicKeys()
    {
        Assert.Equal("\r", Encode(Keys.kbEnter));
        Assert.Equal("\x7f", Encode(Keys.kbBack));
        Assert.Equal("\x08", Encode(Keys.kbCtrlBack));
        Assert.Equal("\t", Encode(Keys.kbTab));
        Assert.Equal("\x1b[Z", Encode(Keys.kbShiftTab));
        Assert.Equal("\x1b", Encode(Keys.kbEsc));
    }

    [Fact]
    public void KeysWithoutATerminalMeaningAreLeftToTheHost()
    {
        Assert.Null(Encode(Keys.kbCtrlPrtSc));
        Assert.Null(Encode((ushort)(Keys.kbGrayMinus & 0xFF00)));   // a scan code with no character
    }

    // ── paste ────────────────────────────────────────────────────────────────

    [Fact]
    public void PasteTurnsLineBreaksIntoCarriageReturns()
        => Assert.Equal("one\rtwo\rthree č€", TerminalInputEncoder.EncodePaste("one\r\ntwo\nthree č€", bracketed: false));

    [Fact]
    public void BracketedPasteWrapsThePayload()
        => Assert.Equal("\x1b[200~a\rb\x1b[201~", TerminalInputEncoder.EncodePaste("a\nb", bracketed: true));

    [Fact]
    public void BracketedPasteCannotBeEndedEarlyByThePastedText()
        => Assert.Equal("\x1b[200~evilrm -rf\r\x1b[201~",
            TerminalInputEncoder.EncodePaste("evil\x1b[201~rm -rf\n", bracketed: true));
}
