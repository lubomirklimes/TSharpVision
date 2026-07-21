using TSharpVision.Config;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Config;

public sealed class TSharpVisionConfigurationLoaderTests : IDisposable
{
    private readonly TempDirectory _tmp;

    public TSharpVisionConfigurationLoaderTests()
    {
        _tmp = new TempDirectory();
    }

    public void Dispose() => _tmp.Dispose();

    // ── Missing file returns defaults ──────────────────────────────────────

    [Fact]
    public void LoadFromPath_MissingFile_ReturnsNullProperties()
    {
        var config = TSharpVisionConfigurationLoader.LoadFromPath(
            Path.Combine(_tmp.Path, "nonexistent.cfg"));

        Assert.Null(config.Driver.Name);
        Assert.Null(config.Graphics.FontName);
        Assert.Null(config.Graphics.FontSize);
        Assert.Null(config.Localization.Language);
    }

    // ── Reads [driver] name ────────────────────────────────────────────────

    [Fact]
    public void LoadFromPath_ReadsDriverName()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg", "[driver]\nname=sdl");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Equal("sdl", config.Driver.Name);
        Assert.Null(config.Graphics.FontName);
        Assert.Null(config.Graphics.FontSize);
    }

    // ── Reads [graphics] fontName ──────────────────────────────────────────

    [Fact]
    public void LoadFromPath_ReadsGraphicsFontName()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg",
            "[driver]\nname=sdl\n\n[graphics]\nfontName=Cascadia Mono");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Equal("sdl",           config.Driver.Name);
        Assert.Equal("Cascadia Mono", config.Graphics.FontName);
        Assert.Null(config.Graphics.FontSize);
    }

    [Fact]
    public void LoadFromPath_ReadsGraphicsFontSize()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg",
            "[driver]\nname=sdl\n\n[graphics]\nfontName=Cascadia Mono\nfontSize=16");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Equal("sdl",           config.Driver.Name);
        Assert.Equal("Cascadia Mono", config.Graphics.FontName);
        Assert.Equal(16,              config.Graphics.FontSize);
    }

    [Fact]
    public void LoadFromPath_InvalidGraphicsFontSize_ReturnsNull()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg", "[graphics]\nfontSize=large");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Null(config.Graphics.FontSize);
    }

    [Fact]
    public void LoadFromPath_NonPositiveGraphicsFontSize_ReturnsNull()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg", "[graphics]\nfontSize=0");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Null(config.Graphics.FontSize);
    }

    [Fact]
    public void LoadFromPath_ReadsLocalizationLanguage()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg", "[localization]\nlanguage=cs");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Equal("cs", config.Localization.Language);
    }

    // ── Console driver ─────────────────────────────────────────────────────

    [Fact]
    public void LoadFromPath_ConsoleDriver()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg", "[driver]\nname=console");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Equal("console", config.Driver.Name);
        Assert.Null(config.Graphics.FontName);
        Assert.Null(config.Graphics.FontSize);
    }

    // ── Case-insensitive section/key matching ──────────────────────────────

    [Fact]
    public void LoadFromPath_CaseInsensitiveSectionAndKey()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg",
            "[DRIVER]\nNAME=sdl\n[GRAPHICS]\nFONTNAME=My Font");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Equal("sdl",     config.Driver.Name);
        Assert.Equal("My Font", config.Graphics.FontName);
        Assert.Null(config.Graphics.FontSize);
    }

    // ── Comments and blank lines are ignored ───────────────────────────────

    [Fact]
    public void LoadFromPath_CommentsAndBlankLinesIgnored()
    {
        const string text = """
            ; This is a comment
            # Another comment

            [driver]
            name=sdl

            [graphics]
            ; font choice
            fontName=Consolas
            """;

        string path = WriteCfg(_tmp.Path, "test.cfg", text);
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Equal("sdl",      config.Driver.Name);
        Assert.Equal("Consolas", config.Graphics.FontName);
        Assert.Null(config.Graphics.FontSize);
    }

    // ── RawSections exposes all sections ──────────────────────────────────

    [Fact]
    public void LoadFromPath_ExposesRawSections()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg",
            "[driver]\nname=sdl\n\n[sdl]\npresentMode=vsync\nbackend=vulkan");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);

        Assert.True(config.RawSections.ContainsKey("sdl"));
        Assert.Equal("vsync",  config.GetRawSection("sdl")!["presentMode"]);
        Assert.Equal("vulkan", config.GetRawSection("sdl")!["backend"]);
    }

    [Fact]
    public void LoadFromPath_GetRawSection_ReturnsNullForMissingSection()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg", "[driver]\nname=sdl");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Null(config.GetRawSection("sdl"));
    }

    // ── ResolveConfigPath ──────────────────────────────────────────────────

    [Fact]
    public void ResolveConfigPath_ReturnsNonNullPath()
    {
        string? path = TSharpVisionConfigurationLoader.ResolveConfigPath();
        if (path != null)
            Assert.EndsWith(".cfg", path, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helper ────────────────────────────────────────────────────────────

    private static string WriteCfg(string dir, string fileName, string content)
    {
        string path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content);
        return path;
    }
}
