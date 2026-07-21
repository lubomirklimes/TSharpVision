using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;
using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Diagnostics.SdlGlyphs;

/// <summary>
/// Tiles generated CP437 glyphs into a single <see cref="AlphaBitmap"/> on an exact cell grid.
/// <para>
/// Accepts every character <see cref="Cp437GlyphGenerator"/> can produce, plus spaces for empty
/// cells. Any other character is left blank and reported through <see cref="ComposeScene"/>'s
/// <c>unsupported</c> output rather than throwing, so a scene may legitimately contain glyphs
/// that come from the font.
/// </para>
/// <para>
/// Each cell is generated at its own lattice <see cref="GlyphPhase"/>, derived from the cell's
/// grid position, so a phase-correct dither continues unbroken across the whole scene exactly as
/// it would on screen.
/// </para>
/// </summary>
internal sealed class CellSceneComposer
{
    private readonly CellGlyphBitmapCache _cache;

    internal CellSceneComposer(CellGlyphBitmapCache cache) =>
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    internal CellSceneComposer(Cp437GlyphGenerator generator)
        : this(new CellGlyphBitmapCache(generator)) { }

    internal int CellWidth  => _cache.CellWidth;
    internal int CellHeight => _cache.CellHeight;

    /// <summary>
    /// Composes <paramref name="rows"/> into a bitmap of
    /// <c>maxRowLength * CellWidth</c> × <c>rows.Length * CellHeight</c> pixels.
    /// Rows may have different lengths; short rows are padded with blank cells.
    /// </summary>
    /// <param name="rows">Scene text; one character per cell.</param>
    /// <param name="originColumn">
    /// Grid column of the scene's left edge, used only to derive the dither phase. Pass a
    /// non-zero value when the scene is a window onto a larger surface.
    /// </param>
    /// <param name="originRow">Grid row of the scene's top edge; see <paramref name="originColumn"/>.</param>
    /// <param name="unsupported">Characters encountered that the generator cannot produce.</param>
    internal AlphaBitmap ComposeScene(
        IReadOnlyList<string> rows,
        int originColumn,
        int originRow,
        out IReadOnlyCollection<char> unsupported)
    {
        if (rows.Count == 0)
            throw new ArgumentException("rows must not be empty.", nameof(rows));

        int cols = 0;
        foreach (string row in rows)
            cols = Math.Max(cols, row.Length);

        if (cols == 0)
            throw new ArgumentException("rows must contain at least one cell.", nameof(rows));

        var scene   = AlphaBitmap.CreateEmpty(cols * CellWidth, rows.Count * CellHeight);
        var missing = new SortedSet<char>();

        for (int r = 0; r < rows.Count; r++)
        {
            string row = rows[r];
            for (int c = 0; c < row.Length; c++)
            {
                char ch = row[c];
                if (ch == ' ' || ch == '\0')
                    continue;

                GlyphPhase phase = GlyphPhase.ForCell(
                    originColumn + c, originRow + r, CellWidth, CellHeight);

                if (!_cache.TryGet(ch, phase, out AlphaBitmap glyph))
                {
                    missing.Add(ch);
                    continue;
                }

                AlphaBitmapDrawing.Blit(glyph, scene, c * CellWidth, r * CellHeight);
            }
        }

        unsupported = missing;
        return scene;
    }

    /// <summary>Overload for scenes anchored at grid origin (0, 0).</summary>
    internal AlphaBitmap ComposeScene(IReadOnlyList<string> rows) =>
        ComposeScene(rows, 0, 0, out _);

    /// <summary>Composes a solid rectangle of one repeated character.</summary>
    internal AlphaBitmap ComposeFill(char ch, int columns, int rows)
    {
        var text = new string[rows];
        for (int r = 0; r < rows; r++)
            text[r] = new string(ch, columns);

        return ComposeScene(text, 0, 0, out _);
    }
}
