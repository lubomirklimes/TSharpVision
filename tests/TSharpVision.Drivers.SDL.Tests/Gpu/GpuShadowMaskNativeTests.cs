using TSharpVision.Drivers.SDL.Gpu;
using Xunit.Abstractions;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// ShadowMask on the real GPU, through the production renderer and the capture seam. Opt-in with
/// <c>TSV_NATIVE_GPU_TESTS=1</c>; backend from <c>TSHARPVISION_SDLGPU_BACKEND</c>. Screens use
/// only spaces and full blocks, so no font raster enters the comparisons.
/// </summary>
[Collection("SDL initialization")]
public sealed class GpuShadowMaskNativeTests
{
    private const uint Cols = 10, Rows = 4;
    private const int ReferenceTolerance = 2;

    private readonly ITestOutputHelper _output;
    public GpuShadowMaskNativeTests(ITestOutputHelper output) => _output = output;

    private void Report(GpuFrameCapture c, string note = "")
    {
        _output.WriteLine($"backend={c.Backend} format={c.NativeFormat} mode={c.Mode} effect={c.Effect} {c.Width}x{c.Height} {note}");
        string? requested = SDLGpuOptions.NormalizeBackend(Environment.GetEnvironmentVariable("TSHARPVISION_SDLGPU_BACKEND"));
        if (requested is "direct3d12" or "vulkan" or "metal") Assert.Equal(requested, c.Backend);
    }

    private static ScreenBuffer FlatScreen(byte attr, uint cols = Cols, uint rows = Rows)
    {
        var screen = new ScreenBuffer((ushort)cols, (ushort)rows);
        for (uint row = 0; row < rows; row++)
            for (uint col = 0; col < cols; col++)
                screen.SetChar(col, row, new TScreenChar(' ', (TColorAttr)attr));
        return screen;
    }

    private static ScreenBuffer VariedScreen(uint cols = Cols, uint rows = Rows)
    {
        var screen = new ScreenBuffer((ushort)cols, (ushort)rows);
        for (uint row = 0; row < rows; row++)
            for (uint col = 0; col < cols; col++)
            {
                byte attr = (byte)(((col + row) % 8) << 4 | ((col * 3 + row) % 16));
                screen.SetChar(col, row, new TScreenChar((col + row) % 2 == 0 ? '█' : ' ', (TColorAttr)attr));
            }
        return screen;
    }

    // Left half white full blocks, right half black: one sharp vertical edge at Cols/2.
    private static ScreenBuffer EdgeScreen()
    {
        var screen = new ScreenBuffer((ushort)Cols, (ushort)Rows);
        for (uint row = 0; row < Rows; row++)
            for (uint col = 0; col < Cols; col++)
                screen.SetChar(col, row, new TScreenChar(col < Cols / 2 ? '█' : ' ', (TColorAttr)0x0F));
        return screen;
    }

    private sealed class Session : IDisposable
    {
        public IntPtr Window;
        public SDLGpuRenderer Renderer = null!;
        public bool LastFramePresented;

        public static Session Open(int? fontSize = null)
        {
            SDL3.SDL.SetHint(SDL3.SDL.Hints.QuitOnLastWindowClose, "0");
            Assert.True(SDL3.SDL.Init(SDL3.SDL.InitFlags.Video), SDL3.SDL.GetError());
            var s = new Session();
            s.Window = SDL3.SDL.CreateWindow("TSV shadow-mask native test", 320, 200, SDL3.SDL.WindowFlags.Hidden);
            Assert.True(s.Window != IntPtr.Zero, SDL3.SDL.GetError());
            s.Renderer = fontSize is int size
                ? new SDLGpuRenderer(s.Window, SDLGpuOptions.FromEnvironment(), null, size)
                : new SDLGpuRenderer(s.Window, SDLGpuOptions.FromEnvironment());
            s.Renderer.Initialize();
            Assert.True(s.Renderer.ShaderPipelineActive);
            s.Renderer.StartRenderThread();
            s.Renderer.SetCursor(-1, -1, 0);
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

    private static int MaxDeviationFromReference(GpuFrameCapture plain, GpuFrameCapture masked, int gridW, int gridH, float density)
    {
        byte[] src = Grid(plain, gridW, gridH);
        var expected = new byte[src.Length];
        GpuShadowMask.ApplyReference(src, expected, gridW, gridH, density);
        byte[] actual = Grid(masked, gridW, gridH);
        int max = 0;
        for (int i = 0; i < src.Length; i++)
            if (i % 4 != 3) max = Math.Max(max, Math.Abs(expected[i] - actual[i]));
        return max;
    }

    // On a flat field of value `level`: every grid pixel is the bright level or the dark level,
    // exactly where IsDark says so for `pitch` (device-pixel anchored, no cell input).
    private static (int Dark, int Bright) AssertFlatPattern(GpuFrameCapture masked, int gridW, int gridH, int pitch, byte level)
    {
        byte dark = GpuShadowMask.Scale(level, GpuShadowMask.DarkFactor);
        int nd = 0, nb = 0;
        for (int y = 0; y < gridH; y++)
            for (int x = 0; x < gridW; x++)
            {
                var p = Px(masked, x, y);
                bool isDark = GpuShadowMask.IsDark(x, y, pitch);
                int expected = isDark ? dark : level;
                Assert.True(Math.Abs(p.R - expected) <= 1 && Math.Abs(p.G - expected) <= 1 && Math.Abs(p.B - expected) <= 1,
                    $"({x},{y}) pitch {pitch}: {p}, expected {(isDark ? "dark" : "bright")} {expected}");
                if (isDark) nd++; else nb++;
            }
        return (nd, nb);
    }

    [NativeGpuFact]
    public void ShadowMask_draws_the_periodic_pattern_and_matches_the_cpu_reference()
    {
        using var session = Session.Open();
        float density = session.Renderer.Metrics.Density;
        int pitch = GpuShadowMask.PitchPixels(density);
        int gridW = (int)Cols * session.Renderer.CellWidth, gridH = (int)Rows * session.Renderer.CellHeight;
        var target = session.Target();

        // Flat light-gray field (0xAA): the pattern itself.
        ScreenBuffer flat = FlatScreen(0x70);
        GpuFrameCapture plain = session.Capture(flat, GpuDisplayEffect.Default, target);
        GpuFrameCapture mask  = session.Capture(flat, GpuDisplayEffect.ShadowMask, target);
        Assert.Equal("shader", mask.Mode);
        Assert.Equal(GpuDisplayEffect.ShadowMask, mask.Effect);
        session.AssertPresented(GpuDisplayEffect.ShadowMask);
        Assert.Null(session.Renderer.PostProcessUnavailableReason);
        Assert.NotEqual(plain.Rgba, mask.Rgba);
        AssertLetterboxBlack(mask, gridW, gridH);
        var (nd, nb) = AssertFlatPattern(mask, gridW, gridH, pitch, 0xAA);
        int devFlat = MaxDeviationFromReference(plain, mask, gridW, gridH, density);
        Report(mask, $"screen=flat density={density} pitch={pitch} dark={nd} bright={nb} sample(0..5,0)={string.Join(",", Enumerable.Range(0, 6).Select(x => Px(mask, x * pitch, 0).R))} maxDevFromCpuRef={devFlat}");
        Assert.True(devFlat <= ReferenceTolerance, $"flat deviation {devFlat}");

        // Varied colours: the GPU equals the CPU reference.
        ScreenBuffer varied = VariedScreen();
        GpuFrameCapture vp = session.Capture(varied, GpuDisplayEffect.Default, target);
        GpuFrameCapture vm = session.Capture(varied, GpuDisplayEffect.ShadowMask, target);
        AssertLetterboxBlack(vm, gridW, gridH);
        int devVaried = MaxDeviationFromReference(vp, vm, gridW, gridH, density);
        Report(vm, $"screen=varied maxDevFromCpuRef={devVaried}");
        Assert.True(devVaried <= ReferenceTolerance, $"varied deviation {devVaried}");
    }

    [NativeGpuFact]
    public void A_sharp_edge_stays_sharp()
    {
        using var session = Session.Open();
        float density = session.Renderer.Metrics.Density;
        int pitch = GpuShadowMask.PitchPixels(density);
        int cw = session.Renderer.CellWidth, ch = session.Renderer.CellHeight;
        int gridW = (int)Cols * cw, gridH = (int)Rows * ch, edge = (int)(Cols / 2) * cw;
        var target = session.Target();

        GpuFrameCapture plain = session.Capture(EdgeScreen(), GpuDisplayEffect.Default, target);
        GpuFrameCapture mask  = session.Capture(EdgeScreen(), GpuDisplayEffect.ShadowMask, target);
        Assert.Equal(GpuDisplayEffect.ShadowMask, mask.Effect);
        for (int y = 0; y < gridH; y++)
        {
            // Plain: white up to the edge, black after (full blocks fill their cell).
            Assert.Equal((byte)255, Px(plain, edge - 1, y).R);
            Assert.Equal((byte)0, Px(plain, edge, y).R);
            // Masked: the last white pixel is white or the dark level, never blended with black;
            // the first black pixel stays exactly black.
            byte expected = GpuShadowMask.IsDark(edge - 1, y, pitch) ? GpuShadowMask.Scale(255, GpuShadowMask.DarkFactor) : (byte)255;
            Assert.True(Math.Abs(Px(mask, edge - 1, y).R - expected) <= 1, $"edge-1 row {y}: {Px(mask, edge - 1, y)}");
            Assert.True(Px(mask, edge, y) == (0, 0, 0, 255), $"edge row {y}: {Px(mask, edge, y)}");
        }
        int dev = MaxDeviationFromReference(plain, mask, gridW, gridH, density);
        Report(mask, $"screen=edge edgeX={edge} maxDevFromCpuRef={dev}");
        Assert.True(dev <= ReferenceTolerance);
    }

    [NativeGpuFact]
    public void Default_after_ShadowMask_is_byte_identical_to_Default()
    {
        using var session = Session.Open();
        ScreenBuffer screen = VariedScreen();
        var target = session.Target();
        int gridW = (int)Cols * session.Renderer.CellWidth, gridH = (int)Rows * session.Renderer.CellHeight;

        GpuFrameCapture d1 = session.Capture(screen, GpuDisplayEffect.Default, target);
        Assert.Null(session.Renderer.PipelineForTesting!.PostProcessForTesting);
        GpuFrameCapture m1 = session.Capture(screen, GpuDisplayEffect.ShadowMask, target);
        session.AssertPresented(GpuDisplayEffect.ShadowMask);
        GpuFrameCapture d2 = session.Capture(screen, GpuDisplayEffect.Default, target);
        session.AssertPresented(GpuDisplayEffect.Default);
        GpuFrameCapture m2 = session.Capture(screen, GpuDisplayEffect.ShadowMask, target);
        Report(d2, "default-after-shadowmask");
        Assert.Equal(GpuDisplayEffect.Default, d2.Effect);
        Assert.Equal(d1.Rgba, d2.Rgba);
        Assert.Equal(m1.Rgba, m2.Rgba);
        Assert.NotEqual(d1.Rgba, m1.Rgba);

        // Default still equals the CPU oracle.
        byte[] oracle = session.Renderer.CompositeForTesting(screen, Cols, Rows);
        for (int y = 0; y < gridH; y++)
            for (int x = 0; x < gridW; x++)
            {
                int o = (y * gridW + x) * 4;
                Assert.True(Px(d2, x, y) == (oracle[o], oracle[o + 1], oracle[o + 2], (byte)255), $"Default ({x},{y}) differs from the oracle");
            }
        GpuPostProcess post = session.Renderer.PipelineForTesting!.PostProcessForTesting!;
        Assert.True(post.HasPipeline(GpuDisplayEffect.ShadowMask));
        Assert.Equal(3, post.LiveResources); // texture + sampler + ShadowMask pipeline
    }

    [NativeGpuFact]
    public void A_viewport_change_keeps_the_pattern_anchored_to_device_pixels()
    {
        using var session = Session.Open();
        float density = session.Renderer.Metrics.Density;
        int pitch = GpuShadowMask.PitchPixels(density);
        int cw = session.Renderer.CellWidth, ch = session.Renderer.CellHeight;

        GpuFrameCapture big = session.Capture(FlatScreen(0x70), GpuDisplayEffect.ShadowMask, session.Target());
        GpuPostProcess post = session.Renderer.PipelineForTesting!.PostProcessForTesting!;
        int created = post.TextureCreations;

        var smallTarget = session.Target(7, 3);
        GpuFrameCapture small = session.Capture(FlatScreen(0x70, 7, 3), GpuDisplayEffect.ShadowMask, smallTarget);
        Assert.Equal((7 * cw, 3 * ch), post.TextureSize);
        Assert.True(post.TextureCreations > created);
        AssertLetterboxBlack(small, 7 * cw, 3 * ch);
        AssertFlatPattern(small, 7 * cw, 3 * ch, pitch, 0xAA);
        for (int y = 0; y < 3 * ch; y++)
            for (int x = 0; x < 7 * cw; x++)
                Assert.Equal(Px(big, x, y), Px(small, x, y));
        Report(small, $"resized texture={post.TextureSize} pitch={pitch} common area identical");
        Assert.Equal(3, post.LiveResources);
    }

    [NativeGpuFact]
    public void Density_2_doubles_the_pattern_pitch()
    {
        using var session = Session.Open();
        SdlCellMetrics next = session.Renderer.RequestPixelDensity(2f);
        var target = session.Target(metrics: next);
        int gridW = (int)Cols * next.PixelWidth, gridH = (int)Rows * next.PixelHeight;

        GpuFrameCapture plain = session.Capture(FlatScreen(0x70), GpuDisplayEffect.Default, target);
        GpuFrameCapture mask  = session.Capture(FlatScreen(0x70), GpuDisplayEffect.ShadowMask, target);
        Assert.Equal(2f, session.Renderer.Metrics.Density);
        Assert.Equal(GpuDisplayEffect.ShadowMask, mask.Effect);
        AssertLetterboxBlack(mask, gridW, gridH);
        var (nd, nb) = AssertFlatPattern(mask, gridW, gridH, 2, 0xAA);
        // Pitch 2: pixel pairs share a factor, and the horizontal period is 6 device pixels.
        Assert.Equal(Px(mask, 4, 0), Px(mask, 5, 0));
        Assert.NotEqual(Px(mask, 3, 0), Px(mask, 4, 0));
        Assert.Equal(Px(mask, 4, 0), Px(mask, 10, 0));
        int dev = MaxDeviationFromReference(plain, mask, gridW, gridH, 2f);
        Report(mask, $"density=2 pitch={GpuShadowMask.PitchPixels(2f)} dark={nd} bright={nb} row0={string.Join(",", Enumerable.Range(0, 12).Select(x => Px(mask, x, 0).R))} maxDevFromCpuRef={dev}");
        Assert.True(dev <= ReferenceTolerance);

        ScreenBuffer varied = VariedScreen();
        GpuFrameCapture vp = session.Capture(varied, GpuDisplayEffect.Default, target);
        GpuFrameCapture vm = session.Capture(varied, GpuDisplayEffect.ShadowMask, target);
        Assert.True(MaxDeviationFromReference(vp, vm, gridW, gridH, 2f) <= ReferenceTolerance);
    }

    [NativeGpuFact]
    public void The_pattern_does_not_move_with_the_cell_size()
    {
        // Two fonts, two cell sizes: the masked flat field is identical on the common pixels.
        GpuFrameCapture a, b;
        (int W, int H, int Cw, int Ch) ga, gb;
        int pitch;
        using (var s = Session.Open(fontSize: 14))
        {
            pitch = GpuShadowMask.PitchPixels(s.Renderer.Metrics.Density);
            a = s.Capture(FlatScreen(0x70), GpuDisplayEffect.ShadowMask, s.Target());
            ga = ((int)Cols * s.Renderer.CellWidth, (int)Rows * s.Renderer.CellHeight, s.Renderer.CellWidth, s.Renderer.CellHeight);
            Assert.Equal(GpuDisplayEffect.ShadowMask, a.Effect);
        }
        using (var s = Session.Open(fontSize: 22))
        {
            Assert.Equal(pitch, GpuShadowMask.PitchPixels(s.Renderer.Metrics.Density));
            b = s.Capture(FlatScreen(0x70), GpuDisplayEffect.ShadowMask, s.Target());
            gb = ((int)Cols * s.Renderer.CellWidth, (int)Rows * s.Renderer.CellHeight, s.Renderer.CellWidth, s.Renderer.CellHeight);
        }
        Assert.NotEqual((ga.Cw, ga.Ch), (gb.Cw, gb.Ch));
        AssertFlatPattern(a, ga.W, ga.H, pitch, 0xAA);
        AssertFlatPattern(b, gb.W, gb.H, pitch, 0xAA);
        int w = Math.Min(ga.W, gb.W), h = Math.Min(ga.H, gb.H);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Assert.Equal(Px(a, x, y), Px(b, x, y));
        Report(b, $"cellA={ga.Cw}x{ga.Ch} cellB={gb.Cw}x{gb.Ch} pitch={pitch} common {w}x{h} identical");
    }

    [NativeGpuFact]
    public unsafe void Recording_a_ShadowMask_frame_allocates_no_managed_memory()
    {
        using var session = Session.Open();
        ScreenBuffer screen = VariedScreen();
        var target = session.Target();
        session.Capture(screen, GpuDisplayEffect.ShadowMask, target);

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
                    GpuPhysicalPalette.Default, GpuDisplayEffect.ShadowMask, session.Renderer.Metrics.Density);
            long before = GC.GetAllocatedBytesForCurrentThread();
            pipeline.RenderFrame(cmd, tex, target.W, target.H, screen, Cols, Rows, -1, -1, 0,
                GpuPhysicalPalette.Default, GpuDisplayEffect.ShadowMask, session.Renderer.Metrics.Density);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(GpuDisplayEffect.ShadowMask, pipeline.LastDrawnEffect);
            SDL3.SDL.SubmitGPUCommandBuffer(cmd);
            SDL3.SDL.WaitForGPUIdle(device);
            _output.WriteLine($"effect=ShadowMask allocatedBytes={allocated}");
            Assert.Equal(0, allocated);
        }
        finally { SDL3.SDL.ReleaseGPUTexture(device, tex); }
    }
}
