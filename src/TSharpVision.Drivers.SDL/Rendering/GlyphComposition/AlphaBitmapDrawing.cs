using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

internal static class AlphaBitmapDrawing
{
    /// <summary>
    /// Draws a horizontal line of given thickness centered on y, from x1 to x2 (inclusive).
    /// Clips safely to target bounds. Uses max-alpha compositing.
    /// </summary>
    public static void DrawHorizontalLine(
        AlphaBitmap target,
        int y,
        int x1,
        int x2,
        int thickness,
        byte alpha = 255)
    {
        if (x1 > x2)
            (x1, x2) = (x2, x1);

        int half = thickness / 2;
        int yStart = y - half;
        int yEnd   = yStart + thickness - 1;

        for (int row = yStart; row <= yEnd; row++)
        {
            if ((uint)row >= (uint)target.Height)
                continue;

            for (int col = x1; col <= x2; col++)
            {
                if ((uint)col >= (uint)target.Width)
                    continue;

                int idx = row * target.Width + col;
                if (target.Alpha[idx] < alpha)
                    target.Alpha[idx] = alpha;
            }
        }
    }

    /// <summary>
    /// Draws a vertical line of given thickness centered on x, from y1 to y2 (inclusive).
    /// Clips safely to target bounds. Uses max-alpha compositing.
    /// </summary>
    public static void DrawVerticalLine(
        AlphaBitmap target,
        int x,
        int y1,
        int y2,
        int thickness,
        byte alpha = 255)
    {
        if (y1 > y2)
            (y1, y2) = (y2, y1);

        int half   = thickness / 2;
        int xStart = x - half;
        int xEnd   = xStart + thickness - 1;

        for (int col = xStart; col <= xEnd; col++)
        {
            if ((uint)col >= (uint)target.Width)
                continue;

            for (int row = y1; row <= y2; row++)
            {
                if ((uint)row >= (uint)target.Height)
                    continue;

                int idx = row * target.Width + col;
                if (target.Alpha[idx] < alpha)
                    target.Alpha[idx] = alpha;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Band helpers
    // ─────────────────────────────────────────────────────────────────────────
    // DrawHorizontalLine / DrawVerticalLine above centre a stroke on a coordinate
    // (half = thickness / 2), which is asymmetric for even thicknesses. The band helpers
    // below take the FIRST row/column of the stroke instead, so the painted range is exactly
    // [start, start + thickness - 1] with no rounding. CellGlyphGeometry and
    // Cp437GlyphGenerator use these.

    /// <summary>
    /// Fills rows <c>[y, y + thickness - 1]</c> across columns <c>[x1, x2]</c> (inclusive).
    /// Clips safely to target bounds. Uses max-alpha compositing.
    /// </summary>
    public static void DrawHorizontalBand(
        AlphaBitmap target,
        int y,
        int thickness,
        int x1,
        int x2,
        byte alpha = 255)
    {
        if (thickness <= 0) return;
        if (x1 > x2) (x1, x2) = (x2, x1);

        FillRect(target, x1, y, x2, y + thickness - 1, alpha);
    }

    /// <summary>
    /// Fills columns <c>[x, x + thickness - 1]</c> down rows <c>[y1, y2]</c> (inclusive).
    /// Clips safely to target bounds. Uses max-alpha compositing.
    /// </summary>
    public static void DrawVerticalBand(
        AlphaBitmap target,
        int x,
        int thickness,
        int y1,
        int y2,
        byte alpha = 255)
    {
        if (thickness <= 0) return;
        if (y1 > y2) (y1, y2) = (y2, y1);

        FillRect(target, x, y1, x + thickness - 1, y2, alpha);
    }

    /// <summary>
    /// Sets every pixel of the inclusive rectangle to <paramref name="alpha"/> when it is
    /// larger than the current value. Coordinates are clipped to the bitmap.
    /// </summary>
    public static void FillRect(
        AlphaBitmap target,
        int x1, int y1, int x2, int y2,
        byte alpha = 255)
    {
        if (x1 > x2) (x1, x2) = (x2, x1);
        if (y1 > y2) (y1, y2) = (y2, y1);

        int xa = Math.Max(0, x1);
        int xb = Math.Min(target.Width  - 1, x2);
        int ya = Math.Max(0, y1);
        int yb = Math.Min(target.Height - 1, y2);

        for (int row = ya; row <= yb; row++)
        {
            int rowBase = row * target.Width;
            for (int col = xa; col <= xb; col++)
            {
                int idx = rowBase + col;
                if (target.Alpha[idx] < alpha)
                    target.Alpha[idx] = alpha;
            }
        }
    }

    /// <summary>
    /// Unconditionally sets every pixel of the inclusive rectangle to zero.
    /// Used to open the channel of a double line where a perpendicular double line passes
    /// through, which is what gives CP437 junctions such as <c>╬</c> their four-corner look.
    /// Coordinates are clipped to the bitmap; an empty range is a no-op.
    /// </summary>
    public static void ClearRect(
        AlphaBitmap target,
        int x1, int y1, int x2, int y2)
    {
        if (x1 > x2 || y1 > y2) return;

        int xa = Math.Max(0, x1);
        int xb = Math.Min(target.Width  - 1, x2);
        int ya = Math.Max(0, y1);
        int yb = Math.Min(target.Height - 1, y2);

        for (int row = ya; row <= yb; row++)
        {
            int rowBase = row * target.Width;
            for (int col = xa; col <= xb; col++)
                target.Alpha[rowBase + col] = 0;
        }
    }

    /// <summary>
    /// Blits source onto target at (offsetX, offsetY) using max-alpha compositing.
    /// Out-of-bounds source pixels are silently clipped.
    /// </summary>
    public static void Blit(
        AlphaBitmap source,
        AlphaBitmap target,
        int offsetX,
        int offsetY)
    {
        for (int sy = 0; sy < source.Height; sy++)
        {
            int ty = sy + offsetY;
            if ((uint)ty >= (uint)target.Height)
                continue;

            for (int sx = 0; sx < source.Width; sx++)
            {
                int tx = sx + offsetX;
                if ((uint)tx >= (uint)target.Width)
                    continue;

                byte srcAlpha = source.Alpha[sy * source.Width + sx];
                int  tidx     = ty * target.Width + tx;
                if (target.Alpha[tidx] < srcAlpha)
                    target.Alpha[tidx] = srcAlpha;
            }
        }
    }
}
