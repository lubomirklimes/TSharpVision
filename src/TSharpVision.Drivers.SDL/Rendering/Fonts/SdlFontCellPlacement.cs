namespace TSharpVision.Drivers.SDL.Rendering.Fonts;

/// <summary>
/// Places an SDL_ttf-rasterised glyph surface inside a terminal cell canvas.
/// <para>
/// Shared by both back-ends so a font glyph sits in the same place in its cell either way.
/// Applies only to non-generated glyphs — <c>B0</c>–<c>DF</c> bitmaps are already cell-sized.
/// </para>
/// </summary>
internal static class SdlFontCellPlacement
{
    /// <summary>
    /// Computes where the rasterised glyph surface should be sampled from and drawn to.
    /// </summary>
    /// <param name="font">Open SDL_ttf font handle.</param>
    /// <param name="codepoint">Glyph to place.</param>
    /// <param name="ascent">Font ascent, from <c>TTF_GetFontAscent</c>.</param>
    /// <param name="srcClipTop">
    /// Rows to skip at the top of the glyph surface. SDL_ttf grows the surface upward for glyphs
    /// that exceed the ascent; trimming that overshoot is what leaves exactly cell-height rows.
    /// </param>
    /// <param name="dstX">
    /// Horizontal destination offset, never positive: a negative left side bearing is corrected by
    /// shifting the blit left so the ink lands inside the cell.
    /// </param>
    public static void Compute(
        IntPtr font, uint codepoint, int ascent, out int srcClipTop, out int dstX)
    {
        srcClipTop = 0;
        dstX       = 0;

        if (SDL3.TTF.GetGlyphMetrics(font, codepoint,
                out int minX, out _, out _, out int maxY, out _))
        {
            srcClipTop = Math.Max(0, maxY - ascent);
            dstX       = Math.Min(0, minX);
        }
    }

    /// <summary>
    /// Blits <paramref name="glyphSurf"/> onto <paramref name="canvas"/> using the offsets from
    /// <see cref="Compute"/>. Clipping is applied via a source rect so the glyph's baseline lands
    /// on the cell grid.
    /// </summary>
    public static void BlitToCanvas(
        IntPtr glyphSurf, IntPtr canvas, int srcClipTop, int dstX, int dstY)
    {
        SDL3.SDL.GetSurfaceClipRect(glyphSurf, out SDL3.SDL.Rect glyphClip);

        if (srcClipTop > 0)
        {
            var srcRect = new SDL3.SDL.Rect
            {
                X = 0,
                Y = srcClipTop,
                W = glyphClip.W,
                H = Math.Max(0, glyphClip.H - srcClipTop),
            };
            var dst = new SDL3.SDL.Rect { X = dstX, Y = dstY };
            SDL3.SDL.BlitSurface(glyphSurf, in srcRect, canvas, in dst);
        }
        else
        {
            var dst = new SDL3.SDL.Rect { X = dstX, Y = dstY };
            SDL3.SDL.BlitSurface(glyphSurf, IntPtr.Zero, canvas, in dst);
        }
    }
}
