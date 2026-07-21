using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

/// <summary>
/// Produces exact cell-sized <see cref="AlphaBitmap"/>s for every CP437 graphics character in
/// <c>0xB0</c>–<c>0xDF</c>, using only <see cref="CellGlyphGeometry"/> — never the selected font.
/// <para>
/// The type is immutable and stateless apart from its geometry and options, so a single instance
/// can be shared freely across threads. It touches no SDL handle, no window, no texture and no
/// font face, which is what lets the headless diagnostic tool and the unit tests exercise exactly
/// the code a renderer would use.
/// </para>
/// </summary>
internal sealed class Cp437GlyphGenerator
{
    private readonly CellGlyphGeometry            _geometry;
    private readonly Cp437GlyphGeneratorOptions   _options;

    public Cp437GlyphGenerator(CellGlyphGeometry geometry, Cp437GlyphGeneratorOptions? options = null)
    {
        _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        _options  = options  ?? Cp437GlyphGeneratorOptions.Default;
    }

    public Cp437GlyphGenerator(int cellWidth, int cellHeight, Cp437GlyphGeneratorOptions? options = null)
        : this(CellGlyphGeometry.FromCellSize(cellWidth, cellHeight), options) { }

    public CellGlyphGeometry          Geometry => _geometry;
    public Cp437GlyphGeneratorOptions Options  => _options;

    public int CellWidth  => _geometry.CellWidth;
    public int CellHeight => _geometry.CellHeight;

    // ─────────────────────────────────────────────────────────────────────────
    // Queries
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>True when <paramref name="ch"/> is one of the 48 generated characters.</summary>
    public static bool IsGenerated(char ch) => Cp437GraphicsCharacters.Contains(ch);

    /// <summary>
    /// True when the glyph for <paramref name="ch"/> depends on the cell's
    /// <see cref="GlyphPhase"/>. Only the three shading characters do, and only in
    /// <see cref="ShadingMode.PhasedDither"/>.
    /// </summary>
    public bool RequiresPhase(char ch) =>
        _options.ShadingMode == ShadingMode.PhasedDither &&
        Cp437GraphicsCharacters.TryGet(ch, out Cp437GraphicsCharacter e) &&
        e.Category == Cp437GraphicsCategory.Shading;

    // ─────────────────────────────────────────────────────────────────────────
    // Generation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates the glyph at phase (0, 0). Correct for every character except shading in
    /// <see cref="ShadingMode.PhasedDither"/>, which needs the real cell phase — see
    /// <see cref="TryGenerate(char, GlyphPhase, out AlphaBitmap)"/>.
    /// </summary>
    public bool TryGenerate(char ch, out AlphaBitmap bitmap) =>
        TryGenerate(ch, GlyphPhase.Zero, out bitmap);

    /// <summary>
    /// Generates the glyph for <paramref name="ch"/> at the given lattice
    /// <paramref name="phase"/>. The returned bitmap is always exactly
    /// <see cref="CellWidth"/> × <see cref="CellHeight"/> pixels.
    /// </summary>
    public bool TryGenerate(char ch, GlyphPhase phase, out AlphaBitmap bitmap)
    {
        if (!Cp437GraphicsCharacters.TryGet(ch, out Cp437GraphicsCharacter entry))
        {
            bitmap = null!;
            return false;
        }

        var cell = AlphaBitmap.CreateEmpty(_geometry.CellWidth, _geometry.CellHeight);

        switch (entry.Category)
        {
            case Cp437GraphicsCategory.Shading:
                ComposeShading(cell, entry.Code, phase.Normalized());
                break;

            case Cp437GraphicsCategory.Block:
                ComposeBlock(cell, entry.Code);
                break;

            case Cp437GraphicsCategory.BoxDrawing:
                ComposeBox(cell, entry.Shape);
                break;

            default:
                throw new InvalidOperationException($"Unhandled category {entry.Category}.");
        }

        bitmap = cell;
        return true;
    }

    /// <summary>
    /// Convenience wrapper that throws instead of returning <c>false</c>.
    /// </summary>
    public AlphaBitmap Generate(char ch, GlyphPhase phase = default) =>
        TryGenerate(ch, phase, out AlphaBitmap bitmap)
            ? bitmap
            : throw new ArgumentOutOfRangeException(
                nameof(ch), ch, $"U+{(int)ch:X4} is not a CP437 B0-DF graphics character.");

    // ─────────────────────────────────────────────────────────────────────────
    // Shading — B0 ░, B1 ▒, B2 ▓
    // ─────────────────────────────────────────────────────────────────────────

    private void ComposeShading(AlphaBitmap cell, byte code, GlyphPhase phase)
    {
        if (_options.ShadingMode == ShadingMode.Uniform)
        {
            // Flat partial coverage: identical in every pixel, so adjacent cells can never
            // disagree at a boundary regardless of cell size.
            byte alpha = Cp437ShadingLattice.UniformAlpha(code);
            AlphaBitmapDrawing.FillRect(cell, 0, 0, cell.Width - 1, cell.Height - 1, alpha);
            return;
        }

        // Phase-correct dither: sample the infinite lattice in absolute screen pixels.
        // phase is the cell's own origin modulo the lattice period, so cell (c, r) and cell
        // (c + 1, r) continue the same pattern even when cellWidth is odd.
        for (int y = 0; y < cell.Height; y++)
        {
            int absoluteY = phase.Y + y;
            for (int x = 0; x < cell.Width; x++)
            {
                if (Cp437ShadingLattice.HasInk(code, phase.X + x, absoluteY))
                    cell.SetAlpha(x, y, Cp437ShadingLattice.InkAlpha);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Block elements — DB █, DC ▄, DD ▌, DE ▐, DF ▀
    // ─────────────────────────────────────────────────────────────────────────

    private void ComposeBlock(AlphaBitmap cell, byte code)
    {
        int w = _geometry.CellWidth;
        int h = _geometry.CellHeight;
        int topHeight = _geometry.TopHeight;   // (h + 1) / 2 — extra row goes to the upper half
        int leftWidth = _geometry.LeftWidth;   // (w + 1) / 2 — extra column goes to the left half

        switch (code)
        {
            case 0xDB:  // █ full block
                AlphaBitmapDrawing.FillRect(cell, 0, 0, w - 1, h - 1);
                break;

            case 0xDF:  // ▀ upper half: rows [0, topHeight - 1]
                AlphaBitmapDrawing.FillRect(cell, 0, 0, w - 1, topHeight - 1);
                break;

            case 0xDC:  // ▄ lower half: rows [topHeight, h - 1] — exact complement of ▀
                AlphaBitmapDrawing.FillRect(cell, 0, topHeight, w - 1, h - 1);
                break;

            case 0xDD:  // ▌ left half: columns [0, leftWidth - 1]
                AlphaBitmapDrawing.FillRect(cell, 0, 0, leftWidth - 1, h - 1);
                break;

            case 0xDE:  // ▐ right half: columns [leftWidth, w - 1] — exact complement of ▌
                AlphaBitmapDrawing.FillRect(cell, leftWidth, 0, w - 1, h - 1);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(code), code, "Block elements are 0xDB..0xDF.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Box drawing — B3 .. DA
    // ─────────────────────────────────────────────────────────────────────────
    //
    // Bands (band-start semantics, inclusive of `thickness` pixels):
    //   single vertical   : column band at SingleBandX,  thickness ts
    //   single horizontal : row    band at SingleBandY,  thickness ts
    //   double vertical   : column bands at X1 and X2,   thickness td   ("rails")
    //   double horizontal : row    bands at Y1 and Y2,   thickness td
    //
    // Invariant that makes neighbouring cells join: a side that connects always paints from
    // (or to) the exact cell edge — column 0, column w-1, row 0 or row h-1 — and every glyph
    // uses the same band coordinates, which come only from the cell size.
    //
    // Three junction cases, handled separately because their inner terminations differ:
    //   1. single / single   — strokes cross or meet at the single bands.
    //   2. mixed             — a single stroke meets a double pair, or vice versa.
    //   3. double / double   — the closed-form corner rule, then "channel openings" are
    //                          cleared so that ╬ ╠ ╦ … show the classic broken crossing.

    private void ComposeBox(AlphaBitmap cell, BoxGlyphShape shape)
    {
        BoxSideStyle hStyle = shape.HorizontalStyle;
        BoxSideStyle vStyle = shape.VerticalStyle;

        if (hStyle == BoxSideStyle.Double && vStyle == BoxSideStyle.Double)
        {
            ComposeDoubleDouble(cell, shape);
            return;
        }

        ComposeSingleOrMixed(cell, shape, hStyle, vStyle);
    }

    // ── Case 1 & 2: at least one axis is single (or absent) ────────────────────

    private void ComposeSingleOrMixed(
        AlphaBitmap cell, BoxGlyphShape shape, BoxSideStyle hStyle, BoxSideStyle vStyle)
    {
        int w  = _geometry.CellWidth;
        int h  = _geometry.CellHeight;
        int ts = _geometry.SingleStrokeThickness;
        int td = _geometry.DoubleStrokeThickness;

        int sx = _geometry.SingleBandX;
        int sy = _geometry.SingleBandY;
        int x1 = _geometry.DoubleBandX1;
        int x2 = _geometry.DoubleBandX2;
        int y1 = _geometry.DoubleBandY1;
        int y2 = _geometry.DoubleBandY2;

        bool left  = shape.HasLeft;
        bool right = shape.HasRight;
        bool up    = shape.HasUp;
        bool down  = shape.HasDown;

        // ── Horizontal strokes ────────────────────────────────────────────────
        if (hStyle != BoxSideStyle.None)
        {
            // Inner terminus of a one-sided horizontal stroke: it must reach far enough into
            // the cell to bond with the vertical stroke(s) it meets.
            //   vertical single  -> far edge of the single band
            //   vertical double  -> "near rail" when the vertical passes straight through
            //                       (both up and down present), otherwise the "far rail" so
            //                       that a corner closes on both rails.
            //   no vertical      -> the opposite cell edge (never happens in B3..DA, but keeps
            //                       the generator total).
            int leftEnd, rightStart;

            if (vStyle == BoxSideStyle.None)
            {
                leftEnd    = w - 1;
                rightStart = 0;
            }
            else if (vStyle == BoxSideStyle.Single)
            {
                leftEnd    = sx + ts - 1;
                rightStart = sx;
            }
            else // vertical double
            {
                bool through = up && down;
                leftEnd    = through ? x1 + td - 1 : x2 + td - 1;
                rightStart = through ? x2          : x1;
            }

            int hStart = left  ? 0     : rightStart;
            int hEnd   = right ? w - 1 : leftEnd;

            if (left || right)
            {
                if (hStyle == BoxSideStyle.Single)
                {
                    AlphaBitmapDrawing.DrawHorizontalBand(cell, sy, ts, hStart, hEnd);
                }
                else
                {
                    AlphaBitmapDrawing.DrawHorizontalBand(cell, y1, td, hStart, hEnd);
                    AlphaBitmapDrawing.DrawHorizontalBand(cell, y2, td, hStart, hEnd);
                }
            }
        }

        // ── Vertical strokes ──────────────────────────────────────────────────
        if (vStyle != BoxSideStyle.None)
        {
            int upEnd, downStart;

            if (hStyle == BoxSideStyle.None)
            {
                upEnd     = h - 1;
                downStart = 0;
            }
            else if (hStyle == BoxSideStyle.Single)
            {
                upEnd     = sy + ts - 1;
                downStart = sy;
            }
            else // horizontal double
            {
                bool through = left && right;
                upEnd     = through ? y1 + td - 1 : y2 + td - 1;
                downStart = through ? y2          : y1;
            }

            int vStart = up   ? 0     : downStart;
            int vEnd   = down ? h - 1 : upEnd;

            if (up || down)
            {
                if (vStyle == BoxSideStyle.Single)
                {
                    AlphaBitmapDrawing.DrawVerticalBand(cell, sx, ts, vStart, vEnd);
                }
                else
                {
                    AlphaBitmapDrawing.DrawVerticalBand(cell, x1, td, vStart, vEnd);
                    AlphaBitmapDrawing.DrawVerticalBand(cell, x2, td, vStart, vEnd);
                }
            }
        }
    }

    // ── Case 3: both axes double ──────────────────────────────────────────────
    //
    // Closed-form rail endpoints (outer rail bonds to outer rail, inner to inner):
    //
    //   Y1: xStart = left  ? 0   : (up    ? X2 : X1)
    //       xEnd   = right ? W-1 : (up    ? X1 : X2) + td - 1
    //   Y2: xStart = left  ? 0   : (down  ? X2 : X1)
    //       xEnd   = right ? W-1 : (down  ? X1 : X2) + td - 1
    //   X1: yStart = up    ? 0   : (left  ? Y2 : Y1)
    //       yEnd   = down  ? H-1 : (left  ? Y1 : Y2) + td - 1
    //   X2: yStart = up    ? 0   : (right ? Y2 : Y1)
    //       yEnd   = down  ? H-1 : (right ? Y1 : Y2) + td - 1
    //
    // Then the "channel openings" are cleared. A double line is a channel between two rails;
    // where a branch leaves the junction, the rail nearest that branch is interrupted so the
    // channel stays open. That is what turns ╬ into four corner pieces, breaks the right rail
    // of ╠, and breaks the lower rail of ╦ — the classic CP437 appearance.

    private void ComposeDoubleDouble(AlphaBitmap cell, BoxGlyphShape shape)
    {
        int w  = _geometry.CellWidth;
        int h  = _geometry.CellHeight;
        int td = _geometry.DoubleStrokeThickness;

        int x1 = _geometry.DoubleBandX1;
        int x2 = _geometry.DoubleBandX2;
        int y1 = _geometry.DoubleBandY1;
        int y2 = _geometry.DoubleBandY2;

        bool left  = shape.HasLeft;
        bool right = shape.HasRight;
        bool up    = shape.HasUp;
        bool down  = shape.HasDown;

        bool hasH = left || right;
        bool hasV = up   || down;

        if (hasH)
        {
            int y1Start = left  ? 0     : (up   ? x2 : x1);
            int y1End   = right ? w - 1 : (up   ? x1 : x2) + td - 1;
            AlphaBitmapDrawing.DrawHorizontalBand(cell, y1, td, y1Start, y1End);

            int y2Start = left  ? 0     : (down ? x2 : x1);
            int y2End   = right ? w - 1 : (down ? x1 : x2) + td - 1;
            AlphaBitmapDrawing.DrawHorizontalBand(cell, y2, td, y2Start, y2End);
        }

        if (hasV)
        {
            int x1Start = up   ? 0     : (left  ? y2 : y1);
            int x1End   = down ? h - 1 : (left  ? y1 : y2) + td - 1;
            AlphaBitmapDrawing.DrawVerticalBand(cell, x1, td, x1Start, x1End);

            int x2Start = up   ? 0     : (right ? y2 : y1);
            int x2End   = down ? h - 1 : (right ? y1 : y2) + td - 1;
            AlphaBitmapDrawing.DrawVerticalBand(cell, x2, td, x2Start, x2End);
        }

        // Channel openings. Skipped when the cell is too small for two distinct rails,
        // in which case the ranges below are empty anyway.
        if (!_geometry.DoubleVerticalSupported || !_geometry.DoubleHorizontalSupported)
            return;

        int channelRowStart = y1 + td;   // first row strictly between the horizontal rails
        int channelRowEnd   = y2 - 1;    // last  row strictly between the horizontal rails
        int channelColStart = x1 + td;   // first column strictly between the vertical rails
        int channelColEnd   = x2 - 1;    // last  column strictly between the vertical rails

        // A branch leaving left/right opens the vertical rail nearest to it.
        if (left)  AlphaBitmapDrawing.ClearRect(cell, x1, channelRowStart, x1 + td - 1, channelRowEnd);
        if (right) AlphaBitmapDrawing.ClearRect(cell, x2, channelRowStart, x2 + td - 1, channelRowEnd);

        // A branch leaving up/down opens the horizontal rail nearest to it.
        if (up)    AlphaBitmapDrawing.ClearRect(cell, channelColStart, y1, channelColEnd, y1 + td - 1);
        if (down)  AlphaBitmapDrawing.ClearRect(cell, channelColStart, y2, channelColEnd, y2 + td - 1);
    }
}
