using TSharpVision.CodeEditor.Syntax;
using TSharpVision.CodeEditor.Syntax.TextMate;
using Xunit;

namespace TSharpVision.CodeEditor.Tests;

/// <summary>
/// <see cref="ISyntaxService.GetLanguages"/>: the languages a person can choose — every one usable, each listed once,
/// in a predictable order, and described without anything of the backend.
/// </summary>
public sealed class LanguageCatalogTests
{
    private static readonly TextMateSyntaxService Service = TextMateSyntaxService.Default;

    [Theory]
    [InlineData("json", "JSON")]
    [InlineData("jsonc", "JSON with Comments")]
    [InlineData("xml", "XML")]
    [InlineData("yaml", "YAML")]
    [InlineData("toml", "TOML")]
    [InlineData("ini", "Ini")]
    [InlineData("markdown", "Markdown")]
    [InlineData("sql", "SQL")]
    [InlineData("csharp", "C#")]
    [InlineData("cpp", "C++")]
    [InlineData("python", "Python")]
    [InlineData("shellscript", "Shell Script")]
    [InlineData("powershell", "PowerShell")]
    [InlineData("javascript", "JavaScript")]
    [InlineData("typescript", "TypeScript")]
    public void TheModernFormatsAndTheSourceLanguagesAreListed(string id, string displayName)
    {
        SyntaxLanguageInfo info = Assert.Single(Service.GetLanguages(), language => language.Language.Id == id);
        Assert.Equal(displayName, info.Language.DisplayName);
    }

    [Fact]
    public void EveryListedLanguageHasAClassifierAndAStableIdentity()
    {
        IReadOnlyList<SyntaxLanguageInfo> languages = Service.GetLanguages();
        Assert.True(languages.Count >= 50, $"only {languages.Count} languages");

        foreach (SyntaxLanguageInfo info in languages)
        {
            Assert.False(info.Language.IsPlainText);
            Assert.Equal(info.Language.Id, info.Language.Id.Trim().ToLowerInvariant());
            Assert.False(string.IsNullOrWhiteSpace(info.Language.DisplayName));
            ISyntaxClassifier? classifier = Service.CreateClassifier(info.Language);
            Assert.True(classifier is not null, $"{info.Language.Id} has no classifier");
            Assert.Equal(info.Language, classifier!.Language);
        }
    }

    [Fact]
    public void EachLanguageIsListedOnceUnderOneName()
    {
        IReadOnlyList<SyntaxLanguageInfo> languages = Service.GetLanguages();
        Assert.Equal(languages.Count, languages.Select(l => l.Language.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(languages.Count, languages.Select(l => l.Language.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void TheListIsSortedByDisplayNameAndIsTheSameEveryTime()
    {
        IReadOnlyList<SyntaxLanguageInfo> languages = Service.GetLanguages();
        string[] names = languages.Select(l => l.Language.DisplayName).ToArray();
        Assert.Equal(names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), names);
        Assert.Same(languages, Service.GetLanguages());
        Assert.Equal(names, new TextMateSyntaxService().GetLanguages().Select(l => l.Language.DisplayName));
    }

    [Fact]
    public void PlainTextIsNotALanguageOfTheList()
        => Assert.DoesNotContain(Service.GetLanguages(), language => language.Language.IsPlainText);

    /// <summary>Grammar variants are not offered twice; their names find the language they repeat.</summary>
    [Theory]
    [InlineData("dockercompose", "yaml", "Compose")]
    [InlineData("properties", "ini", "Properties")]
    [InlineData("typst-code", "typst", "typc")]
    public void AVariantIsFoldedIntoTheLanguageItRepeats(string variantId, string listedUnder, string alias)
    {
        IReadOnlyList<SyntaxLanguageInfo> languages = Service.GetLanguages();
        Assert.DoesNotContain(languages, language => language.Language.Id == variantId);

        SyntaxLanguageInfo owner = Assert.Single(languages, language => language.Language.Id == listedUnder);
        Assert.Contains(variantId, owner.Aliases, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(alias, owner.Aliases, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Folding a variant away from the list does not change what detection calls a file.</summary>
    [Fact]
    public void DetectionStillReportsAVariantByItsOwnIdentity()
        => Assert.Equal("properties", Service.DetectLanguage(".env", null).Id);

    [Theory]
    [InlineData("python", "py")]
    [InlineData("shellscript", "sh")]
    [InlineData("shellscript", "bash")]
    [InlineData("javascript", "js")]
    [InlineData("typescript", "ts")]
    [InlineData("powershell", "ps1")]
    public void CommonShortNamesAreAliases(string id, string alias)
        => Assert.Contains(alias, Service.GetLanguages().Single(l => l.Language.Id == id).Aliases, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void AliasesRepeatNeitherTheIdentifierNorTheDisplayName()
    {
        foreach (SyntaxLanguageInfo info in Service.GetLanguages())
        {
            Assert.DoesNotContain(info.Language.Id, info.Aliases, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(info.Language.DisplayName, info.Aliases, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(info.Aliases.Count, info.Aliases.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    [Theory]
    [InlineData("csharp", ".cs")]
    [InlineData("json", ".json")]
    [InlineData("yaml", ".yml")]
    [InlineData("toml", ".toml")]
    [InlineData("markdown", ".md")]
    [InlineData("ini", ".ini")]
    [InlineData("ini", ".properties")]
    public void ExtensionsDescribeTheLanguage(string id, string extension)
    {
        SyntaxLanguageInfo info = Service.GetLanguages().Single(l => l.Language.Id == id);
        Assert.Contains(extension, info.FileExtensions, StringComparer.OrdinalIgnoreCase);
        Assert.All(info.FileExtensions, e => Assert.DoesNotContain('*', e));
    }

    [Fact]
    public void ExactFileNamesAreDescribedToo()
    {
        Assert.Contains("Makefile", Service.GetLanguages().Single(l => l.Language.Id == "makefile").FileNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Cargo.lock", Service.GetLanguages().Single(l => l.Language.Id == "toml").FileNames, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The description names nothing of the backend: no scope names, no grammar paths.</summary>
    [Fact]
    public void NothingOfTheBackendIsExposed()
    {
        foreach (SyntaxLanguageInfo info in Service.GetLanguages())
        {
            foreach (string text in info.Aliases.Append(info.Language.Id).Append(info.Language.DisplayName))
            {
                Assert.False(text.StartsWith("source.", StringComparison.Ordinal) || text.StartsWith("text.", StringComparison.Ordinal), text);
                Assert.DoesNotContain("tmLanguage", text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>A service written before the member existed still compiles and offers nothing to choose.</summary>
    [Fact]
    public void AServiceThatDoesNotEnumerateOffersAnEmptyList()
    {
        ISyntaxService minimal = new MinimalService();
        Assert.Empty(minimal.GetLanguages());
    }

    private sealed class MinimalService : ISyntaxService
    {
        public SyntaxLanguage DetectLanguage(string? fileName, string? contentSample) => SyntaxLanguage.PlainText;

        public ISyntaxClassifier? CreateClassifier(SyntaxLanguage language) => null;
    }
}
