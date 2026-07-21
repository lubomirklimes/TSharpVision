namespace TSharpVision.Config;

/// <summary>
/// Typed representation of the <c>[driver]</c> section in a .cfg file.
/// </summary>
public sealed class DriverConfiguration
{
    /// <summary>Driver to use: "sdl", "console", or a driver class name. Null = auto.</summary>
    public string? Name { get; init; }
}
