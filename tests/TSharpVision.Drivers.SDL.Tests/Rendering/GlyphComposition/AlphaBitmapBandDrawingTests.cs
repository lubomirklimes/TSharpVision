using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;
using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Drivers.SDL.Tests.Rendering.GlyphComposition;

/// <summary>
/// Covers the band helpers added for the generator. The existing centred line helpers are
/// unchanged and remain covered by <c>AlphaBitmapDrawingTests</c>.
/// </summary>
public class AlphaBitmapBandDrawingTests
{
    [Fact]
    public void DrawHorizontalBand_PaintsExactlyThicknessRowsStartingAtY()
    {
        AlphaBitmap b = AlphaBitmap.CreateEmpty(6, 6);

        AlphaBitmapDrawing.DrawHorizontalBand(b, y: 2, thickness: 2, x1: 1, x2: 4);

        for (int x = 0; x < 6; x++)
        {
            Assert.Equal(0, b[x, 1]);
            Assert.Equal(x is >= 1 and <= 4 ? 255 : 0, b[x, 2]);
            Assert.Equal(x is >= 1 and <= 4 ? 255 : 0, b[x, 3]);
            Assert.Equal(0, b[x, 4]);
        }
    }

    [Fact]
    public void DrawVerticalBand_PaintsExactlyThicknessColumnsStartingAtX()
    {
        AlphaBitmap b = AlphaBitmap.CreateEmpty(6, 6);

        AlphaBitmapDrawing.DrawVerticalBand(b, x: 3, thickness: 2, y1: 0, y2: 5);

        for (int y = 0; y < 6; y++)
        {
            Assert.Equal(0, b[2, y]);
            Assert.Equal(255, b[3, y]);
            Assert.Equal(255, b[4, y]);
            Assert.Equal(0, b[5, y]);
        }
    }

    [Fact]
    public void Bands_ClipToTheBitmapAndAcceptReversedRanges()
    {
        AlphaBitmap b = AlphaBitmap.CreateEmpty(4, 4);

        AlphaBitmapDrawing.DrawHorizontalBand(b, y: 3, thickness: 5, x1: 6, x2: -2);

        for (int x = 0; x < 4; x++)
            Assert.Equal(255, b[x, 3]);
    }

    [Fact]
    public void Bands_WithNonPositiveThickness_DoNothing()
    {
        AlphaBitmap b = AlphaBitmap.CreateEmpty(4, 4);

        AlphaBitmapDrawing.DrawHorizontalBand(b, 1, 0, 0, 3);
        AlphaBitmapDrawing.DrawVerticalBand(b, 1, -1, 0, 3);

        Assert.All(b.Alpha, a => Assert.Equal(0, a));
    }

    [Fact]
    public void FillRect_UsesMaxAlphaCompositing()
    {
        AlphaBitmap b = AlphaBitmap.CreateEmpty(3, 3);

        AlphaBitmapDrawing.FillRect(b, 0, 0, 2, 2, 200);
        AlphaBitmapDrawing.FillRect(b, 0, 0, 2, 2, 100);   // lower alpha must not win

        Assert.Equal(200, b[1, 1]);
    }

    [Fact]
    public void ClearRect_ZeroesUnconditionally()
    {
        AlphaBitmap b = AlphaBitmap.CreateEmpty(4, 4);
        AlphaBitmapDrawing.FillRect(b, 0, 0, 3, 3);

        AlphaBitmapDrawing.ClearRect(b, 1, 1, 2, 2);

        Assert.Equal(0, b[1, 1]);
        Assert.Equal(0, b[2, 2]);
        Assert.Equal(255, b[0, 0]);
        Assert.Equal(255, b[3, 3]);
    }

    [Fact]
    public void ClearRect_WithAnEmptyRange_IsANoOp()
    {
        AlphaBitmap b = AlphaBitmap.CreateEmpty(3, 3);
        AlphaBitmapDrawing.FillRect(b, 0, 0, 2, 2);

        AlphaBitmapDrawing.ClearRect(b, 2, 0, 1, 2);   // x1 > x2

        Assert.All(b.Alpha, a => Assert.Equal(255, a));
    }
}
