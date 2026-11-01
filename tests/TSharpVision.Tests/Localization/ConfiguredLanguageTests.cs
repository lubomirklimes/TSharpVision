// [localization] language configuration -> built-in framework translations.
//
// TSharpVisionIntl.Current, ScreenDriverFactory.Configuration/WindowTitle and the
// culture are process-global, so every test runs in the NonParallel collection and
// restores them through ConfiguredLanguageScope.
using System.Globalization;
using TSharpVision.Config;
using TSharpVision.Drivers;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Localization;

internal sealed class ConfiguredLanguageScope : IDisposable
{
    private readonly ITSharpVisionStringProvider _savedProvider = TSharpVisionIntl.Current;
    private readonly TSharpVisionConfiguration? _savedConfiguration = ScreenDriverFactory.Configuration;
    private readonly string? _savedTitle = ScreenDriverFactory.WindowTitle;
    private readonly CultureInfo _savedCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _savedUiCulture = CultureInfo.CurrentUICulture;

    public ConfiguredLanguageScope()
    {
        // Start every test from the process start state: the stock English provider.
        TSharpVisionLocalization.ResetToBuiltInDefault();
    }

    public void Dispose()
    {
        TSharpVisionLocalization.ResetToBuiltInDefault();
        TSharpVisionIntl.Current = _savedProvider;
        ScreenDriverFactory.Configuration = _savedConfiguration;
        ScreenDriverFactory.WindowTitle = _savedTitle;
        CultureInfo.CurrentCulture = _savedCulture;
        CultureInfo.CurrentUICulture = _savedUiCulture;
    }
}

[Collection("NonParallel")]
public sealed class ConfiguredLanguageTests : IDisposable
{
    private readonly TempDirectory _tmp = new();
    private readonly ConfiguredLanguageScope _scope = new();

    public void Dispose()
    {
        _scope.Dispose();
        _tmp.Dispose();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private TSharpVisionConfiguration LoadConfig(string text)
    {
        string path = Path.Combine(_tmp.Path, "app.cfg");
        File.WriteAllText(path, text);
        return TSharpVisionConfigurationLoader.LoadFromPath(path);
    }

    // Runs the post-load part of TSharpVisionRuntime.Configure for a config file body.
    private void ConfigureFrom(string cfgText)
        => TSharpVisionRuntime.Apply(LoadConfig(cfgText), "test");

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

    private static string RenderConfirmationBox()
    {
        using var driver = new DriverScope(80, 25);
        using var app = new TApplication();
        var dialog = MsgBox.BuildMessageBox(
            new TRect(10, 5, 60, 14), "?",
            (ushort)(MsgBox.mfConfirmation | MsgBox.mfYesNoCancel));
        app.DeskTop!.Insert(dialog);
        app.Redraw();
        return RenderedText(dialog);
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

    // ── configuration selects a built-in translation ──────────────────────────

    [Fact]
    public void ConfigLanguageCs_RendersCzechMessageBox_WithoutProviderSetup()
    {
        ConfigureFrom("[localization]\nlanguage=cs\n");

        string text = RenderConfirmationBox();

        Assert.Contains("Potvrzení", text);
        Assert.Contains("Ano", text);
        Assert.Contains("Ne", text);
        Assert.Contains("Zrušit", text);
        Assert.DoesNotContain("Cancel", text);
        Assert.DoesNotContain("Confirm", text);
    }

    [Fact]
    public void ConfigLanguageDe_RendersGermanFileDialog()
    {
        ConfigureFrom("[localization]\nlanguage=de\n");

        using var driver = new DriverScope(80, 25);
        using var app = new TApplication();
        var dialog = new TFileDialog("*.*", "Test", TSharpVisionIntl.Get("File_Label_Name", "~N~ame"),
            (ushort)(FileDialogOptions.fdOpenButton | FileDialogOptions.fdHelpButton), 0);
        app.DeskTop!.Insert(dialog);
        app.Redraw();
        string text = RenderedText(dialog);

        Assert.Contains("Öffnen", text);
        Assert.Contains("Dateien", text);
        Assert.Contains("Abbrechen", text);
        Assert.Contains("Hilfe", text);
        Assert.DoesNotContain("Cancel", text);
    }

    [Theory]
    [InlineData("cs", "Btn_Cancel", "~Z~rušit")]
    [InlineData("de", "Btn_Cancel", "~A~bbrechen")]
    [InlineData("es", "MsgTitle_Warning", "Advertencia")]
    [InlineData("fr", "Btn_Yes", "~O~ui")]
    [InlineData("it", "Btn_Cancel", "~A~nnulla")]
    [InlineData("pl", "Btn_Yes", "~T~ak")]
    [InlineData("sk", "Btn_Yes", "~Á~no")]
    public void EveryBuiltInLanguage_IsSelectableFromConfig(string language, string key, string expected)
    {
        ConfigureFrom($"[localization]\nlanguage={language}\n");

        Assert.Equal(expected, TSharpVisionIntl.Get(key, "fallback"));
    }

    [Theory]
    [InlineData("CS")]
    [InlineData(" cs ")]
    [InlineData("cs-CZ")]
    [InlineData("cs_CZ.UTF-8")]
    public void LanguageValue_IsNormalizedToPrimarySubtag(string value)
    {
        ConfigureFrom($"[localization]\nlanguage={value}\n");

        Assert.Equal("~Z~rušit", TSharpVisionIntl.Get("Btn_Cancel", "~C~ancel"));
    }

    // ── English stays the default and the fallback ────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("[localization]\n")]
    [InlineData("[localization]\nlanguage=\n")]
    [InlineData("[localization]\nlanguage=en\n")]
    [InlineData("[localization]\nlanguage=en-US\n")]
    [InlineData("[localization]\nlanguage=xx\n")]
    [InlineData("[localization]\nlanguage=c\n")]
    public void MissingEmptyEnglishOrUnknownLanguage_KeepsBuiltInEnglish(string cfg)
    {
        ConfigureFrom(cfg);

        Assert.Same(TSharpVisionIntl.BuiltInDefault, TSharpVisionIntl.Current);
        string text = RenderConfirmationBox();
        Assert.Contains("Confirm", text);
        Assert.Contains("Cancel", text);
    }

    [Fact]
    public void MissingConfigFile_KeepsEnglish()
    {
        var config = TSharpVisionConfigurationLoader.LoadFromPath(Path.Combine(_tmp.Path, "none.cfg"));
        TSharpVisionRuntime.Apply(config, "test");

        Assert.Same(TSharpVisionIntl.BuiltInDefault, TSharpVisionIntl.Current);
        Assert.Equal("~C~ancel", TSharpVisionIntl.Get("Btn_Cancel", "x"));
    }

    [Fact]
    public void KeyMissingFromTranslation_FallsBackToEnglish()
    {
        // An application-directory translation for a language with no built-in table and
        // only one key: every other framework key must resolve through English.
        WriteStringTable(Path.Combine(_tmp.Path, "tsharpvision_xq.tvr"),
            new Dictionary<string, string> { ["Btn_Cancel"] = "~Q~uit-xq" });

        Assert.True(TSharpVisionLocalization.ApplyConfiguredLanguage("xq", _tmp.Path));

        Assert.Equal("~Q~uit-xq", TSharpVisionIntl.Get("Btn_Cancel", "x"));
        Assert.Equal("~Y~es", TSharpVisionIntl.Get("Btn_Yes", "x"));
        Assert.Equal("Confirm", TSharpVisionIntl.Get("MsgTitle_Confirm", "x"));
    }

    [Fact]
    public void KeyUnknownEverywhere_ReturnsCallerFallback_AndRaisesMissingKey()
    {
        ConfigureFrom("[localization]\nlanguage=cs\n");
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
    public void ApplicationDirectoryFile_OverridesSingleKeys_OfBuiltInTranslation()
    {
        WriteStringTable(Path.Combine(_tmp.Path, "tsharpvision_cs.tvr"),
            new Dictionary<string, string> { ["Btn_Cancel"] = "~S~torno" });

        Assert.True(TSharpVisionLocalization.ApplyConfiguredLanguage("cs", _tmp.Path));

        Assert.Equal("~S~torno", TSharpVisionIntl.Get("Btn_Cancel", "x"));
        Assert.Equal("~A~no", TSharpVisionIntl.Get("Btn_Yes", "x")); // embedded Czech
        Assert.Equal("~O~K", TSharpVisionIntl.Get("Btn_OK", "x"));
    }

    // ── precedence: the application's provider always wins ───────────────────

    private sealed class AppProvider : ITSharpVisionStringProvider
    {
        public string? Get(string key, string? fallback) => "app:" + key;
    }

    [Fact]
    public void AppProviderInstalledBeforeConfigure_IsNeverReplaced()
    {
        var app = new AppProvider();
        TSharpVisionIntl.Current = app;

        ConfigureFrom("[localization]\nlanguage=cs\n");

        Assert.Same(app, TSharpVisionIntl.Current);
        Assert.Equal("app:Btn_Cancel", TSharpVisionIntl.Get("Btn_Cancel", "x"));
    }

    [Fact]
    public void AppAssignedEnglishProvider_IsAnExplicitChoice_AndIsNotReplaced()
    {
        var english = new DefaultEnglishStringProvider();
        TSharpVisionIntl.Current = english;

        ConfigureFrom("[localization]\nlanguage=de\n");

        Assert.Same(english, TSharpVisionIntl.Current);
        Assert.Equal("~C~ancel", TSharpVisionIntl.Get("Btn_Cancel", "x"));
    }

    [Fact]
    public void AppProviderInstalledAfterConfigure_Wins_AndLaterConfigureKeepsIt()
    {
        ConfigureFrom("[localization]\nlanguage=cs\n");
        var app = new AppProvider();
        TSharpVisionIntl.Current = app;

        ConfigureFrom("[localization]\nlanguage=de\n");

        Assert.Same(app, TSharpVisionIntl.Current);
    }

    [Fact]
    public void Reconfigure_ReplacesOnlyTheConfigurationInstalledProvider()
    {
        ConfigureFrom("[localization]\nlanguage=cs\n");
        Assert.Equal("~Z~rušit", TSharpVisionIntl.Get("Btn_Cancel", "x"));

        ConfigureFrom("[localization]\nlanguage=de\n");
        Assert.Equal("~A~bbrechen", TSharpVisionIntl.Get("Btn_Cancel", "x"));

        ConfigureFrom("[localization]\nlanguage=en\n");
        Assert.Same(TSharpVisionIntl.BuiltInDefault, TSharpVisionIntl.Current);
    }

    // ── independent of the operating-system culture ───────────────────────────

    [Fact]
    public void Language_IgnoresCurrentUICulture()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("cs-CZ");
        CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
        ConfigureFrom("");
        Assert.Equal("~C~ancel", TSharpVisionIntl.Get("Btn_Cancel", "x"));

        CultureInfo.CurrentUICulture = new CultureInfo("fr-FR");
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        ConfigureFrom("[localization]\nlanguage=de\n");
        Assert.Equal("~A~bbrechen", TSharpVisionIntl.Get("Btn_Cancel", "x"));
    }

    // ── the public entry point, end to end through the .cfg file next to the app ──

    [Fact]
    public void RuntimeConfigure_ReadsAppConfigFile_AndRendersCzech()
    {
        string? cfgPath = TSharpVisionConfigurationLoader.ResolveConfigPath();
        Assert.NotNull(cfgPath);
        // Never clobber a config file that something else placed beside the test host.
        Assert.False(File.Exists(cfgPath), $"Unexpected existing config '{cfgPath}'.");

        File.WriteAllText(cfgPath!, "[localization]\nlanguage=cs\n");
        try
        {
            var config = TSharpVisionRuntime.Configure("test");
            Assert.Equal("cs", config.Localization.Language);
        }
        finally
        {
            File.Delete(cfgPath!);
        }

        string text = RenderConfirmationBox();
        Assert.Contains("Potvrzení", text);
        Assert.Contains("Zrušit", text);
    }
}
