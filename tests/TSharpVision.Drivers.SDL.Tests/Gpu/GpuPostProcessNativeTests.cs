using TSharpVision.Drivers.SDL.Gpu;
using Xunit.Abstractions;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// Post-process effects (Glow) on the real GPU, through the production renderer and the capture
/// seam (the frame is rerendered by the same RenderFrame into a readable texture). Opt-in with
/// <c>TSV_NATIVE_GPU_TESTS=1</c>; backend from <c>TSHARPVISION_SDLGPU_BACKEND</c>. Screens use
/// only spaces and full blocks, so no font raster enters the comparisons.
/// </summary>
[Collection("SDL initialization")]
public sealed class GpuPostProcessNativeTests
{
    private const uint Cols = 10, Rows = 4;

    private readonly ITestOutputHelper _output;
    public GpuPostProcessNativeTests(ITestOutputHelper output) => _output = output;

    private void Report(GpuFrameCapture c, string note = "")
    {
        _output.WriteLine($"backend={c.Backend} format={c.NativeFormat} mode={c.Mode} effect={c.Effect} {c.Width}x{c.Height} {note}");
        string? requested = SDLGpuOptions.NormalizeBackend(Environment.GetEnvironmentVariable("TSHARPVISION_SDLGPU_BACKEND"));
        if (requested is "direct3d12" or "vulkan" or "metal") Assert.Equal(requested, c.Backend);
    }

    // Varied colours: blocks in all 16 foregrounds over 8 backgrounds.
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

    // Blue desktop, two white blocks, a black row far from them.
    private static ScreenBuffer SparseScreen()
    {
        var screen = new ScreenBuffer((ushort)Cols, (ushort)Rows);
        for (uint row = 0; row < Rows; row++)
            for (uint col = 0; col < Cols; col++)
                screen.SetChar(col, row, new TScreenChar(' ', (TColorAttr)(row == 3 ? 0x00 : 0x1F)));
        screen.SetChar(2, 0, new TScreenChar('█', (TColorAttr)0x1F));
        screen.SetChar(7, 1, new TScreenChar('█', (TColorAttr)0x1E));
        return screen;
    }

    private sealed class Session : IDisposable
    {
        public IntPtr Window;
        public SDLGpuRenderer Renderer = null!;

        public static Session Open(bool cpuFallback = false)
        {
            SDL3.SDL.SetHint(SDL3.SDL.Hints.QuitOnLastWindowClose, "0");
            Assert.True(SDL3.SDL.Init(SDL3.SDL.InitFlags.Video), SDL3.SDL.GetError());
            var s = new Session();
            s.Window = SDL3.SDL.CreateWindow("TSV post-process native test", 320, 200, SDL3.SDL.WindowFlags.Hidden);
            Assert.True(s.Window != IntPtr.Zero, SDL3.SDL.GetError());
            s.Renderer = new SDLGpuRenderer(s.Window, SDLGpuOptions.FromEnvironment())
            {
                SimulatePipelineFailureForTesting = cpuFallback,
            };
            s.Renderer.Initialize();
            Assert.Equal(!cpuFallback, s.Renderer.ShaderPipelineActive);
            s.Renderer.StartRenderThread();
            return s;
        }

        public (uint W, uint H) Target(uint cols = Cols, uint rows = Rows, SdlCellMetrics? metrics = null) =>
            // Larger than the grid: the letterbox must stay black under every effect.
            ((uint)((metrics?.PixelWidth ?? Renderer.CellWidth) * cols + 13),
             (uint)((metrics?.PixelHeight ?? Renderer.CellHeight) * rows + 7));

        // True when the last Capture's frame reached a swapchain image (a hidden window may get
        // none, e.g. on Vulkan); PresentedEffect only changes for presented frames.
        public bool LastFramePresented;

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

    // Pixels within Chebyshev distance `reach` of a pixel that has a bright part.
    private static bool[] NearBright(byte[] grid, int w, int h, int reach)
    {
        var near = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                var c = new System.Numerics.Vector3(grid[o], grid[o + 1], grid[o + 2]) / 255f;
                if (GpuGlow.Bright(c) == System.Numerics.Vector3.Zero) continue;
                for (int yy = Math.Max(0, y - reach); yy <= Math.Min(h - 1, y + reach); yy++)
                    for (int xx = Math.Max(0, x - reach); xx <= Math.Min(w - 1, x + reach); xx++)
                        near[yy * w + xx] = true;
            }
        return near;
    }

    /// <summary>
    /// Glow vs Default: letterbox black in both; outside reach+1 of bright pixels equal within ±1;
    /// no pixel darker than Default (−1 tolerance); some halo pixels lit. Returns (changed, maxGain).
    /// </summary>
    private static (int Changed, int MaxGain) AssertGlowLocality(GpuFrameCapture plain, GpuFrameCapture glow, int gridW, int gridH, float density)
    {
        Assert.Equal(plain.Width, glow.Width);
        Assert.Equal(plain.Height, glow.Height);
        byte[] grid = Grid(plain, gridW, gridH);
        bool[] near = NearBright(grid, gridW, gridH, GpuGlow.ReachPixels(density) + 1);
        int changed = 0, maxGain = 0;
        for (int y = 0; y < plain.Height; y++)
            for (int x = 0; x < plain.Width; x++)
            {
                var a = Px(plain, x, y);
                var b = Px(glow, x, y);
                if (x >= gridW || y >= gridH)
                {
                    Assert.True(b == (0, 0, 0, 255), $"letterbox ({x},{y}) is {b}");
                    continue;
                }
                int[] d = { b.R - a.R, b.G - a.G, b.B - a.B };
                foreach (int v in d) Assert.True(v >= -1, $"({x},{y}) darker under Glow: {a} -> {b}");
                if (!near[y * gridW + x])
                    foreach (int v in d) Assert.True(Math.Abs(v) <= 1, $"({x},{y}) far from bright pixels changed: {a} -> {b}");
                if (d.Max() > 1) changed++;
                maxGain = Math.Max(maxGain, d.Max());
            }
        Assert.True(changed > 0, "Glow changed nothing");
        return (changed, maxGain);
    }

    private static int MaxDeviationFromReference(GpuFrameCapture plain, GpuFrameCapture glow, int gridW, int gridH, float density)
    {
        byte[] src = Grid(plain, gridW, gridH);
        var expected = new byte[src.Length];
        GpuGlow.ApplyReference(src, expected, gridW, gridH, density);
        byte[] actual = Grid(glow, gridW, gridH);
        int max = 0;
        for (int i = 0; i < src.Length; i++)
            if (i % 4 != 3) max = Math.Max(max, Math.Abs(expected[i] - actual[i]));
        return max;
    }

    private const int ReferenceTolerance = 2;

    [NativeGpuFact]
    public void Default_still_matches_the_cpu_oracle_and_creates_no_post_resources()
    {
        using var session = Session.Open();
        ScreenBuffer screen = VariedScreen();
        var target = session.Target();
        int gridW = (int)Cols * session.Renderer.CellWidth, gridH = (int)Rows * session.Renderer.CellHeight;

        GpuFrameCapture plain = session.Capture(screen, GpuDisplayEffect.Default, target);
        Report(plain);
        Assert.Equal(GpuDisplayEffect.Default, plain.Effect);
        session.AssertPresented(GpuDisplayEffect.Default);
        Assert.Null(session.Renderer.PipelineForTesting!.PostProcessForTesting);
        Assert.Equal(0, GpuPostProcess.TotalLiveResources);

        session.Renderer.SetCursor(-1, -1, 0);
        byte[] oracle = session.Renderer.CompositeForTesting(screen, Cols, Rows);
        for (int y = 0; y < plain.Height; y++)
            for (int x = 0; x < plain.Width; x++)
            {
                var p = Px(plain, x, y);
                if (x < gridW && y < gridH)
                {
                    int o = (y * gridW + x) * 4;
                    Assert.True(p == (oracle[o], oracle[o + 1], oracle[o + 2], (byte)255),
                        $"Default shader pixel ({x},{y}) {p} differs from the CPU oracle");
                }
                else Assert.True(p == (0, 0, 0, 255), $"letterbox pixel ({x},{y}) is {p}");
            }

        // Scanlines still creates no post resources either.
        session.Capture(screen, GpuDisplayEffect.Scanlines, target);
        Assert.Null(session.Renderer.PipelineForTesting!.PostProcessForTesting);
    }

    [NativeGpuFact]
    public void Glow_is_a_local_halo_that_matches_the_cpu_reference()
    {
        using var session = Session.Open();
        float density = session.Renderer.Metrics.Density;
        int gridW = (int)Cols * session.Renderer.CellWidth, gridH = (int)Rows * session.Renderer.CellHeight;
        var target = session.Target();

        foreach (var (name, screen) in new[] { ("sparse", SparseScreen()), ("varied", VariedScreen()) })
        {
            GpuFrameCapture plain = session.Capture(screen, GpuDisplayEffect.Default, target);
            GpuFrameCapture glow  = session.Capture(screen, GpuDisplayEffect.Glow, target);
            Assert.Equal("shader", glow.Mode);
            Assert.Equal(GpuDisplayEffect.Glow, glow.Effect);
            session.AssertPresented(GpuDisplayEffect.Glow);
            Assert.Null(session.Renderer.PostProcessUnavailableReason);
            var (changed, maxGain) = AssertGlowLocality(plain, glow, gridW, gridH, density);
            int deviation = MaxDeviationFromReference(plain, glow, gridW, gridH, density);
            Report(glow, $"screen={name} density={density} reach={GpuGlow.ReachPixels(density)} changed={changed} maxGain={maxGain} maxDevFromCpuRef={deviation}");
            Assert.True(deviation <= ReferenceTolerance, $"GPU Glow deviates from the CPU reference by {deviation}");
        }

        // Sparse screen: the blue background right of the white block at cell (2,0) lightens; the
        // black row (row 3) is more than the reach away from both blocks and stays black.
        GpuFrameCapture p2 = session.Capture(SparseScreen(), GpuDisplayEffect.Default, target);
        GpuFrameCapture g2 = session.Capture(SparseScreen(), GpuDisplayEffect.Glow, target);
        int cw = session.Renderer.CellWidth, ch = session.Renderer.CellHeight;
        int hx = 3 * cw, hy = ch / 2;
        Assert.True(Px(g2, hx, hy).R > Px(p2, hx, hy).R + 10, $"no halo next to the block: {Px(p2, hx, hy)} -> {Px(g2, hx, hy)}");
        for (int x = 0; x < gridW; x++)
            Assert.Equal(Px(p2, x, 3 * ch + ch / 2), Px(g2, x, 3 * ch + ch / 2));
        Assert.Equal((byte)255, Px(g2, 2 * cw + cw / 2, hy).R); // block core keeps its brightness
    }

    [NativeGpuFact]
    public void Switching_effects_back_and_forth_is_stable()
    {
        using var session = Session.Open();
        ScreenBuffer screen = VariedScreen();
        var target = session.Target();

        GpuFrameCapture d1 = session.Capture(screen, GpuDisplayEffect.Default, target);
        GpuFrameCapture g1 = session.Capture(screen, GpuDisplayEffect.Glow, target);
        session.AssertPresented(GpuDisplayEffect.Glow);
        GpuFrameCapture d2 = session.Capture(screen, GpuDisplayEffect.Default, target);
        session.AssertPresented(GpuDisplayEffect.Default);
        GpuFrameCapture s1 = session.Capture(screen, GpuDisplayEffect.Scanlines, target);
        session.AssertPresented(GpuDisplayEffect.Scanlines);
        GpuFrameCapture g2 = session.Capture(screen, GpuDisplayEffect.Glow, target);
        GpuFrameCapture c1 = session.Capture(screen, GpuDisplayEffect.Curvature, target);
        GpuFrameCapture m1 = session.Capture(screen, GpuDisplayEffect.ShadowMask, target);
        Report(g2);

        Assert.Equal(d1.Rgba, d2.Rgba);           // Default after Glow: identical
        Assert.Equal(g1.Rgba, g2.Rgba);           // Glow after Scanlines: identical
        Assert.NotEqual(d1.Rgba, g1.Rgba);
        Assert.NotEqual(d1.Rgba, s1.Rgba);
        Assert.Equal(GpuDisplayEffect.Scanlines, s1.Effect);
        // Not registered (yet): drawn as Default without error.
        foreach (var (requested, c) in new[] { (GpuDisplayEffect.Curvature, c1), (GpuDisplayEffect.ShadowMask, m1) })
        {
            if (GpuPostEffectCatalog.TryGet(requested, out _))
                Assert.Equal(requested, c.Effect);
            else
            {
                Assert.Equal(GpuDisplayEffect.Default, c.Effect);
                Assert.Equal(d1.Rgba, c.Rgba);
            }
        }
        Assert.Null(session.Renderer.PostProcessUnavailableReason);
        GpuPostProcess post = session.Renderer.PipelineForTesting!.PostProcessForTesting!;
        Assert.Equal(1, post.TextureCreations);   // same viewport throughout: created once
        Assert.True(post.HasPipeline(GpuDisplayEffect.Glow));
    }

    [NativeGpuFact]
    public void A_viewport_change_recreates_the_offscreen_texture()
    {
        using var session = Session.Open();
        int cw = session.Renderer.CellWidth, ch = session.Renderer.CellHeight;
        float density = session.Renderer.Metrics.Density;

        GpuFrameCapture a = session.Capture(VariedScreen(), GpuDisplayEffect.Glow, session.Target());
        GpuPostProcess post = session.Renderer.PipelineForTesting!.PostProcessForTesting!;
        Assert.Equal(((int)Cols * cw, (int)Rows * ch), post.TextureSize);
        int created = post.TextureCreations;

        ScreenBuffer small = VariedScreen(7, 3);
        var smallTarget = session.Target(7, 3);
        GpuFrameCapture plain = session.Capture(small, GpuDisplayEffect.Default, smallTarget);
        GpuFrameCapture b = session.Capture(small, GpuDisplayEffect.Glow, smallTarget);
        Report(b, $"texture={post.TextureSize}");
        Assert.Equal((7 * cw, 3 * ch), post.TextureSize);
        Assert.True(post.TextureCreations > created);
        Assert.Equal(GpuDisplayEffect.Glow, b.Effect);
        AssertGlowLocality(plain, b, 7 * cw, 3 * ch, density);
        Assert.True(MaxDeviationFromReference(plain, b, 7 * cw, 3 * ch, density) <= ReferenceTolerance);
        Assert.Equal(3, post.LiveResources);      // texture + sampler + Glow pipeline, no leak on resize

        GpuFrameCapture again = session.Capture(VariedScreen(), GpuDisplayEffect.Glow, session.Target());
        Assert.Equal(a.Rgba, again.Rgba);
        Assert.Equal(3, post.LiveResources);
    }

    [NativeGpuFact]
    public void Glow_follows_a_density_change_and_the_old_resources_are_released()
    {
        using var session = Session.Open();
        session.Capture(VariedScreen(), GpuDisplayEffect.Glow, session.Target());
        GpuPostProcess before = session.Renderer.PipelineForTesting!.PostProcessForTesting!;
        Assert.Equal(3, before.LiveResources);

        SdlCellMetrics next = session.Renderer.RequestPixelDensity(2f);
        var target = session.Target(metrics: next);
        GpuFrameCapture plain = session.Capture(SparseScreen(), GpuDisplayEffect.Default, target);
        GpuFrameCapture glow  = session.Capture(SparseScreen(), GpuDisplayEffect.Glow, target);
        Assert.Equal(2f, session.Renderer.Metrics.Density);
        Assert.Equal(0, before.LiveResources);    // the density rebuild disposed the old pipeline's post resources
        GpuPostProcess after = session.Renderer.PipelineForTesting!.PostProcessForTesting!;
        Assert.NotSame(before, after);
        Assert.Equal(3, GpuPostProcess.TotalLiveResources);
        int gridW = (int)Cols * next.PixelWidth, gridH = (int)Rows * next.PixelHeight;
        var (changed, maxGain) = AssertGlowLocality(plain, glow, gridW, gridH, 2f);
        int deviation = MaxDeviationFromReference(plain, glow, gridW, gridH, 2f);
        Report(glow, $"density=2 reach={GpuGlow.ReachPixels(2f)} changed={changed} maxGain={maxGain} maxDevFromCpuRef={deviation}");
        Assert.True(deviation <= ReferenceTolerance, $"deviation {deviation}");
        Assert.Equal((gridW, gridH), after.TextureSize);
    }

    [NativeGpuFact]
    public void Unavailable_post_process_renders_Default_and_says_so_once()
    {
        using var session = Session.Open();
        ScreenBuffer screen = VariedScreen();
        var target = session.Target();
        GpuFrameCapture plain = session.Capture(screen, GpuDisplayEffect.Default, target);
        session.Renderer.PipelineForTesting!.SimulatePostProcessFailureForTesting = true;

        var previous = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        GpuFrameCapture first, second;
        try
        {
            first  = session.Capture(screen, GpuDisplayEffect.Glow, target);
            second = session.Capture(screen, GpuDisplayEffect.Glow, target);
        }
        finally { Console.SetError(previous); }

        Assert.NotNull(session.Renderer.PostProcessUnavailableReason);
        Assert.Equal(GpuDisplayEffect.Default, first.Effect);
        session.AssertPresented(GpuDisplayEffect.Default);
        Assert.Equal(plain.Rgba, first.Rgba);
        Assert.Equal(plain.Rgba, second.Rgba);
        Assert.Equal(0, session.Renderer.PipelineForTesting!.PostProcessForTesting!.LiveResources);
        string log = stderr.ToString();
        const string line = "Post-process display effects unavailable";
        int idx = log.IndexOf(line, StringComparison.Ordinal);
        Assert.True(idx >= 0, log);
        Assert.Equal(-1, log.IndexOf(line, idx + 1, StringComparison.Ordinal));
        _output.WriteLine(log.Trim());

        // Scanlines is independent of the post-process failure.
        GpuFrameCapture lined = session.Capture(screen, GpuDisplayEffect.Scanlines, target);
        Assert.Equal(GpuDisplayEffect.Scanlines, lined.Effect);
    }

    [NativeGpuFact]
    public void Cpu_fallback_renders_Glow_as_Default_and_logs_once()
    {
        var previous = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            using var session = Session.Open(cpuFallback: true);
            ScreenBuffer screen = VariedScreen();
            var target = session.Target();
            GpuFrameCapture plain = session.Capture(screen, GpuDisplayEffect.Default, target);
            GpuFrameCapture glow  = session.Capture(screen, GpuDisplayEffect.Glow, target);
            GpuFrameCapture glow2 = session.Capture(screen, GpuDisplayEffect.Glow, target);
            Assert.Equal("cpu-composite", glow.Mode);
            Assert.Equal(GpuDisplayEffect.Default, glow.Effect);
            session.AssertPresented(GpuDisplayEffect.Default);
            Assert.Equal(plain.Rgba, glow.Rgba);
            Assert.Equal(plain.Rgba, glow2.Rgba);
            GpuFrameCapture lined = session.Capture(screen, GpuDisplayEffect.Scanlines, target);
            Assert.Equal(GpuDisplayEffect.Scanlines, lined.Effect);
            session.AssertPresented(GpuDisplayEffect.Scanlines);
        }
        finally { Console.SetError(previous); }
        string log = stderr.ToString();
        const string line = "Glow display effect needs the shader pipeline";
        int idx = log.IndexOf(line, StringComparison.Ordinal);
        Assert.True(idx >= 0, log);
        Assert.Equal(-1, log.IndexOf(line, idx + 1, StringComparison.Ordinal));
    }

    [NativeGpuFact]
    public void Dispose_releases_every_post_resource()
    {
        GpuPostProcess post;
        var session = Session.Open();
        try
        {
            session.Capture(VariedScreen(), GpuDisplayEffect.Glow, session.Target());
            post = session.Renderer.PipelineForTesting!.PostProcessForTesting!;
            Assert.Equal(3, post.LiveResources);
            Assert.Equal(3, GpuPostProcess.TotalLiveResources);
        }
        finally { session.Dispose(); }
        Assert.Equal(0, post.LiveResources);
        Assert.Equal(0, GpuPostProcess.TotalLiveResources);
    }

    [NativeGpuFact]
    public unsafe void Recording_a_frame_allocates_no_managed_memory_on_any_path()
    {
        using var session = Session.Open();
        ScreenBuffer screen = VariedScreen();
        var target = session.Target();
        // Warm every path once through the render thread (glyph cache, atlas, lazy pipelines).
        foreach (var e in new[] { GpuDisplayEffect.Default, GpuDisplayEffect.Scanlines, GpuDisplayEffect.Glow })
            session.Capture(screen, e, target);

        // The render thread is idle now (it only wakes on Render); record directly on this thread.
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
            foreach (var e in new[] { GpuDisplayEffect.Default, GpuDisplayEffect.Scanlines, GpuDisplayEffect.Glow })
            {
                IntPtr cmd = SDL3.SDL.AcquireGPUCommandBuffer(device);
                Assert.True(cmd != IntPtr.Zero);
                for (int warm = 0; warm < 2; warm++)
                    pipeline.RenderFrame(cmd, tex, target.W, target.H, screen, Cols, Rows, -1, -1, 0,
                        GpuPhysicalPalette.Default, e, session.Renderer.Metrics.Density);
                long before = GC.GetAllocatedBytesForCurrentThread();
                pipeline.RenderFrame(cmd, tex, target.W, target.H, screen, Cols, Rows, -1, -1, 0,
                    GpuPhysicalPalette.Default, e, session.Renderer.Metrics.Density);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(e, pipeline.LastDrawnEffect);
                SDL3.SDL.SubmitGPUCommandBuffer(cmd);
                SDL3.SDL.WaitForGPUIdle(device);
                _output.WriteLine($"effect={e} allocatedBytes={allocated}");
                Assert.Equal(0, allocated);
            }
        }
        finally { SDL3.SDL.ReleaseGPUTexture(device, tex); }
    }
}
