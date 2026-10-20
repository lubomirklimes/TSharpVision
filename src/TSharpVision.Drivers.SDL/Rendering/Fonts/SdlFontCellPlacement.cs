using TSharpVision.Config;

namespace TSharpVision.Drivers.SDL.Rendering.Fonts;

/// <summary>How a font glyph is put into its cell.</summary>
internal enum SdlFontGlyphMode
{
    /// <summary>
    /// Text: the glyph sits on the shared text baseline and must fit the cell completely. A glyph
    /// that cannot fit at the font's size is re-rasterised to fit (<see cref="SdlFontCellPlacement.FitSteps"/>).
    /// </summary>
    Text,

    /// <summary>
    /// Box drawing and block elements: the glyph is designed to span the font's ascent and
    /// descent, so it sits on the font baseline and whatever exceeds the cell is clipped. Fitting
    /// it would open gaps between neighbouring cells.
    /// </summary>
    CellFill,
}

/// <summary>Ink bounding box of a rasterised glyph, relative to its pen origin (x) and baseline (y, negative above).</summary>
/// <param name="Left">First inked column.</param>
/// <param name="Top">First inked row; negative above the baseline.</param>
/// <param name="Right">One past the last inked column.</param>
/// <param name="Bottom">One past the last inked row.</param>
internal readonly record struct SdlGlyphInk(int Left, int Top, int Right, int Bottom)
{
    public int Width  => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>
/// Glyph placement policy shared by both SDL back-ends, so a font glyph lands on the same pixels
/// whichever renderer draws it. Pure arithmetic; <see cref="SdlCellGlyphRasterizer"/> applies it
/// to SDL_ttf output.
/// <para>
/// The cell size never changes here (<see cref="SdlFontMetrics"/> owns it). Some fonts — Consolas
/// in particular — report an ascent that excludes the accents of capitals (Ž, Ů, Á…), so their
/// SDL_ttf surfaces are taller than the cell. Two rules keep those accents visible:
/// </para>
/// <list type="number">
/// <item>The text baseline moves down into rows the font's descenders never use, but only by as
/// many rows as its tallest accented capitals overflow (<see cref="TextBaseline"/>).</item>
/// <item>A glyph whose ink still does not fit is re-rasterised by FreeType, never clipped and
/// never bitmap-scaled: the overflowing axis is condensed by at most 15 %, otherwise the glyph is
/// shrunk uniformly (<see cref="FitSteps"/>, applied by <see cref="SdlCellGlyphRasterizer"/>).</item>
/// </list>
/// <para>
/// Those are the rules of <see cref="FontFitMode.Condense"/>. <see cref="FontFitMode.Uniform"/>
/// instead rasterises all ordinary text at one smaller size (<see cref="UniformSizes"/>,
/// <see cref="UniformFits"/>) on one baseline (<see cref="UniformTextBaseline"/>).
/// </para>
/// </summary>
internal static class SdlFontCellPlacement
{
    /// <summary>
    /// ASCII glyphs that reach deepest below the baseline. Their ink decides how many cell rows the
    /// descent really needs.
    /// </summary>
    public const string DescenderProbe = "gjpqy,;_()[]{}|$@Q";

    /// <summary>
    /// Accented Latin capitals (Latin-1 and Latin Extended-A). Their ink decides how far the
    /// accents of a font overflow its ascent.
    /// </summary>
    public const string AccentProbe =
        "ÀÁÂÃÄÅÈÉÊËÌÍÎÏÑÒÓÔÕÖÙÚÛÜÝ" +
        "ĀĂĄĆĈĊČĎĒĖĚĜĞĠĤĨĪĮİĴĹĽŃŇŌŐŔŘŚŜŞŠŢŤŨŪŬŮŰŲŴŶŸŹŻŽ";

    /// <summary>
    /// The row (from the top of the cell) the text baseline sits on.
    /// </summary>
    /// <param name="cellHeight">Cell height in pixels.</param>
    /// <param name="ascent">Font ascent: the font's own baseline row.</param>
    /// <param name="descenderDepth">Deepest descender ink, rows below the baseline (<see cref="DescenderProbe"/>).</param>
    /// <param name="accentOverflow">Rows the tallest accented capital rises above <paramref name="ascent"/> (<see cref="AccentProbe"/>).</param>
    /// <returns>
    /// <paramref name="ascent"/>, moved down by at most the rows below the deepest descender, and
    /// only as far as the accents need. A font whose accents fit keeps its own baseline.
    /// </returns>
    public static int TextBaseline(int cellHeight, int ascent, int descenderDepth, int accentOverflow)
    {
        int spareBelow = cellHeight - ascent - Math.Max(0, descenderDepth);
        int shift = Math.Clamp(Math.Min(accentOverflow, spareBelow), 0, cellHeight);
        return ascent + shift;
    }

    /// <summary>
    /// Places <paramref name="ink"/> drawn with its pen at <paramref name="penX"/> on a baseline at
    /// row <paramref name="baseline"/>.
    /// </summary>
    /// <returns>
    /// The pen column to draw at, or <c>null</c> when the ink cannot fit the cell: too wide, or
    /// above the top or below the bottom of the cell. Ink that fits in width but sticks out to one
    /// side is moved horizontally into the cell; it is never moved vertically, which would break
    /// the shared baseline.
    /// </returns>
    public static int? PlaceText(SdlGlyphInk ink, int cellWidth, int cellHeight, int baseline, int penX)
    {
        if (ink.Width <= 0 || ink.Height <= 0)
            return penX;

        if (ink.Width > cellWidth || baseline + ink.Top < 0 || baseline + ink.Bottom > cellHeight)
            return null;

        int left = penX + ink.Left;
        int right = penX + ink.Right;
        if (left < 0) return penX - left;
        if (right > cellWidth) return penX - (right - cellWidth);
        return penX;
    }

    /// <summary>
    /// Pen column that centres a glyph re-rasterised at a smaller size, whose advance no longer
    /// fills the cell.
    /// </summary>
    public static int CentredPen(int cellWidth, int advance) => Math.Max(0, (cellWidth - advance) / 2);

    /// <summary>The DPI SDL_ttf rasterises at when none is given; a point is then one pixel.</summary>
    public const int NativeDpi = 72;

    /// <summary>Lowest DPI a single axis is condensed to (85 %) before the whole glyph is shrunk instead.</summary>
    public const int MinCondensedDpi = 62;

    /// <summary>One way of re-rasterising a glyph that does not fit: a point size and per-axis DPI.</summary>
    public readonly record struct FitStep(float PointSize, int HorizontalDpi, int VerticalDpi)
    {
        public override string ToString() =>
            HorizontalDpi != NativeDpi ? $"condensed-x {HorizontalDpi * 100 / NativeDpi}%"
            : VerticalDpi != NativeDpi ? $"condensed-y {VerticalDpi * 100 / NativeDpi}%"
            : $"{PointSize:0.#}pt";
    }

    /// <summary>
    /// Re-rasterisations tried, in order, for a glyph that does not fit the cell at
    /// <paramref name="pointSize"/>. When only one axis overflows, that axis alone is condensed in
    /// one-DPI steps down to <see cref="MinCondensedDpi"/>: FreeType hints the narrower outline, so
    /// strokes keep their weight and the other axis keeps the neighbours' proportions. After that
    /// (or when both axes overflow) the glyph is shrunk uniformly in half-point steps down to half
    /// its size.
    /// </summary>
    public static IEnumerable<FitStep> FitSteps(float pointSize, bool tooWide, bool tooTall)
    {
        if (tooWide != tooTall)
        {
            for (int dpi = NativeDpi - 1; dpi >= MinCondensedDpi; dpi--)
                yield return tooWide ? new FitStep(pointSize, dpi, NativeDpi) : new FitStep(pointSize, NativeDpi, dpi);
        }
        for (float size = pointSize - 0.5f; size >= pointSize / 2; size -= 0.5f)
            yield return new FitStep(size, NativeDpi, NativeDpi);
    }

    /// <summary>Whether <paramref name="ink"/> overflows the cell horizontally / vertically at <paramref name="baseline"/>.</summary>
    public static (bool TooWide, bool TooTall) Overflow(SdlGlyphInk ink, int cellWidth, int cellHeight, int baseline) =>
        (ink.Width > cellWidth, baseline + ink.Top < 0 || baseline + ink.Bottom > cellHeight);

    // ─────────────────────────────────────────────────────────────────────────
    // fontFit=Uniform
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Unicode compatibility characters in the Latin ranges of <see cref="UniformRepertoire"/>
    /// (their compatibility decomposition differs from the canonical one): Ĳ ĳ Ŀ ŀ ŉ ſ. They are
    /// presentation forms of letter pairs, not ordinary text, so they do not size the text.
    /// </summary>
    public const string UniformExcluded = "ĲĳĿŀŉſ";

    /// <summary>
    /// Characters whose ink decides the common text size of <see cref="FontFitMode.Uniform"/>, in
    /// this order: printable ASCII (letters, digits, punctuation, descenders), the Latin-1 letters
    /// U+00C0–U+00FF except × and ÷, and Latin Extended-A U+0100–U+017F except
    /// <see cref="UniformExcluded"/>. A bounded, deterministic set: upper- and lowercase accents,
    /// carons with negative or overhanging bearings (ť ď ľ), cedillas and ogoneks.
    /// </summary>
    public static readonly string UniformRepertoire = BuildUniformRepertoire();

    private static string BuildUniformRepertoire()
    {
        var chars = new List<char>();
        for (char c = '!'; c <= '~'; c++) chars.Add(c);
        for (char c = 'À'; c <= 'ÿ'; c++)
            if (c is not ('×' or '÷')) chars.Add(c);
        for (char c = 'Ā'; c <= 'ſ'; c++)
            if (!UniformExcluded.Contains(c)) chars.Add(c);
        return new string(chars.ToArray());
    }

    /// <summary>
    /// Point sizes tried for <see cref="FontFitMode.Uniform"/>, largest first: the configured size,
    /// then whole points down to half of it. Hinted FreeType rasterisation at 72 dpi snaps to whole
    /// pixels per em, so fractional points would repeat a neighbour's glyphs. Never larger than
    /// <paramref name="pointSize"/>.
    /// </summary>
    public static IEnumerable<float> UniformSizes(float pointSize)
    {
        yield return pointSize;
        for (float size = MathF.Ceiling(pointSize) - 1; size >= pointSize / 2; size--)
            yield return size;
    }

    /// <summary>
    /// Whether text whose ink reaches <paramref name="maxAbove"/> rows above and
    /// <paramref name="maxBelow"/> rows below a common baseline, and is at most
    /// <paramref name="maxWidth"/> columns wide, fits the cell.
    /// </summary>
    public static bool UniformFits(int maxAbove, int maxBelow, int maxWidth, int cellWidth, int cellHeight) =>
        maxWidth <= cellWidth && Math.Max(0, maxAbove) + Math.Max(0, maxBelow) <= cellHeight;

    /// <summary>
    /// The text baseline for <see cref="FontFitMode.Uniform"/>: the line box of the font at the
    /// common size (<paramref name="ascent"/> + descent = <paramref name="fontHeight"/>) centred in
    /// the cell, then kept where the repertoire's ink fits (<paramref name="maxAbove"/> rows above,
    /// <paramref name="maxBelow"/> below). A font whose line box fills the cell keeps its own baseline.
    /// </summary>
    public static int UniformTextBaseline(int cellHeight, int ascent, int fontHeight, int maxAbove, int maxBelow)
    {
        int centred = ascent + (cellHeight - fontHeight) / 2;
        int lowest = Math.Max(0, maxAbove);
        int highest = cellHeight - Math.Max(0, maxBelow);
        return highest < lowest ? lowest : Math.Clamp(centred, lowest, highest);
    }
}
