using System.Diagnostics;
using TSharpVision.Samples.TVDemo;
using Xunit;
using Xunit.Abstractions;

namespace TSharpVision.Tests.Samples;

/// <summary>TV13t: names, aliases, CLDR annotations, synonyms and ranked search of the Character Map database.</summary>
public sealed class UnicodeCharacterNamesTests(ITestOutputHelper output)
{
    private static UnicodeCharacterNames Db => UnicodeCharacterNames.Shared;

    [Theory]
    [InlineData(0x0041, "LATIN CAPITAL LETTER A")]
    [InlineData(0x0301, "COMBINING ACUTE ACCENT")]
    [InlineData(0x2713, "CHECK MARK")]
    [InlineData(0x1F600, "GRINNING FACE")]
    [InlineData(0x1D11E, "MUSICAL SYMBOL G CLEF")]
    [InlineData(0x4E00, "CJK UNIFIED IDEOGRAPH-4E00")]
    [InlineData(0x20000, "CJK UNIFIED IDEOGRAPH-20000")]
    [InlineData(0x17000, "TANGUT IDEOGRAPH-17000")]
    [InlineData(0xF900, "CJK COMPATIBILITY IDEOGRAPH-F900")]
    [InlineData(0xAC00, "HANGUL SYLLABLE GA")]
    [InlineData(0xD7A3, "HANGUL SYLLABLE HIH")]
    [InlineData(0xAE00, "HANGUL SYLLABLE GEUL")]
    public void OfficialAndAlgorithmicNames(int code, string name) => Assert.Equal(name, Db.GetName(code));

    [Theory]
    [InlineData(0x0000, "<NULL>")]
    [InlineData(0x0378, "<unassigned>")]
    [InlineData(0xE000, "<private use>")]
    [InlineData(0xD800, "<surrogate>")]
    public void LabelsForCodePointsWithoutAName(int code, string label)
    {
        Assert.Null(Db.GetName(code));
        Assert.Equal(label, Db.Label(code));
    }

    [Fact]
    public void AliasesKeywordsAndBlocksAreSeparateFromNames()
    {
        Assert.Contains("BYTE ORDER MARK", Db.GetAliases(0xFEFF));
        Assert.Contains("LATIN CAPITAL LETTER GHA", Db.GetAliases(0x01A2));          // a correction alias
        Assert.Equal("grinning face", Db.GetCldrName(0x1F600));                    // CLDR short name, not a Unicode name
        Assert.Contains("tick", Db.GetKeywords(0x2705));                           // CLDR keyword
        Assert.DoesNotContain("TICK", Db.GetName(0x2713)!);                        // never claimed as an official name
        Assert.Equal("Basic Latin", Db.GetBlock(0x41));
        Assert.Equal("Emoticons", Db.GetBlock(0x1F600));
        Assert.Equal("CJK Unified Ideographs", Db.GetBlock(0x4E00));
        Assert.Null(Db.GetBlock(0x2FE0));                                          // between blocks
        Assert.Equal("16.0.0", UnicodeCharacterNames.UnicodeVersion);
        Assert.True(Db.ExplicitEntries > 39000);
    }

    [Fact]
    public void TickIsASynonymNotAName()
    {
        CharacterSearchResult result = Db.Search("tick");
        Assert.Equal([0x2713, 0x2714, 0x2611, 0x2705], result.Codes[..4]);
        Assert.Contains(0x2705, result.Codes);
        Assert.Equal([0x2713, 0x2714, 0x2611, 0x2705], UnicodeCharacterNames.Synonyms["TICK"]);
    }

    [Theory]
    [InlineData("check mark", 0x2713)]
    [InlineData("CHECK MARK", 0x2713)]
    [InlineData("combining acute", 0x0301)]
    [InlineData("grinning", 0x1F600)]
    [InlineData("U+1F600", 0x1F600)]
    [InlineData("0x1f600", 0x1F600)]
    [InlineData("1F600", 0x1F600)]
    [InlineData("hangul syllable ga", 0xAC00)]
    [InlineData("byte order mark", 0xFEFF)]
    public void FindsTheExpectedCharacter(string query, int code)
    {
        CharacterSearchResult result = Db.Search(query);
        output.WriteLine($"{query}: {result.Count} results, first {string.Join(" ", result.Codes.Take(5).Select(c => $"U+{c:X4}"))}");
        Assert.Contains(code, result.Codes);
        Assert.InRange(Array.IndexOf(result.Codes, code), 0, 9);
    }

    [Fact]
    public void SmilingFaceFindsNamesFirstThenCldrKeywords()
    {
        int[] codes = Db.Search("smiling face").Codes;
        // Names starting with the query (U+1F603 SMILING FACE WITH OPEN MOUTH ...) before names containing it
        // (U+263A WHITE SMILING FACE), and those before CLDR keyword hits (U+1F600 grinning face: smile, smiling).
        Assert.Equal(0x1F603, codes[0]);
        Assert.True(Array.IndexOf(codes, 0x263A) < Array.IndexOf(codes, 0x1F600));
        Assert.Contains(0x1F600, codes);
    }

    [Fact]
    public void ExactNamesRankFirstAndCodePointsBeforeNames()
    {
        Assert.Equal(0x2713, Db.Search("check mark").Codes[0]);
        Assert.Equal(0x1F600, Db.Search("U+1F600").Codes[0]);
        Assert.Equal(0x0041, Db.Search("latin capital letter a").Codes[0]);
    }

    [Theory]
    [InlineData("arrow", 500)]
    [InlineData("greek", 300)]
    [InlineData("CJK", 90000)]
    public void BroadQueriesReturnEveryMatchWithoutTruncation(string query, int atLeast)
    {
        CharacterSearchResult result = Db.Search(query);
        Assert.True(result.Count >= atLeast, $"{query}: {result.Count}");
        Assert.Equal(result.Count, result.Codes.Distinct().Count());
    }

    [Fact]
    public void EmptyAndUnmatchedQueriesReturnNothing()
    {
        Assert.Empty(Db.Search("").Codes);
        Assert.Empty(Db.Search("   ").Codes);
        Assert.Empty(Db.Search("zzqqxx nothing").Codes);
        Assert.DoesNotContain(0x0378, Db.Search("0378").Codes);    // unassigned: never a result
        Assert.DoesNotContain(0xD800, Db.Search("D800").Codes);    // surrogate
    }

    [Fact]
    public void ResultsAreDeterministic()
    {
        Assert.Equal(Db.Search("face").Codes, Db.Search("FACE").Codes);
        Assert.Equal(Db.Search("arrow left").Codes, Db.Search("arrow  left").Codes);
    }

    [Fact]
    public void SearchIsFastEnoughForADialogConfirm()
    {
        Db.Search("warm up");
        var sw = Stopwatch.StartNew();
        foreach (string q in new[] { "tick", "arrow", "greek", "smiling face", "combining acute", "CJK" }) Db.Search(q);
        output.WriteLine($"6 searches: {sw.ElapsedMilliseconds} ms");
        Assert.True(sw.ElapsedMilliseconds < 5000, $"{sw.ElapsedMilliseconds} ms");
    }
}
