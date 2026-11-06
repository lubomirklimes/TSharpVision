using TSharpVision.Drivers.SDL.Gpu;
using Xunit.Abstractions;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// Curvature on the real GPU, through the production renderer and the capture seam. Opt-in with
/// <c>TSV_NATIVE_GPU_TESTS=1</c>; backend from <c>TSHARPVISION_SDLGPU_BACKEND</c>. Screens are
/// full blocks only (no font raster). The key check is the hit test: the colour the GPU drew at a
/// destination pixel is the colour of the cell <c>PixelToCell(MapToSource(pixel))</c>, so pointer
/// mapping through <see cref="GpuCurvature.MapToSource(float, float, int, int, out float, out float)"/>
/// lands on the cell the user sees.
/// </summary>
[Collection("SDL initialization")]
public sealed class GpuCurvatureNativeTests
{
    private const uint Cols = 24, Rows = 8;
    private const int ReferenceTolerance = 3;

    private readonly ITestOutputHelper _output;
    public GpuCurvatureNativeTests(ITestOutputHelper output) => _output = output;

    private void Report(GpuFrameCapture c, string note = "")
    {
        _output.WriteLine($"backend={c.Backend} format={c.NativeFormat} mode={c.Mode} effect={c.Effect} {c.Width}x{c.Height} {note}");
        string? requested = SDLGpuOptions.NormalizeBackend(Environment.GetEnvironmentVariable("TSHARPVISION_SDLGPU_BACKEND"));
        if (requested is "direct3d12" or "vulkan" or "metal") Assert.Equal(requested, c.Backend);
    }

    // Full blocks; colour index (col + 4*row) % 16 differs from all 8 neighbours.
    private static ScreenBuffer CellScreen(uint cols = Cols, uint rows = Rows)
    {
        var screen = new ScreenBuffer((ushort)cols, (ushort)rows);
        for (uint row = 0; row < rows; row++)
            for (uint col = 0; col < cols; col++)
            {
                byte fg = (byte)((col + 4 * row) % 16);
                screen.SetChar(col, row, new TScreenChar('█', (TColorAttr)fg));
            }
        return screen;
    }

    private sealed class Session : IDisposable
    {
        public IntPtr Window;
        public SDLGpuRenderer Renderer = null!;
        public bool LastFramePresented;

        public static Session Open()
        {
            SDL3.SDL.SetHint(SDL3.SDL.Hints.QuitOnLastWindowClose, "0");
            Assert.True(SDL3.SDL.Init(SDL3.SDL.InitFlags.Video), SDL3.SDL.GetError());
            var s = new Session();
            s.Window = SDL3.SDL.CreateWindow("TSV curvature native test", 320, 200, SDL3.SDL.WindowFlags.Hidden);
            Assert.True(s.Window != IntPtr.Zero, SDL3.SDL.GetError());
            s.Renderer = new SDLGpuRenderer(s.Window, SDLGpuOptions.FromEnvironment());
            s.Renderer.Initialize();
            Assert.True(s.Renderer.ShaderPipelineActive);
            s.Renderer.SetCursor(-1, -1, 0);
            s.Renderer.StartRenderThread();
            return s;
        }

        public (uint W, uint H) Target(uint cols = Cols, uint rows = Rows, SdlCellMetrics? metrics = null) =>
            ((uint)((metrics?.PixelWidth ?? Renderer.CellWidth) * cols + 13),
             (uint)((metrics?.PixelHeight ?? Renderer.CellHeight) * rows + 7));

        public GpuFrameCapture Capture(ScreenBuffer screen, GpuDisplayEffect effect, (uint W, uint H) target)
        {
            long presented = Renderer.FramesPresented;
            Renderer.DisplayEffect = effect;
            var pending = Renderer.RequestCaptureForTesting(target.W, target.H, TimeSpan.FromSeconds(10));
            Renderer.Render(screen, 0, 0, screen.Width, screen.Height);
            Assert.True(pending.Wait(TimeSpan.FromSeconds(20)), "capture timed out");
            LastFramePresented = Renderer.FramesPresented > presented;
            return pending.Result;
        }

        public void AssertPresented(GpuDisplayEffect expected)
        {
            if (LastFramePresented) Assert.Equal(expected, Renderer.PresentedEffect);
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

    private static void AssertLetterboxBlack(GpuFrameCapture c, int gridW, int gridH)
    {
        for (int y = 0; y < c.Height; y++)
            for (int x = 0; x < c.Width; x++)
                if (x >= gridW || y >= gridH)
                    Assert.True(Px(c, x, y) == (0, 0, 0, 255), $"letterbox ({x},{y}) is {Px(c, x, y)}");
    }

    /// <summary>Max channel deviation from the CPU reference, and pixels above the tolerance.</summary>
    private static (int Max, int Over) CompareToReference(GpuFrameCapture plain, GpuFrameCapture curved, int gridW, int gridH)
    {
        byte[] src = Grid(plain, gridW, gridH);
        var expected = new byte[src.Length];
        GpuCurvature.ApplyReference(src, expected, gridW, gridH);
        byte[] actual = Grid(curved, gridW, gridH);
        int max = 0, over = 0;
        for (int p = 0; p < gridW * gridH; p++)
        {
            int dev = 0;
            for (int ch = 0; ch < 3; ch++) dev = Math.Max(dev, Math.Abs(expected[p * 4 + ch] - actual[p * 4 + ch]));
            max = Math.Max(max, dev);
            if (dev > ReferenceTolerance) over++;
        }
        return (max, over);
    }

    /// <summary>
    /// Hit test: for every destination pixel inside the warped image whose mapped source point is
    /// at least 1 px from a cell border and from the image edge, the GPU pixel equals (±2) the
    /// Default colour of cell PixelToCell(MapToSource(pixel centre)). Returns (checked, displaced):
    /// displaced = pixels whose cell differs from the unwarped cell under them.
    /// </summary>
    private static (int Checked, int Displaced) AssertHitTest(GpuFrameCapture plain, GpuFrameCapture curved, int gridW, int gridH, int cw, int ch)
    {
        int checkedPixels = 0, displaced = 0, mismatches = 0;
        string? firstMismatch = null;
        for (int y = 0; y < gridH; y++)
            for (int x = 0; x < gridW; x++)
            {
                if (!GpuCurvature.MapToSource(x + 0.5f, y + 0.5f, gridW, gridH, out float sx, out float sy)) continue;
                float lx = sx - MathF.Floor(sx / cw) * cw, ly = sy - MathF.Floor(sy / ch) * ch;
                if (lx < 1 || lx > cw - 1 || ly < 1 || ly > ch - 1) continue;
                if (sx < 1 || sy < 1 || sx > gridW - 1 || sy > gridH - 1) continue;
                TPoint cell = SdlMouseTranslator.PixelToCell((int)sx, (int)sy, cw, ch);
                var expected = Px(plain, cell.x * cw + cw / 2, cell.y * ch + ch / 2);
                var actual = Px(curved, x, y);
                checkedPixels++;
                if (cell != SdlMouseTranslator.PixelToCell(x, y, cw, ch)) displaced++;
                if (Math.Abs(expected.R - actual.R) > 2 || Math.Abs(expected.G - actual.G) > 2 || Math.Abs(expected.B - actual.B) > 2)
                {
                    mismatches++;
                    firstMismatch ??= $"dst ({x},{y}) -> src ({sx:F2},{sy:F2}) cell ({cell.x},{cell.y}) expected {expected} got {actual}";
                }
            }
        Assert.True(mismatches == 0, $"{mismatches} hit-test mismatches; first: {firstMismatch}");
        Assert.True(checkedPixels > gridW * gridH / 2, $"only {checkedPixels} pixels checked");
        Assert.True(displaced > 0, "the warp moved no pixel to another cell");
        return (checkedPixels, displaced);
    }

    // Every Default cell is one uniform colour, and neighbours differ: the hit test's premise.
    private static void AssertCellsUniform(GpuFrameCapture plain, uint cols, uint rows, int cw, int ch)
    {
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                var centre = Px(plain, c * cw + cw / 2, r * ch + ch / 2);
                for (int y = r * ch; y < (r + 1) * ch; y++)
                    for (int x = c * cw; x < (c + 1) * cw; x++)
                        Assert.True(Px(plain, x, y) == centre, $"cell ({c},{r}) not uniform at ({x},{y})");
                if (c + 1 < cols) Assert.NotEqual(centre, Px(plain, (c + 1) * cw + cw / 2, r * ch + ch / 2));
                if (r + 1 < rows) Assert.NotEqual(centre, Px(plain, c * cw + cw / 2, (r + 1) * ch + ch / 2));
            }
    }

    private void RunFrame(Session session, ScreenBuffer screen, uint cols, uint rows, int cw, int ch, (uint W, uint H) target, string label)
    {
        int gridW = (int)cols * cw, gridH = (int)rows * ch;
        GpuFrameCapture plain  = session.Capture(screen, GpuDisplayEffect.Default, target);
        GpuFrameCapture curved = session.Capture(screen, GpuDisplayEffect.Curvature, target);
        Assert.Equal("shader", curved.Mode);
        Assert.Equal(GpuDisplayEffect.Curvature, curved.Effect);
        session.AssertPresented(GpuDisplayEffect.Curvature);
        Assert.Null(session.Renderer.PostProcessUnavailableReason);
        Assert.NotEqual(plain.Rgba, curved.Rgba);
        AssertCellsUniform(plain, cols, rows, cw, ch);

        // Letterbox and corners black.
        AssertLetterboxBlack(curved, gridW, gridH);
        foreach (var (x, y) in new[] { (0, 0), (gridW - 1, 0), (0, gridH - 1), (gridW - 1, gridH - 1), (1, 1), (gridW - 2, gridH - 2) })
            Assert.True(Px(curved, x, y) == (0, 0, 0, 255), $"corner ({x},{y}) is {Px(curved, x, y)}");

        // Centre region (middle quarter) nearly identical to Default away from cell borders (where
        // the sub-pixel shift blends neighbouring cells).
        int centreMax = 0;
        for (int y = gridH * 3 / 8; y < gridH * 5 / 8; y++)
            for (int x = gridW * 3 / 8; x < gridW * 5 / 8; x++)
            {
                if (x % cw == 0 || x % cw == cw - 1 || y % ch == 0 || y % ch == ch - 1) continue;
                var a = Px(plain, x, y); var b = Px(curved, x, y);
                centreMax = Math.Max(centreMax, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
            }
        Assert.True(centreMax <= 2, $"centre deviates by {centreMax}");

        var (max, over) = CompareToReference(plain, curved, gridW, gridH);
        var (checkedPixels, displaced) = AssertHitTest(plain, curved, gridW, gridH, cw, ch);
        Report(curved, $"{label} grid={gridW}x{gridH} density={session.Renderer.Metrics.Density} maxDevFromCpuRef={max} overTol={over} centreMaxDev={centreMax} hitChecked={checkedPixels} hitDisplaced={displaced}");
        Assert.True(max <= ReferenceTolerance, $"GPU Curvature deviates from the CPU reference by {max} ({over} pixels over {ReferenceTolerance})");
    }

    [NativeGpuFact]
    public void Curvature_matches_the_cpu_reference_and_the_pointer_mapping()
    {
        using var session = Session.Open();
        int cw = session.Renderer.CellWidth, ch = session.Renderer.CellHeight;
        RunFrame(session, CellScreen(), Cols, Rows, cw, ch, session.Target(), "base");
    }

    [NativeGpuFact]
    public void Curvature_follows_a_resize_and_a_density_change()
    {
        using var session = Session.Open();
        int cw = session.Renderer.CellWidth, ch = session.Renderer.CellHeight;
        RunFrame(session, CellScreen(), Cols, Rows, cw, ch, session.Target(), "base");
        GpuPostProcess post = session.Renderer.PipelineForTesting!.PostProcessForTesting!;
        int created = post.TextureCreations;

        RunFrame(session, CellScreen(7, 3), 7, 3, cw, ch, session.Target(7, 3), "resized");
        Assert.Equal((7 * cw, 3 * ch), post.TextureSize);
        Assert.True(post.TextureCreations > created);
        Assert.Equal(3, post.LiveResources);   // texture + sampler + Curvature pipeline

        SdlCellMetrics next = session.Renderer.RequestPixelDensity(2f);
        RunFrame(session, CellScreen(), Cols, Rows, next.PixelWidth, next.PixelHeight, session.Target(metrics: next), "density2");
        Assert.Equal(2f, session.Renderer.Metrics.Density);
        Assert.Equal(((int)Cols * next.PixelWidth, (int)Rows * next.PixelHeight),
            session.Renderer.PipelineForTesting!.PostProcessForTesting!.TextureSize);
    }

    [NativeGpuFact]
    public void Default_after_Curvature_is_identical_to_the_Default_oracle()
    {
        using var session = Session.Open();
        ScreenBuffer screen = CellScreen();
        var target = session.Target();
        int gridW = (int)Cols * session.Renderer.CellWidth, gridH = (int)Rows * session.Renderer.CellHeight;

        GpuFrameCapture d1 = session.Capture(screen, GpuDisplayEffect.Default, target);
        GpuFrameCapture c1 = session.Capture(screen, GpuDisplayEffect.Curvature, target);
        GpuFrameCapture d2 = session.Capture(screen, GpuDisplayEffect.Default, target);
        session.AssertPresented(GpuDisplayEffect.Default);
        GpuFrameCapture c2 = session.Capture(screen, GpuDisplayEffect.Curvature, target);
        Report(d2);
        Assert.Equal(d1.Rgba, d2.Rgba);
        Assert.Equal(c1.Rgba, c2.Rgba);
        Assert.NotEqual(d1.Rgba, c1.Rgba);

        byte[] oracle = session.Renderer.CompositeForTesting(screen, Cols, Rows);
        for (int y = 0; y < gridH; y++)
            for (int x = 0; x < gridW; x++)
            {
                int o = (y * gridW + x) * 4;
                Assert.True(Px(d2, x, y) == (oracle[o], oracle[o + 1], oracle[o + 2], (byte)255),
                    $"Default pixel ({x},{y}) after Curvature differs from the CPU oracle");
            }
        AssertLetterboxBlack(d2, gridW, gridH);
    }

    [NativeGpuFact]
    public void Recording_a_Curvature_frame_allocates_no_managed_memory()
    {
        using var session = Session.Open();
        ScreenBuffer screen = CellScreen();
        var target = session.Target();
        session.Capture(screen, GpuDisplayEffect.Curvature, target);   // warm through the render thread

        TerminalGpuPipeline pipeline = session.Renderer.PipelineForTesting!;
        IntPtr device = session.Renderer.DeviceForTesting;
        SDL3.SDL.GPUTextureFormat format = SDL3.SDL.GetGPUSwapchainTextureFormat(device, session.Window);
        var texInfo = new SDL3.SDL.GPUTextureCreateInfo
        {
            Type = SDL3.SDL.GPUTextureType.TextureType2D, Format = format,
            Usage = SDL3.SDL.GPUTextureUsageFlags.ColorTarget, Width = target.W, Height = target.H,
            LayerCountOrDepth = 1, NumLevels = 1, SampleCount = SDL3.SDL.GPUSampleCount.SampleCount1,
        };
        IntPtr tex = SDL3.SDL.CreateGPUTexture(device, in texInfo);
        Assert.True(tex != IntPtr.Zero, SDL3.SDL.GetError());
        try
        {
            IntPtr cmd = SDL3.SDL.AcquireGPUCommandBuffer(device);
            Assert.True(cmd != IntPtr.Zero);
            for (int warm = 0; warm < 2; warm++)
                pipeline.RenderFrame(cmd, tex, target.W, target.H, screen, Cols, Rows, -1, -1, 0,
                    GpuPhysicalPalette.Default, GpuDisplayEffect.Curvature, session.Renderer.Metrics.Density);
            long before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.RenderFrame(cmd, tex, target.W, target.H, screen, Cols, Rows, -1, -1, 0,
                GpuPhysicalPalette.Default, GpuDisplayEffect.Curvature, session.Renderer.Metrics.Density);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(GpuDisplayEffect.Curvature, pipeline.LastDrawnEffect);
            SDL3.SDL.SubmitGPUCommandBuffer(cmd);
            SDL3.SDL.WaitForGPUIdle(device);
            _output.WriteLine($"effect=Curvature allocatedBytes={allocated}");
            Assert.Equal(0, allocated);
        }
        finally { SDL3.SDL.ReleaseGPUTexture(device, tex); }
    }
}