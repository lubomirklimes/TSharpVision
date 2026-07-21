using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests;

public sealed class SDLGpuOptionsTests
{
    // ── NormalizeBackend ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(null,         null)]
    [InlineData("",           null)]
    [InlineData("  ",         null)]
    [InlineData("metal",      "metal")]
    [InlineData("METAL",      "metal")]
    [InlineData("  Metal  ",  "metal")]
    [InlineData("vulkan",     "vulkan")]
    [InlineData("direct3d12", "direct3d12")]
    public void NormalizeBackend_ReturnsExpected(string? input, string? expected)
    {
        Assert.Equal(expected, SDLGpuOptions.NormalizeBackend(input));
    }

    // ── ParseAllowedFrames ───────────────────────────────────────────────────

    [Theory]
    [InlineData(null,  null)]
    [InlineData("",    null)]
    [InlineData("0",   null)]   // below minimum
    [InlineData("4",   null)]   // above maximum
    [InlineData("abc", null)]
    [InlineData("1",   1u)]
    [InlineData("2",   2u)]
    [InlineData("3",   3u)]
    public void ParseAllowedFrames_ReturnsExpected(string? input, uint? expected)
    {
        Assert.Equal(expected, SDLGpuOptions.ParseAllowedFrames(input));
    }

    // ── ParsePresentMode ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(null,         null)]
    [InlineData("",           null)]
    [InlineData("unknown",    null)]
    [InlineData("vsync",      SDL3.SDL.GPUPresentMode.VSync)]
    [InlineData("VSYNC",      SDL3.SDL.GPUPresentMode.VSync)]
    [InlineData("  vsync  ",  SDL3.SDL.GPUPresentMode.VSync)]
    [InlineData("immediate",  SDL3.SDL.GPUPresentMode.Immediate)]
    [InlineData("IMMEDIATE",  SDL3.SDL.GPUPresentMode.Immediate)]
    [InlineData("mailbox",    SDL3.SDL.GPUPresentMode.Mailbox)]
    [InlineData("MAILBOX",    SDL3.SDL.GPUPresentMode.Mailbox)]
    public void ParsePresentMode_ReturnsExpected(string? input, SDL3.SDL.GPUPresentMode? expected)
    {
        Assert.Equal(expected, SDLGpuOptions.ParsePresentMode(input));
    }

    // ── ParseBool ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1",     true,  true)]
    [InlineData("on",    true,  true)]
    [InlineData("ON",    true,  true)]
    [InlineData("true",  true,  true)]
    [InlineData("TRUE",  true,  true)]
    [InlineData("0",     false, false)]
    [InlineData("off",   false, false)]
    [InlineData("OFF",   false, false)]
    [InlineData("false", false, false)]
    [InlineData("FALSE", false, false)]
    [InlineData(null,    true,  true)]   // null → default=true
    [InlineData(null,    false, false)]  // null → default=false
    [InlineData("",      true,  true)]   // empty → default
    [InlineData("  ",    true,  true)]   // whitespace → default
    [InlineData("yes",   true,  true)]   // unrecognised → default
    [InlineData("yes",   false, false)]  // unrecognised → default
    public void ParseBool_ReturnsExpected(string? input, bool defaultValue, bool expected)
    {
        Assert.Equal(expected, SDLGpuOptions.ParseBool(input, defaultValue));
    }
}
