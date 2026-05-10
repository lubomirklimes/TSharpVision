using System.Text;
using TSharpVision.Text;
using Xunit;

namespace TSharpVision.Tests.Text;

public sealed class KamenickyEncodingTests
{
    [Theory]
    [InlineData("kamenicky")]
    [InlineData("KAMENICKY")]
    [InlineData("keybcs2")]
    [InlineData("keybcs")]
    [InlineData("cp895")]
    [InlineData("895")]
    public void RegistryAliasesResolveToBuiltInKamenicky(string name)
    {
        Assert.True(LegacyTextEncodings.TryGet(name, out var encoding));
        Assert.Same(LegacyTextEncodings.Kamenicky, encoding);
    }

    [Theory]
    [InlineData(0x87, 'č')]
    [InlineData(0xA9, 'ř')]
    [InlineData(0x91, 'ž')]
    [InlineData(0x96, 'ů')]
    [InlineData(0x88, 'ě')]
    [InlineData(0xA0, 'á')]
    [InlineData(0x82, 'é')]
    [InlineData(0xA1, 'í')]
    [InlineData(0xA2, 'ó')]
    [InlineData(0xA3, 'ú')]
    [InlineData(0x98, 'ý')]
    public void LowercaseCzechMappingsAreByteExact(byte value, char ch)
        => AssertByteCharMapping(value, ch);

    [Theory]
    [InlineData(0x80, 'Č')]
    [InlineData(0x9E, 'Ř')]
    [InlineData(0x92, 'Ž')]
    [InlineData(0xA6, 'Ů')]
    [InlineData(0x89, 'Ě')]
    [InlineData(0x8F, 'Á')]
    [InlineData(0x90, 'É')]
    [InlineData(0x8B, 'Í')]
    [InlineData(0x95, 'Ó')]
    [InlineData(0x97, 'Ú')]
    [InlineData(0x9D, 'Ý')]
    public void UppercaseCzechMappingsAreByteExact(byte value, char ch)
        => AssertByteCharMapping(value, ch);

    [Theory]
    [InlineData(0x84, 'ä')]
    [InlineData(0x8E, 'Ä')]
    [InlineData(0x93, 'ô')]
    [InlineData(0xA7, 'Ô')]
    [InlineData(0x8C, 'ľ')]
    [InlineData(0x9C, 'Ľ')]
    [InlineData(0x8D, 'ĺ')]
    [InlineData(0x8A, 'Ĺ')]
    [InlineData(0xAA, 'ŕ')]
    [InlineData(0xAB, 'Ŕ')]
    public void SlovakAndCommonCentralEuropeanMappingsAreByteExact(byte value, char ch)
        => AssertByteCharMapping(value, ch);

    [Theory]
    [InlineData(0xB0, '░')]
    [InlineData(0xB1, '▒')]
    [InlineData(0xB2, '▓')]
    [InlineData(0xB3, '│')]
    [InlineData(0xC4, '─')]
    [InlineData(0xDA, '┌')]
    [InlineData(0xBF, '┐')]
    [InlineData(0xC0, '└')]
    [InlineData(0xD9, '┘')]
    [InlineData(0xC3, '├')]
    [InlineData(0xB4, '┤')]
    [InlineData(0xC2, '┬')]
    [InlineData(0xC1, '┴')]
    [InlineData(0xC5, '┼')]
    public void Cp437StyleBoxDrawingBytesArePreserved(byte value, char ch)
        => AssertByteCharMapping(value, ch);

    [Fact]
    public void CzechPhraseRoundTrips()
    {
        var encoding = LegacyTextEncodings.Kamenicky;
        string text = "Příliš žluťoučký kůň";
        byte[] expected =
        {
            0x50, 0xA9, 0xA1, 0x6C, 0x69, 0xA8, 0x20,
            0x91, 0x6C, 0x75, 0x9F, 0x6F, 0x75, 0x87, 0x6B, 0x98,
            0x20, 0x6B, 0x96, 0xA4,
        };

        byte[] encoded = encoding.Encode(text);

        Assert.Equal(expected, encoded);
        Assert.Equal(text, encoding.Decode(encoded));
    }

    [Theory]
    [InlineData("漢")]
    [InlineData("🙂")]
    public void UnsupportedUnicodeFailsInStrictEncode(string text)
    {
        var encoding = LegacyTextEncodings.Kamenicky;

        Assert.Throws<EncoderFallbackException>(() => encoding.Encode(text));
        Assert.False(encoding.TryEncodeChar(text[0], out _));
    }

    [Fact]
    public void PrintableAsciiIsIdentity()
    {
        var encoding = LegacyTextEncodings.Kamenicky;

        for (byte b = 0x20; b <= 0x7E; b++)
        {
            char ch = (char)b;
            Assert.Equal(ch, encoding.DecodeByte(b));
            Assert.True(encoding.TryEncodeChar(ch, out byte encoded));
            Assert.Equal(b, encoded);
        }
    }

    [Fact]
    public void KamenickyIsTableBasedAndHasNoRendererSideEffects()
    {
        Assert.IsType<SingleByteTextEncoding>(LegacyTextEncodings.Kamenicky);
        char before = TSharpVisionGlyphs.FrameHorizontal;

        Assert.Equal('č', LegacyTextEncodings.Kamenicky.DecodeByte(0x87));

        Assert.Equal('─', before);
        Assert.Equal('─', TSharpVisionGlyphs.FrameHorizontal);
    }

    private static void AssertByteCharMapping(byte value, char ch)
    {
        var encoding = LegacyTextEncodings.Kamenicky;

        Assert.Equal(ch, encoding.DecodeByte(value));
        Assert.True(encoding.TryEncodeChar(ch, out byte encoded));
        Assert.Equal(value, encoded);
        Assert.Equal(new byte[] { value }, encoding.Encode(ch.ToString()));
        Assert.Equal(ch.ToString(), encoding.Decode(new byte[] { value }));
    }
}
