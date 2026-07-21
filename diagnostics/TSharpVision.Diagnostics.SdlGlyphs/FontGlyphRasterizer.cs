using SDL3;

namespace TSharpVision.Diagnostics.SdlGlyphs;

/// <summary>
/// Rasterises a glyph from the loaded TTF face into a cell-sized alpha mask, reproducing the
/// placement <c>SDLRenderer.CreateFontCellSurface</c> uses today
/// (<c>srcClipTop = max(0, maxY - ascent)</c>, <c>dstX = min(0, minX)</c>, <c>dstY = 0</c>).
/// <para>
/// This exists purely for <b>comparison</b>: the diagnostic renders characters outside
/// CP437 <c>B0</c>–<c>DF</c> (scrollbar arrows, the thumb) the way the current driver does, and
/// reports what the font produces for the graphics range it is about to stop using. It is never
/// used to produce a generated glyph.
/// </para>
/// </summary>
internal sealed class FontGlyphRasterizer
{
    private readonly IntPtr _font;
    private readonly int    _ascent;
    private readonly int    _cellWidth;
    private readonly int    _cellHeight;
    private readonly Dictionary<char, byte[]?> _cache = new();

    public FontGlyphRasterizer(IntPtr font, int ascent, int cellWidth, int cellHeight)
    {
        _font       = font;
        _ascent     = ascent;
        _cellWidth  = cellWidth;
        _cellHeight = cellHeight;
    }

    /// <summary>Cell-sized alpha mask, or <c>null</c> when the face cannot render the glyph.</summary>
    public byte[]? GetAlpha(char ch)
    {
        if (_cache.TryGetValue(ch, out byte[]? cached))
            return cached;

        byte[]? alpha = Rasterize(ch);
        _cache[ch] = alpha;
        return alpha;
    }

    /// <summary>Raw measurements of the face's own raster for one glyph, for font-info.txt.</summary>
    public FontGlyphMeasurement Measure(char ch)
    {
        bool haveMetrics = SDL3.TTF.GetGlyphMetrics(
            _font, ch, out int minX, out int maxX, out int minY, out int maxY, out int advance);

        int surfW = 0, surfH = 0;
        int inkTop = -1, inkBottom = -1, inkLeft = -1, inkRight = -1;

        IntPtr surf = SDL3.TTF.RenderGlyphBlended(_font, ch, White);
        if (surf != IntPtr.Zero)
        {
            try
            {
                SDL.GetSurfaceClipRect(surf, out SDL.Rect r);
                surfW = r.W;
                surfH = r.H;
            }
            finally
            {
                SDL.DestroySurface(surf);
            }
        }

        byte[]? cell = GetAlpha(ch);
        if (cell != null)
        {
            for (int y = 0; y < _cellHeight; y++)
                for (int x = 0; x < _cellWidth; x++)
                {
                    if (cell[y * _cellWidth + x] == 0) continue;
                    if (inkTop    < 0 || y < inkTop)    inkTop    = y;
                    if (inkBottom < 0 || y > inkBottom) inkBottom = y;
                    if (inkLeft   < 0 || x < inkLeft)   inkLeft   = x;
                    if (inkRight  < 0 || x > inkRight)  inkRight  = x;
                }
        }

        return new FontGlyphMeasurement(
            ch,
            haveMetrics ? minX : 0, haveMetrics ? maxX : 0,
            haveMetrics ? minY : 0, haveMetrics ? maxY : 0,
            haveMetrics ? advance : 0,
            surfW, surfH,
            inkTop, inkBottom, inkLeft, inkRight);
    }

    private byte[]? Rasterize(char ch)
    {
        IntPtr surf = SDL3.TTF.RenderGlyphBlended(_font, ch, White);
        if (surf == IntPtr.Zero) return null;

        try
        {
            SDL.GetSurfaceClipRect(surf, out SDL.Rect r);
            int sw = r.W;
            int sh = r.H;
            if (sw <= 0 || sh <= 0) return null;

            int srcClipTop = 0;
            int dstX       = 0;
            if (SDL3.TTF.GetGlyphMetrics(_font, ch, out int minX, out _, out _, out int maxY, out _))
            {
                srcClipTop = Math.Max(0, maxY - _ascent);
                dstX       = Math.Min(0, minX);
            }

            var alpha = new byte[_cellWidth * _cellHeight];

            for (int y = 0; y < _cellHeight; y++)
            {
                int sy = y + srcClipTop;
                if ((uint)sy >= (uint)sh) continue;

                for (int x = 0; x < _cellWidth; x++)
                {
                    int sx = x - dstX;   // dstX <= 0, so this shifts the source right
                    if ((uint)sx >= (uint)sw) continue;

                    SDL.ReadSurfacePixel(surf, sx, sy, out _, out _, out _, out byte a);
                    alpha[y * _cellWidth + x] = a;
                }
            }

            return alpha;
        }
        finally
        {
            SDL.DestroySurface(surf);
        }
    }

    private static SDL.Color White => new() { R = 255, G = 255, B = 255, A = 255 };
}

/// <summary>Measurements of one font-rasterised glyph, as reported in <c>font-info.txt</c>.</summary>
internal readonly record struct FontGlyphMeasurement(
    char Character,
    int MinX, int MaxX, int MinY, int MaxY, int Advance,
    int SurfaceWidth, int SurfaceHeight,
    int CellInkTop, int CellInkBottom, int CellInkLeft, int CellInkRight);
