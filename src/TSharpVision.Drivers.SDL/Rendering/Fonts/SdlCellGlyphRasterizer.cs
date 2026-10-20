using TSharpVision.Config;
using System.Runtime.InteropServices;

namespace TSharpVision.Drivers.SDL.Rendering.Fonts;

/// <summary>
/// Turns an SDL_ttf glyph into a cell-sized alpha mask by the rules of
/// <see cref="SdlFontCellPlacement"/>. Shared by <c>SDLRenderer</c> (which tints the mask into a
/// texture) and <c>SDLGpuRenderer</c> (which uploads it to the atlas, or composites it on the CPU),
/// so both back-ends draw identical font glyphs. CP437 B0–DF never come here: they are generated.
/// <para>
/// Borrows the renderer's open font. Glyphs that do not fit the cell are re-rasterised through a
/// second handle of the same font file (plus the symbol fallback), resized with
/// <c>TTF_SetFontSizeDPI</c>, so the primary font's size and glyph cache are never disturbed.
/// </para>
/// <para>
/// With <see cref="FontFitMode.Uniform"/> all text comes from a third handle at the common size
/// (<see cref="UniformPointSize"/>), measured once from the sizing repertoire when the rasteriser is created. The
/// mode is fixed for the rasteriser's lifetime; each renderer owns one, so masks of different
/// modes never share a cache.
/// </para>
/// </summary>
internal sealed class SdlCellGlyphRasterizer : IDisposable
{
    private static readonly SDL3.SDL.Color White = new() { R = 255, G = 255, B = 255, A = 255 };

    private readonly IntPtr  _font;
    private readonly string  _fontPath;
    private readonly string? _symbolFontPath;
    private readonly float   _pointSize;

    private IntPtr _fitFont;
    private IntPtr _fitSymbolFont;
    private bool   _fitFontUnavailable;
    private int    _textBaseline = -1;

    // fontFit=Uniform: the text font at the common size (IntPtr.Zero while the configured size is
    // kept, then _font draws the text), measured lazily.
    private IntPtr _uniformFont;
    private IntPtr _uniformSymbolFont;
    private bool   _uniformMeasured;
    private float  _uniformPointSize;
    private int    _uniformPen;
    private readonly List<string> _uniformRejected = new();

    public SdlCellGlyphRasterizer(
        IntPtr font, string fontPath, float pointSize, string? symbolFontPath, int cellWidth, int cellHeight,
        FontFitMode fontFit = FontFitMode.Condense)
    {
        _font           = font;
        _fontPath       = fontPath;
        _pointSize      = pointSize;
        _symbolFontPath = symbolFontPath;
        CellWidth       = cellWidth;
        CellHeight      = cellHeight;
        FontFit         = fontFit;
        Ascent          = SDL3.TTF.GetFontAscent(font);

        // The common size is measured once, here, on the thread that initialises the renderer
        // (before a GPU render thread rasterises anything), never per frame.
        if (fontFit == FontFitMode.Uniform) EnsureUniform();
    }

    public int CellWidth  { get; }
    public int CellHeight { get; }

    /// <summary>How text is fitted into the cell (<c>[graphics] fontFit</c>); fixed for the rasteriser's lifetime.</summary>
    public FontFitMode FontFit { get; }

    /// <summary>The configured point size.</summary>
    public float PointSize => _pointSize;

    /// <summary>
    /// The size ordinary text is rasterised at: the common size under <see cref="FontFitMode.Uniform"/>
    /// (measured at construction), the configured size under <see cref="FontFitMode.Condense"/>.
    /// </summary>
    public float UniformPointSize
    {
        get
        {
            if (FontFit != FontFitMode.Uniform) return _pointSize;
            EnsureUniform();
            return _uniformPointSize;
        }
    }

    /// <summary>Uniform: the pen column of every text glyph (the common advance centred in the cell).</summary>
    public int UniformPen { get { EnsureUniform(); return _uniformPen; } }

    /// <summary>Uniform: deepest rows above / below the baseline and widest ink of the repertoire at the common size.</summary>
    public int UniformMaxAbove { get; private set; }
    public int UniformMaxBelow { get; private set; }
    public int UniformMaxWidth { get; private set; }

    /// <summary>Uniform: repertoire glyphs that reach <see cref="UniformMaxAbove"/>, <see cref="UniformMaxBelow"/>, <see cref="UniformMaxWidth"/>.</summary>
    public string UniformExtremes { get; private set; } = "";

    /// <summary>Uniform: sizes tried and rejected before the common size, with the glyph that did not fit.</summary>
    public IReadOnlyList<string> UniformRejectedSizes => _uniformRejected;

    /// <summary>Uniform: repertoire characters the font has (the rest come from the fallback or nowhere).</summary>
    public int UniformMeasuredGlyphs { get; private set; }

    /// <summary>Uniform: one-time cost of measuring the repertoire.</summary>
    public double UniformMeasureMilliseconds { get; private set; }

    /// <summary>
    /// Uniform: glyphs outside the sizing repertoire (symbols, other scripts, compatibility
    /// characters) that did not fit at the common size and were fitted by the Condense procedure
    /// starting from it. Ordinary text never counts here (diagnostics).
    /// </summary>
    public int OutlierGlyphs { get; private set; }

    /// <summary>The font's own baseline row; box drawing and block elements sit on it.</summary>
    public int Ascent { get; }

    /// <summary>Deepest ASCII descender ink below the baseline, in rows (measured on first use).</summary>
    public int DescenderDepth { get; private set; }

    /// <summary>Rows the tallest accented capital rises above <see cref="Ascent"/> (measured on first use).</summary>
    public int AccentOverflow { get; private set; }

    /// <summary>
    /// Row text glyphs sit on: <see cref="SdlFontCellPlacement.TextBaseline"/> under
    /// <see cref="FontFitMode.Condense"/>, <see cref="SdlFontCellPlacement.UniformTextBaseline"/> under
    /// <see cref="FontFitMode.Uniform"/>.
    /// </summary>
    public int TextBaseline
    {
        get
        {
            if (FontFit == FontFitMode.Uniform) EnsureUniform();
            else if (_textBaseline < 0) MeasureFont();
            return _textBaseline;
        }
    }

    /// <summary>Glyphs re-rasterised smaller so that they fit (diagnostics).</summary>
    public int FittedGlyphs { get; private set; }

    /// <summary>Glyphs that did not fit even at the smallest size tried and were clipped (diagnostics).</summary>
    public int ClippedGlyphs { get; private set; }

    /// <summary>Placement mode for a font-rendered character.</summary>
    public static SdlFontGlyphMode ModeFor(char ch) =>
        (uint)ch is >= 0x2500 and <= 0x259F ? SdlFontGlyphMode.CellFill : SdlFontGlyphMode.Text;

    /// <summary>
    /// Cell-sized alpha mask (<see cref="CellWidth"/> × <see cref="CellHeight"/>, row-major) for
    /// <paramref name="ch"/>, or <c>null</c> when SDL_ttf renders nothing.
    /// </summary>
    public byte[]? Rasterize(char ch) => Rasterize(ch, ModeFor(ch));

    public byte[]? Rasterize(char ch, SdlFontGlyphMode mode)
    {
        // Box drawing and block elements always come from the configured size, on the font
        // baseline, so they keep filling the cell in either fit mode.
        bool uniform = FontFit == FontFitMode.Uniform && mode == SdlFontGlyphMode.Text;
        if (uniform) EnsureUniform();

        Glyph? glyph = Render(uniform && _uniformFont != IntPtr.Zero ? _uniformFont : _font, ch);
        if (glyph == null) return null;

        var cell = new byte[CellWidth * CellHeight];
        if (mode == SdlFontGlyphMode.CellFill)
        {
            glyph.CopyTo(cell, CellWidth, CellHeight, penX: 0, baseline: Ascent);
            return cell;
        }

        int baseline = TextBaseline;
        int penX = uniform ? _uniformPen : 0;
        if (SdlFontCellPlacement.PlaceText(glyph.Ink, CellWidth, CellHeight, baseline, penX) is int pen)
        {
            glyph.CopyTo(cell, CellWidth, CellHeight, pen, baseline);
            return cell;
        }

        // Uniform: only a glyph outside the sizing repertoire can get here (the repertoire fits at
        // the common size by construction). It takes the Condense fit, starting from the common size.
        if (uniform) OutlierGlyphs++;
        return Fit(ch, glyph, uniform ? _uniformPointSize : _pointSize, baseline, cell);
    }

    /// <summary>
    /// Too big for the cell at <paramref name="pointSize"/>: let FreeType re-rasterise it to fit
    /// (condense the overflowing axis, else shrink), keep the baseline and centre the advance.
    /// </summary>
    private byte[] Fit(char ch, Glyph glyph, float pointSize, int baseline, byte[] cell)
    {
        Glyph? smallest = null;
        int smallestPen = 0;
        if (EnsureFitFont())
        {
            var (tooWide, tooTall) = SdlFontCellPlacement.Overflow(glyph.Ink, CellWidth, CellHeight, baseline);
            foreach (SdlFontCellPlacement.FitStep step in SdlFontCellPlacement.FitSteps(pointSize, tooWide, tooTall))
            {
                SDL3.TTF.SetFontSizeDPI(_fitFont, step.PointSize, step.HorizontalDpi, step.VerticalDpi);
                if (_fitSymbolFont != IntPtr.Zero)
                    SDL3.TTF.SetFontSizeDPI(_fitSymbolFont, step.PointSize, step.HorizontalDpi, step.VerticalDpi);

                Glyph? scaled = Render(_fitFont, ch);
                if (scaled == null) continue;

                int centred = SdlFontCellPlacement.CentredPen(CellWidth, scaled.Advance);
                smallest = scaled;
                smallestPen = centred;
                if (SdlFontCellPlacement.PlaceText(scaled.Ink, CellWidth, CellHeight, baseline, centred) is int fitPen)
                {
                    scaled.CopyTo(cell, CellWidth, CellHeight, fitPen, baseline);
                    FittedGlyphs++;
                    return cell;
                }
            }
        }

        // Nothing fits (or no second font handle): draw the smallest attempt, clipped to the cell.
        ClippedGlyphs++;
        if (smallest != null) smallest.CopyTo(cell, CellWidth, CellHeight, smallestPen, baseline);
        else glyph.CopyTo(cell, CellWidth, CellHeight, penX: 0, baseline);
        return cell;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Font measurement
    // ─────────────────────────────────────────────────────────────────────────

    private void MeasureFont()
    {
        int depth = 0;
        foreach (char ch in SdlFontCellPlacement.DescenderProbe)
        {
            if (!SDL3.TTF.FontHasGlyph(_font, ch)) continue;
            if (Render(_font, ch) is { Ink: { Height: > 0 } ink }) depth = Math.Max(depth, ink.Bottom);
        }

        int overflow = 0;
        foreach (char ch in SdlFontCellPlacement.AccentProbe)
        {
            if (!SDL3.TTF.FontHasGlyph(_font, ch)) continue;
            if (Render(_font, ch) is { Ink: { Height: > 0 } ink }) overflow = Math.Max(overflow, -ink.Top - Ascent);
        }

        DescenderDepth = depth;
        AccentOverflow = overflow;
        _textBaseline  = SdlFontCellPlacement.TextBaseline(CellHeight, Ascent, depth, overflow);
    }

    /// <summary>
    /// fontFit=Uniform: finds the largest of <see cref="SdlFontCellPlacement.UniformSizes"/> at
    /// which the ink of every repertoire character the font has fits the cell on one baseline,
    /// opens the text font at that size and fixes the baseline and pen. Runs once.
    /// </summary>
    private void EnsureUniform()
    {
        if (_uniformMeasured || FontFit != FontFitMode.Uniform) return;
        _uniformMeasured = true;
        var timer = System.Diagnostics.Stopwatch.StartNew();

        char[] repertoire = SdlFontCellPlacement.UniformRepertoire.Where(c => SDL3.TTF.FontHasGlyph(_font, c)).ToArray();
        UniformMeasuredGlyphs = repertoire.Length;

        IntPtr probe = SDL3.TTF.OpenFont(_fontPath, _pointSize);
        float chosen = _pointSize;
        try
        {
            float smallest = _pointSize;
            bool found = false;
            char lastFailure = '\0';
            foreach (float size in SdlFontCellPlacement.UniformSizes(_pointSize))
            {
                smallest = size;
                IntPtr font = size == _pointSize || probe == IntPtr.Zero ? _font : probe;
                if (font == probe) SDL3.TTF.SetFontSize(probe, size);
                if (MeasureRepertoire(font, repertoire, lastFailure, stopAtFailure: true, out char failure))
                {
                    chosen = size;
                    found = true;
                    break;
                }
                _uniformRejected.Add($"{size:0.#}pt: U+{(int)failure:X4} '{failure}' does not fit");
                lastFailure = failure;
                if (probe == IntPtr.Zero) break; // no second handle: the configured size is all there is
            }
            if (!found)
            {
                // Nothing fits down to half the size: keep the smallest size tried. Repertoire glyphs
                // that still overflow take the outlier fit (counted in OutlierGlyphs).
                chosen = smallest;
                IntPtr font = chosen == _pointSize || probe == IntPtr.Zero ? _font : probe;
                if (font == probe) SDL3.TTF.SetFontSize(probe, chosen);
                MeasureRepertoire(font, repertoire, '\0', stopAtFailure: false, out _);
            }
        }
        catch
        {
            if (probe != IntPtr.Zero) SDL3.TTF.CloseFont(probe);
            throw;
        }

        if (chosen == _pointSize || probe == IntPtr.Zero)
        {
            // The configured size already fits: the text keeps the primary font.
            if (probe != IntPtr.Zero) SDL3.TTF.CloseFont(probe);
            chosen = _pointSize;
        }
        else
        {
            _uniformFont = probe;
            if (_symbolFontPath != null)
            {
                _uniformSymbolFont = SDL3.TTF.OpenFont(_symbolFontPath, chosen);
                if (_uniformSymbolFont != IntPtr.Zero)
                    SDL3.TTF.AddFallbackFont(_uniformFont, _uniformSymbolFont);
            }
        }

        IntPtr text = _uniformFont != IntPtr.Zero ? _uniformFont : _font;
        int advance = SDL3.TTF.GetGlyphMetrics(text, 'M', out _, out _, out _, out _, out int adv) ? adv : CellWidth;
        _uniformPointSize = chosen;
        _uniformPen = SdlFontCellPlacement.CentredPen(CellWidth, advance);
        _textBaseline = SdlFontCellPlacement.UniformTextBaseline(
            CellHeight, SDL3.TTF.GetFontAscent(text), SDL3.TTF.GetFontHeight(text), UniformMaxAbove, UniformMaxBelow);
        UniformMeasureMilliseconds = timer.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// Measures the repertoire's ink at <paramref name="font"/>'s current size. Stops at the first
    /// glyph that makes it overflow when <paramref name="stopAtFailure"/> (checking
    /// <paramref name="tryFirst"/>, the previous size's culprit, before the rest); otherwise, and on
    /// success, records the extremes.
    /// </summary>
    private bool MeasureRepertoire(IntPtr font, char[] repertoire, char tryFirst, bool stopAtFailure, out char failure)
    {
        int above = 0, below = 0, width = 0;
        char top = '\0', bottom = '\0', wide = '\0';
        failure = '\0';
        IEnumerable<char> order = tryFirst == '\0' ? repertoire : repertoire.Where(c => c != tryFirst).Prepend(tryFirst);
        foreach (char ch in order)
        {
            if (Render(font, ch) is not { Ink: { Height: > 0 } ink }) continue;
            if (-ink.Top > above) (above, top) = (-ink.Top, ch);
            if (ink.Bottom > below) (below, bottom) = (ink.Bottom, ch);
            if (ink.Width > width) (width, wide) = (ink.Width, ch);
            if (failure == '\0' && !SdlFontCellPlacement.UniformFits(above, below, width, CellWidth, CellHeight))
            {
                failure = ch;
                if (stopAtFailure) return false;
            }
        }
        UniformMaxAbove = above;
        UniformMaxBelow = below;
        UniformMaxWidth = width;
        UniformExtremes = $"above {above} '{top}', below {below} '{bottom}', width {width} '{wide}'";
        return failure == '\0';
    }

    private bool EnsureFitFont()
    {
        if (_fitFont != IntPtr.Zero) return true;
        if (_fitFontUnavailable) return false;

        _fitFont = SDL3.TTF.OpenFont(_fontPath, _pointSize);
        if (_fitFont == IntPtr.Zero)
        {
            _fitFontUnavailable = true;
            return false;
        }

        if (_symbolFontPath != null)
        {
            _fitSymbolFont = SDL3.TTF.OpenFont(_symbolFontPath, _pointSize);
            if (_fitSymbolFont != IntPtr.Zero)
                SDL3.TTF.AddFallbackFont(_fitFont, _fitSymbolFont);
        }
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SDL_ttf glyph → alpha
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A rasterised glyph. SDL_ttf starts the surface at the pen origin, or at the left bearing
    /// when that is negative, and puts the baseline <c>max(ascent, maxY)</c> rows below its top.
    /// </summary>
    private sealed record Glyph(byte[] Alpha, int Width, int Height, int OriginX, int BaselineRow, int Advance, SdlGlyphInk Ink)
    {
        /// <summary>Copies the glyph onto <paramref name="cell"/> with its pen at <paramref name="penX"/>, clipped to the cell.</summary>
        public void CopyTo(byte[] cell, int cellWidth, int cellHeight, int penX, int baseline)
        {
            int dx = penX + OriginX, dy = baseline - BaselineRow;
            for (int sy = 0; sy < Height; sy++)
            {
                int cy = sy + dy;
                if (cy < 0 || cy >= cellHeight) continue;
                for (int sx = 0; sx < Width; sx++)
                {
                    int cx = sx + dx;
                    if (cx < 0 || cx >= cellWidth) continue;
                    cell[cy * cellWidth + cx] = Alpha[sy * Width + sx];
                }
            }
        }
    }

    private static Glyph? Render(IntPtr font, char ch)
    {
        int ascent = SDL3.TTF.GetFontAscent(font);
        if (!SDL3.TTF.GetGlyphMetrics(font, ch, out int minX, out _, out _, out int maxY, out int advance))
        {
            minX = 0;
            maxY = ascent;
            advance = 0;
        }

        IntPtr surface = SDL3.TTF.RenderGlyphBlended(font, ch, White);
        if (surface == IntPtr.Zero) return null;

        IntPtr argb = SDL3.SDL.ConvertSurface(surface, SDL3.SDL.PixelFormat.ARGB8888);
        SDL3.SDL.DestroySurface(surface);
        if (argb == IntPtr.Zero) return null;

        try
        {
            SDL3.SDL.Surface s = Marshal.PtrToStructure<SDL3.SDL.Surface>(argb);
            int width = s.Width, height = s.Height, pitch = s.Pitch;
            var raw = new byte[pitch * height];
            Marshal.Copy(s.Pixels, raw, 0, raw.Length);

            // ARGB8888 on little-endian: memory order B, G, R, A — alpha at byte 3.
            var alpha = new byte[width * height];
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte a = raw[y * pitch + x * 4 + 3];
                    alpha[y * width + x] = a;
                    if (a == 0) continue;
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
            }

            int originX = Math.Min(0, minX);
            int baselineRow = Math.Max(ascent, maxY);
            SdlGlyphInk ink = x1 < 0
                ? default
                : new SdlGlyphInk(x0 + originX, y0 - baselineRow, x1 + 1 + originX, y1 + 1 - baselineRow);
            return new Glyph(alpha, width, height, originX, baselineRow, advance, ink);
        }
        finally
        {
            SDL3.SDL.DestroySurface(argb);
        }
    }

    public void Dispose()
    {
        if (_fitFont != IntPtr.Zero)
        {
            SDL3.TTF.ClearFallbackFonts(_fitFont);
            SDL3.TTF.CloseFont(_fitFont);
            _fitFont = IntPtr.Zero;
        }
        if (_fitSymbolFont != IntPtr.Zero)
        {
            SDL3.TTF.CloseFont(_fitSymbolFont);
            _fitSymbolFont = IntPtr.Zero;
        }
        if (_uniformFont != IntPtr.Zero)
        {
            SDL3.TTF.ClearFallbackFonts(_uniformFont);
            SDL3.TTF.CloseFont(_uniformFont);
            _uniformFont = IntPtr.Zero;
        }
        if (_uniformSymbolFont != IntPtr.Zero)
        {
            SDL3.TTF.CloseFont(_uniformSymbolFont);
            _uniformSymbolFont = IntPtr.Zero;
        }
    }

    /// <summary>Font handles this rasteriser opened and still holds (diagnostics: leak checks).</summary>
    public int OpenAuxiliaryFonts =>
        (_fitFont != IntPtr.Zero ? 1 : 0) + (_fitSymbolFont != IntPtr.Zero ? 1 : 0) +
        (_uniformFont != IntPtr.Zero ? 1 : 0) + (_uniformSymbolFont != IntPtr.Zero ? 1 : 0);
}
