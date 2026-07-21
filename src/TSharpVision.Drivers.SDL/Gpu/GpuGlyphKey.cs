using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>
/// Identity of one glyph slot in <see cref="TerminalAtlas"/> and in the GPU renderer's managed
/// alpha cache.
/// <para>
/// Under <see cref="ShadingMode.PhasedDither"/> the same character produces a different bitmap
/// depending on where its cell sits in the shading lattice, so a shading glyph at phase (0,0) and
/// at phase (1,0) are different images that must not share a slot. Phase is normalised to (0,0)
/// for every glyph that cannot depend on it, so everything else keeps one slot per codepoint.
/// </para>
/// </summary>
/// <param name="Codepoint">UTF-16 code unit of the character.</param>
/// <param name="PhaseX">Shading-lattice phase on the X axis, or 0.</param>
/// <param name="PhaseY">Shading-lattice phase on the Y axis, or 0.</param>
internal readonly record struct GpuGlyphKey(uint Codepoint, int PhaseX, int PhaseY)
{
    /// <summary>The character this key refers to.</summary>
    internal char Character => (char)Codepoint;

    /// <summary>The lattice phase this key refers to.</summary>
    internal GlyphPhase Phase => new(PhaseX, PhaseY);

    /// <summary>Key for a glyph that does not depend on the shading lattice.</summary>
    internal static GpuGlyphKey PhaseIndependent(char ch) => new((uint)ch, 0, 0);

    public override string ToString() =>
        $"U+{Codepoint:X4}@({PhaseX},{PhaseY})";
}

/// <summary>
/// Supplies cell-sized glyph alpha masks to the GPU pipeline and atlas.
/// <para>
/// Exists so that <see cref="TerminalGpuPipeline"/> and <see cref="TerminalAtlas"/> never need to
/// know which characters are phase-dependent: they ask the source to build the key (which applies
/// normalisation) and then to produce the bytes for it. That knowledge stays in
/// <see cref="SDLGpuRenderer"/>, which owns the generator.
/// </para>
/// </summary>
internal interface IGpuGlyphSource
{
    /// <summary>
    /// Builds the atlas/cache key for <paramref name="ch"/> at the given cell
    /// <paramref name="phase"/>, normalising the phase away when the glyph cannot depend on it.
    /// </summary>
    GpuGlyphKey CreateKey(char ch, GlyphPhase phase);

    /// <summary>
    /// Produces the cell-sized alpha mask for <paramref name="key"/>, or <c>null</c> when the
    /// glyph cannot be produced at all. The result must be exactly
    /// <c>cellWidth * cellHeight</c> bytes; <see cref="TerminalAtlas"/> enforces this.
    /// </summary>
    byte[]? GetAlpha(GpuGlyphKey key);
}
