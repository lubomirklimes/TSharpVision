using TSharpVision.Text;
using Xunit;
namespace TSharpVision.Tests.Text;

public sealed class KamenickySpecificationTests
{
    [Fact]
    public void AllBytesRoundTrip()
    {
        var encoding = LegacyTextEncodings.Kamenicky;
        byte[] bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        foreach (byte value in bytes)
        {
            Assert.True(encoding.TryEncodeChar(encoding.DecodeByte(value), out byte encoded)); Assert.Equal(value, encoded);
        }
        string decoded = encoding.Decode(bytes);
        Assert.Equal(bytes, encoding.Encode(decoded)); Assert.Equal(decoded, encoding.Decode(bytes));
    }
    [Theory]
    [InlineData(0x80, 'Č')] [InlineData(0x87, 'č')] [InlineData(0x9E, 'Ř')]
    [InlineData(0xA9, 'ř')] [InlineData(0xAA, 'ŕ')] [InlineData(0xAD, '§')]
    [InlineData(0x15, '\u0015')] [InlineData(0x7F, '\u007F')]
    [InlineData(0xE1, 'ß')] [InlineData(0xE6, 'µ')] [InlineData(0xFF, '\u00A0')]
    public void SpecificationSpotAssignments(int value, char expected)
        => Assert.Equal(expected, LegacyTextEncodings.Kamenicky.DecodeByte((byte)value));
    [Fact]
    public void TextControlsAndAsciiRemainIdentity()
    {
        for (int value = 0; value < 128; value++) Assert.Equal((char)value, LegacyTextEncodings.Kamenicky.DecodeByte((byte)value));
    }
    [Fact]
    public void DosCompatibilityOverrideDoesNotEncodeTheReferenceVariant()
    {
        Assert.False(LegacyTextEncodings.Kamenicky.TryEncodeChar('¡', out _));
        Assert.Equal(new byte[] { 0xAD }, LegacyTextEncodings.Kamenicky.Encode("§"));
    }
}
