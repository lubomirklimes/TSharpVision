using System.Runtime.InteropServices;
using TSharpVision.Drivers;

namespace TSharpVision.Graphics;

/// <summary>
/// Records the primitives a <see cref="GView"/>'s display list draws in one pass. Coordinates are logical pixels of
/// the cell grid (see <see cref="IPixelGraphicsDriver"/>); colours are 0xRRGGBBAA. Not thread-safe: used on the UI
/// thread while the view draws. The buffer is reused across passes, so drawing allocates only when it grows.
/// </summary>
/// <remarks>
/// <para><b>Shapes.</b> <see cref="DrawRect"/>, <see cref="DrawPolyline"/>, <see cref="DrawEllipse"/> and
/// <see cref="FillEllipse"/> are expanded here into the driver's <see cref="GraphicsPrimitiveKind.Line"/> and
/// <see cref="GraphicsPrimitiveKind.FillRect"/> primitives, so every backend draws them identically. Rectangles and
/// ellipses cover each of their pixels exactly once, so translucent colours blend uniformly.</para>
/// <para><b>Bounds.</b> Shapes are expanded only inside <c>[0, <see cref="ShapeLimit"/>)²</c>. The cell grid's logical
/// pixels are never negative and stay far below the limit, so no visible pixel is lost, while huge or negative
/// coordinates cost bounded work and cannot overflow. <see cref="DrawLine"/>, <see cref="FillRect"/> and
/// <see cref="DrawImage"/> record their arguments as given; the driver clips them.</para>
/// </remarks>
public sealed class GraphicsCanvas
{
    /// <summary>Exclusive upper bound of the logical coordinates that shapes are expanded into (both axes).</summary>
    public const int ShapeLimit = 32768;

    private readonly List<GraphicsPrimitive> _primitives = new();
    private readonly List<PixelImage> _images = new();

    /// <summary>Number of recorded primitives.</summary>
    public int Count => _primitives.Count;

    /// <summary>The recorded primitives, in drawing order. Valid until the next change to the canvas.</summary>
    public ReadOnlySpan<GraphicsPrimitive> Primitives => CollectionsMarshal.AsSpan(_primitives);

    /// <summary>The images the recorded image primitives refer to, each instance once, in first-use order.</summary>
    public ReadOnlySpan<PixelImage> Images => CollectionsMarshal.AsSpan(_images);

    /// <summary>
    /// Records <paramref name="image"/> scaled into the given rectangle. The canvas keeps a reference (the image is
    /// immutable); drawing the same instance again reuses it, so the driver uploads it once.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is null.</exception>
    public void DrawImage(PixelImage image, int x, int y, int width, int height,
        GraphicsImageFilter filter = GraphicsImageFilter.Nearest, byte opacity = 255)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (width <= 0 || height <= 0 || opacity == 0) return;
        int index = _images.IndexOf(image);
        if (index < 0) { index = _images.Count; _images.Add(image); }
        _primitives.Add(GraphicsPrimitive.Image(index, x, y, width, height, filter, opacity));
    }

    /// <summary>Records a line; both end points are drawn.</summary>
    public void DrawLine(int x1, int y1, int x2, int y2, uint rgba) =>
        _primitives.Add(GraphicsPrimitive.Line(x1, y1, x2, y2, rgba));

    /// <summary>Records a filled rectangle; empty sizes are ignored.</summary>
    public void FillRect(int x, int y, int width, int height, uint rgba)
    {
        if (width > 0 && height > 0)
            _primitives.Add(GraphicsPrimitive.FillRect(x, y, width, height, rgba));
    }

    /// <summary>
    /// Records the one-pixel outline of the rectangle <c>[x, x + width) × [y, y + height)</c>: its first and last
    /// rows and columns. Emitted as up to four non-overlapping filled rectangles (top row, bottom row, then the
    /// columns between them), so corners are not blended twice. Empty sizes are ignored.
    /// </summary>
    public void DrawRect(int x, int y, int width, int height, uint rgba)
    {
        if (width <= 0 || height <= 0) return;
        long x1 = (long)x + width, y1 = (long)y + height;
        AddRect(x, y, x1, y + 1L, rgba);
        if (height > 1) AddRect(x, y1 - 1, x1, y1, rgba);
        if (height > 2)
        {
            AddRect(x, y + 1L, x + 1L, y1 - 1, rgba);
            if (width > 1) AddRect(x1 - 1, y + 1L, x1, y1 - 1, rgba);
        }
    }

    /// <summary>
    /// Records lines through <paramref name="points"/> in order, and back to the first point when
    /// <paramref name="closed"/> is set and there are at least three points. One point draws that pixel.
    /// </summary>
    /// <remarks>
    /// Each segment is one <see cref="GraphicsPrimitiveKind.Line"/> including both end points, so every joint pixel
    /// (and, when closed, the first point) is drawn by both segments meeting there: with a translucent colour it is
    /// blended twice and looks slightly stronger. Use an opaque colour where that matters. Segments the driver would
    /// not draw (|dx| or |dy| above <see cref="GraphicsPrimitive.MaxLineExtent"/>) or that lie wholly outside the
    /// shape bounds are left out.
    /// </remarks>
    public void DrawPolyline(ReadOnlySpan<TPoint> points, uint rgba, bool closed = false)
    {
        if (points.Length == 1)
        {
            AddSegment(points[0], points[0], rgba);
            return;
        }
        for (int i = 1; i < points.Length; i++) AddSegment(points[i - 1], points[i], rgba);
        if (closed && points.Length >= 3) AddSegment(points[^1], points[0], rgba);
    }

    /// <summary>
    /// Records the filled ellipse inscribed in <c>[x, x + width) × [y, y + height)</c>, one filled rectangle per
    /// pixel row. Empty sizes are ignored.
    /// </summary>
    /// <remarks>
    /// Pixel set: pixel <c>(x + i, y + j)</c> with <c>0 ≤ i &lt; width</c>, <c>0 ≤ j &lt; height</c> is filled when its
    /// centre lies inside or on the ellipse: <c>(2i + 1 − w)²·h² + (2j + 1 − h)²·w² ≤ w²·h²</c> (exact integer test).
    /// The set is mirror-symmetric about both centre lines, each row is one contiguous run, and swapping width and
    /// height transposes the set. Ellipses two pixels thin may have empty end rows or columns.
    /// </remarks>
    public void FillEllipse(int x, int y, int width, int height, uint rgba)
    {
        if (width <= 0 || height <= 0) return;
        long jStart = Math.Max(0, -(long)y), jEnd = Math.Min(height, ShapeLimit - (long)y);
        for (long j = jStart; j < jEnd; j++)
            if (EllipseSpan(width, height, j, out long a, out long b))
                AddRect(x + a, y + j, x + b + 1, y + j + 1, rgba);
    }

    /// <summary>
    /// Records the one-pixel outline of the ellipse <see cref="FillEllipse"/> would fill: the filled pixels that have
    /// a 4-neighbour (left, right, above or below) outside the fill. The outline is 8-connected without gaps, has the
    /// fill's symmetry, and is emitted as at most two non-overlapping runs per row, so each pixel is drawn once.
    /// Empty sizes are ignored.
    /// </summary>
    public void DrawEllipse(int x, int y, int width, int height, uint rgba)
    {
        if (width <= 0 || height <= 0) return;
        long jStart = Math.Max(0, -(long)y), jEnd = Math.Min(height, ShapeLimit - (long)y);
        if (jStart >= jEnd) return;

        // Rolling spans of the rows above, at and below j.
        bool hasUp = EllipseSpan(width, height, jStart - 1, out long ua, out long ub);
        bool has = EllipseSpan(width, height, jStart, out long a, out long b);
        for (long j = jStart; j < jEnd; j++)
        {
            bool hasDown = EllipseSpan(width, height, j + 1, out long da, out long db);
            if (has)
            {
                long rowY = y + j;
                // Interior pixels: not a row end and covered by both neighbour rows.
                long lo = 0, hi = -1;
                if (hasUp && hasDown)
                {
                    lo = Math.Max(a + 1, Math.Max(ua, da));
                    hi = Math.Min(b - 1, Math.Min(ub, db));
                }
                if (lo > hi)
                    AddRect(x + a, rowY, x + b + 1, rowY + 1, rgba);
                else
                {
                    AddRect(x + a, rowY, x + lo, rowY + 1, rgba);
                    AddRect(x + hi + 1, rowY, x + b + 1, rowY + 1, rgba);
                }
            }
            (hasUp, ua, ub) = (has, a, b);
            (has, a, b) = (hasDown, da, db);
        }
    }

    // Discards the recorded primitives; the view does this before each pass. Not public, so a GObject cannot erase
    // what earlier objects of the same pass recorded.
    internal void Clear()
    {
        _primitives.Clear();
        _images.Clear();
    }

    // Columns a..b (relative, inclusive) of row j of the w x h ellipse; false when the row is empty or outside.
    // The largest |u| = |2i + 1 - w| with u ≡ w + 1 (mod 2) and u²h² ≤ w²(h² - v²), v = 2j + 1 - h, gives both ends.
    private static bool EllipseSpan(long w, long h, long j, out long a, out long b)
    {
        a = b = 0;
        if (j < 0 || j >= h) return false;
        long v = 2 * j + 1 - h;
        long rest = (h - v) * (h + v); // h² - v² > 0; fits because |v| < h < 2^31
        Int128 rhs = (Int128)w * w * rest, h2 = (Int128)h * h;
        long u = (long)(w * Math.Sqrt(rest) / h); // estimate, corrected exactly below
        while (u > 0 && (Int128)u * u * h2 > rhs) u--;
        while ((Int128)(u + 1) * (u + 1) * h2 <= rhs) u++;
        if (((u ^ (w + 1)) & 1) != 0) u--;
        if (u < 0) return false;
        a = (w - 1 - u) / 2;
        b = (w - 1 + u) / 2;
        return true;
    }

    // The part of [x0, x1) x [y0, y1) inside the shape bounds, as one FillRect.
    private void AddRect(long x0, long y0, long x1, long y1, uint rgba)
    {
        x0 = Math.Max(x0, 0); y0 = Math.Max(y0, 0);
        x1 = Math.Min(x1, ShapeLimit); y1 = Math.Min(y1, ShapeLimit);
        if (x0 < x1 && y0 < y1)
            _primitives.Add(GraphicsPrimitive.FillRect((int)x0, (int)y0, (int)(x1 - x0), (int)(y1 - y0), rgba));
    }

    private void AddSegment(TPoint p, TPoint q, uint rgba)
    {
        long dx = (long)q.x - p.x, dy = (long)q.y - p.y;
        if (Math.Abs(dx) > GraphicsPrimitive.MaxLineExtent || Math.Abs(dy) > GraphicsPrimitive.MaxLineExtent) return;
        if (Math.Max(p.x, q.x) < 0 || Math.Min(p.x, q.x) >= ShapeLimit
            || Math.Max(p.y, q.y) < 0 || Math.Min(p.y, q.y) >= ShapeLimit) return;
        _primitives.Add(GraphicsPrimitive.Line(p.x, p.y, q.x, q.y, rgba));
    }
}
