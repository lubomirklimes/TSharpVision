namespace TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

/// <summary>
/// Deterministic, font-independent stroke and block geometry for one terminal cell size.
/// <para>
/// Every value is a pure function of <see cref="CellWidth"/> and <see cref="CellHeight"/>.
/// Nothing here is measured from a TTF face — that is the entire point: two adjacent cells
/// always agree on where a stroke or a block edge sits, so glyphs join by construction
/// regardless of which font produced the cell size.
/// </para>
/// <para>
/// All positions use band-start semantics: a band occupies the inclusive range
/// <c>[start, start + thickness - 1]</c>, which is unambiguous for even thicknesses.
/// </para>
/// </summary>
internal sealed record CellGlyphGeometry
{
    /// <summary>Largest stroke thickness this geometry will ever choose.</summary>
    public const int MaxStrokeThickness = 3;

    private CellGlyphGeometry(
        int cellWidth,
        int cellHeight,
        int singleStrokeThickness,
        int doubleStrokeThickness,
        int singleBandX,
        int singleBandY,
        int doubleBandX1,
        int doubleBandX2,
        int doubleBandY1,
        int doubleBandY2,
        bool doubleVerticalSupported,
        bool doubleHorizontalSupported,
        int topHeight,
        int leftWidth)
    {
        CellWidth                 = cellWidth;
        CellHeight                = cellHeight;
        SingleStrokeThickness     = singleStrokeThickness;
        DoubleStrokeThickness     = doubleStrokeThickness;
        SingleBandX               = singleBandX;
        SingleBandY               = singleBandY;
        DoubleBandX1              = doubleBandX1;
        DoubleBandX2              = doubleBandX2;
        DoubleBandY1              = doubleBandY1;
        DoubleBandY2              = doubleBandY2;
        DoubleVerticalSupported   = doubleVerticalSupported;
        DoubleHorizontalSupported = doubleHorizontalSupported;
        TopHeight                 = topHeight;
        LeftWidth                 = leftWidth;
    }

    // ── Cell ───────────────────────────────────────────────────────────────────

    /// <summary>Terminal cell width in pixels. Always &gt;= 1.</summary>
    public int CellWidth { get; }

    /// <summary>Terminal cell height in pixels. Always &gt;= 1.</summary>
    public int CellHeight { get; }

    // ── Stroke thickness ───────────────────────────────────────────────────────

    /// <summary>Thickness of a single-line stroke, in pixels. Always &gt;= 1.</summary>
    public int SingleStrokeThickness { get; }

    /// <summary>
    /// Thickness of each rail of a double-line stroke, in pixels. Always &gt;= 1.
    /// May be smaller than <see cref="SingleStrokeThickness"/> in narrow cells, because two
    /// rails plus a gap must fit inside the cell.
    /// </summary>
    public int DoubleStrokeThickness { get; }

    // ── Single-line band positions ─────────────────────────────────────────────

    /// <summary>First column of the single vertical stroke band.</summary>
    public int SingleBandX { get; }

    /// <summary>First row of the single horizontal stroke band.</summary>
    public int SingleBandY { get; }

    // ── Double-line band positions (X1 &lt; X2, Y1 &lt; Y2 when supported) ──────

    /// <summary>First column of the left rail of a double vertical stroke.</summary>
    public int DoubleBandX1 { get; }

    /// <summary>First column of the right rail of a double vertical stroke.</summary>
    public int DoubleBandX2 { get; }

    /// <summary>First row of the upper rail of a double horizontal stroke.</summary>
    public int DoubleBandY1 { get; }

    /// <summary>First row of the lower rail of a double horizontal stroke.</summary>
    public int DoubleBandY2 { get; }

    /// <summary>
    /// <c>false</c> when the cell is too narrow to hold two vertical rails plus a gap.
    /// In that case <see cref="DoubleBandX1"/> == <see cref="DoubleBandX2"/> ==
    /// <see cref="SingleBandX"/> and double vertical strokes degrade to a single stroke.
    /// </summary>
    public bool DoubleVerticalSupported { get; }

    /// <summary>
    /// <c>false</c> when the cell is too short to hold two horizontal rails plus a gap.
    /// In that case <see cref="DoubleBandY1"/> == <see cref="DoubleBandY2"/> ==
    /// <see cref="SingleBandY"/> and double horizontal strokes degrade to a single stroke.
    /// </summary>
    public bool DoubleHorizontalSupported { get; }

    // ── Block splits ───────────────────────────────────────────────────────────

    /// <summary>
    /// Row count of the upper half block (<c>▀</c>, CP437 <c>DF</c>).
    /// <c>(CellHeight + 1) / 2</c> — the extra row of an odd cell belongs to the upper half.
    /// The lower half (<c>▄</c>, <c>DC</c>) covers the remaining
    /// <c>CellHeight - TopHeight</c> rows, so the two partition the cell exactly.
    /// </summary>
    public int TopHeight { get; }

    /// <summary>Row count of the lower half block (<c>▄</c>, CP437 <c>DC</c>).</summary>
    public int BottomHeight => CellHeight - TopHeight;

    /// <summary>
    /// Column count of the left half block (<c>▌</c>, CP437 <c>DD</c>).
    /// <c>(CellWidth + 1) / 2</c> — the extra column of an odd cell belongs to the left half.
    /// The right half (<c>▐</c>, <c>DE</c>) covers the remaining columns.
    /// </summary>
    public int LeftWidth { get; }

    /// <summary>Column count of the right half block (<c>▐</c>, CP437 <c>DE</c>).</summary>
    public int RightWidth => CellWidth - LeftWidth;

    // ── Factory ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Derives the complete geometry for a cell of <paramref name="cellWidth"/> ×
    /// <paramref name="cellHeight"/> pixels.
    /// <para>
    /// Rules:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>Single stroke thickness</b> — <c>min(w,h) / 12</c>, clamped to
    /// <c>[1, min(3, w, h)]</c>: 1 px up to an 11 px minimum dimension, 2 px from 24 px, 3 px
    /// from 36 px. The <i>smaller</i> dimension drives it — that is cell width for every
    /// realistic terminal font — so all common 20 pt monospace faces (cell widths 11–12) get the
    /// same 1 px stroke and box art looks identical across them.
    /// </description></item>
    /// <item><description>
    /// <b>Single band position</b> — <c>(extent - thickness) / 2</c>: the band is centred, with
    /// any odd leftover pixel falling on the far (right / bottom) side. The rule is identical for
    /// both axes so a horizontal and a vertical stroke cross symmetrically.
    /// </description></item>
    /// <item><description>
    /// <b>Double rails</b> — two bands of <c>DoubleStrokeThickness</c> separated by a gap of
    /// <c>max(1, thickness)</c> blank pixels, the pair centred in the cell. The thickness is
    /// reduced (never below 1) until the pair fits in <i>both</i> axes, so a double line always
    /// has one thickness value. If the pair still does not fit on an axis, that axis reports
    /// <c>DoubleXxxSupported == false</c> and its rails collapse onto the single band.
    /// </description></item>
    /// <item><description>
    /// <b>Block splits</b> — ceil for the first half, floor for the second, so
    /// <c>TopHeight + BottomHeight == CellHeight</c> and
    /// <c>LeftWidth + RightWidth == CellWidth</c> exactly, for odd and even sizes alike.
    /// </description></item>
    /// </list>
    /// <para>
    /// Never throws for <paramref name="cellWidth"/> or <paramref name="cellHeight"/> &gt;= 1;
    /// values below 1 are clamped to 1 so callers cannot produce an unusable geometry.
    /// </para>
    /// </summary>
    public static CellGlyphGeometry FromCellSize(int cellWidth, int cellHeight)
    {
        int w = Math.Max(1, cellWidth);
        int h = Math.Max(1, cellHeight);

        // 1. Single stroke thickness.
        int minExtent = Math.Min(w, h);
        int ts = Math.Clamp(minExtent / 12, 1, Math.Min(MaxStrokeThickness, minExtent));

        // 2. Single band positions (centred, leftover pixel on the far side).
        int singleX = (w - ts) / 2;
        int singleY = (h - ts) / 2;

        // 3. Double rail thickness: shrink until "rail + gap + rail" fits on both axes.
        int td  = ts;
        int gap = Math.Max(1, td);
        while (td > 1 && (2 * td + gap > w || 2 * td + gap > h))
        {
            td  = td - 1;
            gap = Math.Max(1, td);
        }

        bool doubleVertical   = 2 * td + gap <= w;
        bool doubleHorizontal = 2 * td + gap <= h;

        int x1, x2, y1, y2;

        if (doubleVertical)
        {
            int total = 2 * td + gap;
            x1 = (w - total) / 2;
            x2 = x1 + td + gap;
        }
        else
        {
            x1 = x2 = singleX;
        }

        if (doubleHorizontal)
        {
            int total = 2 * td + gap;
            y1 = (h - total) / 2;
            y2 = y1 + td + gap;
        }
        else
        {
            y1 = y2 = singleY;
        }

        // 4. Block splits: exact complementary partitions.
        int topHeight = (h + 1) / 2;
        int leftWidth = (w + 1) / 2;

        return new CellGlyphGeometry(
            cellWidth:                 w,
            cellHeight:                h,
            singleStrokeThickness:     ts,
            doubleStrokeThickness:     td,
            singleBandX:               singleX,
            singleBandY:               singleY,
            doubleBandX1:              x1,
            doubleBandX2:              x2,
            doubleBandY1:              y1,
            doubleBandY2:              y2,
            doubleVerticalSupported:   doubleVertical,
            doubleHorizontalSupported: doubleHorizontal,
            topHeight:                 topHeight,
            leftWidth:                 leftWidth);
    }
}
