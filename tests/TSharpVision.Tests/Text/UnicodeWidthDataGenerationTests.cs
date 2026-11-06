using System.Globalization;
using System.Text;
using TSharpVision.Tests.Infrastructure;
using TSharpVision.Text;
using Xunit;

namespace TSharpVision.Tests.Text;

/// <summary>The checked-in width table is exactly what the generator produces from the hash-pinned Unicode files.</summary>
public sealed class UnicodeWidthDataGenerationTests
{
    private static readonly string Root = RepositoryLayout.Root(typeof(UnicodeWidthDataGenerationTests).Assembly);
    private static readonly string DataDirectory = UnicodeWidthTableGenerator.DataDirectory(Root);

    [Fact]
    public void CheckedInTableEqualsRegeneratedTable()
    {
        string generated = UnicodeWidthTableGenerator.GenerateSource(DataDirectory);
        string target = UnicodeWidthTableGenerator.TargetFile(Root);
        if (Environment.GetEnvironmentVariable("TSV_REGENERATE_UNICODE_WIDTH") == "1")
            File.WriteAllText(target, generated, new UTF8Encoding(false));

        string checkedIn = File.ReadAllText(target).Replace("\r\n", "\n");
        Assert.True(checkedIn == generated,
            "UnicodeWidthData.g.cs is stale; run this test with TSV_REGENERATE_UNICODE_WIDTH=1 and rebuild.");
    }

    [Fact]
    public void EveryInputMatchesItsPinnedHash()
    {
        foreach (var input in UnicodeWidthTableGenerator.Inputs)
            Assert.NotEmpty(UnicodeWidthTableGenerator.ReadVerified(DataDirectory, input.File));
    }

    [Fact]
    public void LicenceNoticeTravelsWithTheData()
    {
        string table = File.ReadAllText(UnicodeWidthTableGenerator.TargetFile(Root));
        Assert.Contains("Unicode License v3", table);
        Assert.Contains("UNICODE LICENSE V3", File.ReadAllText(Path.Combine(Root, "resources", "licences", "THIRD-PARTY-NOTICES.md")));
        Assert.True(File.Exists(Path.Combine(DataDirectory, "LICENSE-UNICODE.txt")));
    }

    [Fact]
    public void CompiledTableMatchesTheUnicodeFilesForEveryCodePoint()
    {
        byte[] expected = UnicodeWidthTableGenerator.BuildProperties(DataDirectory);
        int mismatches = 0;
        for (int cp = 0; cp <= 0x10FFFF; cp++)
            if (UnicodeWidthData.Get(cp) != expected[cp]) mismatches++;
        Assert.Equal(0, mismatches);
        Assert.Equal("16.0.0", UnicodeWidthData.UnicodeVersion);
        Assert.Equal("16.0.0", TextLayout.UnicodeVersion);
    }

    [Theory]
    [InlineData(0x0041, 0)]                                             // A: narrow
    [InlineData(0x6F22, UnicodeWidthData.EawWide)]                      // 漢
    [InlineData(0x00B1, UnicodeWidthData.EawAmbiguous)]                 // ±
    [InlineData(0x0301, UnicodeWidthData.EawAmbiguous | UnicodeWidthData.ZeroWidth)]
    [InlineData(0x00AD, UnicodeWidthData.EawAmbiguous)]                 // soft hyphen is not zero-width
    [InlineData(0x200B, UnicodeWidthData.ZeroWidth)]                    // ZWSP (Cf)
    [InlineData(0x2630, UnicodeWidthData.EawWide)]                      // trigram: wide since 16.0
    [InlineData(0x1160, UnicodeWidthData.HangulMedialFinal)]
    [InlineData(0x1F1E8, UnicodeWidthData.EmojiPresentation | UnicodeWidthData.RegionalIndicator)]
    public void SpotChecks(int codePoint, int mustHave)
    {
        Assert.Equal(mustHave, UnicodeWidthData.Get(codePoint) & (mustHave == 0 ? 0xFF : mustHave));
    }

    [Fact]
    public void EmojiPropertiesSpotChecks()
    {
        byte grinning = UnicodeWidthData.Get(0x1F600);
        Assert.NotEqual(0, grinning & UnicodeWidthData.EmojiPresentation);
        Assert.NotEqual(0, grinning & UnicodeWidthData.EawWide);
        byte heart = UnicodeWidthData.Get(0x2764);
        Assert.Equal(0, heart & UnicodeWidthData.EmojiPresentation);
        Assert.NotEqual(0, heart & UnicodeWidthData.EmojiVariationBase);
        Assert.NotEqual(0, heart & UnicodeWidthData.ExtendedPictographic);
        Assert.NotEqual(0, UnicodeWidthData.Get('#') & UnicodeWidthData.EmojiVariationBase);
        Assert.Equal(0, UnicodeWidthData.Get(0x110000));
    }

    [Fact]
    public void RuntimeUnicodeDataSentinel()
    {
        Assert.True(UnicodeWidthData.RuntimeMatchesPinnedVersion,
            $"The runtime {Environment.Version} reports Unicode data other than {UnicodeWidthData.UnicodeVersion} " +
            $"(U+0897 {CharUnicodeInfo.GetUnicodeCategory(0x0897)}, U+1FAE9 {CharUnicodeInfo.GetUnicodeCategory(0x1FAE9)}, " +
            $"U+1FAEA {CharUnicodeInfo.GetUnicodeCategory(0x1FAEA)}). Re-pin the width table to the runtime's version.");
    }
}
