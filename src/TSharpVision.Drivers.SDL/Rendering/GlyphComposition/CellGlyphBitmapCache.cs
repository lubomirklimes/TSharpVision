using System.Collections.Concurrent;
using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

/// <summary>
/// Thread-safe memoisation of <see cref="Cp437GlyphGenerator"/> output.
/// <para>
/// Holds only managed <see cref="AlphaBitmap"/> instances — no SDL textures, no atlas slots, no
/// unmanaged memory — so it is safe to share between the SDL_Renderer and SDL_GPU back-ends
/// later. Each back-end keeps its own upload cache (texture handle / atlas slot) on top.
/// </para>
/// <para>
/// The key includes everything that can change the pixels:
/// codepoint, cell width, cell height, shading mode and lattice phase. Phase is part of the key
/// because a phase-correct dither genuinely differs between cells; it contributes at most
/// <see cref="GlyphPhase.Combinations"/> entries and only for the three shading characters —
/// every other glyph is stored once, at phase (0, 0).
/// </para>
/// <para>
/// Cached bitmaps are handed out by reference and must be treated as read-only.
/// </para>
/// </summary>
internal sealed class CellGlyphBitmapCache
{
    private readonly Cp437GlyphGenerator _generator;
    private readonly ConcurrentDictionary<Key, AlphaBitmap> _entries = new();

    public CellGlyphBitmapCache(Cp437GlyphGenerator generator) =>
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));

    public CellGlyphBitmapCache(int cellWidth, int cellHeight, Cp437GlyphGeneratorOptions? options = null)
        : this(new Cp437GlyphGenerator(cellWidth, cellHeight, options)) { }

    public Cp437GlyphGenerator Generator => _generator;

    public int CellWidth  => _generator.CellWidth;
    public int CellHeight => _generator.CellHeight;

    /// <summary>Number of distinct bitmaps currently held.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Returns the cached bitmap for <paramref name="ch"/> at phase (0, 0).
    /// Do not use for shading characters while
    /// <see cref="Cp437GlyphGeneratorOptions.ShadingMode"/> is
    /// <see cref="ShadingMode.PhasedDither"/> — pass the real cell phase instead.
    /// </summary>
    public bool TryGet(char ch, out AlphaBitmap bitmap) =>
        TryGet(ch, GlyphPhase.Zero, out bitmap);

    /// <summary>
    /// Returns the cached bitmap for <paramref name="ch"/> at <paramref name="phase"/>,
    /// generating it on first use.
    /// </summary>
    public bool TryGet(char ch, GlyphPhase phase, out AlphaBitmap bitmap)
    {
        if (!Cp437GlyphGenerator.IsGenerated(ch))
        {
            bitmap = null!;
            return false;
        }

        // Collapse the phase for glyphs that cannot depend on it, so ▓ in Uniform mode and every
        // box/block glyph occupy exactly one cache slot.
        GlyphPhase effective = _generator.RequiresPhase(ch) ? phase.Normalized() : GlyphPhase.Zero;

        var key = new Key(ch, _generator.CellWidth, _generator.CellHeight,
                          _generator.Options.ShadingMode, effective.X, effective.Y);

        bitmap = _entries.GetOrAdd(key, static (k, gen) => gen.Generate(k.Character, new GlyphPhase(k.PhaseX, k.PhaseY)), _generator);
        return true;
    }

    /// <summary>Drops every cached bitmap.</summary>
    public void Clear() => _entries.Clear();

    private readonly record struct Key(
        char        Character,
        int         CellWidth,
        int         CellHeight,
        ShadingMode ShadingMode,
        int         PhaseX,
        int         PhaseY);
}
