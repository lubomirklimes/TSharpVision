// SDL palette + SDLDriver headless lifecycle tests.
// SdlPalette.DecodeAttr is pure; SDLDriver runs under TSharpVision_NO_SDL=1.
using System.IO;
using TSharpVision;
using TSharpVision.Drivers.SDL;
using TSharpVision.Drivers.SDL.Rendering.Fonts;
using Xunit;

namespace TSharpVision.Drivers.SDL.Tests;

public sealed class SdlPaletteTests
{
    // ── SdlPalette.DecodeAttr ─────────────────────────────────────────────

    [Fact]
    public void DecodeAttr_White_on_Black()
    {
        // 0x07 = FG light gray, BG black (CGA/VGA standard)
        var (fg, bg) = SdlPalette.DecodeAttr(0x07);
        Assert.Equal(0xFFAAAAAA, fg);
        Assert.Equal(0xFF000000, bg);
    }

    [Fact]
    public void DecodeAttr_White_on_Blue()
    {
        // 0x1F = bright white on blue
        var (fg, bg) = SdlPalette.DecodeAttr(0x1F);
        Assert.Equal(0xFFFFFFFF, fg);
        Assert.Equal(0xFF0000AA, bg);
    }

    [Fact]
    public void DecodeAttr_Black_on_Gray()
    {
        // 0x70 = black on light gray (reversed)
        var (fg, bg) = SdlPalette.DecodeAttr(0x70);
        Assert.Equal(0xFF000000, fg);
        Assert.Equal(0xFFAAAAAA, bg);
    }

    [Fact]
    public void DecodeAttr_White_on_Red()
    {
        // 0x4F = bright white on red
        var (fg, bg) = SdlPalette.DecodeAttr(0x4F);
        Assert.Equal(0xFFFFFFFF, fg);
        Assert.Equal(0xFFAA0000, bg);
    }

    // ── SDLDriver static capabilities (no SDL_Init) ───────────────────────

    [Fact]
    public void SDLDriver_SupportsTrueColor_False()
    {
        Assert.False(new SDLDriver().SupportsTrueColor);
    }

    [Fact]
    public void SDLDriver_SupportsMouse_True()
    {
        Assert.True(new SDLDriver().SupportsMouse);
    }

    // ── SDLDriver headless lifecycle ──────────────────────────────────────

    [Fact]
    public void SDLDriver_Lifecycle_NoThrow()
    {
        Environment.SetEnvironmentVariable("TSharpVision_NO_SDL", "1");
        try
        {
            var sdl = new SDLDriver();
            sdl.Initialize();
            Assert.False(sdl.ReadKeyEvent(out _));
            sdl.PumpMessages();
            sdl.Shutdown();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TSharpVision_NO_SDL", null);
        }
    }

    [Fact]
    public void SDLDriver_DoubleInitShutdown_Idempotent()
    {
        Environment.SetEnvironmentVariable("TSharpVision_NO_SDL", "1");
        try
        {
            var d = new SDLDriver();
            d.Shutdown();
            d.Shutdown();
            d.Initialize();
            d.Initialize();
            d.Shutdown();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TSharpVision_NO_SDL", null);
        }
    }

    // ── Font lookup (no SDL_Init) ─────────────────────────────────────────

    [Fact]
    public void ProbeFontPath_ReturnsNullOrAnExistingFile()
    {
        string? path = SdlFontLocator.ProbeFontPath();

        if (path != null)
            Assert.True(File.Exists(path));
    }
}
