using TSharpVision.Config;
using TSharpVision.Drivers;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Config;

/// <summary>
/// <c>[graphics] palette</c> / <c>[graphics] effect</c> as <see cref="GraphicsDriverConfiguration.Palette"/>
/// and <see cref="GraphicsDriverConfiguration.Effect"/> (TV12e): defaults, .cfg loading and layering.
/// </summary>
public sealed class DisplayPresetConfigurationTests : IDisposable
{
    private readonly TempDirectory _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Defaults_are_null()
    {
        Assert.Null(new GraphicsDriverConfiguration().Palette);
        Assert.Null(new GraphicsDriverConfiguration().Effect);
        Assert.Null(new TSharpVisionConfiguration().Graphics.Palette);
        Assert.Null(new TSharpVisionConfiguration().Graphics.Effect);
    }

    [Fact]
    public void LoadFromPath_reads_palette_and_effect_alongside_font_settings()
    {
        var cfg = TSharpVisionConfigurationLoader.LoadFromPath(
            Write("app.cfg", "[graphics]\nfontName=Consolas\nfontSize=20\npalette=Amber\neffect=Scanlines\n"));

        Assert.Equal("Amber", cfg.Graphics.Palette);
        Assert.Equal("Scanlines", cfg.Graphics.Effect);
        Assert.Equal("Consolas", cfg.Graphics.FontName);
        Assert.Equal(20, cfg.Graphics.FontSize);
    }

    [Fact]
    public void Values_are_trimmed_and_case_is_preserved_as_written()
    {
        var cfg = TSharpVisionConfigurationLoader.LoadFromPath(
            Write("app.cfg", "[graphics]\npalette =   gReEn  \neffect=\tscanLINES \n"));

        Assert.Equal("gReEn", cfg.Graphics.Palette);
        Assert.Equal("scanLINES", cfg.Graphics.Effect);
    }

    [Fact]
    public void Key_and_section_names_are_case_insensitive()
    {
        var cfg = TSharpVisionConfigurationLoader.LoadFromPath(
            Write("app.cfg", "[GRAPHICS]\nPALETTE=Grayscale\nEffect=Default\n"));

        Assert.Equal("Grayscale", cfg.Graphics.Palette);
        Assert.Equal("Default", cfg.Graphics.Effect);
    }

    [Theory]
    [InlineData("Glow")]
    [InlineData("Curvature")]
    [InlineData("ShadowMask")]
    public void Post_process_effect_ids_are_stored(string effect)
    {
        var cfg = TSharpVisionConfigurationLoader.LoadFromPath(
            Write("app.cfg", $"[graphics]\neffect={effect}\n"));

        Assert.Equal(effect, cfg.Graphics.Effect);
    }

    [Fact]
    public void Core_stores_ids_it_does_not_know_without_validation()
    {
        // VGA/CGA are no longer driver palettes; core still stores the raw value and the driver warns.
        var cfg = TSharpVisionConfigurationLoader.LoadFromPath(
            Write("app.cfg", "[graphics]\npalette=VGA\neffect=Glow,Scanlines\n"));

        Assert.Equal("VGA", cfg.Graphics.Palette);
        Assert.Equal("Glow,Scanlines", cfg.Graphics.Effect);
    }

    [Fact]
    public void Blank_values_mean_not_set()
    {
        var cfg = TSharpVisionConfigurationLoader.LoadFromPath(
            Write("app.cfg", "[graphics]\npalette=\neffect=   \n"));

        Assert.Null(cfg.Graphics.Palette);
        Assert.Null(cfg.Graphics.Effect);
    }

    [Fact]
    public void Unknown_ids_are_kept_because_drivers_own_the_id_set()
    {
        var cfg = TSharpVisionConfigurationLoader.LoadFromPath(
            Write("app.cfg", "[graphics]\npalette=Plasma\neffect=Bloom\n"));

        Assert.Equal("Plasma", cfg.Graphics.Palette);
        Assert.Equal("Bloom", cfg.Graphics.Effect);
    }

    [Fact]
    public void Absent_section_or_keys_leave_null()
    {
        var noSection = TSharpVisionConfigurationLoader.LoadFromPath(Write("a.cfg", "[driver]\nname=sdl\n"));
        Assert.Null(noSection.Graphics.Palette);
        Assert.Null(noSection.Graphics.Effect);

        var noKeys = TSharpVisionConfigurationLoader.LoadFromPath(Write("b.cfg", "[graphics]\nfontName=Consolas\n"));
        Assert.Null(noKeys.Graphics.Palette);
        Assert.Null(noKeys.Graphics.Effect);

        var missingFile = TSharpVisionConfigurationLoader.LoadFromPath(Path.Combine(_tmp.Path, "missing.cfg"));
        Assert.Null(missingFile.Graphics.Palette);
        Assert.Null(missingFile.Graphics.Effect);
    }

    [Fact]
    public void Merge_override_wins_when_set()
    {
        var user = TSharpVisionConfigurationLoader.LoadFromPath(
            Write("user.cfg", "[graphics]\npalette=Amber\neffect=Scanlines\n"));
        var app = TSharpVisionConfigurationLoader.LoadFromPath(
            Write("app.cfg", "[graphics]\npalette=Green\neffect=Default\n"));

        var merged = TSharpVisionConfigurationLoader.Merge(user, app);

        Assert.Equal("Green", merged.Graphics.Palette);
        Assert.Equal("Default", merged.Graphics.Effect);
    }

    [Fact]
    public void Merge_keeps_base_when_override_does_not_set_them()
    {
        var user = TSharpVisionConfigurationLoader.LoadFromPath(
            Write("user.cfg", "[graphics]\npalette=Amber\neffect=Scanlines\n"));
        var app = TSharpVisionConfigurationLoader.LoadFromPath(
            Write("app.cfg", "[graphics]\nfontName=Consolas\npalette=\n"));

        var merged = TSharpVisionConfigurationLoader.Merge(user, app);

        Assert.Equal("Amber", merged.Graphics.Palette);
        Assert.Equal("Scanlines", merged.Graphics.Effect);
        Assert.Equal("Consolas", merged.Graphics.FontName);
    }

    [Fact]
    public void Merge_sets_independently_per_property()
    {
        var user = TSharpVisionConfigurationLoader.LoadFromPath(Write("user.cfg", "[graphics]\npalette=Amber\n"));
        var app = TSharpVisionConfigurationLoader.LoadFromPath(Write("app.cfg", "[graphics]\neffect=Scanlines\n"));

        var merged = TSharpVisionConfigurationLoader.Merge(user, app);

        Assert.Equal("Amber", merged.Graphics.Palette);
        Assert.Equal("Scanlines", merged.Graphics.Effect);
    }

    [Fact]
    public void Programmatic_values_are_read_back_through_ScreenDriverFactory()
    {
        TSharpVisionConfiguration? previous = ScreenDriverFactory.Configuration;
        try
        {
            ScreenDriverFactory.Configuration = new TSharpVisionConfiguration
            {
                Graphics = new GraphicsDriverConfiguration
                {
                    Palette = DisplayPresetNames.Grayscale,
                    Effect = DisplayPresetNames.Scanlines,
                },
            };

            Assert.Equal("Grayscale", ScreenDriverFactory.Configuration?.Graphics.Palette);
            Assert.Equal("Scanlines", ScreenDriverFactory.Configuration?.Graphics.Effect);
        }
        finally
        {
            ScreenDriverFactory.Configuration = previous;
        }
    }

    private string Write(string fileName, string content)
    {
        string path = Path.Combine(_tmp.Path, fileName);
        File.WriteAllText(path, content);
        return path;
    }
}
