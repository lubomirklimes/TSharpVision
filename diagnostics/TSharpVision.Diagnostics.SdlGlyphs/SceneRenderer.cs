using TSharpVision.Drivers.SDL;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;
using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Diagnostics.SdlGlyphs;

/// <summary>
/// Rasterises a <see cref="TextGrid"/> to an <see cref="ArgbBitmap"/> on an exact cell grid,
/// mirroring what a renderer does per cell: fill the background, then blend the glyph's alpha
/// in the foreground colour.
/// <para>
/// Glyph alpha for CP437 <c>B0</c>–<c>DF</c> comes from the production
/// <see cref="CellGlyphBitmapCache"/> / <see cref="Cp437GlyphGenerator"/>, at the lattice phase
/// implied by the cell's grid position. Everything else optionally comes from the TTF face via
/// <see cref="FontGlyphRasterizer"/> so that a scene can show generated and font glyphs together.
/// </para>
/// </summary>
internal sealed class SceneRenderer
{
    private readonly CellGlyphBitmapCache  _generated;
    private readonly FontGlyphRasterizer?  _font;

    public SceneRenderer(CellGlyphBitmapCache generated, FontGlyphRasterizer? font)
    {
        _generated = generated;
        _font      = font;
    }

    public int CellWidth  => _generated.CellWidth;
    public int CellHeight => _generated.CellHeight;

    /// <summary>Characters that had to be skipped because nothing could render them.</summary>
    public SortedSet<char> Skipped { get; } = new();

    /// <summary>
    /// Renders <paramref name="grid"/>.
    /// </summary>
    /// <param name="includeFontGlyphs">
    /// When <c>false</c>, characters outside CP437 <c>B0</c>–<c>DF</c> are left as background.
    /// Used to produce the "generated glyphs only" half of the scrollbar diagnostic.
    /// </param>
    public ArgbBitmap Render(TextGrid grid, bool includeFontGlyphs = true)
    {
        var bitmap = new ArgbBitmap(grid.Columns * CellWidth, grid.Rows * CellHeight);

        for (int row = 0; row < grid.Rows; row++)
        {
            for (int col = 0; col < grid.Columns; col++)
            {
                SceneCell cell = grid[col, row];

                int px = col * CellWidth;
                int py = row * CellHeight;

                bitmap.FillRect(px, py, CellWidth, CellHeight, SdlPalette.Vga16[cell.Background & 0x0F]);

                if (cell.Character is ' ' or '\0')
                    continue;

                uint fg = SdlPalette.Vga16[cell.Foreground & 0x0F];

                GlyphPhase phase = GlyphPhase.ForCell(col, row, CellWidth, CellHeight);

                if (_generated.TryGet(cell.Character, phase, out AlphaBitmap glyph))
                {
                    for (int y = 0; y < CellHeight; y++)
                        for (int x = 0; x < CellWidth; x++)
                            bitmap.Blend(px + x, py + y, fg, glyph[x, y]);
                    continue;
                }

                if (!includeFontGlyphs)
                    continue;

                byte[]? alpha = _font?.GetAlpha(cell.Character);
                if (alpha == null)
                {
                    Skipped.Add(cell.Character);
                    continue;
                }

                for (int y = 0; y < CellHeight; y++)
                    for (int x = 0; x < CellWidth; x++)
                        bitmap.Blend(px + x, py + y, fg, alpha[y * CellWidth + x]);
            }
        }

        return bitmap;
    }

    /// <summary>Stacks bitmaps vertically with a separator band, left-aligned.</summary>
    public static ArgbBitmap StackVertically(IReadOnlyList<ArgbBitmap> parts, int gap, uint gapColor)
    {
        int width  = parts.Max(static p => p.Width);
        int height = parts.Sum(static p => p.Height) + gap * (parts.Count - 1);

        var result = new ArgbBitmap(width, height, gapColor);

        int y = 0;
        foreach (ArgbBitmap part in parts)
        {
            result.Blit(part, 0, y);
            y += part.Height + gap;
        }

        return result;
    }
}
