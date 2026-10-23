namespace TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

/// <summary>
/// Options for <see cref="Cp437GlyphGenerator"/>. Both renderers use <see cref="Default"/>;
/// <see cref="UniformShading"/> exists so the glyph diagnostics can render the two shading
/// styles side by side.
/// </summary>
internal sealed record Cp437GlyphGeneratorOptions
{
    /// <summary>Phase-correct DOS dither — what both renderers use.</summary>
    public static readonly Cp437GlyphGeneratorOptions Default = new();

    /// <summary>Flat partial coverage. Diagnostic comparison only.</summary>
    public static readonly Cp437GlyphGeneratorOptions UniformShading =
        new() { ShadingMode = ShadingMode.Uniform };

    /// <summary>How B0/B1/B2 are rendered.</summary>
    public ShadingMode ShadingMode { get; init; } = ShadingMode.PhasedDither;

    /// <summary>
    /// Size of one dither dot in cell pixels under <see cref="ShadingMode.PhasedDither"/>. 1 at 1×;
    /// the renderers pass the integral pixel density on a HiDPI drawable so the pattern keeps its
    /// 1× dot pitch. Ignored (treated as 1) when the cell is not divisible by it.
    /// </summary>
    public int ShadingDotSize { get; init; } = 1;
}
