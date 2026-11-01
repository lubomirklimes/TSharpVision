using TSharpVision.Drivers.SDL.Gpu;
using TSharpVision.Drivers.SDL.Graphics;
using Xunit.Abstractions;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// TV12n/TV12o pixel graphics on the real GPU (opt-in, <see cref="NativeGpuFactAttribute"/>; run once per backend with
/// <c>TSHARPVISION_SDLGPU_BACKEND=direct3d12|vulkan</c>): the production renderer renders a frame with
/// <see cref="IPixelGraphicsDriver"/> layers and the capture seam reads it back. The instanced shader pass and the
/// CPU-composite fallback must equal the CPU oracle (<c>SDLGpuRenderer.CompositeForTesting</c>, i.e.
/// <see cref="PixelGraphicsRasterizer"/>) pixel for pixel for opaque colours and nearest images, within ±1 for
/// translucent blending and within <see cref="LinearTolerance"/> for linear images. Frames without layers must be
/// unchanged by graphics having been drawn, under every display effect. The screen uses only spaces and full blocks.
/// </summary>
[Collection("SDL initialization")]
public sealed class GpuGraphicsNativeTests
{
    private const uint Cols = 10, Rows = 4;

    // Translucent results: float GPU blending vs the CPU integer formula.
    private const int BlendTolerance = 1;
    // Linear images: hardware bilinear (8-bit sub-texel weights) vs the CPU double form (±2), plus blending (±1).
    private const int LinearTolerance = 3;

    private readonly ITestOutputHelper _output;
    public GpuGraphicsNativeTests(ITestOutputHelper output) => _output = output;

    private static ScreenBuffer Screen()
    {
        var screen = new ScreenBuffer((ushort)Cols, (ushort)Rows);
        for (uint row = 0; row < Rows; row++)
            for (uint col = 0; col < Cols; col++)
            {
                byte attr = (byte)(((col + row) % 8) << 4 | ((col * 3 + row) % 16));
                screen.SetChar(col, row, new TScreenChar((col + row) % 2 == 0 ? '█' : ' ', (TColorAttr)attr));
            }
        return screen;
    }

    private static PixelGraphicsFrame Layers(params (PixelRect[] Clip, GraphicsPrimitive[] Prims, PixelImage[]? Images)[] layers)
    {
        var store = new PixelGraphicsLayers();
        foreach (var (clip, prims, images) in layers) store.Set(new object(), clip, prims, images);
        return store.Frame;
    }

    // Two disjoint clip rectangles (cells 1-3 and 6-8 of rows 1-2), a fill larger than both and two crossing lines.
    private static PixelGraphicsFrame Layer(int cw, int ch) => Layers(
        ([new(cw, ch, 3 * cw, 2 * ch), new(6 * cw, ch, 3 * cw, 2 * ch)],
        [
            GraphicsPrimitive.FillRect(0, ch + 3, (int)Cols * cw, ch, 0x0000AAFF),
            GraphicsPrimitive.Line(0, 0, (int)Cols * cw - 1, (int)Rows * ch - 1, 0xFFFFFFFF),
            GraphicsPrimitive.Line((int)Cols * cw - 1, ch, 0, 3 * ch - 1, 0x55FF55FF),
        ], null));

    private static PixelRect Full(int cw, int ch) => new(0, 0, (int)Cols * cw, (int)Rows * ch);

    // Deterministic RGBA picture; alpha 255 unless `alphas` is given (cycled per texel).
    private static PixelImage Picture(int w, int h, int seed, byte[]? alphas = null) => PixelImage.Create(w, h, px =>
    {
        var rnd = new Random(seed);
        for (int i = 0; i < w * h; i++)
        {
            px[i * 4] = (byte)rnd.Next(256);
            px[i * 4 + 1] = (byte)rnd.Next(256);
            px[i * 4 + 2] = (byte)rnd.Next(256);
            px[i * 4 + 3] = alphas == null ? (byte)255 : alphas[i % alphas.Length];
        }
    });

    private sealed class Session : IDisposable
    {
        public IntPtr Window;
        public SDLGpuRenderer Renderer = null!;
        private SdlCellMetrics? _metrics;

        public static Session Open(bool cpuFallback = false)
        {
            SDL3.SDL.SetHint(SDL3.SDL.Hints.QuitOnLastWindowClose, "0");
            Assert.True(SDL3.SDL.Init(SDL3.SDL.InitFlags.Video), SDL3.SDL.GetError());
            var s = new Session();
            s.Window = SDL3.SDL.CreateWindow("TSV graphics native test", 320, 200, SDL3.SDL.WindowFlags.Hidden);
            Assert.True(s.Window != IntPtr.Zero, SDL3.SDL.GetError());
            s.Renderer = new SDLGpuRenderer(s.Window, SDLGpuOptions.FromEnvironment())
            {
                SimulatePipelineFailureForTesting = cpuFallback,
            };
            s.Renderer.Initialize();
            Assert.Equal(!cpuFallback, s.Renderer.ShaderPipelineActive);
            s.Renderer.SetCursor(-1, -1, 0);
            s.Renderer.StartRenderThread();
            return s;
        }

        public int Lw => Renderer.Metrics.LogicalWidth;
        public int Lh => Renderer.Metrics.LogicalHeight;

        // The density the next frame applies (the target is sized for it).
        public void Density(float density) => _metrics = Renderer.RequestPixelDensity(density);

        public (uint W, uint H) Target() =>
            ((uint)((_metrics?.PixelWidth ?? Renderer.CellWidth) * Cols + 13),
             (uint)((_metrics?.PixelHeight ?? Renderer.CellHeight) * Rows + 7));

        public GpuFrameCapture Capture(ScreenBuffer screen, PixelGraphicsFrame graphics, GpuDisplayEffect effect = GpuDisplayEffect.Default)
        {
            Renderer.DisplayEffect = effect;
            Renderer.GraphicsFrame = graphics;
            var (w, h) = Target();
            var pending = Renderer.RequestCaptureForTesting(w, h, TimeSpan.FromSeconds(10));
            Renderer.Render(screen, 0, 0, Cols, Rows);
            Assert.True(pending.Wait(TimeSpan.FromSeconds(20)), "capture timed out");
            return pending.Result;
        }

        public void Dispose()
        {
            Renderer?.Dispose();
            if (Window != IntPtr.Zero) SDL3.SDL.DestroyWindow(Window);
            SDL3.SDL.Quit();
        }
    }

    private static (byte R, byte G, byte B, byte A) Px(GpuFrameCapture c, int x, int y)
    {
        int o = (y * c.Width + x) * 4;
        return (c.Rgba[o], c.Rgba[o + 1], c.Rgba[o + 2], c.Rgba[o + 3]);
    }

    private static byte[] Grid(GpuFrameCapture c, int gridW, int gridH)
    {
        var px = new byte[gridW * gridH * 4];
        for (int y = 0; y < gridH; y++)
            Array.Copy(c.Rgba, y * c.Width * 4, px, y * gridW * 4, gridW * 4);
        return px;
    }

    // Compares the capture's grid with the CPU oracle (letterbox black); returns the largest channel deviation.
    private int AssertEqualsOracle(Session session, GpuFrameCapture capture, ScreenBuffer screen, PixelGraphicsFrame graphics,
        int tolerance = 0, string what = "")
    {
        int gridW = (int)Cols * session.Renderer.CellWidth, gridH = (int)Rows * session.Renderer.CellHeight;
        session.Renderer.DisplayEffect = GpuDisplayEffect.Default;
        byte[] oracle = session.Renderer.CompositeForTesting(screen, Cols, Rows, graphics);
        int max = 0, differing = 0;
        string? first = null;
        for (int y = 0; y < capture.Height; y++)
            for (int x = 0; x < capture.Width; x++)
            {
                var p = Px(capture, x, y);
                if (x < gridW && y < gridH)
                {
                    int o = (y * gridW + x) * 4;
                    int d = Math.Max(Math.Max(Math.Abs(p.R - oracle[o]), Math.Abs(p.G - oracle[o + 1])), Math.Abs(p.B - oracle[o + 2]));
                    if (d > 0) differing++;
                    if (d > max || (p.A != 255 && first == null))
                    {
                        max = Math.Max(max, d);
                        if (d > tolerance || p.A != 255)
                            first ??= $"{what} {capture.Mode} pixel ({x},{y}) {p} differs from the CPU oracle ({oracle[o]},{oracle[o + 1]},{oracle[o + 2]}) by {d}";
                    }
                }
                else Assert.True(p == (0, 0, 0, 255), $"{what} letterbox pixel ({x},{y}) is {p}");
            }
        _output.WriteLine($"backend={capture.Backend} format={capture.NativeFormat} mode={capture.Mode} {capture.Width}x{capture.Height} " +
            $"density={session.Renderer.Metrics.Density} {what}: maxDev={max} differing={differing} " +
            $"instances={session.Renderer.LastGraphicsInstanceCount}");
        Assert.True(first == null, $"{first} (max deviation {max}, {differing} pixels differ, tolerance {tolerance})");
        return max;
    }

    [NativeGpuFact]
    public void Shader_graphics_equal_the_cpu_oracle_and_leave_the_default_frame_unchanged()
    {
        using var session = Session.Open();
        ScreenBuffer screen = Screen();
        int cw = session.Lw, ch = session.Lh;

        GpuFrameCapture before = session.Capture(screen, PixelGraphicsFrame.Empty);
        Assert.Equal("shader", before.Mode);
        Assert.False(session.Renderer.PipelineForTesting!.GraphicsPipelineCreated); // no layer creates nothing

        PixelGraphicsFrame graphics = Layer(cw, ch);
        GpuFrameCapture drawn = session.Capture(screen, graphics);
        Assert.Equal("shader", drawn.Mode);
        Assert.Null(session.Renderer.GraphicsUnavailableReason);
        Assert.True(session.Renderer.PipelineForTesting!.GraphicsPipelineCreated);
        Assert.Equal(3, session.Renderer.LastGraphicsInstanceCount);                // one instance per primitive
        Assert.Equal(2, session.Renderer.PipelineForTesting!.LastGraphicsDrawCount); // one batch x two clips
        Assert.NotEqual(before.Rgba, drawn.Rgba);
        AssertEqualsOracle(session, drawn, screen, graphics);

        // Removing the layer restores the exact text-only frame.
        GpuFrameCapture after = session.Capture(screen, PixelGraphicsFrame.Empty);
        Assert.Equal(before.Rgba, after.Rgba);
    }

    [NativeGpuFact]
    public void Shader_scanlines_apply_to_graphics_and_change_nothing_outside_the_clip()
    {
        using var session = Session.Open();
        ScreenBuffer screen = Screen();
        int cw = session.Lw, ch = session.Lh;
        PixelGraphicsFrame graphics = Layer(cw, ch);

        GpuFrameCapture lined = session.Capture(screen, PixelGraphicsFrame.Empty, GpuDisplayEffect.Scanlines);
        GpuFrameCapture linedGraphics = session.Capture(screen, graphics, GpuDisplayEffect.Scanlines);
        int dx = session.Renderer.CellWidth / cw, dy = session.Renderer.CellHeight / ch;
        int changed = 0;
        for (int y = 0; y < lined.Height; y++)
            for (int x = 0; x < lined.Width; x++)
            {
                bool inClip = y >= ch * dy && y < 3 * ch * dy
                    && ((x >= cw * dx && x < 4 * cw * dx) || (x >= 6 * cw * dx && x < 9 * cw * dx));
                if (!inClip) Assert.True(Px(lined, x, y) == Px(linedGraphics, x, y), $"pixel ({x},{y}) outside the clip changed");
                else if (Px(lined, x, y) != Px(linedGraphics, x, y)) changed++;
            }
        Assert.True(changed > 0, "graphics did not change the clipped region");
    }

    [NativeGpuFact]
    public void Cpu_fallback_graphics_equal_the_cpu_oracle()
    {
        using var session = Session.Open(cpuFallback: true);
        ScreenBuffer screen = Screen();
        PixelGraphicsFrame graphics = Layer(session.Lw, session.Lh);
        GpuFrameCapture drawn = session.Capture(screen, graphics);
        Assert.Equal("cpu-composite", drawn.Mode);
        AssertEqualsOracle(session, drawn, screen, graphics);

        // Images on the CPU fallback (PixelGraphicsRasterizer image support).
        var (prims, images) = ImageScene(session.Lw, session.Lh, GraphicsImageFilter.Nearest);
        PixelGraphicsFrame withImages = Layers(([Full(session.Lw, session.Lh)], prims, images));
        AssertEqualsOracle(session, session.Capture(screen, withImages), screen, withImages, what: "cpu images");
    }

    // Lines in every octant and direction: a star from the centre to points around (and beyond) the grid, short
    // lines of every slope, axis-parallel lines, single pixels and lines that start outside the grid.
    private static GraphicsPrimitive[] LineSet(int w, int h)
    {
        var lines = new List<GraphicsPrimitive>();
        int cx = w / 2, cy = h / 2;
        static uint Color(int i) =>
            (uint)(((40 + i * 53) & 255) << 24 | ((90 + i * 97) & 255) << 16 | ((200 + i * 31) & 255) << 8) | 0xFF;
        for (int i = 0; i < 48; i++)
        {
            double a = i * Math.PI * 2 / 48;
            int ex = cx + (int)Math.Round(Math.Cos(a) * w * 0.6), ey = cy + (int)Math.Round(Math.Sin(a) * h * 0.6);
            lines.Add(i % 2 == 0 ? GraphicsPrimitive.Line(cx, cy, ex, ey, Color(i)) : GraphicsPrimitive.Line(ex, ey, cx, cy, Color(i)));
        }
        for (int dx = -5; dx <= 5; dx++)
            for (int dy = -5; dy <= 5; dy++)
            {
                int x = 6 + (dx + 5) * (w - 12) / 10, y = 6 + (dy + 5) * (h - 12) / 10;
                lines.Add(GraphicsPrimitive.Line(x, y, x + dx, y + dy, Color(dx * 11 + dy + 60)));
            }
        lines.Add(GraphicsPrimitive.Line(0, 1, w - 1, 1, 0xFFFF00FF));
        lines.Add(GraphicsPrimitive.Line(w - 2, 0, w - 2, h - 1, 0x00FFFFFF));
        lines.Add(GraphicsPrimitive.Line(3, h - 2, 3, h - 2, 0xFF00FFFF));
        lines.Add(GraphicsPrimitive.Line(-37, -11, w + 23, h + 5, 0xFFFFFFFF));
        lines.Add(GraphicsPrimitive.Line(w + 40, -3, -9, h / 2, 0x8080FFFF));
        lines.Add(GraphicsPrimitive.Line(5, -200, 9, h + 200, 0xFF8000FF));
        return lines.ToArray();
    }

    [NativeGpuFact]
    public void Lines_of_every_slope_and_direction_equal_the_cpu_oracle_at_density_1_2_and_1_5()
    {
        using var session = Session.Open();
        ScreenBuffer screen = Screen();
        foreach (float density in new[] { 1f, 2f, 1.5f })
        {
            session.Density(density);
            int cw = session.Lw, ch = session.Lh;
            PixelGraphicsFrame graphics = Layers(([Full(cw, ch)], LineSet((int)Cols * cw, (int)Rows * ch), null));
            GpuFrameCapture drawn = session.Capture(screen, graphics);
            Assert.Equal(density, session.Renderer.Metrics.Density);
            Assert.Equal(graphics.PrimitiveCount, session.Renderer.LastGraphicsInstanceCount);
            AssertEqualsOracle(session, drawn, screen, graphics, what: $"lines@{density}");
        }
    }

    [NativeGpuFact]
    public void Clipping_with_several_rectangles_and_fills_equal_the_cpu_oracle()
    {
        using var session = Session.Open();
        ScreenBuffer screen = Screen();
        foreach (float density in new[] { 1f, 2f })
        {
            session.Density(density);
            int cw = session.Lw, ch = session.Lh, w = (int)Cols * cw, h = (int)Rows * ch;
            // Four rectangles, two of them overlapping, one partly outside the grid; opaque fills and lines.
            PixelRect[] clip =
            [
                new(3, 2, cw * 2 + 5, ch + 3), new(cw * 4, 0, cw * 2, h), new(cw * 5, ch, cw * 3, ch * 2), new(w - 7, h - 9, 40, 40),
            ];
            var prims = new List<GraphicsPrimitive>
            {
                GraphicsPrimitive.FillRect(-5, -5, w + 10, h + 10, 0x203040FF),
                GraphicsPrimitive.FillRect(1, 1, w / 2, h / 2, 0xC0C000FF),
                GraphicsPrimitive.FillRect(w / 3, h / 4, 1, 1, 0xFF00FFFF),
            };
            prims.AddRange(LineSet(w, h).Take(30));
            PixelGraphicsFrame graphics = Layers((clip, prims.ToArray(), null));
            GpuFrameCapture drawn = session.Capture(screen, graphics);
            Assert.Equal(4, session.Renderer.PipelineForTesting!.LastGraphicsDrawCount); // one batch x four clips
            AssertEqualsOracle(session, drawn, screen, graphics, what: $"clips@{density}");
        }
    }

    [NativeGpuFact]
    public void Translucent_fills_and_lines_blend_within_one_step_of_the_cpu_oracle()
    {
        using var session = Session.Open();
        ScreenBuffer screen = Screen();
        int cw = session.Lw, ch = session.Lh, w = (int)Cols * cw, h = (int)Rows * ch;
        PixelGraphicsFrame graphics = Layers(([Full(cw, ch)],
        [
            GraphicsPrimitive.FillRect(0, 0, w / 2, h, 0xFF000080),
            GraphicsPrimitive.FillRect(w / 4, h / 4, w / 2, h / 2, 0x00FF0040),
            GraphicsPrimitive.FillRect(w / 3, 0, 5, h, 0x0000FFC0),
            GraphicsPrimitive.Line(0, 0, w - 1, h - 1, 0xFFFFFF7F),
            GraphicsPrimitive.Line(0, h - 1, w - 1, 0, 0xFFFFFF01),
        ], null));
        GpuFrameCapture drawn = session.Capture(screen, graphics);
        AssertEqualsOracle(session, drawn, screen, graphics, BlendTolerance, "translucent");
    }

    // Image primitives at integer, non-integer, down- and off-grid scales.
    private static (GraphicsPrimitive[] Prims, PixelImage[] Images) ImageScene(int cw, int ch, GraphicsImageFilter filter, byte opacity = 255)
    {
        PixelImage a = Picture(7, 5, 1), b = Picture(16, 9, 2), c = Picture(3, 3, 3);
        GraphicsPrimitive[] prims =
        [
            GraphicsPrimitive.Image(0, 1, 1, 14, 10, filter, opacity),                          // 2x
            GraphicsPrimitive.Image(0, 17, 2, 23, 13, filter, opacity),                         // non-integer 3.29 x 2.6
            GraphicsPrimitive.Image(1, 42, 1, 11, 7, filter, opacity),                          // down-scaled 16x9 -> 11x7
            GraphicsPrimitive.Image(1, -9, ch * 2, cw * 3, ch + 5, filter, opacity),            // partly off the grid
            GraphicsPrimitive.Image(2, cw * 5 + 1, ch, cw * 4 - 3, ch * 2 + 1, filter, opacity), // big upscale
            GraphicsPrimitive.Image(1, cw * 9, ch * 3, 1, 1, filter, opacity),                  // one pixel
        ];
        return (prims, [a, b, c]);
    }

    [NativeGpuFact]
    public void Nearest_images_equal_the_cpu_oracle_exactly_at_integer_and_non_integer_scales()
    {
        using var session = Session.Open();
        ScreenBuffer screen = Screen();
        foreach (float density in new[] { 1f, 2f, 1.5f })
        {
            session.Density(density);
            var (prims, images) = ImageScene(session.Lw, session.Lh, GraphicsImageFilter.Nearest);
            PixelGraphicsFrame graphics = Layers(([Full(session.Lw, session.Lh)], prims, images));
            GpuFrameCapture drawn = session.Capture(screen, graphics);
            Assert.Null(session.Renderer.GraphicsUnavailableReason);
            Assert.Equal(prims.Length, session.Renderer.LastGraphicsInstanceCount);
            AssertEqualsOracle(session, drawn, screen, graphics, what: $"nearest@{density}");
        }
    }

    [NativeGpuFact]
    public void Linear_images_stay_within_tolerance_of_the_cpu_oracle()
    {
        using var session = Session.Open();
        ScreenBuffer screen = Screen();
        foreach (float density in new[] { 1f, 2f })
        {
            session.Density(density);
            var (prims, images) = ImageScene(session.Lw, session.Lh, GraphicsImageFilter.Linear);
            PixelGraphicsFrame graphics = Layers(([Full(session.Lw, session.Lh)], prims, images));
            GpuFrameCapture drawn = session.Capture(screen, graphics);
            AssertEqualsOracle(session, drawn, screen, graphics, LinearTolerance, $"linear@{density}");
        }
    }

    [NativeGpuFact]
    public void Image_transparency_and_opacity_blend_within_tolerance_of_the_cpu_oracle()
    {
        using var session = Session.Open();
        ScreenBuffer screen = Screen();
        int cw = session.Lw, ch = session.Lh;
        byte[] alphas = [0, 255, 64, 128, 200, 1, 254];
        PixelImage translucent = Picture(9, 6, 4, alphas);
        foreach (byte opacity in new byte[] { 255, 128, 37 })
        {
            PixelGraphicsFrame graphics = Layers(([Full(cw, ch)],
            [
                GraphicsPrimitive.Image(0, 2, 2, cw * 4, ch * 2, GraphicsImageFilter.Nearest, opacity),
                GraphicsPrimitive.Image(0, cw * 5, 2, cw * 4 + 3, ch * 3, GraphicsImageFilter.Linear, opacity),
            ], [translucent]));
            GpuFrameCapture drawn = session.Capture(screen, graphics);
            AssertEqualsOracle(session, drawn, screen, graphics, LinearTolerance, $"alpha opacity={opacity}");
        }
        // Nearest alone: translucent blending only (±1).
        PixelGraphicsFrame nearest = Layers(([Full(cw, ch)],
            [GraphicsPrimitive.Image(0, 1, 1, cw * 8, ch * 3, GraphicsImageFilter.Nearest, 150)], [translucent]));
        AssertEqualsOracle(session, session.Capture(screen, nearest), screen, nearest, BlendTolerance, "nearest translucent");
    }

    [NativeGpuFact]
    public void Images_and_primitives_keep_their_order_across_kinds_and_layers()
    {
        using var session = Session.Open();
        ScreenBuffer screen = Screen();
        foreach (float density in new[] { 1f, 2f })
        {
            session.Density(density);
            int cw = session.Lw, ch = session.Lh, w = (int)Cols * cw, h = (int)Rows * ch;
            PixelImage a = Picture(8, 8, 5), b = Picture(5, 3, 6);
            PixelGraphicsFrame graphics = Layers(
                ([Full(cw, ch)],
                [
                    GraphicsPrimitive.FillRect(0, 0, w, h, 0x101010FF),
                    GraphicsPrimitive.Image(0, 4, 4, cw * 3, ch * 2),
                    GraphicsPrimitive.FillRect(cw, ch, cw, ch * 3, 0xFF0000FF),       // covers part of the image
                    GraphicsPrimitive.Image(1, cw * 2, ch / 2, cw * 4, ch),            // over the fill
                    GraphicsPrimitive.Line(0, h - 1, w - 1, 0, 0x00FF00FF),            // over both images
                    GraphicsPrimitive.Image(0, cw * 7, ch, cw * 2, ch * 2),
                ], [a, b]),
                // Second layer: its own clip; an image over the first layer and a line over that image.
                ([new(cw * 3, ch, cw * 4, ch * 2)],
                [
                    GraphicsPrimitive.Image(0, cw * 2, 0, cw * 6, ch * 4),
                    GraphicsPrimitive.Line(cw * 3, ch, cw * 7 - 1, ch * 3 - 1, 0xFFFF00FF),
                ], [a]));
            GpuFrameCapture drawn = session.Capture(screen, graphics);
            Assert.Equal(8, session.Renderer.LastGraphicsInstanceCount);
            AssertEqualsOracle(session, drawn, screen, graphics, what: $"order@{density}");
        }
    }

    [NativeGpuFact]
    public void Image_textures_are_uploaded_once_reused_and_released_with_their_layer()
    {
        using var session = Session.Open();
        ScreenBuffer screen = Screen();
        int cw = session.Lw, ch = session.Lh;
        PixelImage a = Picture(32, 20, 7);
        PixelGraphicsFrame first = Layers(([Full(cw, ch)], [GraphicsPrimitive.Image(0, 0, 0, cw * 4, ch * 2)], [a]));
        session.Capture(screen, first);
        TerminalGpuPipeline pipeline = session.Renderer.PipelineForTesting!;
        GpuImageCache cache = pipeline.GraphicsImagesForTesting!;
        Assert.Equal(1, cache.Created);

        // A changed layer with the same image: nothing but the instance is uploaded, no texture is created.
        PixelGraphicsFrame moved = Layers(([Full(cw, ch)], [GraphicsPrimitive.Image(0, cw, ch, cw * 4, ch * 2)], [a]));
        GpuFrameCapture drawn = session.Capture(screen, moved);
        Assert.Equal(1, cache.Created);
        Assert.Equal(GpuGraphicsInstance.Size, pipeline.LastGraphicsUploadBytes);
        AssertEqualsOracle(session, drawn, screen, moved, what: "reused");

        // A new image replaces the old one: one more texture, the old one released.
        PixelImage b = Picture(32, 20, 8);
        session.Capture(screen, Layers(([Full(cw, ch)], [GraphicsPrimitive.Image(0, 0, 0, cw * 4, ch * 2)], [b])));
        Assert.Equal((2, 1, 1), (cache.Created, cache.Released, cache.Count));
        Assert.Equal(32L * 20 * 4, cache.TotalBytes);

        // No graphics: every texture is released; the frame is the text-only frame.
        GpuFrameCapture none = session.Capture(screen, PixelGraphicsFrame.Empty);
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.TotalBytes);
        AssertEqualsOracle(session, none, screen, PixelGraphicsFrame.Empty, what: "released");
    }

    [NativeGpuFact]
    public void A_failed_graphics_frame_falls_back_to_text_only_rendering()
    {
        using var session = Session.Open();
        ScreenBuffer screen = Screen();
        int cw = session.Lw, ch = session.Lh;
        GpuFrameCapture text = session.Capture(screen, PixelGraphicsFrame.Empty);
        PixelImage picture = Picture(4, 4, 11);
        PixelGraphicsFrame graphics = Layers(([Full(cw, ch)],
            [GraphicsPrimitive.Image(0, 0, 0, cw, ch), GraphicsPrimitive.Line(0, 0, cw * 9, ch * 3, 0xFFFFFFFF)], [picture]));
        session.Capture(screen, graphics);
        Assert.Null(session.Renderer.GraphicsUnavailableReason);

        session.Renderer.PipelineForTesting!.SimulateGraphicsFrameFailureForTesting = true;
        PixelGraphicsFrame changed = Layers(([Full(cw, ch)], [GraphicsPrimitive.Line(0, 0, cw * 9, ch * 2, 0xFFFFFFFF)], null));
        GpuFrameCapture failed = session.Capture(screen, changed);
        Assert.NotNull(session.Renderer.GraphicsUnavailableReason);
        Assert.Equal(text.Rgba, failed.Rgba);
        Assert.Equal(0, session.Renderer.PipelineForTesting!.GraphicsImagesForTesting!.Count);
        Assert.False(session.Renderer.PipelineForTesting!.GraphicsPipelineCreated); // resources released
        // Later frames keep rendering text.
        Assert.Equal(text.Rgba, session.Capture(screen, graphics).Rgba);
    }

    // ── Display effects and palettes ───────────────────────────────────────

    private static int MaxDeviation(byte[] expected, byte[] actual)
    {
        int max = 0;
        for (int i = 0; i < expected.Length; i++)
            if (i % 4 != 3) max = Math.Max(max, Math.Abs(expected[i] - actual[i]));
        return max;
    }

    [NativeGpuFact]
    public void Display_effects_apply_to_graphics_and_graphics_free_frames_stay_unchanged()
    {
        using var session = Session.Open();
        ScreenBuffer screen = Screen();
        int cw = session.Lw, ch = session.Lh;
        PixelImage picture = Picture(12, 8, 9);
        PixelGraphicsFrame graphics = Layers(([new(cw, 2, cw * 7, ch * 3)],
        [
            GraphicsPrimitive.FillRect(0, ch, cw * 10, ch, 0xFFFFFFFF),
            GraphicsPrimitive.Image(0, cw * 2, 4, cw * 3, ch * 2),
            GraphicsPrimitive.Line(0, 0, cw * 10, ch * 4, 0xFFFF00FF),
        ], [picture]));
        int gridW = (int)Cols * session.Renderer.CellWidth, gridH = (int)Rows * session.Renderer.CellHeight;
        float density = session.Renderer.Metrics.Density;

        GpuDisplayEffect[] effects =
            [GpuDisplayEffect.Default, GpuDisplayEffect.Scanlines, GpuDisplayEffect.Glow, GpuDisplayEffect.Curvature, GpuDisplayEffect.ShadowMask];
        var textOnly = new Dictionary<GpuDisplayEffect, GpuFrameCapture>();
        foreach (GpuDisplayEffect effect in effects) textOnly[effect] = session.Capture(screen, PixelGraphicsFrame.Empty, effect);
        GpuFrameCapture plain = session.Capture(screen, graphics);
        AssertEqualsOracle(session, plain, screen, graphics, what: "effects base");
        byte[] src = Grid(plain, gridW, gridH);

        foreach (GpuDisplayEffect effect in effects)
        {
            GpuFrameCapture drawn = session.Capture(screen, graphics, effect);
            Assert.Equal(effect, drawn.Effect);
            Assert.NotEqual(textOnly[effect].Rgba, drawn.Rgba);
            byte[] actual = Grid(drawn, gridW, gridH);
            var expected = new byte[src.Length];
            int tolerance;
            switch (effect)
            {
                case GpuDisplayEffect.Default:
                    src.CopyTo(expected);
                    tolerance = 0;
                    break;
                case GpuDisplayEffect.Scanlines:
                    src.CopyTo(expected);
                    foreach (GpuScanlineStrip s in GpuScanlineGeometry.ComputeStrips(gridW, gridH, density))
                        for (int y = s.Y; y < s.Y + s.Height; y++)
                            for (int i = y * gridW * 4; i < (y + 1) * gridW * 4; i++)
                                if (i % 4 != 3) expected[i] = GpuAlphaBlend.Blend(0, src[i], GpuScanlineGeometry.StripAlpha);
                    tolerance = 1;
                    break;
                case GpuDisplayEffect.Glow:
                    GpuGlow.ApplyReference(src, expected, gridW, gridH, density);
                    tolerance = 2;
                    break;
                case GpuDisplayEffect.Curvature:
                    GpuCurvature.ApplyReference(src, expected, gridW, gridH);
                    tolerance = 3;
                    break;
                default:
                    GpuShadowMask.ApplyReference(src, expected, gridW, gridH, density);
                    tolerance = 2;
                    break;
            }
            int deviation = MaxDeviation(expected, actual);
            _output.WriteLine($"backend={drawn.Backend} effect={effect} with graphics: maxDevFromCpuRef={deviation}");
            Assert.True(deviation <= tolerance, $"{effect} with graphics deviates from the reference of the graphics frame by {deviation}");

            // The same effect without graphics, after graphics were drawn: byte-identical to before.
            Assert.Equal(textOnly[effect].Rgba, session.Capture(screen, PixelGraphicsFrame.Empty, effect).Rgba);
        }
    }

    [NativeGpuFact]
    public void Physical_palettes_do_not_recolour_graphics()
    {
        using var session = Session.Open();
        ScreenBuffer screen = Screen();
        int cw = session.Lw, ch = session.Lh;
        PixelImage picture = Picture(6, 4, 10);
        PixelGraphicsFrame graphics = Layers(([new PixelRect(cw, ch, cw * 6, ch * 2)],
        [
            GraphicsPrimitive.FillRect(0, 0, cw * 10, ch * 4, 0x3366CCFF),
            GraphicsPrimitive.Image(0, cw * 2, ch, cw * 3, ch * 2),
            GraphicsPrimitive.Line(0, 0, cw * 10, ch * 4, 0xFFAA00FF),
        ], [picture]));
        GpuFrameCapture reference = session.Capture(screen, graphics);
        int dw = session.Renderer.CellWidth, dh = session.Renderer.CellHeight;
        try
        {
            foreach (GpuPhysicalPalette palette in GpuPhysicalPalette.All.Where(p => p != GpuPhysicalPalette.Default))
            {
                session.Renderer.PhysicalPalette = palette;
                GpuFrameCapture drawn = session.Capture(screen, graphics);
                int textDiffers = 0;
                for (int y = 0; y < (int)Rows * dh; y++)
                    for (int x = 0; x < (int)Cols * dw; x++)
                    {
                        bool inClip = x >= dw && x < 7 * dw && y >= dh && y < 3 * dh;
                        if (inClip) Assert.True(Px(drawn, x, y) == Px(reference, x, y), $"{palette.Id}: graphics pixel ({x},{y}) recoloured");
                        else if (Px(drawn, x, y) != Px(reference, x, y)) textDiffers++;
                    }
                Assert.True(textDiffers > 0, $"{palette.Id} did not change the text");
                _output.WriteLine($"backend={drawn.Backend} palette={palette.Id}: graphics unchanged, {textDiffers} text pixels recoloured");
            }
        }
        finally
        {
            session.Renderer.PhysicalPalette = GpuPhysicalPalette.Default;
        }
    }
}
