using System.Reflection;
using System.Runtime.InteropServices;
using TSharpVision.Drivers.SDL.Gpu;
using TSharpVision.Drivers.SDL.Graphics;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// TV12n/TV12o pixel graphics in the SDL GPU renderer without a device: the instance build of the instanced graphics
/// pass (packing, batching, scissors, limits), the image texture cache, a C# mirror of the shape shader's coverage
/// test against the exact line formula, and the CPU-composite fallback.
/// </summary>
public sealed class GpuGraphicsHostTests
{
    private const uint Red = 0xFF0000FF, Green = 0x00FF00FF;

    private static PixelGraphicsFrame Frame(PixelRect[] clip, params GraphicsPrimitive[] prims)
    {
        var layers = new PixelGraphicsLayers();
        layers.Set(new object(), clip, prims);
        return layers.Frame;
    }

    private static PixelGraphicsFrame Frame(PixelRect[] clip, PixelImage[] images, params GraphicsPrimitive[] prims)
    {
        var layers = new PixelGraphicsLayers();
        layers.Set(new object(), clip, prims, images);
        return layers.Frame;
    }

    private static PixelImage Image(int w, int h, byte seed = 0) => PixelImage.Create(w, h, px =>
    {
        for (int i = 0; i < px.Length; i++) px[i] = (byte)(i * 7 + seed);
    });

    private sealed class FakeBackend : IGpuImageTextureBackend
    {
        public int Next = 0x1000, Live, FailNext;
        public readonly List<(int W, int H)> CreatedSizes = new();

        public IntPtr CreateTexture(int width, int height)
        {
            if (FailNext > 0) { FailNext--; return IntPtr.Zero; }
            CreatedSizes.Add((width, height));
            Live++;
            return Next++;
        }

        public void ReleaseTexture(IntPtr texture) => Live--;
    }

    private static (GpuGraphicsBuildStats Stats, GpuGraphicsInstance[] Instances, List<GpuGraphicsDraw> Draws) Build(
        PixelGraphicsFrame frame, PixelScale scale, int gridW, int gridH, GpuImageCache? images = null, int capacity = 64)
    {
        var instances = new GpuGraphicsInstance[capacity];
        var draws = new List<GpuGraphicsDraw>();
        images?.BeginFrame(frame);
        GpuGraphicsBuildStats stats = GpuGraphicsBatch.Build(instances, frame, scale, gridW, gridH, images, new(), draws);
        return (stats, instances[..stats.Instances], draws);
    }

    private static (int, int, int, int) Box(in GpuGraphicsInstance i) => (i.X0, i.Y0, i.X1, i.Y1);
    private static (int, int, int, int) P(in GpuGraphicsInstance i) => (i.P0, i.P1, i.P2, i.P3);
    private static (int, int, int, int) Rect(in GpuGraphicsDraw d) => (d.Scissor.X, d.Scissor.Y, d.Scissor.W, d.Scissor.H);

    // ── Instance build ─────────────────────────────────────────────────────

    [Fact]
    public void Instance_layout_is_32_bytes_with_the_shader_offsets()
    {
        Assert.Equal(32, Marshal.SizeOf<GpuGraphicsInstance>());
        Assert.Equal(GpuGraphicsInstance.Size, Marshal.SizeOf<GpuGraphicsInstance>());
        Assert.Equal(0, (int)Marshal.OffsetOf<GpuGraphicsInstance>(nameof(GpuGraphicsInstance.X0)));
        Assert.Equal(8, (int)Marshal.OffsetOf<GpuGraphicsInstance>(nameof(GpuGraphicsInstance.P0)));
        Assert.Equal(24, (int)Marshal.OffsetOf<GpuGraphicsInstance>(nameof(GpuGraphicsInstance.R)));
        Assert.Equal(28, (int)Marshal.OffsetOf<GpuGraphicsInstance>(nameof(GpuGraphicsInstance.Flags)));
    }

    [Fact]
    public void One_instance_per_primitive_with_ordered_end_points_and_packed_bytes()
    {
        PixelGraphicsFrame frame = Frame([new(0, 0, 64, 32)],
            GraphicsPrimitive.FillRect(2, 3, 10, 4, Red),
            GraphicsPrimitive.Line(40, 5, 3, 1, 0x11223344),   // x-major, given right to left
            GraphicsPrimitive.Line(7, 30, 9, 2, Green));        // y-major, given bottom to top
        var (stats, inst, draws) = Build(frame, PixelScale.Identity(8, 16), 64, 32);

        Assert.Equal(3, stats.Instances);
        Assert.Equal((2, 3, 12, 7), Box(inst[0]));
        Assert.Equal(GpuGraphicsBatch.KindFill, inst[0].Flags);
        Assert.Equal((255, 0, 0, 255), (inst[0].R, inst[0].G, inst[0].B, inst[0].A));

        Assert.Equal(GpuGraphicsBatch.KindLineX, inst[1].Flags);
        Assert.Equal((3, 1, 40, 5), P(inst[1]));
        Assert.Equal((3, 1, 41, 6), Box(inst[1]));
        Assert.Equal((0x11, 0x22, 0x33, 0x44), (inst[1].R, inst[1].G, inst[1].B, inst[1].A));

        Assert.Equal(GpuGraphicsBatch.KindLineY, inst[2].Flags);
        Assert.Equal((9, 2, 7, 30), P(inst[2]));
        Assert.Equal((7, 2, 10, 31), Box(inst[2]));

        // Raw bytes: the box is int16 little endian, the colour R, G, B, A.
        byte[] raw = MemoryMarshal.AsBytes(inst.AsSpan(0, 1)).ToArray();
        Assert.Equal(new byte[] { 2, 0, 3, 0, 12, 0, 7, 0 }, raw[..8]);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, raw[24..28]);

        GpuGraphicsDraw draw = Assert.Single(draws);
        Assert.Equal((GpuGraphicsBatchKind.Shapes, 0, 3), (draw.Kind, draw.FirstInstance, draw.InstanceCount));
        Assert.Equal((0, 0, 64, 32), Rect(draw));
    }

    [Fact]
    public void Batches_keep_primitive_order_across_kinds_and_repeat_per_clip_rectangle()
    {
        PixelImage a = Image(4, 4), b = Image(2, 2);
        PixelGraphicsFrame frame = Frame([new(0, 0, 16, 16), new(32, 0, 16, 16)], [a, b],
            GraphicsPrimitive.FillRect(0, 0, 64, 32, Red),
            GraphicsPrimitive.Line(0, 0, 63, 31, Green),
            GraphicsPrimitive.Image(0, 0, 0, 8, 8),
            GraphicsPrimitive.Image(0, 32, 0, 8, 8, GraphicsImageFilter.Linear),
            GraphicsPrimitive.Image(1, 4, 4, 40, 8),
            GraphicsPrimitive.FillRect(4, 4, 40, 4, Red));
        var backend = new FakeBackend();
        var cache = new GpuImageCache(backend);
        var (stats, inst, draws) = Build(frame, PixelScale.Identity(8, 16), 64, 32, cache);

        Assert.Equal(6, stats.Instances);
        // Batches: shapes [0,2), image a [2,4), image b [4,5), shapes [5,6); each under both scissors, batch-major.
        var expected = new (GpuGraphicsBatchKind, int, int)[]
        {
            (GpuGraphicsBatchKind.Shapes, 0, 2), (GpuGraphicsBatchKind.Image, 2, 2),
            (GpuGraphicsBatchKind.Image, 4, 1), (GpuGraphicsBatchKind.Shapes, 5, 1),
        };
        Assert.Equal(8, draws.Count);
        for (int i = 0; i < draws.Count; i++)
        {
            Assert.Equal(expected[i / 2], (draws[i].Kind, draws[i].FirstInstance, draws[i].InstanceCount));
            Assert.Equal(i % 2 == 0 ? (0, 0, 16, 16) : (32, 0, 16, 16), Rect(draws[i]));
        }
        Assert.Same(draws[2].Image, draws[3].Image);
        Assert.NotSame(draws[2].Image, draws[4].Image);
        Assert.Equal(a.Id, draws[2].Image!.Image.Id);
        // Image instances: destination in P, white with the opacity, the filter flag.
        Assert.Equal((0, 0, 8, 8), P(inst[2]));
        Assert.Equal(0u, inst[2].Flags);
        Assert.Equal(GpuGraphicsBatch.FlagLinear, inst[3].Flags);
        Assert.Equal((255, 255, 255, 255), (inst[2].R, inst[2].G, inst[2].B, inst[2].A));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Boxes_clamp_to_the_grid_and_the_clip_bounds_and_invisible_primitives_produce_nothing()
    {
        PixelGraphicsFrame frame = Frame([new(4, 2, 8, 8), new(20, 2, 4, 4)],
            GraphicsPrimitive.FillRect(-10, -10, 100, 100, Red),      // clamped to the clip bounds (4,2)-(24,10)
            GraphicsPrimitive.FillRect(0, 0, 100, 100, 0xFFFFFF00),  // transparent
            GraphicsPrimitive.FillRect(5, 5, 0, 3, Red),             // empty
            GraphicsPrimitive.FillRect(40, 0, 4, 4, Red),            // outside the clip bounds
            GraphicsPrimitive.Line(0, 0, GraphicsPrimitive.MaxLineExtent + 1, 0, Red), // too long
            GraphicsPrimitive.Line(0, 3, GraphicsPrimitive.MaxLineExtent, 3, Red));    // longest drawable
        var (stats, inst, draws) = Build(frame, PixelScale.Identity(8, 16), 32, 16);
        Assert.Equal(2, stats.Instances);
        Assert.Equal((4, 2, 24, 10), Box(inst[0]));
        Assert.Equal((4, 3, 24, 4), Box(inst[1]));
        Assert.Equal((0, 3, GraphicsPrimitive.MaxLineExtent, 3), P(inst[1]));
        Assert.Equal(2, draws.Count);
    }

    [Fact]
    public void Device_boxes_use_floor_scaling_at_higher_density()
    {
        // Logical 8 x 16 cells drawn at 12 x 24 (density 1.5): v -> floor(v * 3 / 2).
        PixelGraphicsFrame frame = Frame([new(0, 0, 80, 64)],
            GraphicsPrimitive.FillRect(1, 1, 3, 3, Red), GraphicsPrimitive.Line(-5, 3, 9, 7, Red));
        var (_, inst, draws) = Build(frame, new PixelScale(8, 16, 12, 24), 120, 96);
        Assert.Equal((1, 1, 6, 6), Box(inst[0]));          // [1,4) -> [1,6)
        Assert.Equal((0, 4, 15, 12), Box(inst[1]));        // [-5,10) x [3,8) -> [-8,15) x [4,12), clamped at 0
        Assert.Equal((0, 0, 120, 96), Rect(Assert.Single(draws)));
    }

    [Fact]
    public void Build_stops_at_capacity_and_reports_truncation()
    {
        PixelGraphicsFrame frame = Frame([new(0, 0, 100, 100)],
            GraphicsPrimitive.FillRect(0, 0, 1, 1, Red), GraphicsPrimitive.FillRect(1, 0, 1, 1, Red), GraphicsPrimitive.FillRect(2, 0, 1, 1, Red));
        var (stats, _, draws) = Build(frame, PixelScale.Identity(8, 16), 8, 8, capacity: 2);
        Assert.True(stats.Truncated);
        Assert.Equal(2, stats.Instances);
        Assert.Equal((0, 2), (Assert.Single(draws).FirstInstance, draws[0].InstanceCount));
    }

    [Fact]
    public void Empty_frame_and_oversized_grid_record_nothing()
    {
        var draws = new List<GpuGraphicsDraw> { default };
        Assert.Equal(default, GpuGraphicsBatch.Build(new GpuGraphicsInstance[4], PixelGraphicsFrame.Empty,
            PixelScale.Identity(8, 16), 64, 32, null, new(), draws));
        Assert.Empty(draws);

        PixelGraphicsFrame frame = Frame([new(0, 0, 10, 10)], GraphicsPrimitive.FillRect(0, 0, 1, 1, Red));
        var (stats, _, d2) = Build(frame, PixelScale.Identity(8, 16), GpuGraphicsBatch.MaxGridExtent + 1, 32);
        Assert.True(stats.GridTooLarge);
        Assert.Equal(0, stats.Instances);
        Assert.Empty(d2);
    }

    [Fact]
    public void Image_primitives_without_an_image_or_beyond_the_limits_draw_nothing_and_are_counted()
    {
        PixelImage big = Image(17, 1);
        PixelImage small = Image(2, 2);
        PixelGraphicsFrame frame = Frame([new(0, 0, 64, 64)], [small, big],
            GraphicsPrimitive.Image(5, 0, 0, 8, 8),                                     // no such image
            GraphicsPrimitive.Image(0, -100_000, 0, GpuGraphicsBatch.MaxImageDestinationExtent + 1, 8), // too wide
            GraphicsPrimitive.Image(1, 0, 0, 8, 8),                                     // texture too large
            GraphicsPrimitive.Image(0, 0, 0, 8, 8, opacity: 0),                         // invisible
            GraphicsPrimitive.Image(0, 100, 100, 8, 8),                                 // outside the clip
            GraphicsPrimitive.Image(0, 0, 0, 8, 8));                                    // drawn
        var cache = new GpuImageCache(new FakeBackend(), maxDimension: 16);
        var (stats, _, draws) = Build(frame, PixelScale.Identity(8, 16), 64, 64, cache);
        Assert.Equal(1, stats.Instances);
        Assert.Equal(1, stats.SkippedImages);
        Assert.Equal(2, stats.LimitedImages);
        Assert.Equal(1, cache.Count);
        Assert.Equal(1, cache.Refused);
        Assert.Contains("texture limit", cache.LastRefusal);
        Assert.Equal(GpuGraphicsBatchKind.Image, Assert.Single(draws).Kind);

        // Without a cache (no device) images are skipped as limited.
        var (noCache, _, _) = Build(frame, PixelScale.Identity(8, 16), 64, 64, images: null);
        Assert.Equal(0, noCache.Instances);
    }

    [Fact]
    public void Steady_state_build_allocates_nothing()
    {
        var prims = new GraphicsPrimitive[500];
        for (int i = 0; i < prims.Length; i++)
            prims[i] = i % 3 == 0 ? GraphicsPrimitive.FillRect(i % 60, i % 30, 3, 2, Red) : GraphicsPrimitive.Line(i % 64, 0, 63 - i % 64, 31, Green);
        PixelImage img = Image(8, 8);
        var layers = new PixelGraphicsLayers();
        layers.Set(new object(), [new(0, 0, 32, 16), new(32, 0, 32, 16)], [.. prims, GraphicsPrimitive.Image(0, 0, 0, 16, 16)], [img]);
        PixelGraphicsFrame frame = layers.Frame;
        var instances = new GpuGraphicsInstance[1024];
        var draws = new List<GpuGraphicsDraw>();
        var clips = new List<DeviceRect>();
        var cache = new GpuImageCache(new FakeBackend());
        PixelScale scale = new(8, 16, 16, 32);
        void Once()
        {
            cache.BeginFrame(frame);
            GpuGraphicsBatch.Build(instances, frame, scale, 128, 64, cache, clips, draws);
            cache.MarkUploaded();
        }
        Once();
        Once();
        _ = GC.GetAllocatedBytesForCurrentThread(); // Warm the counter outside the measured interval.
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++) Once();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // ── Shape shader coverage (C# mirror of GraphicsShapeFrag.hlsl) vs the exact formula ─────

    // Line of the brief: x-major y = y1 + floorDiv(2(x - x1)dy + dx, 2dx) with x1 <= x2 (y-major symmetric).
    private static HashSet<(int, int)> ReferenceLine(int x1, int y1, int x2, int y2)
    {
        var set = new HashSet<(int, int)>();
        if (Math.Abs(x2 - x1) >= Math.Abs(y2 - y1))
        {
            if (x1 > x2) (x1, y1, x2, y2) = (x2, y2, x1, y1);
            int dx = x2 - x1, dy = y2 - y1;
            if (dx == 0) { set.Add((x1, y1)); return set; }
            for (int x = x1; x <= x2; x++) set.Add((x, y1 + (int)Math.Floor((2.0 * (x - x1) * dy + dx) / (2.0 * dx))));
        }
        else
        {
            if (y1 > y2) (x1, y1, x2, y2) = (x2, y2, x1, y1);
            int dx = x2 - x1, dy = y2 - y1;
            for (int y = y1; y <= y2; y++) set.Add((x1 + (int)Math.Floor((2.0 * (y - y1) * dx + dy) / (2.0 * dy)), y));
        }
        return set;
    }

    private static int FloorDiv(int n, int d) => n >= 0 ? n / d : -((d - 1 - n) / d);

    // Exactly the HLSL: device pixel -> logical pixel by the integer inverse, then the line test.
    private static bool ShaderCovers(in GpuGraphicsInstance i, PixelScale s, int dx, int dy)
    {
        uint kind = i.Flags & 3u;
        if (kind == 0) return true;
        int lx = ((dx + 1) * s.LogicalWidth + s.DeviceWidth - 1) / s.DeviceWidth - 1;
        int ly = ((dy + 1) * s.LogicalHeight + s.DeviceHeight - 1) / s.DeviceHeight - 1;
        int ex = i.P2 - i.P0, ey = i.P3 - i.P1;
        if (kind == 1)
            return lx >= i.P0 && lx <= i.P2 && ly == (ex == 0 ? i.P1 : i.P1 + FloorDiv(2 * (lx - i.P0) * ey + ex, 2 * ex));
        return ly >= i.P1 && ly <= i.P3 && lx == (ey == 0 ? i.P0 : i.P0 + FloorDiv(2 * (ly - i.P1) * ex + ey, 2 * ey));
    }

    public static IEnumerable<object[]> Scales() =>
    [
        [8, 16, 8, 16], [8, 16, 16, 32], [8, 16, 12, 24], [8, 16, 10, 20], [9, 18, 23, 41],
    ];

    [Theory]
    [MemberData(nameof(Scales))]
    public void Shader_line_coverage_equals_the_scaled_reference_pixel_set(int lw, int lh, int dw, int dh)
    {
        var scale = new PixelScale(lw, lh, dw, dh);
        int gw = 6 * dw, gh = 3 * dh; // 6 x 3 cells
        int maxX = 6 * lw - 1, maxY = 3 * lh - 1;
        var lines = new List<(int, int, int, int)>
        {
            (0, 0, maxX, maxY), (maxX, maxY, 0, 0), (maxX, 0, 0, maxY), (3, 3, 3, 3), (0, 5, maxX, 5), (7, 0, 7, maxY),
            (-20, -7, maxX + 9, maxY + 3), (5, 40, 9, -3), (2, 1, 2 + 13, 1 + 13), (30, 2, 1, 9),
        };
        var rnd = new Random(lw * 31 + dw);
        for (int k = 0; k < 40; k++)
            lines.Add((rnd.Next(-10, maxX + 10), rnd.Next(-10, maxY + 10), rnd.Next(-10, maxX + 10), rnd.Next(-10, maxY + 10)));

        foreach (var (x1, y1, x2, y2) in lines)
        {
            PixelGraphicsFrame frame = Frame([new(0, 0, maxX + 1, maxY + 1)], GraphicsPrimitive.Line(x1, y1, x2, y2, Red));
            var (stats, inst, _) = Build(frame, scale, gw, gh);
            var expected = new HashSet<(int, int)>();
            foreach (var (lx, ly) in ReferenceLine(x1, y1, x2, y2))
                for (int y = scale.Y(ly); y < scale.Y(ly + 1); y++)
                    for (int x = scale.X(lx); x < scale.X(lx + 1); x++)
                        if (x >= 0 && y >= 0 && x < gw && y < gh) expected.Add((x, y));
            var actual = new HashSet<(int, int)>();
            if (stats.Instances == 1)
                for (int y = inst[0].Y0; y < inst[0].Y1; y++)
                    for (int x = inst[0].X0; x < inst[0].X1; x++)
                        if (ShaderCovers(inst[0], scale, x, y)) actual.Add((x, y));
            Assert.True(expected.SetEquals(actual), $"line ({x1},{y1})-({x2},{y2}) at {scale}: expected {expected.Count} px, shader {actual.Count}");
        }
    }

    // ── Image texture cache ────────────────────────────────────────────────

    [Fact]
    public void Cache_uploads_an_image_once_reuses_it_and_releases_it_when_no_layer_holds_it()
    {
        var backend = new FakeBackend();
        var cache = new GpuImageCache(backend);
        PixelImage a = Image(10, 5), b = Image(3, 3);
        PixelGraphicsFrame both = Frame([new(0, 0, 64, 64)], [a, b], GraphicsPrimitive.Image(0, 0, 0, 10, 5), GraphicsPrimitive.Image(1, 0, 0, 3, 3));

        cache.BeginFrame(both);
        GpuImageTexture? ta = cache.Acquire(a), tb = cache.Acquire(b);
        Assert.NotNull(ta);
        Assert.Equal(2, cache.PendingUploads.Count);
        Assert.Equal(10 * 5 * 4 + 3 * 3 * 4, cache.PendingBytes);
        Assert.Equal(cache.PendingBytes, cache.TotalBytes);
        cache.MarkUploaded();

        // Next frame: the same images are reused, nothing is uploaded again.
        cache.BeginFrame(both);
        Assert.Same(ta, cache.Acquire(a));
        Assert.Same(tb, cache.Acquire(b));
        Assert.Empty(cache.PendingUploads);
        Assert.Equal(2, backend.Live);

        // A frame whose layers hold only a releases b.
        cache.BeginFrame(Frame([new(0, 0, 64, 64)], [a], GraphicsPrimitive.Image(0, 0, 0, 10, 5)));
        Assert.Equal(1, cache.Count);
        Assert.True(cache.Contains(a));
        Assert.False(cache.Contains(b));
        Assert.Equal(1, backend.Live);
        Assert.Equal(10 * 5 * 4, cache.TotalBytes);

        // An image the layer still holds keeps its texture even when not drawn this frame.
        cache.BeginFrame(Frame([new(0, 0, 64, 64)], [a], GraphicsPrimitive.FillRect(0, 0, 1, 1, Red)));
        Assert.True(cache.Contains(a));

        cache.BeginFrame(PixelGraphicsFrame.Empty);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, backend.Live);
        Assert.Equal((2, 2), (cache.Created, cache.Released));
        Assert.Equal(0, cache.TotalBytes);
    }

    [Fact]
    public void Cache_enforces_the_budget_and_the_dimension_limit_and_does_not_retry_a_failed_texture()
    {
        var backend = new FakeBackend();
        var cache = new GpuImageCache(backend, budgetBytes: 1000, maxDimension: 16);
        PixelImage fits = Image(10, 10), over = Image(12, 12), wide = Image(17, 1), fails = Image(2, 2);
        PixelGraphicsFrame frame = Frame([new(0, 0, 64, 64)], [fits, over, wide, fails]);

        cache.BeginFrame(frame);
        Assert.NotNull(cache.Acquire(fits));                // 400 B
        Assert.NotNull(cache.Acquire(Image(7, 7)));         // 196 B: total 596
        Assert.Null(cache.Acquire(over));                    // 576 B more would exceed 1000 B
        Assert.Contains("budget", cache.LastRefusal);
        Assert.Null(cache.Acquire(wide));
        Assert.Contains("texture limit", cache.LastRefusal);
        backend.FailNext = 1;
        Assert.Null(cache.Acquire(fails));
        Assert.Contains("could not be created", cache.LastRefusal);
        int created = backend.CreatedSizes.Count;
        Assert.Null(cache.Acquire(fails));                   // not retried while held
        Assert.Equal(created, backend.CreatedSizes.Count);
        Assert.True(cache.TotalBytes <= cache.BudgetBytes);

        cache.ReleaseAll();
        Assert.Equal(0, backend.Live);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Cache_drops_a_pending_upload_of_a_released_image_and_requeues_a_cancelled_one()
    {
        var cache = new GpuImageCache(new FakeBackend());
        PixelImage a = Image(4, 4);
        PixelGraphicsFrame frame = Frame([new(0, 0, 8, 8)], [a], GraphicsPrimitive.Image(0, 0, 0, 4, 4));
        cache.BeginFrame(frame);
        cache.Acquire(a);
        cache.CancelPending();
        Assert.Empty(cache.PendingUploads);
        cache.BeginFrame(frame);
        cache.Acquire(a);
        Assert.Single(cache.PendingUploads);                 // never uploaded: queued again
        cache.BeginFrame(PixelGraphicsFrame.Empty);
        Assert.Empty(cache.PendingUploads);
        Assert.Equal(0, cache.PendingBytes);
    }

    // ── CPU-composite fallback (SDL GPU driver without the shader pipeline) ─

    private const uint Cols = 6, Rows = 3;

    private static ScreenBuffer Screen()
    {
        var screen = new ScreenBuffer((ushort)Cols, (ushort)Rows);
        for (uint row = 0; row < Rows; row++)
            for (uint col = 0; col < Cols; col++)
                screen.SetChar(col, row, new TScreenChar(col % 2 == 0 ? '█' : ' ', (TColorAttr)(byte)(0x17 + col)));
        return screen;
    }

    private static SDLGpuRenderer HeadlessRenderer()
    {
        var renderer = new SDLGpuRenderer(IntPtr.Zero, new SDLGpuOptions());
        typeof(SDLGpuRenderer).GetField("_generatedGlyphs", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(renderer, new CellGlyphBitmapCache(renderer.CellWidth, renderer.CellHeight));
        return renderer;
    }

    [Fact]
    public void Cpu_fallback_without_layers_is_byte_identical_to_the_text_only_composite()
    {
        using var renderer = HeadlessRenderer();
        byte[] baseline = renderer.CompositeForTesting(Screen(), Cols, Rows).ToArray();
        Assert.Equal(baseline, renderer.CompositeForTesting(Screen(), Cols, Rows, PixelGraphicsFrame.Empty));
    }

    [Fact]
    public void Cpu_fallback_draws_layers_over_the_text_inside_the_clip_only()
    {
        using var renderer = HeadlessRenderer();
        int cw = renderer.CellWidth, ch = renderer.CellHeight, w = (int)Cols * cw;
        byte[] baseline = renderer.CompositeForTesting(Screen(), Cols, Rows).ToArray();
        // Clip: cells (1..2, 1); the fill spans the whole grid.
        PixelGraphicsFrame frame = Frame([new(cw, ch, 2 * cw, ch)], GraphicsPrimitive.FillRect(0, 0, w, (int)Rows * ch, Green));
        byte[] drawn = renderer.CompositeForTesting(Screen(), Cols, Rows, frame);

        for (int y = 0; y < (int)Rows * ch; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                bool inside = x >= cw && x < 3 * cw && y >= ch && y < 2 * ch;
                if (inside) Assert.Equal((0, 255, 0, 255), (drawn[o], drawn[o + 1], drawn[o + 2], drawn[o + 3]));
                else Assert.True(drawn.AsSpan(o, 4).SequenceEqual(baseline.AsSpan(o, 4)), $"pixel ({x},{y}) outside the clip changed");
            }
    }

    [Fact]
    public void Cpu_fallback_draws_images_nearest_into_the_destination()
    {
        using var renderer = HeadlessRenderer();
        int cw = renderer.CellWidth, ch = renderer.CellHeight;
        // A 2 x 2 opaque image scaled 3x into the top-left corner.
        PixelImage image = PixelImage.Create(2, 2, px =>
        {
            ReadOnlySpan<byte> c = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255];
            c.CopyTo(px);
        });
        PixelGraphicsFrame frame = Frame([new(0, 0, (int)Cols * cw, (int)Rows * ch)], [image], GraphicsPrimitive.Image(0, 0, 0, 6, 6));
        byte[] drawn = renderer.CompositeForTesting(Screen(), Cols, Rows, frame);
        int w = (int)Cols * cw;
        (byte, byte, byte) At(int x, int y) => (drawn[(y * w + x) * 4], drawn[(y * w + x) * 4 + 1], drawn[(y * w + x) * 4 + 2]);
        Assert.Equal((255, 0, 0), At(0, 0));
        Assert.Equal((255, 0, 0), At(2, 2));
        Assert.Equal((0, 255, 0), At(3, 0));
        Assert.Equal((0, 0, 255), At(0, 3));
        Assert.Equal((255, 255, 255), At(5, 5));
    }

    [Fact]
    public void Cpu_fallback_graphics_get_the_scanlines_effect_like_text()
    {
        using var renderer = HeadlessRenderer();
        int cw = renderer.CellWidth, ch = renderer.CellHeight;
        PixelGraphicsFrame frame = Frame([new(0, 0, (int)Cols * cw, (int)Rows * ch)],
            GraphicsPrimitive.FillRect(0, 0, (int)Cols * cw, (int)Rows * ch, 0xFFFFFFFF));
        byte[] plain = renderer.CompositeForTesting(Screen(), Cols, Rows, frame).ToArray();
        renderer.DisplayEffect = GpuDisplayEffect.Scanlines;
        byte[] lined = renderer.CompositeForTesting(Screen(), Cols, Rows, frame);
        Assert.All(Enumerable.Range(0, plain.Length / 4), i => Assert.Equal(255, plain[i * 4]));
        Assert.Contains(Enumerable.Range(0, lined.Length / 4), i => lined[i * 4] < 255);
    }

    [Fact]
    public void Default_renderer_publishes_an_empty_graphics_frame()
    {
        using var renderer = HeadlessRenderer();
        Assert.Same(PixelGraphicsFrame.Empty, renderer.GraphicsFrame);
        renderer.GraphicsFrame = null!;
        Assert.Same(PixelGraphicsFrame.Empty, renderer.GraphicsFrame);
    }
}
