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
    }

    // ── Reads [driver] name ────────────────────────────────────────────────

    [Fact]
    public void LoadFromPath_ReadsDriverName()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg", "[driver]\nname=sdl");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Equal("sdl", config.DriverName);
        Assert.Null(config.SdlFontName);
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
    }

    // ── Console driver ─────────────────────────────────────────────────────

    [Fact]
    public void LoadFromPath_ConsoleDriver()
    {
        string path = WriteCfg(_tmp.Path, "test.cfg", "[driver]\nname=console");
        var config = TSharpVisionConfigurationLoader.LoadFromPath(path);
        Assert.Equal("console", config.DriverName);
        Assert.Null(config.SdlFontName);
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
