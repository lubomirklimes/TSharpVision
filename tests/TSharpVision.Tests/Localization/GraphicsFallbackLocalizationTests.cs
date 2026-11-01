using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Graphics;
using TSharpVision.Tests.Graphics;
using TSharpVision.Tests.Samples;
using Xunit;

namespace TSharpVision.Tests.Localization;

// TV12q: the text-driver fallback of TSharpVision.Graphics (GView.UnavailableText) is a framework string. Its key
// lives in the core inventory and every embedded translation; GView looks it up through TSharpVisionIntl when it
// draws, so a runtime language switch shows on the next draw. An application-set text wins and is never translated.
[Collection("NonParallel")]
public sealed class GraphicsFallbackLocalizationTests : IDisposable
{
    private const string Key = "Graphics_Unavailable";
    private const string English = "Pixel graphics need an SDL display driver.";

    private static readonly Dictionary<string, string> Expected = new()
    {
        ["cs"] = "Pixelová grafika vyžaduje ovladač displeje SDL.",
        ["de"] = "Pixelgrafik erfordert einen SDL-Anzeigetreiber.",
        ["en"] = English,
        ["es"] = "Los gráficos de píxeles requieren un controlador de pantalla SDL.",
        ["fr"] = "Les graphismes en pixels nécessitent un pilote d'affichage SDL.",
        ["it"] = "La grafica a pixel richiede un driver di visualizzazione SDL.",
        ["pl"] = "Grafika pikselowa wymaga sterownika ekranu SDL.",
        ["sk"] = "Pixelová grafika vyžaduje ovládač displeja SDL.",
    };

    private readonly ConfiguredLanguageScope _language = new();

    public void Dispose() => _language.Dispose();

    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (string language in TSharpVisionLocalization.BuiltInLanguages) data.Add(language);
        return data;
    }

    private static GWindow Open(ScriptedDemoApp app, int width = 76)
    {
        var win = new GWindow(new TRect(0, 0, width, 8), "g", Views.wnNoNumber);
        win.Insert(new GLine(0, 0, 100, 100, 0xFFFFFFFF));
        app.DeskTop!.Insert(win);
        return win;
    }

    // The screen row where the view centres its fallback text.
    private static string FallbackRow(GView view)
    {
        TPoint g = view.MakeGlobal(new TPoint(0, 0));
        int row = g.y + view.size.y / 2;
        return new string(Enumerable.Range(g.x, view.size.x)
            .Select(x => TScreen.ScreenBuffer!.GetChar((uint)x, (uint)row).Character).ToArray());
    }

    [Fact]
    public void The_key_is_in_the_English_inventory_and_matches_the_view_default()
    {
        Assert.Equal(Key, GView.UnavailableTextKey);
        Assert.Equal(English, GView.UnavailableTextEnglish);
        Assert.Equal(English, DefaultEnglishStringProvider.Entries[Key]);
        Assert.Equal(English, new GView(new TRect(0, 0, 10, 2)).UnavailableText);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_embedded_language_translates_the_fallback(string language)
    {
        var table = TSharpVisionLocalization.LoadBuiltIn(language)!.Entries;
        Assert.Equal(Expected[language], table[Key]);
        if (language != "en") Assert.NotEqual(English, table[Key]);

        Assert.True(TSharpVisionIntl.UseBuiltInLanguage(language));
        Assert.Equal(Expected[language], new GView(new TRect(0, 0, 10, 2)).UnavailableText);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void A_text_driver_draws_the_translated_fallback(string language)
    {
        Assert.True(TSharpVisionIntl.UseBuiltInLanguage(language));
        using var scope = new DemoTestScope();
        Assert.False(TDisplay.driver is IPixelGraphicsDriver);
        GWindow win = Open(scope.App);
        Assert.False(win.View.HasGraphicsLayer);
        Assert.Contains(Expected[language], FallbackRow(win.View));
        win.Close();
    }

    [Fact]
    public void A_runtime_language_switch_shows_on_the_next_draw()
    {
        using var scope = new DemoTestScope();
        GWindow win = Open(scope.App);
        Assert.Contains(English, FallbackRow(win.View));

        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("de"));
        win.View.DrawView();
        Assert.Contains(Expected["de"], FallbackRow(win.View));

        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("en"));
        win.View.DrawView();
        Assert.Contains(English, FallbackRow(win.View));
        win.Close();
    }

    [Fact]
    public void An_application_provider_wins_and_a_missing_key_falls_back_to_English()
    {
        var view = new GView(new TRect(0, 0, 10, 2));
        TSharpVisionIntl.Current = new OverrideProvider();
        Assert.Equal("APP-NO-PIXELS", view.UnavailableText);

        // A chain in front of a built-in translation: the application's key wins over Czech.
        TSharpVisionIntl.Current = new TSharpVisionStringProviderChain(
            new OverrideProvider(), TSharpVisionIntl.CreateBuiltInProvider("cs")!);
        Assert.Equal("APP-NO-PIXELS", view.UnavailableText);

        // A lookup provider that lacks the key: the English text is shown and MissingKey is raised.
        TSharpVisionIntl.Current = new EmptyLookupProvider();
        string? missing = null;
        EventHandler<MissingLocalizationKeyEventArgs> handler = (_, e) => missing = e.Key;
        TSharpVisionIntl.MissingKey += handler;
        try { Assert.Equal(English, view.UnavailableText); }
        finally { TSharpVisionIntl.MissingKey -= handler; }
        Assert.Equal(Key, missing);

        // A plain provider that does not know the key returns the fallback: English.
        TSharpVisionIntl.Current = new PassThroughProvider();
        Assert.Equal(English, view.UnavailableText);
    }

    [Fact]
    public void An_application_set_text_wins_over_every_language_and_null_restores_the_framework_text()
    {
        var view = new GView(new TRect(0, 0, 10, 2)) { UnavailableText = "Custom note" };
        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("cs"));
        Assert.Equal("Custom note", view.UnavailableText);
        TSharpVisionIntl.Current = new OverrideProvider();
        Assert.Equal("Custom note", view.UnavailableText);

        view.UnavailableText = "";
        Assert.Equal("", view.UnavailableText);

        view.UnavailableText = null!;
        Assert.Equal("APP-NO-PIXELS", view.UnavailableText);
    }

    [Fact]
    public void An_application_set_text_is_drawn_on_a_text_driver()
    {
        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("fr"));
        using var scope = new DemoTestScope();
        GWindow win = Open(scope.App);
        win.View.UnavailableText = "APP-SET-NOTE";
        win.View.DrawView();
        string row = FallbackRow(win.View);
        Assert.Contains("APP-SET-NOTE", row);
        Assert.DoesNotContain("SDL", row);
        win.Close();
    }

    [Fact]
    public void A_pixel_driver_view_draws_no_fallback_in_any_language()
    {
        Assert.True(TSharpVisionIntl.UseBuiltInLanguage("cs"));
        Assert.Equal(0, GView.ExposedCount);
        PixelRecordingDriver? driver = null;
        using var scope = new DemoTestScope(wrapDriver: inner => driver = new PixelRecordingDriver(inner));
        GWindow win = Open(scope.App);
        Assert.True(win.View.HasGraphicsLayer);
        Assert.True(driver!.HasLayer(win.View));
        string row = FallbackRow(win.View);
        Assert.DoesNotContain(Expected["cs"], row);
        Assert.DoesNotContain(English, row);
        Assert.Equal("", row.Trim());
        win.Close();
    }

    private sealed class OverrideProvider : ITSharpVisionStringProvider
    {
        public string? Get(string key, string? fallback) => key == Key ? "APP-NO-PIXELS" : fallback;
    }

    private sealed class PassThroughProvider : ITSharpVisionStringProvider
    {
        public string? Get(string key, string? fallback) => fallback;
    }

    private sealed class EmptyLookupProvider : ITSharpVisionStringLookupProvider
    {
        public string? Get(string key, string? fallback) => fallback;

        public bool TryGet(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
        {
            value = null;
            return false;
        }
    }
}
