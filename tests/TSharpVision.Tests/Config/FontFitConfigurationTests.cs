using TSharpVision.Config;
using TSharpVision.Drivers;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Config;

/// <summary>
/// <c>[graphics] fontFit</c> as the typed <see cref="GraphicsDriverConfiguration.FontFit"/> (TW-5A.4):
/// the public API, real .cfg loading and the user/app layering used by
/// <see cref="TSharpVisionConfigurationLoader.Load"/>.
/// </summary>
public sealed class FontFitConfigurationTests : IDisposable
{
    private readonly TempDirectory _tmp = new();

    public void Dispose() => _tmp.Dispose();

    // ── Public API ────────────────────────────────────────────────────────

    [Fact]
    public void FontFitMode_has_exactly_Condense_and_Uniform()
    {
        Assert.Equal(["Condense", "Uniform"], Enum.GetNames<FontFitMode>());
        Assert.Equal(0, (int)FontFitMode.Condense);
    }

    [Fact]
    public void Default_is_Condense()
    {
        Assert.Equal(FontFitMode.Condense, new GraphicsDriverConfiguration().FontFit);
        Assert.Equal(FontFitMode.Condense, new TSharpVisionConfiguration().Graphics.FontFit);
    }

    [Theory]
    [InlineData(FontFitMode.Condense)]
    [InlineData(FontFitMode.Uniform)]
    public void Programmatic_mode_coexists_with_FontName_and_FontSize(FontFitMode mode)
    {
        var graphics = new GraphicsDriverConfiguration
        {
            FontName = "Consolas",
            FontSize = 20,
            FontFit  = mode,
        };

        Assert.Equal(mode, graphics.FontFit);
        Assert.Equal("Consolas", graphics.FontName);
        Assert.Equal(20, graphics.FontSize);
    }

    [Fact]
    public void Programmatic_configuration_is_read_back_through_ScreenDriverFactory()
    {
        TSharpVisionConfiguration? previous = ScreenDriverFactory.Configuration;
        try
        {
            ScreenDriverFactory.Configuration = new TSharpVisionConfiguration
            {
                Graphics = new GraphicsDriverConfiguration { FontName = "Consolas", FontSize = 20, FontFit = FontFitMode.Uniform },
            };

            GraphicsDriverConfiguration graphics = ScreenDriverFactory.Configuration.Graphics;
            FontFitMode mode = graphics.FontFit;
            Assert.Equal(FontFitMode.Uniform, mode);
        }
        finally
        {
            ScreenDriverFactory.Configuration = previous;
        }
    }

    [Fact]
    public void The_documented_code_override_keeps_the_loaded_values_and_selects_Uniform()
    {
        // docs/drivers.md "Typed configuration": rebuild from the loaded configuration.
        TSharpVisionConfiguration loaded = TSharpVisionConfigurationLoader.LoadFromPath(
            Write("app.cfg", "[driver]\nname=sdl\n\n[graphics]\nfontName=Consolas\nfontSize=20\nfontFit=Condense\n\n[sdl]\nbackend=vulkan\n"));
        var configured = new TSharpVisionConfiguration
        {
            Driver       = loaded.Driver,
            Localization = loaded.Localization,
            RawSections  = loaded.RawSections,
            Graphics     = new GraphicsDriverConfiguration
            {
                FontName = loaded.Graphics.FontName,
                FontSize = loaded.Graphics.FontSize,
                FontFit  = FontFitMode.Uniform,
            },
        };

        Assert.Equal(FontFitMode.Condense, loaded.Graphics.FontFit);
        Assert.Equal(FontFitMode.Uniform, configured.Graphics.FontFit);
        Assert.Equal("Consolas", configured.Graphics.FontName);
        Assert.Equal(20, configured.Graphics.FontSize);
        Assert.Equal("sdl", configured.Driver.Name);
        Assert.Equal("vulkan", configured.GetRawSection("sdl")!["backend"]);
    }

    // ── Real .cfg loading ─────────────────────────────────────────────────

    [Theory]
    [InlineData("[graphics]\nfontName=Consolas\nfontSize=20\n", FontFitMode.Condense)]
    [InlineData("[graphics]\nfontName=Consolas\nfontSize=20\nfontFit=\n", FontFitMode.Condense)]
    [InlineData("[graphics]\nfontName=Consolas\nfontSize=20\nfontFit=   \n", FontFitMode.Condense)]
    [InlineData("[graphics]\nfontName=Consolas\nfontSize=20\nfontFit=Condense\n", FontFitMode.Condense)]
    [InlineData("[graphics]\nfontName=Consolas\nfontSize=20\nfontFit=Uniform\n", FontFitMode.Uniform)]
    [InlineData("[graphics]\nfontName=Consolas\nfontSize=20\nfontFit=uNiFoRm\n", FontFitMode.Uniform)]
    [InlineData("[graphics]\nfontName=Consolas\nfontSize=20\nfontFit=CONDENSE\n", FontFitMode.Condense)]
    [InlineData("[GRAPHICS]\nFONTNAME=Consolas\nFONTSIZE=20\nFONTFIT =  Uniform  \n", FontFitMode.Uniform)]
    public void A_real_cfg_file_populates_FontFit_next_to_FontName_and_FontSize(string text, FontFitMode expected)
    {
        var config = TSharpVisionConfigurationLoader.LoadFromPath(Write("app.cfg", text));
        Assert.Equal(expected, config.Graphics.FontFit);
        Assert.Equal("Consolas", config.Graphics.FontName);
        Assert.Equal(20, config.Graphics.FontSize);
    }

    [Fact]
    public void A_cfg_without_a_graphics_section_is_Condense() =>
        Assert.Equal(FontFitMode.Condense,
            TSharpVisionConfigurationLoader.LoadFromPath(Write("app.cfg", "[driver]\nname=sdl\n")).Graphics.FontFit);

    [Theory]
    [InlineData("Shrink")]
    [InlineData("Uniformly")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("condensed")]
    [InlineData("Condense,Uniform")]
    public void An_unsupported_value_is_a_clear_error_not_the_default(string value)
    {
        string path = Write("app.cfg", $"[driver]\nname=console\n\n[graphics]\nfontFit= {value} \n");
        var error = Assert.Throws<InvalidOperationException>(() => TSharpVisionConfigurationLoader.LoadFromPath(path));
        Assert.Contains("[graphics] fontFit", error.Message);
        Assert.Contains($"'{value}'", error.Message);
        Assert.Contains(path, error.Message);
        Assert.Contains("Supported values: Condense, Uniform", error.Message);
    }

    // ── Layering (user ~/.tsvision.cfg, then <Application>.cfg) ──────────

    [Theory]
    [InlineData("fontFit=Uniform", "", FontFitMode.Uniform)]          // app file silent: user value kept
    [InlineData("fontFit=Uniform", "fontFit=", FontFitMode.Uniform)]  // blank means not set, like fontSize
    [InlineData("fontFit=Uniform", "fontFit=Condense", FontFitMode.Condense)] // explicit Condense overrides
    [InlineData("", "fontFit=Uniform", FontFitMode.Uniform)]
    [InlineData("fontFit=Condense", "fontFit=Uniform", FontFitMode.Uniform)]
    [InlineData("", "", FontFitMode.Condense)]
    public void The_app_cfg_overrides_the_user_cfg_per_key(string userLine, string appLine, FontFitMode expected)
    {
        var user = TSharpVisionConfigurationLoader.LoadFromPath(
            Write("user.cfg", $"[graphics]\nfontName=Consolas\nfontSize=18\n{userLine}\n"));
        var app = TSharpVisionConfigurationLoader.LoadFromPath(
            Write("app.cfg", $"[graphics]\nfontSize=20\n{appLine}\n"));

        var merged = TSharpVisionConfigurationLoader.Merge(user, app);

        Assert.Equal(expected, merged.Graphics.FontFit);
        Assert.Equal("Consolas", merged.Graphics.FontName);
        Assert.Equal(20, merged.Graphics.FontSize);
    }

    // ── Helper ────────────────────────────────────────────────────────────

    private string Write(string fileName, string content)
    {
        string path = Path.Combine(_tmp.Path, fileName);
        File.WriteAllText(path, content);
        return path;
    }
}
