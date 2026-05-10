namespace TSharpVision.Config;

/// <summary>
/// Minimal configuration model loaded from a .cfg file.
/// Null values mean "not set" — use existing default behavior.
/// </summary>
public sealed class TSharpVisionConfiguration
{
    /// <summary>
    /// Driver to use: "sdl", "console", or a driver class name.
    /// Null preserves automatic driver selection.
    /// </summary>
    public string? DriverName { get; init; }

    /// <summary>
    /// Font family name passed to the SDL driver (e.g. "Cascadia Mono").
    /// Null preserves the SDL driver's default font probing.
    /// </summary>
    public string? SdlFontName { get; init; }

    /// <summary>
    /// Two-letter localization language code, e.g. "cs" or "en".
    /// Null preserves the built-in English fallback behavior.
    /// </summary>
    public string? Language { get; init; }
}
