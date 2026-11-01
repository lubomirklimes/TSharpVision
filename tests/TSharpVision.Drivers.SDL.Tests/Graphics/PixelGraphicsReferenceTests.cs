using TSharpVision.Drivers.SDL.Graphics;

namespace TSharpVision.Drivers.SDL.Tests.Graphics;

/// <summary>
/// TV12o CPU reference: the exact integer line formula (coalesced runs, symmetry, end points, connectivity, extent
/// limit, HiDPI blocks) and image rendering in <see cref="PixelGraphicsRasterizer"/> (nearest and bilinear sampling,
/// opacity, straight-alpha source-over, clipping, scale, out-of-range index, drawing order, huge rectangles), plus
/// the layer store with images. These are the semantics the GPU shader and the classic SDL renderer must match.
/// </summary>
public sealed class PixelGraphicsReferenceTests
{
    private const uint Red = 0xFF0000FF, Blue = 0x0000FFFF;

    // ── Line formula ────────────────────────────────────────────────────

    private static long FloorDiv(long a, long b) => (long)Math.Floor((double)a / b);

    // The documented pixel set, evaluated directly (independent of the run-walking implementation).
    private static HashSet<(int, int)> Reference(int x1, int y1, int x2, int y2)
    {
        var set = new HashSet<(int, int)>();
        if (Math.Abs((long)x2 - x1) >= Math.Abs((long)y2 - y1))
        {
            if (x1 > x2) (x1, y1, x2, y2) = (x2, y2, x1, y1);
            long dx = (long)x2 - x1, dy = (long)y2 - y1;
            if (dx == 0) { set.Add((x1, y1)); return set; }
            for (long x = x1; x <= x2; x++) set.Add(((int)x, (int)(y1 + FloorDiv(2 * (x - x1) * dy + dx, 2 * dx))));
        }
        else
        {
            if (y1 > y2) (x1, y1, x2, y2) = (x2, y2, x1, y1);
            long dx = (long)x2 - x1, dy = (long)y2 - y1;
            for (long y = y1; y <= y2; y++) set.Add(((int)(x1 + FloorDiv(2 * (y - y1) * dx + dy, 2 * dy)), (int)y));
        }
        return set;
    }

    private static List<DeviceRect> Rects(GraphicsPrimitive p, PixelScale? scale = null)
    {
        var list = new List<DeviceRect>();
        PixelGraphicsGeometry.AppendRects(p, scale ?? PixelScale.Identity(8, 16), list);
        return list;
    }

    private static HashSet<(int, int)> Pixels(IEnumerable<DeviceRect> rects)
    {
        var set = new HashSet<(int, int)>();
        foreach (DeviceRect r in rects)
            for (int y = r.Y0; y < r.Y1; y++)
                for (int x = r.X0; x < r.X1; x++)
                    Assert.True(set.Add((x, y)), $"pixel ({x},{y}) covered twice");
        return set;
    }

    public static TheoryData<int, int, int, int> LineCases()
    {
        var data = new TheoryData<int, int, int, int>
        {
            { 0, 0, 7, 7 }, { 0, 0, 13, 4 }, { 3, 9, 1, -6 }, { -5, 2, 20, 11 }, { 0, 0, 4, 1 }, { 0, 0, 1, 4 },
            { 0, 0, 4, -1 }, { 10, 3, -10, -3 }, { -7, -7, -2, 9 }, { 0, 0, 2, 1 }, { 5, 5, 5, 5 }, { 0, 0, -3, 3 },
        };
        var random = new Random(1234);
        for (int i = 0; i < 40; i++)
            data.Add(random.Next(-60, 60), random.Next(-60, 60), random.Next(-60, 60), random.Next(-60, 60));
        return data;
    }

    [Theory]
    [MemberData(nameof(LineCases))]
    public void Lines_cover_exactly_the_defined_pixel_set_in_coalesced_runs(int x1, int y1, int x2, int y2)
    {
        List<DeviceRect> rects = Rects(GraphicsPrimitive.Line(x1, y1, x2, y2, Red));
        HashSet<(int, int)> pixels = Pixels(rects);
        Assert.Equal(Reference(x1, y1, x2, y2), pixels);

        // Symmetric in the end-point order, including the rectangles themselves.
        Assert.Equal(rects.OrderBy(r => (r.X0, r.Y0)), Rects(GraphicsPrimitive.Line(x2, y2, x1, y1, Red)).OrderBy(r => (r.X0, r.Y0)));
        Assert.Contains((x1, y1), pixels);
        Assert.Contains((x2, y2), pixels);

        // One pixel per major coordinate, 8-connected.
        bool xMajor = Math.Abs(x2 - x1) >= Math.Abs(y2 - y1);
        Assert.Equal(Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)) + 1, pixels.Count);
        var ordered = pixels.OrderBy(p => xMajor ? p.Item1 : p.Item2).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            Assert.Equal(1, Math.Abs(xMajor ? ordered[i].Item1 - ordered[i - 1].Item1 : ordered[i].Item2 - ordered[i - 1].Item2));
            Assert.True(Math.Abs(xMajor ? ordered[i].Item2 - ordered[i - 1].Item2 : ordered[i].Item1 - ordered[i - 1].Item1) <= 1);
        }

        // Coalesced: one rectangle per run of equal minor coordinate, each one pixel thick.
        int runs = 1;
        for (int i = 1; i < ordered.Count; i++)
            if ((xMajor ? ordered[i].Item2 : ordered[i].Item1) != (xMajor ? ordered[i - 1].Item2 : ordered[i - 1].Item1)) runs++;
        Assert.Equal(runs, rects.Count);
        Assert.All(rects, r => Assert.Equal(1, xMajor ? r.Y1 - r.Y0 : r.X1 - r.X0));
    }

    [Fact]
    public void Line_formula_rounds_half_steps_the_defined_way()
    {
        // 4:1 slope, y = floor((2x + 4) / 8): the minor step happens at x = 2, not at x = 3 (Bresenham's dx/2 error).
        Assert.Equal([new DeviceRect(0, 0, 2, 1), new DeviceRect(2, 1, 5, 2)], Rects(GraphicsPrimitive.Line(0, 0, 4, 1, Red)));
        // Negative slope: y = floor((-2x + 4) / 8) steps at x = 3 (floor toward -infinity).
        Assert.Equal([new DeviceRect(0, 0, 3, 1), new DeviceRect(3, -1, 5, 0)], Rects(GraphicsPrimitive.Line(0, 0, 4, -1, Red)));
        Assert.Equal(-1, PixelGraphicsGeometry.FloorDiv(-1, 8));
        Assert.Equal(0, PixelGraphicsGeometry.FloorDiv(0, 8));
        Assert.Equal(-2, PixelGraphicsGeometry.FloorDiv(-9, 8));
        Assert.Equal(1, PixelGraphicsGeometry.LineMinor(2, 0, 0, 4, 1));
    }

    [Fact]
    public void Lines_at_the_extent_limit_are_drawn_and_beyond_it_are_skipped()
    {
        const int Max = GraphicsPrimitive.MaxLineExtent;
        List<DeviceRect> longest = Rects(GraphicsPrimitive.Line(0, 0, Max, Max, Red));
        Assert.Equal(Max + 1, longest.Count); // a diagonal is one rectangle per pixel
        Assert.Equal(new DeviceRect(Max, Max, Max + 1, Max + 1), longest[^1]);
        Assert.Equal([new DeviceRect(-Max / 2, 3, Max / 2 + 1, 4)], Rects(GraphicsPrimitive.Line(-Max / 2, 3, Max / 2, 3, Red)));
        Assert.Empty(Rects(GraphicsPrimitive.Line(0, 0, Max + 1, 0, Red)));
        Assert.Empty(Rects(GraphicsPrimitive.Line(0, 0, 3, -Max - 1, Red)));
        Assert.Empty(Rects(GraphicsPrimitive.Line(int.MinValue, 0, int.MaxValue, 0, Red)));
        // End points at the int range limits do not overflow.
        Assert.Single(Rects(GraphicsPrimitive.Line(int.MaxValue - 10, 0, int.MaxValue, 0, Red), new PixelScale(1, 1, 1, 1)));
        Assert.Single(Rects(GraphicsPrimitive.Line(0, int.MaxValue - 10, 0, int.MaxValue, Red), new PixelScale(1, 1, 1, 1)));
    }

    [Fact]
    public void Hidpi_lines_cover_whole_device_blocks_of_their_logical_pixels()
    {
        var scale = new PixelScale(8, 16, 12, 24); // 1.5x: logical pixels alternate 1 and 2 device pixels
        HashSet<(int, int)> device = Pixels(Rects(GraphicsPrimitive.Line(0, 0, 9, 4, Red), scale));
        var expected = new HashSet<(int, int)>();
        foreach (var (x, y) in Reference(0, 0, 9, 4))
            for (int dy = scale.Y(y); dy < scale.Y(y + 1); dy++)
                for (int dx = scale.X(x); dx < scale.X(x + 1); dx++)
                    expected.Add((dx, dy));
        Assert.Equal(expected, device);
    }

    // ── Images ──────────────────────────────────────────────────────────

    private static PixelImage Image(int w, int h, Func<int, int, (byte R, byte G, byte B, byte A)> texel) =>
        PixelImage.Create(w, h, px =>
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var t = texel(x, y);
                    int o = (y * w + x) * 4;
                    px[o] = t.R; px[o + 1] = t.G; px[o + 2] = t.B; px[o + 3] = t.A;
                }
        });

    private static PixelImage RandomImage(int w, int h, int seed, bool opaque = true)
    {
        var random = new Random(seed);
        return Image(w, h, (_, _) => ((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256),
            opaque ? (byte)255 : (byte)random.Next(256)));
    }

    private static byte[] Render(int width, int height, PixelRect[] clip, PixelImage[] images, PixelScale scale,
        params GraphicsPrimitive[] primitives)
    {
        var buffer = new byte[width * height * 4];
        var layers = new PixelGraphicsLayers();
        layers.Set(new object(), clip, primitives, images);
        PixelGraphicsRasterizer.Rasterize(buffer, width, height, width * 4, layers.Frame, scale, new());
        return buffer;
    }

    private static byte[] Render(int width, int height, PixelImage image, params GraphicsPrimitive[] primitives) =>
        Render(width, height, [new(0, 0, width, height)], [image], new PixelScale(1, 1, 1, 1), primitives);

    private static (byte R, byte G, byte B, byte A) Px(byte[] buffer, int width, int x, int y)
    {
        int o = (y * width + x) * 4;
        return (buffer[o], buffer[o + 1], buffer[o + 2], buffer[o + 3]);
    }

    private static (byte R, byte G, byte B, byte A) Texel(PixelImage image, int x, int y)
    {
        int o = y * image.Stride + x * 4;
        ReadOnlySpan<byte> p = image.Pixels;
        return (p[o], p[o + 1], p[o + 2], p[o + 3]);
    }

    [Theory]
    [InlineData(3, 4, 7, 5)]
    [InlineData(2, 2, 6, 6)]
    [InlineData(5, 3, 2, 2)]
    [InlineData(4, 1, 2, 1)]
    [InlineData(1, 1, 9, 3)]
    [InlineData(7, 7, 7, 7)]
    public void Nearest_samples_the_texel_under_each_pixel_centre(int w, int h, int destW, int destH)
    {
        PixelImage image = RandomImage(w, h, w * 31 + h);
        const int X0 = 2, Y0 = 1, Width = 12, Height = 10;
        byte[] buffer = Render(Width, Height, image, GraphicsPrimitive.Image(0, X0, Y0, destW, destH));
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                bool inside = x >= X0 && x < X0 + destW && y >= Y0 && y < Y0 + destH;
                if (!inside) { Assert.Equal((0, 0, 0, 0), Px(buffer, Width, x, y)); continue; }
                int tx = (int)Math.Floor((x - X0 + 0.5) * w / destW), ty = (int)Math.Floor((y - Y0 + 0.5) * h / destH);
                Assert.Equal(Texel(image, tx, ty), Px(buffer, Width, x, y));
            }
    }

    [Fact]
    public void Nearest_integer_upscale_maps_each_texel_to_a_block()
    {
        PixelImage image = RandomImage(3, 2, 7);
        byte[] buffer = Render(9, 4, image, GraphicsPrimitive.Image(0, 0, 0, 9, 4));
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 9; x++)
                Assert.Equal(Texel(image, x / 3, y / 2), Px(buffer, 9, x, y));
    }

    [Fact]
    public void Linear_interpolates_between_texel_centres_with_clamped_edges()
    {
        PixelImage image = Image(2, 1, (x, _) => x == 0 ? ((byte)0, (byte)0, (byte)0, (byte)255) : ((byte)255, (byte)100, (byte)0, (byte)255));
        byte[] buffer = Render(4, 1, image, GraphicsPrimitive.Image(0, 0, 0, 4, 1, GraphicsImageFilter.Linear));
        // u = (k + 0.5) * 2 / 4 - 0.5 = -0.25, 0.25, 0.75, 1.25 (clamped at both edges).
        Assert.Equal((0, 0, 0, 255), Px(buffer, 4, 0, 0));
        Assert.Equal((64, 25, 0, 255), Px(buffer, 4, 1, 0));
        Assert.Equal((191, 75, 0, 255), Px(buffer, 4, 2, 0));
        Assert.Equal((255, 100, 0, 255), Px(buffer, 4, 3, 0));
    }

    [Fact]
    public void Linear_matches_a_double_precision_bilinear_reference_and_identity_scale_is_exact()
    {
        PixelImage image = RandomImage(5, 4, 99);
        const int W = 13, H = 7;
        byte[] buffer = Render(W, H, image, GraphicsPrimitive.Image(0, 0, 0, W, H, GraphicsImageFilter.Linear));
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                double u = (x + 0.5) * 5 / W - 0.5, v = (y + 0.5) * 4 / H - 0.5;
                int u0 = (int)Math.Floor(u), v0 = (int)Math.Floor(v);
                double fx = u - u0, fy = v - v0;
                int xa = Math.Clamp(u0, 0, 4), xb = Math.Clamp(u0 + 1, 0, 4), ya = Math.Clamp(v0, 0, 3), yb = Math.Clamp(v0 + 1, 0, 3);
                byte Channel(Func<(byte R, byte G, byte B, byte A), byte> c) => (byte)(int)(
                    c(Texel(image, xa, ya)) * (1 - fx) * (1 - fy) + c(Texel(image, xb, ya)) * fx * (1 - fy)
                    + c(Texel(image, xa, yb)) * (1 - fx) * fy + c(Texel(image, xb, yb)) * fx * fy + 0.5);
                var expected = (Channel(t => t.R), Channel(t => t.G), Channel(t => t.B), (byte)255);
                var actual = Px(buffer, W, x, y);
                // Summation order may differ by one rounding step at an exact .5.
                Assert.True(Math.Abs(expected.Item1 - actual.R) <= 1 && Math.Abs(expected.Item2 - actual.G) <= 1
                    && Math.Abs(expected.Item3 - actual.B) <= 1 && actual.A == 255, $"({x},{y}) {actual} vs {expected}");
            }

        byte[] identity = Render(5, 4, image, GraphicsPrimitive.Image(0, 0, 0, 5, 4, GraphicsImageFilter.Linear));
        Assert.Equal(image.Pixels.ToArray(), identity);
    }

    [Fact]
    public void Opacity_and_texel_alpha_blend_straight_alpha_source_over()
    {
        PixelImage opaqueRed = Image(1, 1, (_, _) => (255, 0, 0, 255));
        PixelImage halfRed = Image(1, 1, (_, _) => (255, 0, 0, 128));
        PixelImage clear = Image(1, 1, (_, _) => (255, 255, 255, 0));
        foreach (GraphicsImageFilter filter in new[] { GraphicsImageFilter.Nearest, GraphicsImageFilter.Linear })
        {
            var clip = new PixelRect[] { new(0, 0, 4, 1) };
            byte[] buffer = Render(4, 1, clip, [opaqueRed, halfRed, clear], new PixelScale(1, 1, 1, 1),
                GraphicsPrimitive.FillRect(0, 0, 4, 1, Blue),
                GraphicsPrimitive.Image(0, 0, 0, 1, 1, filter, 128),  // a = 128
                GraphicsPrimitive.Image(1, 1, 0, 1, 1, filter, 255),  // a = 128
                GraphicsPrimitive.Image(1, 2, 0, 1, 1, filter, 128),  // a = (128 * 128 + 127) / 255 = 64
                GraphicsPrimitive.Image(2, 3, 0, 1, 1, filter, 255)); // a = 0: unchanged
            Assert.Equal((128, 0, 127, 255), Px(buffer, 4, 0, 0));
            Assert.Equal((128, 0, 127, 255), Px(buffer, 4, 1, 0));
            Assert.Equal((64, 0, 191, 255), Px(buffer, 4, 2, 0));
            Assert.Equal((0, 0, 255, 255), Px(buffer, 4, 3, 0));
        }

        // Over a transparent destination the alpha accumulates: a + d(1 - a).
        byte[] empty = Render(1, 1, halfRed, GraphicsPrimitive.Image(0, 0, 0, 1, 1));
        Assert.Equal((128, 0, 0, 128), Px(empty, 1, 0, 0));
        byte[] invisible = Render(1, 1, opaqueRed, GraphicsPrimitive.Image(0, 0, 0, 1, 1, opacity: 0));
        Assert.Equal((0, 0, 0, 0), Px(invisible, 1, 0, 0));
    }

    [Fact]
    public void Images_are_clipped_to_every_clip_rectangle_and_the_buffer()
    {
        PixelImage image = RandomImage(4, 4, 5);
        const int W = 10, H = 6;
        byte[] buffer = Render(W, H, [new(0, 0, 3, 6), new(6, 2, 10, 10)], [image], new PixelScale(1, 1, 1, 1),
            GraphicsPrimitive.Image(0, -2, -2, 16, 16));
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                bool visible = x < 3 || (x >= 6 && y >= 2);
                var expected = visible ? Texel(image, (x + 2) / 4, (y + 2) / 4) : (0, 0, 0, 0);
                Assert.Equal(expected, Px(buffer, W, x, y));
            }
    }

    [Fact]
    public void Hidpi_scales_the_destination_with_the_cell_grid()
    {
        PixelImage image = RandomImage(2, 2, 11);
        var scale = new PixelScale(8, 16, 16, 32);
        byte[] buffer = Render(8, 8, [new(0, 0, 8, 16)], [image], scale, GraphicsPrimitive.Image(0, 1, 1, 2, 2));
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                bool inside = x >= 2 && x < 6 && y >= 2 && y < 6;
                Assert.Equal(inside ? Texel(image, (x - 2) / 2, (y - 2) / 2) : (0, 0, 0, 0), Px(buffer, 8, x, y));
            }
    }

    [Fact]
    public void Out_of_range_image_index_and_empty_destinations_draw_nothing()
    {
        PixelImage image = RandomImage(2, 2, 3);
        byte[] buffer = Render(4, 4, [new(0, 0, 4, 4)], [image], new PixelScale(1, 1, 1, 1),
            GraphicsPrimitive.Image(1, 0, 0, 4, 4), GraphicsPrimitive.Image(-1, 0, 0, 4, 4),
            GraphicsPrimitive.Image(0, 0, 0, 0, 4), new GraphicsPrimitive(GraphicsPrimitiveKind.Image, 3, 3, 1, 1, 0xFFFFFFFF));
        Assert.All(buffer, b => Assert.Equal(0, b));
        byte[] none = Render(4, 4, [new(0, 0, 4, 4)], [], new PixelScale(1, 1, 1, 1), GraphicsPrimitive.Image(0, 0, 0, 4, 4));
        Assert.All(none, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Primitives_draw_in_submission_order_across_kinds()
    {
        PixelImage image = Image(1, 1, (_, _) => (0, 255, 0, 255));
        byte[] buffer = Render(3, 1, [new(0, 0, 3, 1)], [image], new PixelScale(1, 1, 1, 1),
            GraphicsPrimitive.FillRect(0, 0, 2, 1, Red),
            GraphicsPrimitive.Image(0, 1, 0, 2, 1),
            GraphicsPrimitive.Line(2, 0, 2, 0, Blue));
        Assert.Equal((255, 0, 0, 255), Px(buffer, 3, 0, 0));
        Assert.Equal((0, 255, 0, 255), Px(buffer, 3, 1, 0)); // the image covers the rectangle
        Assert.Equal((0, 0, 255, 255), Px(buffer, 3, 2, 0)); // the line covers the image
    }

    [Fact]
    public void Huge_destination_rectangles_are_clipped_without_overflow()
    {
        PixelImage image = Image(2, 2, (x, y) => ((byte)(x * 200), (byte)(y * 200), 0, 255));
        // Logical [-1e9, 1e9): the buffer sees texel column 1 / row 1 (k = 1e9 => ((2k + 1) * 2) / 4e9 = 1).
        byte[] nearest = Render(4, 4, [new(int.MinValue / 2, int.MinValue / 2, int.MaxValue, int.MaxValue)], [image],
            new PixelScale(1, 1, 2, 2), GraphicsPrimitive.Image(0, -1_000_000_000, -1_000_000_000, 2_000_000_000, 2_000_000_000));
        Assert.All(Enumerable.Range(0, 16), i => Assert.Equal((200, 200, 0, 255), Px(nearest, 4, i % 4, i / 4)));
        byte[] linear = Render(4, 4, [new(0, 0, 4, 4)], [image], new PixelScale(1, 1, 1, 1),
            GraphicsPrimitive.Image(0, -1_000_000_000, -1_000_000_000, 2_000_000_000, 2_000_000_000, GraphicsImageFilter.Linear));
        // u = (1e9 + 0.5) * 2 / 2e9 - 0.5 ~ 0.5 + 5e-10: halfway between the two columns, rounded half up.
        Assert.Equal((100, 100, 0, 255), Px(linear, 4, 0, 0));
    }

    // ── Layer store with images ────────────────────────────────────────────

    [Fact]
    public void Layer_store_keeps_images_by_reference_and_unchanged_images_do_not_rebuild()
    {
        PixelImage a = RandomImage(2, 2, 1), b = RandomImage(2, 2, 1); // same pixels, different identity
        var layers = new PixelGraphicsLayers();
        object owner = new();
        GraphicsPrimitive[] prims = [GraphicsPrimitive.Image(0, 0, 0, 4, 4), GraphicsPrimitive.Image(1, 4, 0, 4, 4)];

        Assert.True(layers.Set(owner, [new(0, 0, 8, 4)], prims, [a, b]));
        PixelGraphicsFrame first = layers.Frame;
        Assert.Same(a, first.Layers[0].ImageOf(prims[0]));
        Assert.Same(b, first.Layers[0].ImageOf(prims[1]));
        Assert.Null(first.Layers[0].ImageOf(GraphicsPrimitive.Image(2, 0, 0, 1, 1)));
        Assert.Null(first.Layers[0].ImageOf(GraphicsPrimitive.FillRect(0, 0, 1, 1, Red)));

        Assert.False(layers.Set(owner, [new(0, 0, 8, 4)], prims, [a, b]));
        Assert.Same(first, layers.Frame);
        Assert.True(layers.Set(owner, [new(0, 0, 8, 4)], prims, [b, a])); // order matters
        Assert.True(layers.Set(owner, [new(0, 0, 8, 4)], prims, [b, b]));
        Assert.True(layers.Set(owner, [new(0, 0, 8, 4)], prims, [b]));
        Assert.Throws<ArgumentNullException>(() => layers.Set(owner, [new(0, 0, 8, 4)], prims, [a, null!]));
    }
}
