// Built-in translation inventory checks against the English key inventory
// (DefaultEnglishStringProvider). The sources are resources/localization/*.trc;
// TSharpVision.ResourceCompiler.Tests checks they compile cleanly (no duplicate or
// malformed entries) and that the embedded .tvr images are current.
using System.Text.RegularExpressions;
using Xunit;

namespace TSharpVision.Tests.Localization;

[Collection("NonParallel")] // .tvr reads use the global streamable registry.
public sealed class TranslationParityTests
{
    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (string language in TSharpVisionLocalization.BuiltInLanguages)
            data.Add(language);
        return data;
    }

    // Documented gaps: language -> English keys deliberately absent from that translation
    // (they resolve to English). Empty means the translation covers every English key.
    // Never add an invented translation to close a gap; record it here instead.
    private static readonly IReadOnlyDictionary<string, string[]> KnownMissingKeys =
        new Dictionary<string, string[]>();

    private static readonly Regex HotkeyMarker = new("~[^~]+~", RegexOptions.CultureInvariant);
    private static readonly Regex Placeholder = new(@"\{\d+(?:[,:][^}]*)?\}", RegexOptions.CultureInvariant);

    private static IReadOnlyDictionary<string, string> English => DefaultEnglishStringProvider.Entries;

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        var provider = TSharpVisionLocalization.LoadBuiltIn(language);
        Assert.True(provider != null, $"Built-in translation '{language}' is not embedded or not readable.");
        return provider!.Entries;
    }

    [Fact]
    public void Assembly_EmbedsExactlyTheEightTranslations()
    {
        var names = typeof(TSharpVisionIntl).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(TSharpVisionLocalization.ManifestResourcePrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "cs", "de", "en", "es", "fr", "it", "pl", "sk" }
                .Select(l => TSharpVisionLocalization.ManifestResourcePrefix + l + ".tvr"),
            names);
        Assert.Equal(new[] { "cs", "de", "en", "es", "fr", "it", "pl", "sk" }, TSharpVisionLocalization.BuiltInLanguages);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Translation_CoversEnglishKeyInventory_ExceptDocumentedGaps(string language)
    {
        var table = Load(language);
        string[] missing = English.Keys.Where(k => !table.ContainsKey(k)).Order(StringComparer.Ordinal).ToArray();
        string[] extra = table.Keys.Where(k => !English.ContainsKey(k)).Order(StringComparer.Ordinal).ToArray();
        string[] expectedMissing = KnownMissingKeys.TryGetValue(language, out var gaps)
            ? gaps.Order(StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();

        Assert.True(expectedMissing.SequenceEqual(missing),
            $"{language}: missing keys [{string.Join(", ", missing)}] differ from the documented gap list [{string.Join(", ", expectedMissing)}].");
        Assert.True(extra.Length == 0, $"{language}: keys unknown to English [{string.Join(", ", extra)}].");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Translation_PreservesHotkeyMarkersAndPlaceholders(string language)
    {
        var table = Load(language);
        var problems = new List<string>();
        foreach (var (key, english) in English)
        {
            if (!table.TryGetValue(key, out string? value))
                continue;

            if (string.IsNullOrWhiteSpace(value))
                problems.Add($"{key}: empty value");

            int expectedMarkers = HotkeyMarker.Matches(english).Count;
            int actualMarkers = HotkeyMarker.Matches(value).Count;
            if (expectedMarkers != actualMarkers || english.Count(c => c == '~') != value.Count(c => c == '~'))
                problems.Add($"{key}: hotkey markers '{english}' vs '{value}'");

            var expectedPlaceholders = Placeholder.Matches(english).Select(m => m.Value).Order(StringComparer.Ordinal);
            var actualPlaceholders = Placeholder.Matches(value).Select(m => m.Value).Order(StringComparer.Ordinal);
            if (!expectedPlaceholders.SequenceEqual(actualPlaceholders))
                problems.Add($"{key}: placeholders '{english}' vs '{value}'");

            if (english.StartsWith('\n') != value.StartsWith('\n'))
                problems.Add($"{key}: leading line break '{english}' vs '{value}'");
        }

        Assert.True(problems.Count == 0, $"{language}:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void EnglishTranslation_EqualsBuiltInEnglishProvider()
    {
        var table = Load("en");

        Assert.Equal(
            English.OrderBy(p => p.Key, StringComparer.Ordinal),
            table.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Translation_FormatsEveryPlaceholderString(string language)
    {
        var table = Load(language);
        foreach (var (key, value) in table)
        {
            if (Placeholder.IsMatch(value))
                _ = string.Format(System.Globalization.CultureInfo.InvariantCulture, value, "a", "b", "c");
        }
    }
}
