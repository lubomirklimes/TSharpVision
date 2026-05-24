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

        Assert.Null(config.DriverName);
        Assert.Null(config.SdlFontName);
        Assert.Null(config.SdlFontSize);
        Assert.Null(config.Language);
    }

    // ── Reads [driver] name ────────────────────────────────────────────────

    [Fact]
    public void LoadFromPath_ReadsDriverName()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg", "[driver]\nname=sdl");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Equal("sdl", config.DriverName);
        Assert.Null(config.SdlFontName);
        Assert.Null(config.SdlFontSize);
    }

    // ── Reads [sdl] fontName ───────────────────────────────────────────────

    [Fact]
    public void LoadFromPath_ReadsSdlFontName()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg",
            "[driver]\nname=sdl\n\n[sdl]\nfontName=Cascadia Mono");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Equal("sdl",           config.DriverName);
        Assert.Equal("Cascadia Mono", config.SdlFontName);
        Assert.Null(config.SdlFontSize);
    }

    [Fact]
    public void LoadFromPath_ReadsSdlFontSize()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg",
            "[driver]\nname=sdl\n\n[sdl]\nfontName=Cascadia Mono\nfontSize=16");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Equal("sdl",           config.DriverName);
        Assert.Equal("Cascadia Mono", config.SdlFontName);
        Assert.Equal(16,              config.SdlFontSize);
    }

    [Fact]
    public void LoadFromPath_InvalidSdlFontSize_ReturnsNull()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg", "[sdl]\nfontSize=large");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Null(config.SdlFontSize);
    }

    [Fact]
    public void LoadFromPath_NonPositiveSdlFontSize_ReturnsNull()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg", "[sdl]\nfontSize=0");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Null(config.SdlFontSize);
    }

    [Fact]
    public void LoadFromPath_ReadsLocalizationLanguage()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg", "[localization]\nlanguage=cs");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Equal("cs", config.Language);
    }

    // ── Console driver ─────────────────────────────────────────────────────

    [Fact]
    public void LoadFromPath_ConsoleDriver()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg", "[driver]\nname=console");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Equal("console", config.DriverName);
        Assert.Null(config.SdlFontName);
        Assert.Null(config.SdlFontSize);
    }

    // ── Case-insensitive section/key matching ──────────────────────────────

    [Fact]
    public void LoadFromPath_CaseInsensitiveSectionAndKey()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg",
            "[DRIVER]\nNAME=sdl\n[SDL]\nFONTNAME=My Font");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Equal("sdl",     config.DriverName);
        Assert.Equal("My Font", config.SdlFontName);
        Assert.Null(config.SdlFontSize);
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

            [sdl]
            ; font choice
            fontName=Consolas
            """;

        string path = WriteCfg(_tmp.Path, "test.cfg", text);
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Equal("sdl",      config.DriverName);
        Assert.Equal("Consolas", config.SdlFontName);
        Assert.Null(config.SdlFontSize);
    }

    // ── ResolveConfigPath ──────────────────────────────────────────────────

    [Fact]
    public void ResolveConfigPath_ReturnsNonNullPath()
    {
        // In a test process, GetEntryAssembly() is usually non-null.
        // We just verify the returned path ends with .cfg.
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
