using TSharpVision.Drivers.SDL.Rendering.Fonts;
using static TSharpVision.Drivers.SDL.Tests.Unicode.UnicodeTestText;

namespace TSharpVision.Drivers.SDL.Tests.Unicode;

/// <summary>
/// Runs only where the system fonts of the default fallback chain exist (the Windows chain: Segoe UI Emoji, an outline
/// CJK font, Segoe UI Symbol, plus a monospace base font). Elsewhere the font set differs, so the oracles below (which
/// font, colour or not) do not apply; that is an environment requirement, not a hidden failure.
/// </summary>
public sealed class SystemFontsFactAttribute : FactAttribute
{
    public SystemFontsFactAttribute()
    {
        IReadOnlyList<SdlFallbackFont> chain = SdlFallbackFontChain.Resolve(null, SdlFallbackFontChain.DefaultCandidates(), File.Exists);
        bool complete = OperatingSystem.IsWindows() && SdlFontLocator.ProbeFontPath() != null
            && chain.Any(f => f.Role == SdlFallbackFontRole.Emoji)
            && chain.Any(f => f.Role == SdlFallbackFontRole.Cjk)
            && chain.Any(f => f.Role == SdlFallbackFontRole.Symbol);
        if (!complete) Skip = "Needs the Windows fallback fonts (seguiemj, YuGothM or msyh, seguisym) and a monospace base font.";
    }
}

/// <summary>
/// TV13m cluster shaping with the real SDL_ttf (HarfBuzz) and system fonts, headless (no window): the U0-A oracles
/// rebuilt on the production rasteriser - one ligated glyph for ZWJ and skin-tone sequences, intrinsic colour for emoji,
/// letters for flags (no Windows flag font), the base font for combining marks, an outline CJK font, VS15/VS16, the
/// .notdef box, fitting into the cells, and the cache.
/// </summary>
[Collection("SDL initialization")]
public sealed class SdlClusterRasterizerTests : IDisposable
{
    private readonly IntPtr _font;
    private readonly SdlCellGlyphRasterizer _cells;
    private readonly SdlClusterRasterizer _clusters;

    public SdlClusterRasterizerTests() : this(16f) { }

    private SdlClusterRasterizerTests(float pointSize)
    {
        Assert.True(SDL3.TTF.Init(), SDL3.SDL.GetError());
        string path = SdlFontLocator.ProbeFontPath() ?? throw new InvalidOperationException("no base font");
        _font = SDL3.TTF.OpenFont(path, pointSize);
        Assert.NotEqual(IntPtr.Zero, _font);
        SdlFontMetrics.ComputeMetrics(_font, out int w, out int h);
        _cells = new SdlCellGlyphRasterizer(_font, path, pointSize, null, w, h);
        _clusters = new SdlClusterRasterizer(path, pointSize, w, h, _cells.TextBaseline, SdlFallbackFontChain.Resolve());
    }

    public void Dispose()
    {
        _clusters.Dispose();
        _cells.Dispose();
        SDL3.TTF.CloseFont(_font);
        SDL3.TTF.Quit();
    }

    private static int InkRuns(SdlClusterGlyph g)
    {
        int runs = 0;
        bool inRun = false;
        for (int x = 0; x < g.Width; x++)
        {
            bool ink = false;
            for (int y = 0; y < g.Height && !ink; y++) ink = g.AlphaAt(x, y) > 32;
            if (ink && !inRun) runs++;
            inRun = ink;
        }
        return runs;
    }

    private static int InkTop(SdlClusterGlyph g)
    {
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
                if (g.AlphaAt(x, y) > 32) return y;
        return -1;
    }

    private static int Inked(SdlClusterGlyph g)
    {
        int n = 0;
        for (int i = 3; i < g.Rgba.Length; i += 4) if (g.Rgba[i] > 32) n++;
        return n;
    }

    [SystemFontsFact]
    public void HarfBuzz_is_compiled_in_and_colour_is_decoded()
    {
        SDL3.TTF.GetHarfBuzzVersion(out int major, out int minor, out int patch);
        Assert.NotEqual(0, major + minor + patch);
        Assert.True(_clusters.ColorGlyphsAvailable);
        Assert.Equal(SdlFallbackFontRole.Base, _clusters.Fonts[0].Role);
    }

    [SystemFontsFact]
    public void Emoji_is_one_colour_glyph_fitted_into_two_cells()
    {
        SdlClusterGlyph g = _clusters.Get(Grinning, 2);
        Assert.Equal(SdlFallbackFontRole.Emoji, g.Role);
        Assert.True(g.IsColor);
        Assert.Equal((2 * _clusters.CellWidth, _clusters.CellHeight), (g.Width, g.Height));
        Assert.Equal(1, InkRuns(g));
        Assert.False(g.Scaled);   // the fitted emoji size already fits
        Assert.True(_clusters.PointSizeOf(_clusters.Fonts.ToList().FindIndex(f => f.Role == SdlFallbackFontRole.Emoji)) <= 16f);
    }

    [SystemFontsFact]
    public void Zwj_family_and_skin_tone_ligate_into_one_colour_glyph()
    {
        foreach (string cluster in new[] { Family, ThumbsUpMedium, Technologist })
        {
            SdlClusterGlyph g = _clusters.Get(cluster, 2);
            Assert.Equal(SdlFallbackFontRole.Emoji, g.Role);
            Assert.True(g.IsColor, cluster);
            Assert.Equal(1, InkRuns(g));
        }
    }

    [SystemFontsFact]
    public void Flag_has_no_flag_glyph_on_windows_and_draws_two_monochrome_letters()
    {
        // Documented font-coverage limitation (U0-A K1e): Segoe UI Emoji has regional-indicator letters, no flags.
        SdlClusterGlyph g = _clusters.Get(FlagCz, 2);
        Assert.Equal(SdlFallbackFontRole.Emoji, g.Role);
        Assert.False(g.IsColor);
        Assert.True(Inked(g) > 0);
    }

    [SystemFontsFact]
    public void Combining_acute_stays_on_its_base_in_one_cell()
    {
        SdlClusterGlyph composed = _clusters.Get(EAcute, 1);
        SdlClusterGlyph plain = _clusters.Get("e", 1);
        Assert.Equal(SdlFallbackFontRole.Base, composed.Role);
        Assert.Equal(_clusters.CellWidth, composed.Width);
        Assert.Equal(1, InkRuns(composed));
        Assert.True(InkTop(composed) < InkTop(plain), "the acute rises above the e");
    }

    [SystemFontsFact]
    public void Cjk_uses_the_outline_cjk_font_monochrome()
    {
        SdlClusterGlyph g = _clusters.Get(Kan, 2);
        Assert.Equal(SdlFallbackFontRole.Cjk, g.Role);
        Assert.False(g.IsColor);
        Assert.DoesNotContain("msgothic", g.FontPath!, StringComparison.OrdinalIgnoreCase);
        Assert.True(Inked(g) > 20);
        // Antialiased outline, not a 1-bit bitmap strike: partial coverage exists.
        Assert.Contains(Enumerable.Range(0, g.Width * g.Height), i => g.Rgba[i * 4 + 3] is > 0 and < 255);
    }

    [SystemFontsFact]
    public void Vs15_heart_is_monochrome_and_vs16_heart_is_colour()
    {
        SdlClusterGlyph text = _clusters.Get(HeartText, 1);
        SdlClusterGlyph emoji = _clusters.Get(HeartEmoji, 2);
        Assert.NotEqual(SdlFallbackFontRole.Emoji, text.Role);
        Assert.False(text.IsColor);
        Assert.Equal(SdlFallbackFontRole.Emoji, emoji.Role);
        Assert.True(emoji.IsColor);
    }

    [SystemFontsFact]
    public void Cluster_no_font_covers_is_a_notdef_box_inside_its_cells()
    {
        SdlClusterGlyph g = _clusters.Get(S(0x10FFFD), 2);
        Assert.Equal(SdlClusterCoverage.Missing, g.Coverage);
        Assert.Null(g.Role);
        Assert.Equal(2 * _clusters.CellWidth, g.Width);
        Assert.True(Inked(g) > 0);
        Assert.Equal(0, g.AlphaAt(0, 0));
    }

    [SystemFontsFact]
    public void Excessive_combining_and_zwj_text_stays_inside_the_cells()
    {
        string marks = "a" + new string((char)0x0301, 300);
        SdlClusterGlyph stacked = _clusters.Get(marks, 1);
        Assert.Equal((_clusters.CellWidth, _clusters.CellHeight), (stacked.Width, stacked.Height));

        var zwj = new System.Text.StringBuilder(Grinning);
        for (int i = 0; i < 40; i++) zwj.Append((char)0x200D).Append(Grinning);
        SdlClusterGlyph chain = _clusters.Get(zwj.ToString(), 2);
        Assert.Equal((2 * _clusters.CellWidth, _clusters.CellHeight), (chain.Width, chain.Height));
        Assert.True(chain.Scaled);   // 41 unligated emoji are scaled down into the two cells, not spilled
        Assert.True(Inked(chain) > 0);
    }

    [SystemFontsFact]
    public void Cache_is_keyed_by_cells_and_text()
    {
        int before = _clusters.Rasterized;
        SdlClusterGlyph a = _clusters.Get(Kan, 2);
        Assert.Same(a, _clusters.Get(Kan, 2));
        Assert.NotSame(a, _clusters.Get(Kan, 1));
        Assert.Equal(before + 2, _clusters.Rasterized);
    }

    [SystemFontsFact]
    public void Sizes_fit_at_24_points_too()
    {
        using var big = new SdlClusterRasterizerTests(24f);
        foreach (string cluster in new[] { Grinning, Family, Kan, FlagCz })
        {
            SdlClusterGlyph g = big._clusters.Get(cluster, 2);
            Assert.Equal((2 * big._clusters.CellWidth, big._clusters.CellHeight), (g.Width, g.Height));
            Assert.True(Inked(g) > 0, cluster);
        }
    }
}
