using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Drivers.SDL.Renderer;

/// <summary>Which texture-production path a character takes in <see cref="SDLRenderer"/>.</summary>
internal enum SdlGlyphRenderClass
{
    /// <summary>Space or NUL — background only, no glyph texture.</summary>
    Space,

    /// <summary>CP437 B0–DF, drawn from cell-derived geometry rather than the font.</summary>
    Generated,

    /// <summary>Box drawing outside B0–DF — SDL_ttf glyph fitted to the cell.</summary>
    FontCell,

    /// <summary>Normal text — natural SDL_ttf glyph.</summary>
    Natural,
}

/// <summary>
/// Decides which texture path a character takes. Kept separate from <see cref="SDLRenderer"/> so
/// the decision can be tested without an SDL handle, font or window.
/// </summary>
internal static class SdlGlyphRenderPolicy
{
    internal static SdlGlyphRenderClass Classify(char ch)
    {
        if (ch is ' ' or '\0')
            return SdlGlyphRenderClass.Space;

        if (Cp437GlyphGenerator.IsGenerated(ch))
            return SdlGlyphRenderClass.Generated;

        // Box drawing outside B0–DF still comes from the font, fitted to the cell rather than
        // stretched, so neighbouring cells line up as well as the face allows.
        if ((uint)ch is >= 0x2500 and <= 0x257F)
            return SdlGlyphRenderClass.FontCell;

        return SdlGlyphRenderClass.Natural;
    }
}

/// <summary>
/// Cache key for <see cref="SDLRenderer"/>'s uploaded SDL textures.
/// <para>
/// Background colour is excluded: glyph textures carry foreground ink on a transparent
/// background and the background is painted separately per cell.
/// </para>
/// <para>
/// The lattice phase is included because <see cref="ShadingMode.PhasedDither"/> produces a
/// different bitmap per phase for the same character. <see cref="Create"/> normalises it to zero
/// for every glyph that cannot depend on it, so only B0/B1/B2 ever occupy more than one entry per
/// (codepoint, colour) pair. Cell size is not part of the key — it is fixed for the lifetime of
/// an <see cref="SDLRenderer"/>.
/// </para>
/// </summary>
internal readonly struct SdlGlyphTextureKey : IEquatable<SdlGlyphTextureKey>
{
    public readonly uint Codepoint;
    public readonly uint FgColor;  // 0xAARRGGBB
    public readonly int  PhaseX;
    public readonly int  PhaseY;

    public SdlGlyphTextureKey(uint codepoint, uint fgColor, int phaseX, int phaseY)
    {
        Codepoint = codepoint;
        FgColor   = fgColor;
        PhaseX    = phaseX;
        PhaseY    = phaseY;
    }

    public static SdlGlyphTextureKey Create(
        char ch, uint fgColor, GlyphPhase phase, Cp437GlyphGenerator generator)
    {
        GlyphPhase effective = generator.RequiresPhase(ch) ? phase.Normalized() : GlyphPhase.Zero;
        return new SdlGlyphTextureKey((uint)ch, fgColor, effective.X, effective.Y);
    }

    public bool Equals(SdlGlyphTextureKey other) =>
        Codepoint == other.Codepoint && FgColor == other.FgColor &&
        PhaseX == other.PhaseX && PhaseY == other.PhaseY;

    public override bool Equals(object? obj) => obj is SdlGlyphTextureKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Codepoint, FgColor, PhaseX, PhaseY);
}
