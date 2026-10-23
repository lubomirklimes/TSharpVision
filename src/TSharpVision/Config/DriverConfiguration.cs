namespace TSharpVision.Config;

/// <summary>
/// Typed representation of the <c>[driver]</c> section in a .cfg file.
/// </summary>
public sealed class DriverConfiguration
{
    /// <summary>Driver to use: the alias "sdl", "console" or "terminal", or an exact driver name such as "SDLDriver". Null = auto.</summary>
    public string? Name { get; init; }
}
