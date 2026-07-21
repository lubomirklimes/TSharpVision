using TSharpVision.Config;

namespace TSharpVision.Drivers.SDL.Config;

/// <summary>
/// Typed representation of the <c>[sdl]</c> section in a .cfg file.
/// Contains SDL-specific rendering options (GPU present mode, backend, etc.).
/// Font settings are in <see cref="GraphicsDriverConfiguration"/> (core, <c>[graphics]</c> section).
/// </summary>
public sealed class SdlDriverConfiguration : IConfigurationSection
{
    public string SectionName => "sdl";

    /// <summary>Present mode: "vsync" | "immediate" | "mailbox". Default: immediate.</summary>
    public string? PresentMode { get; private set; }

    /// <summary>GPU backend hint: "metal" | "vulkan" | "direct3d12". Null = auto.</summary>
    public string? Backend { get; private set; }

    /// <summary>Continuous rendering: "1" | "0". Default: off (on-demand).</summary>
    public string? Continuous { get; private set; }

    /// <summary>Allowed frames in flight: "1" | "2" | "3". Null = SDL default.</summary>
    public string? AllowedFrames { get; private set; }

    /// <summary>Diagnostics output: "1" | "verbose". Null/empty = off.</summary>
    public string? Diagnostics { get; private set; }

    public void Bind(IReadOnlyDictionary<string, string> rawValues)
    {
        PresentMode   = rawValues.GetValueOrDefault("presentMode");
        Backend       = rawValues.GetValueOrDefault("backend");
        Continuous    = rawValues.GetValueOrDefault("continuous");
        AllowedFrames = rawValues.GetValueOrDefault("allowedFrames");
        Diagnostics   = rawValues.GetValueOrDefault("diagnostics");
    }
}
