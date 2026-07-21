using TSharpVision.Drivers.SDL.Config;

namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>
/// Parsed configuration for the SDL_GPU driver.
///
/// Resolution order (highest priority first):
///   1. Environment variables (TSHARPVISION_SDLGPU_*)
///   2. Config file values from <see cref="SdlDriverConfiguration"/> (<c>[sdl]</c> section)
///   3. Built-in defaults listed below
///
/// Environment variables:
///   TSHARPVISION_SDLGPU_DIAGNOSTICS      = 1|verbose  (default: off)
///   TSHARPVISION_SDLGPU_CONTINUOUS       = 1           (default: on-demand rendering)
///   TSHARPVISION_SDLGPU_BACKEND          = metal|vulkan|direct3d12  (default: auto)
///   TSHARPVISION_SDLGPU_ALLOWED_FRAMES   = 1|2|3       (default: SDL_GPU default)
///   TSHARPVISION_SDLGPU_PRESENT_MODE     = vsync|immediate|mailbox  (default: immediate)
/// </summary>
internal sealed class SDLGpuOptions
{
    public bool DiagnosticsEnabled     { get; init; }
    public bool DiagnosticsVerbose     { get; init; }
    public bool Continuous             { get; init; }
    public string? Backend             { get; init; }
    public uint? AllowedFramesInFlight { get; init; }
    public SDL3.SDL.GPUPresentMode? PresentMode { get; init; }

    /// <summary>
    /// Reads options from environment variables, falling back to
    /// <paramref name="cfg"/> values, then built-in defaults.
    /// </summary>
    public static SDLGpuOptions FromEnvironment(SdlDriverConfiguration? cfg = null)
    {
        string? diagVal =
            Environment.GetEnvironmentVariable("TSHARPVISION_SDLGPU_DIAGNOSTICS")
            ?? cfg?.Diagnostics;
        bool verbose = string.Equals(diagVal, "verbose", StringComparison.OrdinalIgnoreCase);
        bool enabled = diagVal == "1" || verbose;

        return new SDLGpuOptions
        {
            DiagnosticsEnabled    = enabled,
            DiagnosticsVerbose    = verbose,
            Continuous            = ParseBool(
                Environment.GetEnvironmentVariable("TSHARPVISION_SDLGPU_CONTINUOUS")
                ?? cfg?.Continuous,
                defaultValue: false),
            Backend               = NormalizeBackend(
                Environment.GetEnvironmentVariable("TSHARPVISION_SDLGPU_BACKEND")
                ?? cfg?.Backend),
            AllowedFramesInFlight = ParseAllowedFrames(
                Environment.GetEnvironmentVariable("TSHARPVISION_SDLGPU_ALLOWED_FRAMES")
                ?? cfg?.AllowedFrames),
            PresentMode           =
                ParsePresentMode(
                    Environment.GetEnvironmentVariable("TSHARPVISION_SDLGPU_PRESENT_MODE")
                    ?? cfg?.PresentMode)
                ?? SDL3.SDL.GPUPresentMode.Immediate,
        };
    }

    internal static string? NormalizeBackend(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.ToLowerInvariant().Trim();
    }

    internal static uint? ParseAllowedFrames(string? value)
    {
        if (value is null) return null;
        if (uint.TryParse(value, out uint frames) && frames >= 1 && frames <= 3)
            return frames;
        return null;
    }

    internal static SDL3.SDL.GPUPresentMode? ParsePresentMode(string? value)
    {
        if (value is null) return null;
        return value.ToLowerInvariant().Trim() switch
        {
            "vsync"     => SDL3.SDL.GPUPresentMode.VSync,
            "immediate" => SDL3.SDL.GPUPresentMode.Immediate,
            "mailbox"   => SDL3.SDL.GPUPresentMode.Mailbox,
            _           => null,
        };
    }

    internal static bool ParseBool(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "on" or "true"   => true,
            "0" or "off" or "false" => false,
            _ => defaultValue,
        };
    }
}
