// Glyph placement policy shared by SDLRenderer and SDLGpuRenderer (TW-5A.1). Pure arithmetic —
// no SDL handle, font or window; the rasteriser that applies it is covered by the native-pixel tests.
using TSharpVision.Drivers.SDL.Rendering.Fonts;

namespace TSharpVision.Drivers.SDL.Tests;

public sealed class SdlFontCellPlacementTests
{
    [Theory]
    [InlineData('╳')]
    [InlineData('▁')]
    [InlineData('▟')]
    public void Non_generated_boxes_and_blocks_use_font_cell_masks(char ch) =>
        Assert.Equal(global::TSharpVision.Drivers.SDL.Renderer.SdlGlyphRenderClass.FontCell,
            global::TSharpVision.Drivers.SDL.Renderer.SdlGlyphRenderPolicy.Classify(ch));

    // ── Text baseline ──────────────────────────────────────────────────────────

    [Fact]
    public void Consolas20_moves_the_baseline_down_by_its_single_spare_row()
    {
        // cell 20, ascent 15, descenders 4 rows deep, Ů rises 3 rows above the ascent.
        Assert.Equal(16, SdlFontCellPlacement.TextBaseline(20, 15, 4, 3));
    }

    [Fact]
    public void A_font_whose_accents_fit_keeps_its_own_baseline()
    {
        // JetBrains Mono 20 pt: cell 27, ascent 21, accents within the ascent.
        Assert.Equal(21, SdlFontCellPlacement.TextBaseline(27, 21, 4, 0));
    }

    [Fact]
    public void The_baseline_never_moves_into_rows_the_descenders_need()
    {
        Assert.Equal(11, SdlFontCellPlacement.TextBaseline(14, 11, 3, 1)); // no spare row (Consolas 14 pt)
        Assert.Equal(13, SdlFontCellPlacement.TextBaseline(16, 12, 3, 5)); // one spare row, five wanted
        Assert.Equal(10, SdlFontCellPlacement.TextBaseline(14, 10, 6, 2)); // descent deeper than the cell allows
    }

    // ── Placement ──────────────────────────────────────────────────────────────

    [Fact]
    public void Ink_that_fits_keeps_its_pen_position()
    {
        var z = new SdlGlyphInk(1, -13, 10, 0);
        Assert.Equal(0, SdlFontCellPlacement.PlaceText(z, 11, 20, 16, penX: 0));
    }

    [Fact]
    public void Ink_above_the_cell_does_not_fit_and_is_never_moved_down()
    {
        var zCaron = new SdlGlyphInk(1, -17, 10, 0);
        Assert.Null(SdlFontCellPlacement.PlaceText(zCaron, 11, 20, 16, penX: 0));
        Assert.Equal(0, SdlFontCellPlacement.PlaceText(zCaron, 11, 20, 17, penX: 0));
    }

    [Fact]
    public void Ink_below_the_cell_does_not_fit()
    {
        var g = new SdlGlyphInk(0, -10, 11, 4);
        Assert.Equal(0, SdlFontCellPlacement.PlaceText(g, 11, 20, 16, 0));
        Assert.Null(SdlFontCellPlacement.PlaceText(g, 11, 20, 17, 0));
    }

    [Fact]
    public void Ink_that_sticks_out_sideways_but_is_narrow_enough_moves_into_the_cell()
    {
        Assert.Equal(2, SdlFontCellPlacement.PlaceText(new SdlGlyphInk(-2, -10, 8, 0), 11, 20, 16, 0));
        Assert.Equal(-1, SdlFontCellPlacement.PlaceText(new SdlGlyphInk(1, -10, 12, 0), 11, 20, 16, 0));
    }

    [Fact]
    public void Ink_wider_than_the_cell_does_not_fit()
    {
        var dCaron = new SdlGlyphInk(0, -15, 12, 0);
        Assert.Null(SdlFontCellPlacement.PlaceText(dCaron, 11, 20, 16, 0));
    }

    [Fact]
    public void Blank_glyphs_always_fit()
    {
        Assert.Equal(3, SdlFontCellPlacement.PlaceText(default, 11, 20, 16, 3));
    }

    [Fact]
    public void Overflow_reports_each_axis()
    {
        Assert.Equal((false, true), SdlFontCellPlacement.Overflow(new SdlGlyphInk(1, -17, 10, 0), 11, 20, 16));
        Assert.Equal((true, false), SdlFontCellPlacement.Overflow(new SdlGlyphInk(0, -15, 12, 0), 11, 20, 16));
        Assert.Equal((false, false), SdlFontCellPlacement.Overflow(new SdlGlyphInk(1, -13, 10, 0), 11, 20, 16));
    }

    [Fact]
    public void Centred_pen_splits_the_spare_columns()
    {
        Assert.Equal(0, SdlFontCellPlacement.CentredPen(11, 11));
        Assert.Equal(0, SdlFontCellPlacement.CentredPen(11, 10));
        Assert.Equal(1, SdlFontCellPlacement.CentredPen(11, 9));
        Assert.Equal(0, SdlFontCellPlacement.CentredPen(11, 14));
    }

    // ── Fit steps ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_tall_glyph_is_condensed_vertically_first_then_shrunk_uniformly()
    {
        var steps = SdlFontCellPlacement.FitSteps(20, tooWide: false, tooTall: true).ToList();
        Assert.Equal(new SdlFontCellPlacement.FitStep(20, 72, 71), steps[0]);
        Assert.Equal(new SdlFontCellPlacement.FitStep(20, 72, 62), steps[9]);
        Assert.Equal(new SdlFontCellPlacement.FitStep(19.5f, 72, 72), steps[10]);
        Assert.All(steps.Take(10), s => Assert.Equal(72, s.HorizontalDpi));
        Assert.Equal(10f, steps[^1].PointSize);
    }

    [Fact]
    public void A_wide_glyph_is_condensed_horizontally_first()
    {
        var steps = SdlFontCellPlacement.FitSteps(20, tooWide: true, tooTall: false).ToList();
        Assert.Equal(new SdlFontCellPlacement.FitStep(20, 71, 72), steps[0]);
        Assert.All(steps.Take(10), s => Assert.Equal(72, s.VerticalDpi));
        Assert.All(steps, s => Assert.True(s.HorizontalDpi >= SdlFontCellPlacement.MinCondensedDpi));
    }

    [Fact]
    public void A_glyph_too_big_both_ways_is_only_shrunk_uniformly()
    {
        var steps = SdlFontCellPlacement.FitSteps(20, tooWide: true, tooTall: true).ToList();
        Assert.All(steps, s => Assert.Equal((72, 72), (s.HorizontalDpi, s.VerticalDpi)));
        Assert.Equal(19.5f, steps[0].PointSize);
    }

    // ── Mode ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData('A', false)]
    [InlineData('Ž', false)]
    [InlineData('ď', false)]
    [InlineData('▲', false)]
    [InlineData('╳', true)]
    [InlineData('▁', true)]
    [InlineData('▟', true)]
    public void Box_drawing_and_block_elements_fill_the_cell_everything_else_is_text(char ch, bool cellFill)
    {
        Assert.Equal(cellFill ? SdlFontGlyphMode.CellFill : SdlFontGlyphMode.Text, SdlCellGlyphRasterizer.ModeFor(ch));
    }
}
