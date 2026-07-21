using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Drivers.SDL.Tests.Rendering.GlyphComposition;

public class CellGlyphGeometryTests
{
    public static TheoryData<int, int> RepresentativeCells => new()
    {
        { 11, 20 },  // Consolas 20 pt
        { 12, 23 },  // Courier New 20 pt
        { 12, 20 },  // Lucida Console 20 pt
        {  8, 16 },  // classic VGA
        {  9, 15 },  // odd on both axes
        { 16, 32 },  // large, thickness 3
    };

    [Theory]
    [MemberData(nameof(RepresentativeCells))]
    public void FromCellSize_ReportsRequestedCellSize(int w, int h)
    {
        CellGlyphGeometry g = CellGlyphGeometry.FromCellSize(w, h);

        Assert.Equal(w, g.CellWidth);
        Assert.Equal(h, g.CellHeight);
    }

    [Theory]
    [MemberData(nameof(RepresentativeCells))]
    public void BlockSplits_PartitionTheCellExactly(int w, int h)
    {
        CellGlyphGeometry g = CellGlyphGeometry.FromCellSize(w, h);

        Assert.Equal(h, g.TopHeight + g.BottomHeight);
        Assert.Equal(w, g.LeftWidth + g.RightWidth);
    }

    [Theory]
    [InlineData(20, 10, 10)]  // even height splits evenly
    [InlineData(21, 11, 10)]  // odd height: the extra row goes to the upper half
    [InlineData(1,   1,  0)]
    public void TopHeight_GivesTheExtraRowToTheUpperHalf(int height, int expectedTop, int expectedBottom)
    {
        CellGlyphGeometry g = CellGlyphGeometry.FromCellSize(10, height);

        Assert.Equal(expectedTop, g.TopHeight);
        Assert.Equal(expectedBottom, g.BottomHeight);
    }

    [Theory]
    [InlineData(12, 6, 6)]    // even width splits evenly
    [InlineData(11, 6, 5)]    // odd width: the extra column goes to the left half
    [InlineData(1,  1, 0)]
    public void LeftWidth_GivesTheExtraColumnToTheLeftHalf(int width, int expectedLeft, int expectedRight)
    {
        CellGlyphGeometry g = CellGlyphGeometry.FromCellSize(width, 10);

        Assert.Equal(expectedLeft, g.LeftWidth);
        Assert.Equal(expectedRight, g.RightWidth);
    }

    [Fact]
    public void AllCellSizesFrom1To40_ProduceUsableGeometry()
    {
        for (int w = 1; w <= 40; w++)
        {
            for (int h = 1; h <= 40; h++)
            {
                CellGlyphGeometry g = CellGlyphGeometry.FromCellSize(w, h);

                Assert.InRange(g.SingleStrokeThickness, 1, Math.Min(CellGlyphGeometry.MaxStrokeThickness, Math.Min(w, h)));
                Assert.InRange(g.DoubleStrokeThickness, 1, g.SingleStrokeThickness);

                Assert.InRange(g.SingleBandX, 0, w - g.SingleStrokeThickness);
                Assert.InRange(g.SingleBandY, 0, h - g.SingleStrokeThickness);

                Assert.InRange(g.DoubleBandX1, 0, w - g.DoubleStrokeThickness);
                Assert.InRange(g.DoubleBandX2, 0, w - g.DoubleStrokeThickness);
                Assert.InRange(g.DoubleBandY1, 0, h - g.DoubleStrokeThickness);
                Assert.InRange(g.DoubleBandY2, 0, h - g.DoubleStrokeThickness);

                Assert.True(g.DoubleBandX1 <= g.DoubleBandX2);
                Assert.True(g.DoubleBandY1 <= g.DoubleBandY2);

                if (g.DoubleVerticalSupported)
                    Assert.True(g.DoubleBandX1 + g.DoubleStrokeThickness <= g.DoubleBandX2,
                        $"rails overlap at {w}x{h}");

                if (g.DoubleHorizontalSupported)
                    Assert.True(g.DoubleBandY1 + g.DoubleStrokeThickness <= g.DoubleBandY2,
                        $"rails overlap at {w}x{h}");
            }
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, -5)]
    public void NonPositiveCellSizes_AreClampedRatherThanThrowing(int w, int h)
    {
        CellGlyphGeometry g = CellGlyphGeometry.FromCellSize(w, h);

        Assert.Equal(1, g.CellWidth);
        Assert.Equal(1, g.CellHeight);
    }

    [Fact]
    public void Geometry_DependsOnlyOnCellSize()
    {
        CellGlyphGeometry a = CellGlyphGeometry.FromCellSize(11, 20);
        CellGlyphGeometry b = CellGlyphGeometry.FromCellSize(11, 20);

        Assert.Equal(a, b);
    }
}
