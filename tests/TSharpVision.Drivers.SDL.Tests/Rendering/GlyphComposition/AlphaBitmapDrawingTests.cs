using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;
using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;
using Xunit;

namespace TSharpVision.Drivers.SDL.Tests.Rendering.GlyphComposition;

public sealed class AlphaBitmapDrawingTests
{
    // ── DrawHorizontalLine ──────────────────────────────────────────────────

    [Fact]
    public void DrawHorizontalLine_DrawsExpectedPixels()
    {
        var target = AlphaBitmap.CreateEmpty(11, 20);

        AlphaBitmapDrawing.DrawHorizontalLine(target, y: 10, x1: 0, x2: 10, thickness: 1);

        // Every pixel in the drawn row must have ink.
        for (int x = 0; x <= 10; x++)
            Assert.Equal(255, target[x, 10]);

        // Adjacent rows must be empty.
        for (int x = 0; x <= 10; x++)
        {
            Assert.Equal(0, target[x, 9]);
            Assert.Equal(0, target[x, 11]);
        }
    }

    [Fact]
    public void DrawHorizontalLine_WithThickness2_DrawsTwoRows()
    {
        var target = AlphaBitmap.CreateEmpty(10, 10);

        // thickness=2, half=1 → yStart=4, yEnd=5
        AlphaBitmapDrawing.DrawHorizontalLine(target, y: 5, x1: 2, x2: 7, thickness: 2);

        Assert.Equal(255, target[4, 4]);
        Assert.Equal(255, target[4, 5]);
        Assert.Equal(0,   target[4, 3]);
        Assert.Equal(0,   target[4, 6]);
    }

    [Fact]
    public void DrawHorizontalLine_NormalizesReversedX()
    {
        var target = AlphaBitmap.CreateEmpty(10, 10);

        AlphaBitmapDrawing.DrawHorizontalLine(target, y: 5, x1: 8, x2: 2, thickness: 1);

        for (int x = 2; x <= 8; x++)
            Assert.Equal(255, target[x, 5]);
    }

    // ── DrawVerticalLine ────────────────────────────────────────────────────

    [Fact]
    public void DrawVerticalLine_DrawsExpectedPixels()
    {
        var target = AlphaBitmap.CreateEmpty(11, 20);

        AlphaBitmapDrawing.DrawVerticalLine(target, x: 5, y1: 0, y2: 19, thickness: 1);

        for (int y = 0; y <= 19; y++)
            Assert.Equal(255, target[5, y]);

        // Adjacent columns must be empty.
        for (int y = 0; y <= 19; y++)
        {
            Assert.Equal(0, target[4, y]);
            Assert.Equal(0, target[6, y]);
        }
    }

    [Fact]
    public void DrawVerticalLine_NormalizesReversedY()
    {
        var target = AlphaBitmap.CreateEmpty(10, 10);

        AlphaBitmapDrawing.DrawVerticalLine(target, x: 3, y1: 8, y2: 2, thickness: 1);

        for (int y = 2; y <= 8; y++)
            Assert.Equal(255, target[3, y]);
    }

    // ── Clipping ────────────────────────────────────────────────────────────

    [Fact]
    public void DrawHorizontalLine_ClipsSafelyOutsideBounds()
    {
        var target = AlphaBitmap.CreateEmpty(5, 5);

        // y is out of bounds — should not throw.
        AlphaBitmapDrawing.DrawHorizontalLine(target, y: 10, x1: 0, x2: 4, thickness: 1);
        AlphaBitmapDrawing.DrawHorizontalLine(target, y: -1, x1: 0, x2: 4, thickness: 1);

        // x range extends beyond width — partial draw only within bounds.
        AlphaBitmapDrawing.DrawHorizontalLine(target, y: 2, x1: -2, x2: 10, thickness: 1);

        for (int x = 0; x < 5; x++)
            Assert.Equal(255, target[x, 2]);
    }

    [Fact]
    public void DrawVerticalLine_ClipsSafelyOutsideBounds()
    {
        var target = AlphaBitmap.CreateEmpty(5, 5);

        // x is out of bounds — should not throw.
        AlphaBitmapDrawing.DrawVerticalLine(target, x: 10, y1: 0, y2: 4, thickness: 1);
        AlphaBitmapDrawing.DrawVerticalLine(target, x: -1, y1: 0, y2: 4, thickness: 1);

        // y range extends beyond height — partial draw only within bounds.
        AlphaBitmapDrawing.DrawVerticalLine(target, x: 2, y1: -2, y2: 10, thickness: 1);

        for (int y = 0; y < 5; y++)
            Assert.Equal(255, target[2, y]);
    }

    // ── Blit ────────────────────────────────────────────────────────────────

    [Fact]
    public void Blit_CopiesSourceAlphaToTargetWithOffset()
    {
        var source = AlphaBitmap.CreateEmpty(3, 3);
        source.Alpha[1 * 3 + 1] = 200; // center pixel

        var target = AlphaBitmap.CreateEmpty(10, 10);

        AlphaBitmapDrawing.Blit(source, target, offsetX: 2, offsetY: 3);

        // Center of source (1,1) maps to target (3,4).
        Assert.Equal(200, target[3, 4]);

        // Source corners map to target (2,3), (4,3), (2,5), (4,5) — all were 0.
        Assert.Equal(0, target[2, 3]);
        Assert.Equal(0, target[4, 5]);
    }

    [Fact]
    public void Blit_ClipsOutsideBounds()
    {
        var source = AlphaBitmap.CreateEmpty(5, 5);
        for (int i = 0; i < source.Alpha.Length; i++)
            source.Alpha[i] = 255;

        var target = AlphaBitmap.CreateEmpty(4, 4);

        // Large negative offset — only partially overlapping.
        AlphaBitmapDrawing.Blit(source, target, offsetX: -3, offsetY: -3);

        // Only pixel (0,0) through (1,1) of target should be touched (source[3,3]..[4,4]).
        Assert.Equal(255, target[0, 0]);
        Assert.Equal(255, target[1, 1]);

        // Blit entirely outside target — no throw, nothing changed.
        var empty = AlphaBitmap.CreateEmpty(4, 4);
        AlphaBitmapDrawing.Blit(source, empty, offsetX: 100, offsetY: 100);
        Assert.Equal(0, empty[0, 0]);
    }

    [Fact]
    public void Blit_UsesMaxAlpha_NotOverwriteWithLowerAlpha()
    {
        var source = AlphaBitmap.CreateEmpty(3, 3);
        source.Alpha[0] = 100; // top-left of source

        var target = AlphaBitmap.CreateEmpty(3, 3);
        target.Alpha[0] = 200; // already has higher alpha

        AlphaBitmapDrawing.Blit(source, target, offsetX: 0, offsetY: 0);

        // Target should retain its higher value.
        Assert.Equal(200, target[0, 0]);
    }
}
