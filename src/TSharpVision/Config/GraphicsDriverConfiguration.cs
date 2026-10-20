namespace TSharpVision.Config;

/// <summary>
/// Typed representation of the <c>[graphics]</c> section in a .cfg file.
/// Contains font settings shared by all graphical screen drivers (SDL, future OpenGL, …).
/// Null values mean "not set" — the driver falls back to its own defaults.
/// </summary>
public sealed class GraphicsDriverConfiguration
{
    private FontFitMode? _fontFit;

    /// <summary>Font family name, e.g. "Cascadia Mono" or "consola.ttf".</summary>
    public string? FontName { get; init; }

    /// <summary>Font point size. Null preserves the driver's default.</summary>
    public int? FontSize { get; init; }

    /// <summary>
    /// How text glyphs are fitted into the character cell (<c>fontFit</c>).
    /// Defaults to <see cref="FontFitMode.Condense"/> when not set.
    /// </summary>
    public FontFitMode FontFit
    {
        get => _fontFit ?? FontFitMode.Condense;
        init => _fontFit = value;
    }

    /// <summary>
    /// The <see cref="FontFit"/> value when it was set explicitly (by a .cfg file or by code), else null.
    /// Lets a higher-priority configuration layer that does not mention <c>fontFit</c> keep the lower one's.
    /// </summary>
    internal FontFitMode? ExplicitFontFit
    {
        get => _fontFit;
        init => _fontFit = value;
    }
}
