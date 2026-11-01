// Public runtime language switch: TSharpVisionIntl.UseBuiltInLanguage / CreateBuiltInProvider /
// BuiltInLanguages.
//
// TSharpVisionIntl.Current, ScreenDriverFactory.Configuration, Console.Error and the culture are
// process-global, so every test runs in the NonParallel collection and restores them through
// ConfiguredLanguageScope (see ConfiguredLanguageTests).
using System.Globalization;
using System.Runtime.CompilerServices;
using TSharpVision.Config;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Localization;

[Collection("NonParallel")]
public sealed class RuntimeLanguageTests : IDisposable
{
    private readonly TempDirectory _tmp = new();
    private readonly ConfiguredLanguageScope _scope = new();

    public void Dispose()
    {
        _scope.Dispose();
        _tmp.Dispose();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private void ConfigureFrom(string cfgText)
    {
        string path = Path.Combine(_tmp.Path, "app.cfg");
        File.WriteAllText(path, cfgText);
        TSharpVisionRuntime.Apply(TSharpVisionConfigurationLoader.LoadFromPath(path), "test");
    }

    private static string RenderedText(TView view)
    {
        var screen = RootScreenCopy.CopyRoot();
        var rows = new List<string>();
        for (int y = 0; y < view.size.y; y++)
        {
            var at = view.MakeGlobal(new TPoint(0, y));
            rows.Add(new string(Enumerable.Range(0, view.size.x)
                .Select(x => screen[at.x + x, at.y].Character).ToArray()));
        }
        return string.Join("\n", rows);
    }

    private static TDialog InsertConfirmationBox(TApplication app)
    {
        var dialog = MsgBox.BuildMessageBox(
            new TRect(10, 5, 60, 14), "?",
            (ushort)(MsgBox.mfConfirmation | MsgBox.mfYesNoCancel));
        app.DeskTop!.Insert(dialog);
        return dialog;
    }

    private static void WriteStringTable(string path, IDictionary<string, string> strings)
    {
        StreamableRegistration.RegisterAll();
        var fp = new Fpstream(path);
        try
        {
            var rf = new TResourceFile(fp);
            rf.Put(new TStringResource(strings), TResourceStringProvider.DefaultResourceKey);
            rf.Flush();
        }
        finally
        {
            fp.Close();
        }
    }

    private sealed class AppProvider : ITSharpVisionStringProvider
    {
        public string? Get(string key, string? fallback) => "app:" + key;
    }

    private static string RepoRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));

    // ── the language list ─────────────────────────────────────────────────────

    [Fact]
    public void BuiltInLanguages_ListsTheEightEmbeddedTranslations_ReadOnly()
    {
        Assert.Equal(new[] { "cs", "de", "en", "es", "fr", "it", "pl", "sk" }, TSharpVisionIntl.BuiltInLanguages);
        Assert.IsNotType<string[]>(TSharpVisionIntl.BuiltInLanguages);
        Assert.True(((ICollection<string>)TSharpVisionIntl.BuiltInLanguages).IsReadOnly);
    }

    // ── every built-in language through the public call ──────────────────────

    [Theory]
    [InlineData("cs", "Btn_Cancel", "~Z~rušit")]
    [InlineData("de", "Btn_Cancel", "~A~bbrechen")]
    [InlineData("en", "Btn_Cancel", "~C~ancel")]
    [InlineData("es", "MsgTitle_Warning", "Advertencia")]
    [InlineData("fr", "Btn_Yes", "~O~ui")]
    [InlineData("it", "Btn_Cancel", "~A~nnulla")]
    [InlineData("pl", "Btn_Yes", "~T~ak")]
    [InlineData("sk", "Btn_Yes", "~Á~no")]
    public void UseBuiltInLanguage_ActivatesEveryBuiltInLanguage(string language, string key, string expected)
    {
        Assert.True(TSharpVisionIntl.UseBuiltInLanguage(language));
        Assert.Equal(expected, TSharpVisionIntl.Get(key, "fallback"));

        var provider = TSharpVisionIntl.CreateBuiltInProvider(language);
        Assert.NotNull(provider);
        Assert.Equal(expected, provider!.Get(key, "fallback"));
    }

    [Fact]
    public void EveryListedLanguage_IsAcceptedAndCoversTheWholeEnglishInventory()
    {
        foreach (string language in TSharpVisionIntl.BuiltInLanguages)
        {
            Assert.True(TSharpVisionIntl.UseBuiltInLanguage(language), language);
            var lookup = Assert.IsAssignableFrom<ITSharpVisionStringLookupProvider>(TSharpVisionIntl.Current);
            foreach (string key in DefaultEnglishStringProvider.Entries.Keys)
                Assert.True(lookup.TryGet(key, out _), $"{language}: {key}");
        }
    }

    // ── normalization: same as configuration, never the OS culture ───────────

    [Theory]
    [InlineData("cs")]
    [InlineData("CS")]
    [InlineData(" cs ")]
    [InlineData("cs-CZ")]
    [InlineData("cs_CZ.UTF-8")]
    [InlineData("cs@euro")]
    public void LanguageValue_IsNormalizedToPrimarySubtag_IgnoringCulture(string value)
    {
        CultureInfo.CurrentUICulture = new CultureInfo("fr-FR");
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");

        Assert.True(TSharpVisionIntl.UseBuiltInLanguage(value));

        Assert.Equal("~Z~rušit", TSharpVisionIntl.Get("Btn_Cancel", "~C~ancel"));
    }

    [Fact]
    public void English_IsAnExplicitChoice_NotTheStockInstance()
    {
        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("en-US"));

        Assert.NotSame(TSharpVisionIntl.BuiltInDefault, TSharpVisionIntl.Current);
        Assert.Equal("~C~ancel", TSharpVisionIntl.Get("Btn_Cancel", "x"));
        Assert.Equal("Confirm", TSharpVisionIntl.Get("MsgTitle_Confirm", "x"));
    }

    // ── unsupported language: false, Current untouched, no warning ───────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("xx")]
    [InlineData("c")]
    [InlineData("12")]
    [InlineData("xx-CZ")]
    public void UnsupportedLanguage_ReturnsFalse_AndKeepsCurrentProvider(string? language)
    {
        var app = new AppProvider();
        TSharpVisionIntl.Current = app;
        var savedError = Console.Error;
        var error = new StringWriter();
        Console.SetError(error);
        try
        {
            Assert.False(TSharpVisionIntl.UseBuiltInLanguage(language));
            Assert.Null(TSharpVisionIntl.CreateBuiltInProvider(language));
        }
        finally
        {
            Console.SetError(savedError);
        }

        Assert.Same(app, TSharpVisionIntl.Current);
        Assert.Equal("", error.ToString());
    }

    // ── fallback chain and application-directory overrides ───────────────────

    [Fact]
    public void KeyMissingFromTranslation_FallsBackToEnglish()
    {
        // A language with only an application-directory table holding one key: every other
        // framework key resolves through English.
        WriteStringTable(Path.Combine(_tmp.Path, "tsharpvision_xq.tvr"),
            new Dictionary<string, string> { ["Btn_Cancel"] = "~Q~uit-xq" });

        Assert.True(TSharpVisionLocalization.UseLanguage("xq", _tmp.Path));

        Assert.Equal("~Q~uit-xq", TSharpVisionIntl.Get("Btn_Cancel", "x"));
        Assert.Equal("~Y~es", TSharpVisionIntl.Get("Btn_Yes", "x"));
        Assert.Equal("Confirm", TSharpVisionIntl.Get("MsgTitle_Confirm", "x"));
    }

    [Fact]
    public void KeyUnknownEverywhere_ReturnsCallerFallback_AndRaisesMissingKey()
    {
        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("de"));
        var missing = new List<string>();
        EventHandler<MissingLocalizationKeyEventArgs> handler = (_, e) => missing.Add(e.Key);
        TSharpVisionIntl.MissingKey += handler;
        try
        {
            Assert.Equal("literal", TSharpVisionIntl.Get("No_Such_Framework_Key", "literal"));
        }
        finally
        {
            TSharpVisionIntl.MissingKey -= handler;
        }

        Assert.Equal(new[] { "No_Such_Framework_Key" }, missing);
    }

    [Fact]
    public void ApplicationDirectoryFile_StillOverridesSingleKeys()
    {
        WriteStringTable(Path.Combine(_tmp.Path, "tsharpvision_cs.tvr"),
            new Dictionary<string, string> { ["Btn_Cancel"] = "~S~torno" });

        Assert.True(TSharpVisionLocalization.UseLanguage("cs-CZ", _tmp.Path));

        Assert.Equal("~S~torno", TSharpVisionIntl.Get("Btn_Cancel", "x"));
        Assert.Equal("~A~no", TSharpVisionIntl.Get("Btn_Yes", "x"));   // embedded Czech
        Assert.Equal("~O~K", TSharpVisionIntl.Get("Btn_OK", "x"));
    }

    [Fact]
    public void EnglishOverrideFile_AppliesToExplicitEnglish()
    {
        WriteStringTable(Path.Combine(_tmp.Path, "tsharpvision_en.tvr"),
            new Dictionary<string, string> { ["Btn_Cancel"] = "~D~ismiss" });

        Assert.True(TSharpVisionLocalization.UseLanguage("en", _tmp.Path));

        Assert.Equal("~D~ismiss", TSharpVisionIntl.Get("Btn_Cancel", "x"));
        Assert.Equal("~Y~es", TSharpVisionIntl.Get("Btn_Yes", "x"));
    }

    [Fact]
    public void EmbeddedTranslations_NeedNoLooseFiles()
    {
        // The test host ships no framework .tvr files, and an empty directory gives the same result.
        Assert.Empty(Directory.GetFiles(AppContext.BaseDirectory, "tsharpvision_*.tvr"));
        Assert.Empty(Directory.GetFiles(_tmp.Path));

        foreach (string language in TSharpVisionIntl.BuiltInLanguages)
            Assert.True(TSharpVisionLocalization.UseLanguage(language, _tmp.Path), language);

        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("pl"));
        Assert.Equal("~T~ak", TSharpVisionIntl.Get("Btn_Yes", "x"));
    }

    // ── precedence: the runtime call replaces, configuration does not ────────

    [Fact]
    public void RuntimeCall_ReplacesAnAppProvider()
    {
        TSharpVisionIntl.Current = new AppProvider();

        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("de"));

        Assert.IsNotType<AppProvider>(TSharpVisionIntl.Current);
        Assert.Equal("~A~bbrechen", TSharpVisionIntl.Get("Btn_Cancel", "x"));
    }

    [Fact]
    public void Configuration_StillNeverReplacesAnAppProvider()
    {
        var app = new AppProvider();
        TSharpVisionIntl.Current = app;

        ConfigureFrom("[localization]\nlanguage=cs\n");

        Assert.Same(app, TSharpVisionIntl.Current);
    }

    [Fact]
    public void RuntimeCall_ReplacesTheConfiguredLanguage()
    {
        ConfigureFrom("[localization]\nlanguage=cs\n");
        Assert.Equal("~Z~rušit", TSharpVisionIntl.Get("Btn_Cancel", "x"));

        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("de"));

        Assert.Equal("~A~bbrechen", TSharpVisionIntl.Get("Btn_Cancel", "x"));
    }

    [Fact]
    public void LaterConfigure_TreatsTheRuntimeChoiceAsTheApplications()
    {
        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("de"));
        var chosen = TSharpVisionIntl.Current;

        ConfigureFrom("[localization]\nlanguage=cs\n");
        Assert.Same(chosen, TSharpVisionIntl.Current);
        Assert.Equal("~A~bbrechen", TSharpVisionIntl.Get("Btn_Cancel", "x"));

        ConfigureFrom("[localization]\nlanguage=en\n");
        Assert.Same(chosen, TSharpVisionIntl.Current);
    }

    [Fact]
    public void LaterConfigure_KeepsAnExplicitRuntimeEnglish()
    {
        ConfigureFrom("[localization]\nlanguage=cs\n");
        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("en"));

        ConfigureFrom("[localization]\nlanguage=de\n");

        Assert.Equal("~C~ancel", TSharpVisionIntl.Get("Btn_Cancel", "x"));
    }

    [Fact]
    public void CreateBuiltInProvider_DoesNotInstall_AndComposesBehindAppStrings()
    {
        var before = TSharpVisionIntl.Current;
        var framework = TSharpVisionIntl.CreateBuiltInProvider("sk");
        Assert.Same(before, TSharpVisionIntl.Current);
        Assert.NotSame(framework, TSharpVisionIntl.CreateBuiltInProvider("sk"));

        var appStrings = new TResourceStringProvider(new TStringResource(
            new Dictionary<string, string> { ["MyApp_Title"] = "Moja aplikácia" }));
        TSharpVisionIntl.Current = new TSharpVisionStringProviderChain(appStrings, framework);

        Assert.Equal("Moja aplikácia", TSharpVisionIntl.Get("MyApp_Title", "My Application"));
        Assert.Equal("~Á~no", TSharpVisionIntl.Get("Btn_Yes", "x"));
        Assert.Equal("~O~K", TSharpVisionIntl.Get("Btn_OK", "x"));
    }

    // ── what refreshes: later creations yes, existing views no ────────────────

    [Fact]
    public void RenderedDialog_FollowsRuntimeSwitches_EnCsDeEn()
    {
        using var driver = new DriverScope(80, 25);
        using var app = new TApplication();

        // The message box gives each button a fixed width, so German "~A~bbrechen" is drawn cut
        // to "Abbreche" (the same as with configuration).
        (string Language, string[] Present, string[] Absent)[] steps =
        {
            ("en", new[] { "Confirm", "Yes", "No", "Cancel" }, new[] { "Potvrzení", "Abbreche" }),
            ("cs", new[] { "Potvrzení", "Ano", "Ne", "Zrušit" }, new[] { "Confirm", "Cancel" }),
            ("de", new[] { "Bestätigen", "Ja", "Nein", "Abbreche" }, new[] { "Potvrzení", "Zrušit", "Cancel" }),
            ("en", new[] { "Confirm", "Yes", "No", "Cancel" }, new[] { "Bestätigen", "Abbreche" }),
        };

        foreach (var (language, present, absent) in steps)
        {
            Assert.True(TSharpVisionIntl.UseBuiltInLanguage(language));
            var dialog = InsertConfirmationBox(app);
            app.Redraw();
            string text = RenderedText(dialog);
            foreach (string s in present)
                Assert.True(text.Contains(s), $"{language}: expected '{s}' in\n{text}");
            foreach (string s in absent)
                Assert.False(text.Contains(s), $"{language}: unexpected '{s}' in\n{text}");
            app.DeskTop!.Remove(dialog);
        }
    }

    [Fact]
    public void OpenDialog_KeepsTheTextItWasCreatedWith()
    {
        using var driver = new DriverScope(80, 25);
        using var app = new TApplication();
        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("en"));
        var open = InsertConfirmationBox(app);

        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("cs"));
        app.Redraw();

        string text = RenderedText(open);
        Assert.Contains("Confirm", text);
        Assert.Contains("Cancel", text);
        Assert.DoesNotContain("Zrušit", text);

        app.DeskTop!.Remove(open);
        var reopened = InsertConfirmationBox(app);
        app.Redraw();
        Assert.Contains("Zrušit", RenderedText(reopened));
    }

    [Fact]
    public void ExistingStatusLine_KeepsItsText_UntilRecreated()
    {
        using var driver = new DriverScope(80, 25);
        using var app = new TApplication();
        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("en"));
        var bounds = app.GetExtent();
        TStatusLine Build() => new(new TRect(0, bounds.b.y - 1, bounds.b.x, bounds.b.y),
            new TStatusDef(0, 0xFFFF) +
            new TStatusItem(TSharpVisionIntl.Get("Status_AltX_Exit", "~Alt+X~ Exit"), Constants.Keys.kbAltX, Constants.Views.cmQuit));

        var line = Build();
        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("de"));
        Assert.Equal("~Alt+X~ Exit", line.Items!.Text);

        var rebuilt = Build();
        Assert.NotEqual("~Alt+X~ Exit", rebuilt.Items!.Text);
        Assert.Equal(TSharpVisionIntl.Get("Status_AltX_Exit", "?"), rebuilt.Items!.Text);
    }

    // ── compile-verified before/after example (quoted in docs and the TV12d report) ──

    // Before (TV12c): ship tsharpvision_<lang>.tvr next to the app and build the chain yourself.
    private static void SwitchLanguageBefore(string language, string appDirectory)
    {
        var framework = TResourceStringProvider.TryLoad(
            Path.Combine(appDirectory, "tsharpvision_" + language + ".tvr"));
        TSharpVisionIntl.Current = new TSharpVisionStringProviderChain(
            framework, new DefaultEnglishStringProvider());
    }

    // After (TV12d): the embedded translation, no files to ship.
    private static void SwitchLanguageAfter(string language)
    {
        if (!TSharpVisionIntl.UseBuiltInLanguage(language))
            throw new ArgumentException($"TSharpVision has no '{language}' translation.", nameof(language));
    }

    [Fact]
    public void BeforeAndAfterExamples_GiveTheSameStrings()
    {
        // The "before" path needs loose files; copy the committed images to a temp app directory.
        foreach (string language in new[] { "cs", "de" })
            File.Copy(Path.Combine(RepoRoot(), "resources", "localization", "tsharpvision_" + language + ".tvr"),
                Path.Combine(_tmp.Path, "tsharpvision_" + language + ".tvr"));

        foreach (string language in new[] { "cs", "de" })
        {
            SwitchLanguageBefore(language, _tmp.Path);
            string[] before = DefaultEnglishStringProvider.Entries.Keys.Select(k => TSharpVisionIntl.Get(k, "?")).ToArray();

            SwitchLanguageAfter(language);
            string[] after = DefaultEnglishStringProvider.Entries.Keys.Select(k => TSharpVisionIntl.Get(k, "?")).ToArray();

            Assert.Equal(before, after);
            Assert.NotEqual(DefaultEnglishStringProvider.Entries.Values, after);
        }

        Assert.Throws<ArgumentException>(() => SwitchLanguageAfter("xx"));
    }
}
