using System.Text.Json;
using TSharpVision.Text;
using Xunit;
namespace TSharpVision.Tests.Text;

public sealed class KamenickySpecificationTests
{
    [Fact]
    public void AllBytesMatchSelectedSourceDataAndRoundTrip()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Provenance");
        var expected = new Dictionary<byte, char>();
        foreach (string line in File.ReadLines(Path.Combine(directory, "CP437.TXT")))
        {
            if (!line.StartsWith("0x")) continue;
            string[] columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            expected.Add(Convert.ToByte(columns[0][2..], 16), (char)Convert.ToUInt16(columns[1][2..], 16));
        }
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "kamenicky.json")));
        foreach (var row in manifest.RootElement.GetProperty("latinRows").EnumerateObject())
        {
            byte start = Convert.ToByte(row.Name, 16); string characters = row.Value.GetString()!;
            for (int offset = 0; offset < characters.Length; offset++) expected[(byte)(start + offset)] = characters[offset];
        }
        foreach (var item in manifest.RootElement.GetProperty("compatibilityOverrides").EnumerateObject())
        {
            byte value = Convert.ToByte(item.Name, 16);
            Assert.Equal((char)Convert.ToUInt16(item.Value.GetProperty("referenceCodePoint").GetString(), 16), expected[value]);
            expected[value] = (char)Convert.ToUInt16(item.Value.GetProperty("codePoint").GetString(), 16);
        }
        Assert.Equal(256, expected.Count);
        var encoding = LegacyTextEncodings.Kamenicky;
        byte[] bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        foreach (byte value in bytes)
        {
            Assert.Equal(expected[value], encoding.DecodeByte(value));
            Assert.True(encoding.TryEncodeChar(expected[value], out byte encoded)); Assert.Equal(value, encoded);
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
