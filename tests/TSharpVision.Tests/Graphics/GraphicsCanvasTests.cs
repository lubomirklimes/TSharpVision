using TSharpVision.Drivers;
using TSharpVision.Graphics;
using Xunit;

namespace TSharpVision.Tests.Graphics;

/// <summary>
/// TV12o: the canvas shapes (DrawRect, DrawPolyline, DrawEllipse, FillEllipse) checked as exact pixel sets. A tiny
/// CPU reference applies the driver contract's Line and FillRect semantics at logical scale and counts how often each
/// pixel is covered, so double blending of translucent colours shows up as a count above one.
/// </summary>
public sealed class GraphicsCanvasTests
{
    // ── CPU reference (logical pixels, unscaled) ─────────────────────────

    private static Dictionary<(int X, int Y), int> Coverage(ReadOnlySpan<GraphicsPrimitive> primitives)
    {
        var cover = new Dictionary<(int, int), int>();
        void Hit(int x, int y) => cover[(x, y)] = cover.GetValueOrDefault((x, y)) + 1;
        foreach (GraphicsPrimitive p in primitives)
        {
            switch (p.Kind)
            {
                case GraphicsPrimitiveKind.FillRect:
                    for (int y = p.Y1; y < p.Y2; y++)
                        for (int x = p.X1; x < p.X2; x++) Hit(x, y);
                    break;
                case GraphicsPrimitiveKind.Line:
                    foreach (var pt in LinePixels(p.X1, p.Y1, p.X2, p.Y2)) Hit(pt.X, pt.Y);
                    break;
                default:
                    throw new InvalidOperationException($"unexpected {p.Kind}");
            }
        }
        return cover;
    }

    private static long FloorDiv(long a, long b) => a / b - ((a % b != 0) && ((a < 0) != (b < 0)) ? 1 : 0);

    // The contract's line formula (common rules), including the MaxLineExtent skip.
    internal static IEnumerable<(int X, int Y)> LinePixels(int x1, int y1, int x2, int y2)
    {
        long ddx = (long)x2 - x1, ddy = (long)y2 - y1;
        if (Math.Abs(ddx) > GraphicsPrimitive.MaxLineExtent || Math.Abs(ddy) > GraphicsPrimitive.MaxLineExtent) yield break;
        if (Math.Abs(ddx) >= Math.Abs(ddy))
        {
            if (x1 > x2) (x1, y1, x2, y2) = (x2, y2, x1, y1);
            long dx = x2 - x1, dy = y2 - y1;
            if (dx == 0) { yield return (x1, y1); yield break; }
            for (long x = x1; x <= x2; x++) yield return ((int)x, (int)(y1 + FloorDiv(2 * (x - x1) * dy + dx, 2 * dx)));
        }
        else
        {
            if (y1 > y2) (x1, y1, x2, y2) = (x2, y2, x1, y1);
            long dx = x2 - x1, dy = y2 - y1;
            for (long y = y1; y <= y2; y++) yield return ((int)(x1 + FloorDiv(2 * (y - y1) * dx + dy, 2 * dy)), (int)y);
        }
    }

    // Brute-force definition of the ellipse fill (pixel centre inside or on the inscribed ellipse).
    private static HashSet<(int X, int Y)> EllipseFill(int x, int y, int w, int h)
    {
        var set = new HashSet<(int, int)>();
        long w2 = (long)w * w, h2 = (long)h * h;
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                long u = 2L * i + 1 - w, v = 2L * j + 1 - h;
                if (u * u * h2 + v * v * w2 <= w2 * h2) set.Add((x + i, y + j));
            }
        return set;
    }

    // Fill pixels with a 4-neighbour outside the fill.
    private static HashSet<(int X, int Y)> EllipseOutline(int x, int y, int w, int h)
    {
        var fill = EllipseFill(x, y, w, h);
        return fill.Where(p => !fill.Contains((p.X - 1, p.Y)) || !fill.Contains((p.X + 1, p.Y))
            || !fill.Contains((p.X, p.Y - 1)) || !fill.Contains((p.X, p.Y + 1))).ToHashSet();
    }

    private static bool EightConnected(IReadOnlyCollection<(int X, int Y)> set)
    {
        if (set.Count == 0) return true;
        var all = set.ToHashSet();
        var seen = new HashSet<(int, int)> { set.First() };
        var queue = new Queue<(int X, int Y)>(seen);
        while (queue.Count > 0)
        {
            var (px, py) = queue.Dequeue();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (all.Contains((px + dx, py + dy)) && seen.Add((px + dx, py + dy))) queue.Enqueue((px + dx, py + dy));
        }
        return seen.Count == all.Count;
    }

    private static void AssertEachPixelOnce(Dictionary<(int X, int Y), int> cover) =>
        Assert.All(cover, kv => Assert.Equal(1, kv.Value));

    // ── DrawRect ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 6)]
    [InlineData(6, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 5)]
    [InlineData(5, 2)]
    [InlineData(3, 3)]
    [InlineData(9, 4)]
    public void Rect_outline_is_the_border_and_covers_each_pixel_once(int w, int h)
    {
        var canvas = new GraphicsCanvas();
        canvas.DrawRect(10, 20, w, h, 0xFFFFFF80);
        var cover = Coverage(canvas.Primitives);
        var expected = new HashSet<(int, int)>();
        for (int y = 20; y < 20 + h; y++)
            for (int x = 10; x < 10 + w; x++)
                if (x == 10 || y == 20 || x == 10 + w - 1 || y == 20 + h - 1) expected.Add((x, y));
        Assert.Equal(expected, cover.Keys.ToHashSet());
        AssertEachPixelOnce(cover);
        Assert.InRange(canvas.Count, 1, 4);
        Assert.All(canvas.Primitives.ToArray(), p => Assert.Equal(GraphicsPrimitiveKind.FillRect, p.Kind));
    }

    [Fact]
    public void Rect_with_empty_size_draws_nothing()
    {
        var canvas = new GraphicsCanvas();
        canvas.DrawRect(0, 0, 0, 5, 0xFF);
        canvas.DrawRect(0, 0, 5, -1, 0xFF);
        Assert.Equal(0, canvas.Count);
    }

    [Fact]
    public void Huge_and_negative_rects_are_cut_to_the_shape_bounds_without_overflow()
    {
        var canvas = new GraphicsCanvas();
        canvas.DrawRect(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue, 0xFF); // ends at (-2, -2): invisible
        Assert.Equal(0, canvas.Count);

        canvas.DrawRect(-1_000_000, 5, int.MaxValue, 10, 0xFF);
        Assert.InRange(canvas.Count, 1, 4);
        foreach (GraphicsPrimitive p in canvas.Primitives)
        {
            Assert.InRange(p.X1, 0, GraphicsCanvas.ShapeLimit);
            Assert.InRange(p.X2, 0, GraphicsCanvas.ShapeLimit);
            Assert.True(p.X1 < p.X2 && p.Y1 < p.Y2);
        }
        // The visible part: top row 5 and bottom row 14 run across the window, no left or right edge in it.
        var visible = Coverage(canvas.Primitives).Keys.Where(k => k.X < 100).ToHashSet();
        var expected = Enumerable.Range(0, 100).SelectMany(x => new[] { (x, 5), (x, 14) }).ToHashSet();
        Assert.Equal(expected, visible);
    }

    // ── DrawPolyline ─────────────────────────────────────────────────────

    [Fact]
    public void Polyline_records_one_line_per_segment_and_closes_on_request()
    {
        TPoint[] pts = [new(0, 0), new(10, 3), new(4, 12), new(-3, 5)];
        var canvas = new GraphicsCanvas();
        canvas.DrawPolyline(pts, 0x11223344);
        Assert.Equal(
            [GraphicsPrimitive.Line(0, 0, 10, 3, 0x11223344), GraphicsPrimitive.Line(10, 3, 4, 12, 0x11223344),
             GraphicsPrimitive.Line(4, 12, -3, 5, 0x11223344)],
            canvas.Primitives.ToArray());

        canvas.Clear();
        canvas.DrawPolyline(pts, 0xFF, closed: true);
        Assert.Equal(4, canvas.Count);
        Assert.Equal(GraphicsPrimitive.Line(-3, 5, 0, 0, 0xFF), canvas.Primitives[3]);

        // Pixel set = union of the segment lines; inner joints are covered twice (documented double blend).
        var cover = Coverage(canvas.Primitives);
        var union = pts.Zip(pts.Skip(1).Append(pts[0])).SelectMany(s => LinePixels(s.First.x, s.First.y, s.Second.x, s.Second.y)).ToHashSet();
        Assert.Equal(union, cover.Keys.ToHashSet());
        Assert.All(pts, p => Assert.Equal(2, cover[(p.x, p.y)]));
    }

    [Fact]
    public void Polyline_edge_cases_single_point_two_points_closed_and_empty()
    {
        var canvas = new GraphicsCanvas();
        canvas.DrawPolyline([], 0xFF);
        Assert.Equal(0, canvas.Count);
        canvas.DrawPolyline([new TPoint(7, 8)], 0xFF);
        Assert.Equal([GraphicsPrimitive.Line(7, 8, 7, 8, 0xFF)], canvas.Primitives.ToArray());
        canvas.Clear();
        canvas.DrawPolyline([new TPoint(0, 0), new TPoint(5, 5)], 0xFF, closed: true); // not closed back over itself
        Assert.Equal(1, canvas.Count);
    }

    [Fact]
    public void Polyline_leaves_out_segments_the_driver_would_skip_or_that_lie_outside()
    {
        var canvas = new GraphicsCanvas();
        canvas.DrawPolyline(
        [
            new(0, 0), new(int.MaxValue, 0),          // too long (and would overflow dx)
            new(int.MaxValue, 100), new(int.MinValue, 100), // too long
            new(-50, -50), new(-10, -20),             // wholly left/above the grid
            new(5, 30), new(20, 30),                  // partly and wholly visible: kept
        ], 0xFF);
        Assert.Equal(
            [GraphicsPrimitive.Line(-10, -20, 5, 30, 0xFF), GraphicsPrimitive.Line(5, 30, 20, 30, 0xFF)],
            canvas.Primitives.ToArray());
    }

    // ── Ellipses ─────────────────────────────────────────────────────────

    public static IEnumerable<object[]> EllipseSizes()
    {
        for (int w = 1; w <= 14; w++)
            for (int h = 1; h <= 14; h++) yield return [w, h];
        yield return [31, 7];
        yield return [7, 31];
        yield return [64, 40];
        yield return [101, 99];
    }

    [Theory]
    [MemberData(nameof(EllipseSizes))]
    public void Ellipse_fill_matches_the_centre_rule_with_one_rect_per_row(int w, int h)
    {
        var canvas = new GraphicsCanvas();
        canvas.FillEllipse(3, 5, w, h, 0xFF000080);
        var cover = Coverage(canvas.Primitives);
        Assert.Equal(EllipseFill(3, 5, w, h), cover.Keys.ToHashSet());
        AssertEachPixelOnce(cover);
        GraphicsPrimitive[] prims = canvas.Primitives.ToArray();
        Assert.All(prims, p => Assert.Equal(1, p.Y2 - p.Y1));
        Assert.Equal(prims.Length, prims.Select(p => p.Y1).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(EllipseSizes))]
    public void Ellipse_outline_is_the_fill_border_symmetric_eight_connected_and_drawn_once(int w, int h)
    {
        var canvas = new GraphicsCanvas();
        canvas.DrawEllipse(3, 5, w, h, 0xFFFFFF80);
        var cover = Coverage(canvas.Primitives);
        var set = cover.Keys.ToHashSet();
        Assert.Equal(EllipseOutline(3, 5, w, h), set);
        Assert.Subset(EllipseFill(3, 5, w, h), set);
        AssertEachPixelOnce(cover);
        Assert.True(EightConnected(set), $"{w}x{h} outline has a gap");
        // Mirror symmetry about both centre lines.
        Assert.All(set, p => Assert.Contains((2 * 3 + w - 1 - p.X, p.Y), set));
        Assert.All(set, p => Assert.Contains((p.X, 2 * 5 + h - 1 - p.Y), set));
        // At most two runs per row.
        Assert.All(canvas.Primitives.ToArray().GroupBy(p => p.Y1), g => Assert.InRange(g.Count(), 1, 2));

        // Swapping width and height transposes the set.
        var transposed = new GraphicsCanvas();
        transposed.DrawEllipse(5, 3, h, w, 0xFF);
        Assert.Equal(set.Select(p => (p.Y, p.X)).ToHashSet(), Coverage(transposed.Primitives).Keys.ToHashSet());
    }

    [Fact]
    public void Empty_ellipses_draw_nothing()
    {
        var canvas = new GraphicsCanvas();
        canvas.FillEllipse(0, 0, 0, 10, 0xFF);
        canvas.DrawEllipse(0, 0, 10, 0, 0xFF);
        canvas.DrawEllipse(0, 0, -5, -5, 0xFF);
        Assert.Equal(0, canvas.Count);
    }

    [Fact]
    public void Huge_ellipses_cost_bounded_work_and_stay_exact_where_visible()
    {
        var canvas = new GraphicsCanvas();
        canvas.FillEllipse(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue, 0xFF); // entirely at negative coordinates
        canvas.DrawEllipse(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue, 0xFF);
        Assert.Equal(0, canvas.Count);

        // A 2^31-pixel circle whose top touches row 0 above x = 0: only rows inside [0, ShapeLimit) are expanded.
        int size = int.MaxValue, half = size / 2;
        canvas.DrawEllipse(-half, 0, size, size, 0xFF);
        Assert.InRange(canvas.Count, 1, 2 * GraphicsCanvas.ShapeLimit);
        Assert.All(canvas.Primitives.ToArray(), p => Assert.True(p.X1 >= 0 && p.X2 <= GraphicsCanvas.ShapeLimit && p.Y2 <= GraphicsCanvas.ShapeLimit));
        Assert.Contains(canvas.Primitives.ToArray(), p => p.Y1 == 0 && p.X1 == 0); // the top arc crosses x = 0

        // Centred on the origin: the fill's rows are clipped to [0, ShapeLimit).
        canvas.Clear();
        canvas.FillEllipse(-half, -half, size, size, 0xFF);
        Assert.InRange(canvas.Count, 1, GraphicsCanvas.ShapeLimit);
        foreach (GraphicsPrimitive p in canvas.Primitives)
        {
            Assert.InRange(p.X1, 0, GraphicsCanvas.ShapeLimit);
            Assert.InRange(p.X2, 0, GraphicsCanvas.ShapeLimit);
            Assert.InRange(p.Y1, 0, GraphicsCanvas.ShapeLimit - 1);
        }
        // The centre region lies well inside: row 0 is filled across the whole shape bound.
        Assert.Equal(GraphicsPrimitive.FillRect(0, 0, GraphicsCanvas.ShapeLimit, 1, 0xFF), canvas.Primitives[0]);

        // An ellipse whose right part leaves the shape bound keeps the exact pixels inside it.
        canvas.Clear();
        int x = GraphicsCanvas.ShapeLimit - 20;
        canvas.DrawEllipse(x, 10, 40, 30, 0xFF);
        var expected = EllipseOutline(x, 10, 40, 30).Where(p => p.X < GraphicsCanvas.ShapeLimit).ToHashSet();
        Assert.Equal(expected, Coverage(canvas.Primitives).Keys.ToHashSet());
    }

    [Fact]
    public void Shapes_keep_their_order_and_colour()
    {
        var canvas = new GraphicsCanvas();
        canvas.DrawRect(0, 0, 4, 4, 0x01);
        canvas.FillEllipse(0, 0, 4, 4, 0x02);
        canvas.DrawEllipse(0, 0, 4, 4, 0x03);
        uint[] colours = canvas.Primitives.ToArray().Select(p => p.Rgba).ToArray();
        Assert.Equal(colours.OrderBy(c => c), colours);
        Assert.Equal([0x01u, 0x02u, 0x03u], colours.Distinct());
    }
}
