using System.Text;
using TSharpVision.Text;
using Xunit;

namespace TSharpVision.Tests.Text;

public sealed class LegacyTextEncodingTests
{
    [Theory]
    [InlineData("latin1")]
    [InlineData("LATIN-1")]
    [InlineData("iso-8859-1")]
    [InlineData("28591")]
    [InlineData("cp437")]
    [InlineData("437")]
    [InlineData("ibm437")]
    [InlineData("cp852")]
    [InlineData("852")]
    [InlineData("dos-latin2")]
    [InlineData("dos-latin-2")]
    [InlineData("windows-1250")]
    [InlineData("win1250")]
    [InlineData("cp1250")]
    [InlineData("1250")]
    [InlineData("iso-8859-2")]
    [InlineData("latin2")]
    [InlineData("latin-2")]
    [InlineData("28592")]
    public void BuiltInLookup_SupportsAliasesAndCaseInsensitivity(string name)
    {
        Assert.True(LegacyTextEncodings.TryGet(name, out var encoding));
        Assert.NotNull(encoding);
    }

    [Fact]
    public void Latin1_RoundTripsRepresentableCharsAndFailsStrictly()
    {
        var encoding = LegacyTextEncodings.Latin1;

        byte[] bytes = encoding.Encode("Cafe é");
        Assert.Equal("Cafe é", encoding.Decode(bytes));
        Assert.True(encoding.TryEncodeChar('é', out byte value));
        Assert.Equal('é', encoding.DecodeByte(value));

        Assert.False(encoding.TryEncodeChar('č', out _));
        Assert.Throws<EncoderFallbackException>(() => encoding.Encode("č"));
        Assert.Throws<EncoderFallbackException>(() => encoding.Encode("漢"));
    }

    [Fact]
    public void Cp852_RoundTripsCzechRepresentativeChars()
    {
        var encoding = LegacyTextEncodings.Cp852;
        string text = "áéíóú čřžůě ÁÉÍÓÚ ČŘŽŮĚ";

        byte[] bytes = encoding.Encode(text);

        Assert.Equal(text, encoding.Decode(bytes));
        foreach (char ch in text.Where(c => c != ' '))
            Assert.True(encoding.TryEncodeChar(ch, out _), $"Expected CP852 to encode '{ch}'.");
    }

    [Fact]
    public void Cp437_DecodesBoxDrawingBytes()
    {
        var encoding = LegacyTextEncodings.Cp437;

        Assert.Equal('─', encoding.DecodeByte(0xC4));
        Assert.Equal('│', encoding.DecodeByte(0xB3));
        Assert.Equal('┌', encoding.DecodeByte(0xDA));
        Assert.Equal('┐', encoding.DecodeByte(0xBF));
        Assert.Equal('└', encoding.DecodeByte(0xC0));
        Assert.Equal('┘', encoding.DecodeByte(0xD9));
        Assert.Equal("─│┌┐└┘", encoding.Decode(new byte[] { 0xC4, 0xB3, 0xDA, 0xBF, 0xC0, 0xD9 }));
    }

    [Fact]
    public void Windows1250_RoundTripsCzechRepresentativeChars()
    {
        var encoding = LegacyTextEncodings.Windows1250;
        string text = "áéíóú čřžůě ÁÉÍÓÚ ČŘŽŮĚ";

        byte[] bytes = encoding.Encode(text);

        Assert.Equal(text, encoding.Decode(bytes));
    }

    [Fact]
    public void Iso8859_2_RoundTripsCzechRepresentativeChars()
    {
        var encoding = LegacyTextEncodings.Iso8859_2;
        string text = "áéíóú čřžůě ÁÉÍÓÚ ČŘŽŮĚ";

        byte[] bytes = encoding.Encode(text);

        Assert.Equal(text, encoding.Decode(bytes));
    }

    [Fact]
    public void SingleByteTextEncoding_ValidatesTableLength()
    {
        Assert.Throws<ArgumentException>(() =>
            new SingleByteTextEncoding("short", new char[255]));
        Assert.Throws<ArgumentException>(() =>
            new SingleByteTextEncoding("long", new char[257]));
    }

    [Fact]
    public void SingleByteTextEncoding_DecodesEncodesAndFailsStrictly()
    {
        char[] table = IdentityTable();
        table[0x80] = 'č';

        var encoding = new SingleByteTextEncoding("test-table", table);

        Assert.Equal('č', encoding.DecodeByte(0x80));
        Assert.Equal("Ač", encoding.Decode(new byte[] { 0x41, 0x80 }));
        Assert.True(encoding.TryEncodeChar('č', out byte value));
        Assert.Equal(0x80, value);
        Assert.Equal(new byte[] { 0x41, 0x80 }, encoding.Encode("Ač"));

        Assert.False(encoding.TryEncodeChar('漢', out _));
        Assert.Throws<EncoderFallbackException>(() => encoding.Encode("漢"));
    }

    [Fact]
    public void CustomRegistration_LookupIsCaseInsensitiveAndRejectsDuplicates()
    {
        string name = "custom-" + Guid.NewGuid().ToString("N");
        var encoding = new SingleByteTextEncoding(name, IdentityTable());

        LegacyTextEncodings.Register(name, encoding);

        Assert.True(LegacyTextEncodings.TryGet(name.ToUpperInvariant(), out var found));
        Assert.Same(encoding, found);
        Assert.Throws<ArgumentException>(() => LegacyTextEncodings.Register(name, encoding));
        Assert.False(LegacyTextEncodings.TryRegister(name, encoding));
    }

    [Fact]
    public void CustomRegistration_RejectsInvalidInputs()
    {
        var encoding = new SingleByteTextEncoding("valid", IdentityTable());

        Assert.Throws<ArgumentException>(() => LegacyTextEncodings.Register("", encoding));
        Assert.Throws<ArgumentException>(() => LegacyTextEncodings.Register("   ", encoding));
        Assert.Throws<ArgumentNullException>(() => LegacyTextEncodings.Register("null-encoding", null));
        Assert.Throws<ArgumentException>(() => LegacyTextEncodings.TryRegister("", encoding));
        Assert.Throws<ArgumentNullException>(() => LegacyTextEncodings.TryRegister("null-encoding-2", null));
    }

    [Fact]
    public void LegacyEncodingLookup_HasNoRendererSideEffects()
    {
        char before = TSharpVisionGlyphs.FrameHorizontal;

        Assert.True(LegacyTextEncodings.TryGet("cp437", out _));
        LegacyTextEncodings.TryRegister("side-effect-" + Guid.NewGuid().ToString("N"),
            new SingleByteTextEncoding("side-effect", IdentityTable()));

        Assert.Equal('─', before);
        Assert.Equal('─', TSharpVisionGlyphs.FrameHorizontal);
    }

    private static char[] IdentityTable()
    {
        var table = new char[256];
        for (int i = 0; i < table.Length; i++)
            table[i] = (char)i;
        return table;
    }
}
