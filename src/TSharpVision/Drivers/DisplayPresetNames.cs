namespace TSharpVision.Drivers;

/// <summary>Canonical ids of common physical palettes (<see cref="IPhysicalPaletteDriver"/>:
/// <see cref="Default"/>, <see cref="Green"/>, <see cref="Amber"/>, <see cref="Grayscale"/>) and
/// display effects (<see cref="IDisplayEffectDriver"/>: <see cref="Default"/>, <see cref="Scanlines"/>,
/// <see cref="Glow"/>, <see cref="Curvature"/>, <see cref="ShadowMask"/>).</summary>
/// <remarks>A driver supports any subset and may add its own ids; check the list it reports.
/// Matching is case-insensitive with surrounding whitespace ignored. One effect is active at a time.</remarks>
public static class DisplayPresetNames
{
    /// <summary>The driver's unmodified output; always supported and listed first. For the SDL GPU
    /// driver's physical palette this is the classic VGA 16-colour mapping.</summary>
    public const string Default = "Default";

    /// <summary>Green phosphor monochrome monitor.</summary>
    public const string Green = "Green";

    /// <summary>Amber phosphor monochrome monitor.</summary>
    public const string Amber = "Amber";

    /// <summary>Grayscale rendition of the output colours.</summary>
    public const string Grayscale = "Grayscale";

    /// <summary>CRT-style horizontal scanlines effect.</summary>
    public const string Scanlines = "Scanlines";

    /// <summary>Phosphor glow effect: a soft halo around bright text.</summary>
    public const string Glow = "Glow";

    /// <summary>CRT curvature effect: a barrel warp of the screen image.</summary>
    public const string Curvature = "Curvature";

    /// <summary>CRT shadow mask effect: a fine phosphor mask pattern over the screen.</summary>
    public const string ShadowMask = "ShadowMask";
}
