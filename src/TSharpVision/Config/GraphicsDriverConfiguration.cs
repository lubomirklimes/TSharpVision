namespace TSharpVision.Config;

/// <summary>
/// Typed representation of the <c>[graphics]</c> section in a .cfg file.
/// Contains font settings shared by all graphical screen drivers (SDL, future OpenGL, …).
/// Null values mean "not set" — the driver falls back to its own defaults.
/// </summary>
public sealed class GraphicsDriverConfiguration
{
    /// <summary>Font family name, e.g. "Cascadia Mono" or "consola.ttf".</summary>
    public string? FontName { get; init; }

    /// <summary>Font point size. Null preserves the driver's default.</summary>
    public int? FontSize { get; init; }
}
