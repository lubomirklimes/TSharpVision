using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;
using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Diagnostics.SdlGlyphs;

/// <summary>
/// Renders the labelled <c>cp437-all-b0-df</c> overview: all 48 generated glyphs on an 8×6 grid,
/// each in a tile that shows its exact cell bounds and its CP437 code.
/// <para>
/// The glyph occupies exactly <c>cellWidth × cellHeight</c> pixels. The thin frame sits one pixel
/// outside that rectangle with a one-pixel gap, and the hex label is drawn below the cell, so
/// neither ever overlaps the glyph under test.
/// </para>
/// </summary>
internal static class OverviewRenderer
{
    private const int Columns = 8;
    private const int Rows    = 6;

    private const uint Background = 0xFF101010;
    private const uint FrameColor = 0xFF404060;
    private const uint LabelColor = 0xFF9090C0;
    private const uint GlyphColor = 0xFFFFFFFF;

    public static ArgbBitmap Render(CellGlyphBitmapCache cache)
    {
        int cw = cache.CellWidth;
        int ch = cache.CellHeight;

        const int Pad       = 4;   // space around the cell inside a tile
        const int LabelGap  = 2;   // pixels between cell bottom and the label

        int tileW = cw + 2 * Pad;
        int tileH = ch + 2 * Pad + LabelGap + TinyHexFont.GlyphHeight;

        var bitmap = new ArgbBitmap(Columns * tileW, Rows * tileH, Background);

        IReadOnlyList<Cp437GraphicsCharacter> all = Cp437GraphicsCharacters.All;

        for (int i = 0; i < all.Count; i++)
        {
            Cp437GraphicsCharacter entry = all[i];

            int tileX = (i % Columns) * tileW;
            int tileY = (i / Columns) * tileH;

            int cellX = tileX + Pad;
            int cellY = tileY + Pad;

            // Frame: one pixel outside the cell, with a one-pixel gap so it cannot be mistaken
            // for glyph ink or hide a missing edge pixel.
            DrawFrame(bitmap, cellX - 2, cellY - 2, cw + 4, ch + 4, FrameColor);

            // The glyph itself is generated at phase (0,0): the overview shows one isolated cell,
            // and the tiled variant is what exercises phase continuity.
            if (cache.TryGet(entry.Character, GlyphPhase.Zero, out AlphaBitmap glyph))
            {
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                        bitmap.Blend(cellX + x, cellY + y, GlyphColor, glyph[x, y]);
            }

            string label  = entry.Code.ToString("X2");
            int labelW    = TinyHexFont.MeasureWidth(label);
            int labelX    = cellX + (cw - labelW) / 2;
            int labelY    = cellY + ch + LabelGap + 1;
            TinyHexFont.Draw(bitmap, labelX, labelY, label, LabelColor);
        }

        return bitmap;
    }

    private static void DrawFrame(ArgbBitmap b, int x, int y, int w, int h, uint color)
    {
        for (int i = 0; i < w; i++)
        {
            b[x + i, y]         = color;
            b[x + i, y + h - 1] = color;
        }

        for (int i = 0; i < h; i++)
        {
            b[x,         y + i] = color;
            b[x + w - 1, y + i] = color;
        }
    }
}
