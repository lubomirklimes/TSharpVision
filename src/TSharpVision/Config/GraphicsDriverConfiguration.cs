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
    /// Physical palette id (<c>[graphics] palette</c>), e.g. "Amber". Ids are driver-defined
    /// (see <see cref="Drivers.DisplayPresetNames"/>) and matched case-insensitively by the driver.
    /// Null means "not set", i.e. <see cref="Drivers.DisplayPresetNames.Default"/>. Ignored by drivers
    /// that do not implement <see cref="Drivers.IPhysicalPaletteDriver"/>.
    /// </summary>
    public string? Palette { get; init; }

    /// <summary>
    /// Display effect id (<c>[graphics] effect</c>), e.g. "Scanlines". Ids are driver-defined
    /// (see <see cref="Drivers.DisplayPresetNames"/>) and matched case-insensitively by the driver.
    /// Null means "not set", i.e. <see cref="Drivers.DisplayPresetNames.Default"/>. Ignored by drivers
    /// that do not implement <see cref="Drivers.IDisplayEffectDriver"/>.
    /// </summary>
    public string? Effect { get; init; }

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
