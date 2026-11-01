// TV12: the BlackWhite and Monochrome application palettes must reach the SDL and SDL GPU pixels as neutral grays.
using System.Reflection;
using TSharpVision;
using TSharpVision.Drivers.SDL;
using TSharpVision.Drivers.SDL.Gpu;
using Xunit;

namespace TSharpVision.Drivers.SDL.Tests;

public sealed class SdlMonochromePaletteTests
{
    // Every packed attribute the two palettes define, plus the fixed shadow attribute 0x08.
    public static IEnumerable<object[]> NeutralAttributes() =>
        (TProgram.cpBlackWhite + TProgram.cpMonochrome + "\x08")
            .Where(c => c != '\0').Select(c => (byte)c).Distinct().Select(a => new object[] { a });

    private static bool Gray(uint argb) =>
        ((argb >> 16) & 0xFF) == ((argb >> 8) & 0xFF) && ((argb >> 8) & 0xFF) == (argb & 0xFF);

    [Theory]
    [MemberData(nameof(NeutralAttributes))]
    public void RendererDecodesBlackWhiteAndMonochromeAttributesToGrays(byte attr)
    {
        var (fg, bg) = SdlPalette.DecodeAttr(attr);
        Assert.True(Gray(fg) && Gray(bg), $"{attr:X2} -> {fg:X8}/{bg:X8}");
        Assert.NotEqual(fg, bg);
    }

    [Theory]
    [InlineData(typeof(SDLGpuRenderer))]
    [InlineData(typeof(TerminalGpuPipeline))]
    public void GpuPathsUseTheRendererVgaTable(Type type)
    {
        var table = (uint[])type.GetField("Vga16", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.Equal(SdlPalette.Vga16, table);
    }
}
