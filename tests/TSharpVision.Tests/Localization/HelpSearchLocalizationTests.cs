using System.IO;
using TSharpVision.Constants;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using TSharpVision.Tests.Samples;
using Xunit;

namespace TSharpVision.Tests.Localization;

// TV12m: the Help Search strings (TV12l) are part of the built-in inventory and translated in all eight embedded
// languages; the help viewer's search dialogs show them at run time. General parity, hotkeys and placeholders are also
// covered for every key by TranslationParityTests.
[Collection("NonParallel")]
public sealed class HelpSearchLocalizationTests : IDisposable
{
    private static readonly string[] Keys_ =
    {
        "Help_Search_Title", "Help_Search_Words", "Help_Search_NoResults",
        "Help_Search_ResultsTitle", "Help_Search_Topics", "Help_Search_Unavailable",
    };

    private readonly ConfiguredLanguageScope _language = new();
    private readonly StreamableRegistryScope _streams = new();
    private readonly TempDirectory _tmp = new();

    public void Dispose()
    {
        _tmp.Dispose();
        _streams.Dispose();
        _language.Dispose();
    }

    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (string language in TSharpVisionLocalization.BuiltInLanguages) data.Add(language);
        return data;
    }

    [Fact]
    public void EnglishInventoryHasTheSixKeys()
    {
        var english = DefaultEnglishStringProvider.Entries;
        Assert.All(Keys_, k => Assert.False(string.IsNullOrWhiteSpace(english[k]), k));
        Assert.Equal("~W~ords", english["Help_Search_Words"]);
        Assert.Equal("No help topics contain '{0}'.", english["Help_Search_NoResults"]);
        Assert.Equal("~T~opics ({0})", english["Help_Search_Topics"]);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryEmbeddedLanguageTranslatesTheKeysWithMarkersAndPlaceholders(string language)
    {
        var table = TSharpVisionLocalization.LoadBuiltIn(language)!.Entries;
        var english = DefaultEnglishStringProvider.Entries;
        foreach (string key in Keys_)
        {
            Assert.True(table.TryGetValue(key, out string? value), $"{language}: {key} missing");
            Assert.False(string.IsNullOrWhiteSpace(value), $"{language}: {key} empty");
            if (language != "en") Assert.NotEqual(english[key], value);
            Assert.Equal(english[key].Count(c => c == '~'), value!.Count(c => c == '~'));
            Assert.Equal(english[key].Contains("{0}"), value.Contains("{0}"));
            Assert.DoesNotContain("{1}", value);
        }
        // The word prompt's hotkey must not collide with its OK/Cancel buttons, nor the list label's with Open/Cancel.
        static char Hot(string s) => char.ToUpperInvariant(s[s.IndexOf('~') + 1]);
        Assert.NotEqual(Hot(table["Btn_OK"]), Hot(table["Help_Search_Words"]));
        Assert.NotEqual(Hot(table["Btn_Cancel"]), Hot(table["Help_Search_Words"]));
        Assert.NotEqual(Hot(table["File_Btn_Open"]), Hot(table["Help_Search_Topics"]));
        Assert.NotEqual(Hot(table["Btn_Cancel"]), Hot(table["Help_Search_Topics"]));
    }

    [Fact]
    public void CzechTextIsTheExpectedTranslation()
    {
        var cs = TSharpVisionLocalization.LoadBuiltIn("cs")!.Entries;
        Assert.Equal("Hledat v nápovědě", cs["Help_Search_Title"]);
        Assert.Equal("~S~lova", cs["Help_Search_Words"]);
        Assert.Equal("Žádné téma nápovědy neobsahuje '{0}'.", cs["Help_Search_NoResults"]);
        Assert.Equal("~T~émata ({0})", cs["Help_Search_Topics"]);
    }

    // The real search dialogs in a running program show the active language's strings.
    [Theory]
    [MemberData(nameof(Languages))]
    public void SearchDialogsShowTheActiveLanguage(string language)
    {
        Assert.True(TSharpVisionIntl.UseBuiltInLanguage(language));
        var table = TSharpVisionLocalization.LoadBuiltIn(language)!.Entries;
        string path = BuildHelp();
        using var help = new THelpCollection();
        help.Open(path);
        using var scope = new DemoTestScope(); var app = scope.App;
        var window = new THelpWindow(help, 1);
        app.DeskTop!.Insert(window);
        var viewer = window.Viewer!;

        // No match: prompt title and label, then the localized message with the query.
        app.Step(() =>
        {
            var prompt = app.Modal<TDialog>();
            Assert.Equal(table["Help_Search_Title"], prompt.title);
            Assert.Contains(table["Help_Search_Words"], ScriptedDemoApp.Text(prompt));
            ComboInput.Type("zzzz");
            ComboInput.Send(ComboInput.Key(Keys.kbEnter));
            app.Step(() =>
            {
                Assert.Contains(string.Format(table["Help_Search_NoResults"], "zzzz"), ScriptedDemoApp.Text(app.Modal<TDialog>()));
                ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            });
        });
        viewer.Search();

        // Matches: the result list's title and its label with the count.
        app.Step(() =>
        {
            var prompt = app.Modal<TDialog>();
            var line = ScriptedDemoApp.Descendants(prompt).OfType<TInputLine>().Single();
            line.SetData("common");
            ComboInput.Send(ComboInput.Key(Keys.kbEnter));
            app.Step(() =>
            {
                var results = Assert.IsType<THelpSearchResultsDialog>(TView.ModalTopView);
                Assert.Equal(table["Help_Search_ResultsTitle"], results.title);
                Assert.Contains(string.Format(table["Help_Search_Topics"], 2), ScriptedDemoApp.Text(results));
                ComboInput.Send(ComboInput.Key(Keys.kbEsc));
            });
        });
        viewer.Search();
        window.Close();
    }

    [Fact]
    public void AnApplicationProviderStillOverridesTheBuiltInTranslation()
    {
        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("cs"));
        Assert.Equal("Hledat v nápovědě", TSharpVisionIntl.Get("Help_Search_Title", "x"));
        TSharpVisionIntl.Current = new OverrideProvider();
        Assert.Equal("Custom", TSharpVisionIntl.Get("Help_Search_Title", "x"));
    }

    private sealed class OverrideProvider : ITSharpVisionStringProvider
    {
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(fallback))]
        public string? Get(string key, string? fallback) => key == "Help_Search_Title" ? "Custom" : fallback;
    }

    private string BuildHelp()
    {
        THelpFile.RegisterStreamableTypes();
        string path = Path.Combine(_tmp.Path, "loc.hlp");
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        foreach (var (ctx, text) in new[] { (1, "Index\ncommon start"), (2, "Two\ncommon word") })
        {
            var topic = new THelpTopic();
            topic.AddParagraph(new TParagraph { wrap = false, Text = text });
            hf.RecordPositionInIndex(ctx);
            hf.PutTopic(topic);
        }
        hf.Flush();
        fp.Close();
        return path;
    }
}
