using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

/// <summary>TVDemo's embedded character-name database is exactly what the offline generator produces.</summary>
public sealed class CharacterNameDataTests
{
    private static readonly string Root = RepositoryLayout.Root(typeof(CharacterNameDataTests).Assembly);

    [Fact]
    public void EmbeddedDatabaseEqualsTheRegeneratedOne()
    {
        string generated = CharacterNameDataGenerator.GenerateText(Root);
        string target = CharacterNameDataGenerator.TargetFile(Root);
        if (Environment.GetEnvironmentVariable("TSV_REGENERATE_CHARACTER_NAMES") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, CharacterNameDataGenerator.Compress(generated));
        }
        Assert.True(CharacterNameDataGenerator.Decompress(File.ReadAllBytes(target)) == generated,
            "CharacterNames.txt.gz is stale; run this test with TSV_REGENERATE_CHARACTER_NAMES=1 and rebuild.");
        // The copy compiled into TVDemo is the same file.
        Assert.Equal(generated, UnicodeCharacterNames.ReadEmbeddedTextForTesting());
    }

    [Fact]
    public void EveryInputMatchesItsPinnedHash()
    {
        foreach (var input in CharacterNameDataGenerator.Inputs)
            Assert.NotEmpty(CharacterNameDataGenerator.ReadVerified(Root, input.File));
    }

    [Fact]
    public void LicenceNoticesAreInPlace()
    {
        string notices = File.ReadAllText(Path.Combine(Root, "resources", "licences", "THIRD-PARTY-NOTICES.md"));
        Assert.Contains("CLDR", notices);
        Assert.Contains("NameAliases.txt", notices);
        Assert.Contains("UNICODE LICENSE V3", File.ReadAllText(Path.Combine(Root, "tests", "TSharpVision.Tests", "Samples", "UnicodeNames", "LICENSE-CLDR.txt")));
        Assert.Contains("Unicode License v3", CharacterNameDataGenerator.GenerateText(Root).Split('\n')[0..8].Aggregate((a, b) => a + b));
    }
}
