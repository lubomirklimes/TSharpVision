using TSharpVision.Drivers.SDL.Gpu;
using Xunit.Abstractions;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// A test that needs a real SDL_GPU device and a (hidden) window. Skipped unless
/// <c>TSV_NATIVE_GPU_TESTS=1</c>, so CI machines without a GPU or display skip it. The backend
/// follows <c>TSHARPVISION_SDLGPU_BACKEND</c> (direct3d12 | vulkan | metal; default: SDL's choice).
/// </summary>
public sealed class NativeGpuFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "TSV_NATIVE_GPU_TESTS";

    public NativeGpuFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) != "1")
            Skip = $"Native SDL_GPU test: set {EnvironmentVariable}=1 on a machine with a GPU.";
    }
}

/// <summary>
/// Scanlines on the real GPU: the production renderer renders a frame, the capture seam rerenders it
/// into a readable texture of the swapchain format and reads it back. Default vs Scanlines must
/// differ exactly on the strip rows; Default must equal the CPU-composited oracle pixel for pixel.
/// The screen uses only spaces and full blocks (coverage 0 or 255), so no font raster or
/// fractional blending enters the comparison.
/// </summary>
[Collection("SDL initialization")]
public sealed class GpuScanlineNativeTests
{
    private const uint Cols = 10, Rows = 4;

    private readonly ITestOutputHelper _output;
    public GpuScanlineNativeTests(ITestOutputHelper output) => _output = output;

    // Reports (and, when TSHARPVISION_SDLGPU_BACKEND is set, checks) the backend that really ran.
    private void Report(GpuFrameCapture c)
    {
        _output.WriteLine($"backend={c.Backend} format={c.NativeFormat} mode={c.Mode} effect={c.Effect} {c.Width}x{c.Height}");
        string? requested = SDLGpuOptions.NormalizeBackend(Environment.GetEnvironmentVariable("TSHARPVISION_SDLGPU_BACKEND"));
        if (requested is "direct3d12" or "vulkan" or "metal") Assert.Equal(requested, c.Backend);
    }

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

    private sealed class Session : IDisposable
    {
        public IntPtr Window;
        public SDLGpuRenderer Renderer = null!;

        public static Session Open(bool cpuFallback = false)
        {
            SDL3.SDL.SetHint(SDL3.SDL.Hints.QuitOnLastWindowClose, "0");
            Assert.True(SDL3.SDL.Init(SDL3.SDL.InitFlags.Video), SDL3.SDL.GetError());
            var s = new Session();
            s.Window = SDL3.SDL.CreateWindow("TSV scanlines native test", 320, 200, SDL3.SDL.WindowFlags.Hidden);
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

        public (uint W, uint H) Target(SdlCellMetrics? metrics = null) =>
            // A target larger than the grid: the letterbox must stay untouched by the effect.
            ((uint)((metrics?.PixelWidth ?? Renderer.CellWidth) * Cols + 13),
             (uint)((metrics?.PixelHeight ?? Renderer.CellHeight) * Rows + 7));

        public GpuFrameCapture Capture(ScreenBuffer screen, GpuDisplayEffect effect, (uint W, uint H) target)
        {
            Renderer.DisplayEffect = effect;
            var pending = Renderer.RequestCaptureForTesting(target.W, target.H, TimeSpan.FromSeconds(10));
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

    private static void AssertStripDifference(GpuFrameCapture plain, GpuFrameCapture lined, int gridW, int gridH, float density)
    {
        Assert.Equal(plain.Width, lined.Width);
        Assert.Equal(plain.Height, lined.Height);
        var stripRows = new HashSet<int>();
        foreach (GpuScanlineStrip s in GpuScanlineGeometry.ComputeStrips(gridW, gridH, density))
            for (int y = s.Y; y < s.Y + s.Height; y++) stripRows.Add(y);
        Assert.DoesNotContain(0, stripRows);

        int maxDeviation = 0, changed = 0;
        for (int y = 0; y < plain.Height; y++)
            for (int x = 0; x < plain.Width; x++)
            {
                var a = Px(plain, x, y);
                var b = Px(lined, x, y);
                bool strip = x < gridW && y < gridH && stripRows.Contains(y);
                if (!strip)
                {
                    Assert.True(a == b, $"pixel ({x},{y}) outside the strips changed: {a} -> {b}");
                    continue;
                }
                byte[] pa = { a.R, a.G, a.B }, pb = { b.R, b.G, b.B };
                for (int c = 0; c < 3; c++)
                {
                    int expected = GpuAlphaBlend.Blend(0, pa[c], GpuScanlineGeometry.StripAlpha);
                    maxDeviation = Math.Max(maxDeviation, Math.Abs(expected - pb[c]));
                }
                Assert.Equal(a.A, b.A);
                if (a != b) changed++;
            }
        // GPU UNORM blending rounds in float; allow one step against the integer CPU formula.
        Assert.True(maxDeviation <= 1, $"strip pixels deviate from dst * 166/255 by {maxDeviation}");
        Assert.True(changed > 0, "no strip pixel changed");
    }

    [NativeGpuFact]
    public void Shader_scanlines_change_only_strip_rows_and_Default_matches_the_cpu_oracle()
    {
        using var session = Session.Open();
        ScreenBuffer screen = Screen();
        var target = session.Target();
        int gridW = (int)Cols * session.Renderer.CellWidth, gridH = (int)Rows * session.Renderer.CellHeight;
        float density = session.Renderer.Metrics.Density;

        GpuFrameCapture plain = session.Capture(screen, GpuDisplayEffect.Default, target);
        Assert.Equal("shader", plain.Mode);
        Assert.Equal(GpuDisplayEffect.Default, plain.Effect);
        Assert.False(session.Renderer.PipelineForTesting!.ScanlinePipelineCreated); // Default creates nothing

        GpuFrameCapture lined = session.Capture(screen, GpuDisplayEffect.Scanlines, target);
        Report(plain);
        Report(lined);
        Assert.Equal("shader", lined.Mode);
        Assert.Equal(GpuDisplayEffect.Scanlines, lined.Effect);
        Assert.Null(session.Renderer.ScanlinesUnavailableReason);
        Assert.True(session.Renderer.PipelineForTesting!.ScanlinePipelineCreated);
        Assert.Equal(TerminalGpuPipeline.ScanlineVertexCapacity(gridH, density),
            session.Renderer.PipelineForTesting!.ScanlineVertexCount);
        AssertStripDifference(plain, lined, gridW, gridH, density);

        // Default again after Scanlines: identical to the first Default frame.
        GpuFrameCapture again = session.Capture(screen, GpuDisplayEffect.Default, target);
        Assert.Equal(plain.Rgba, again.Rgba);

        // Default shader output equals the CPU-composited oracle on the grid, black letterbox.
        session.Renderer.DisplayEffect = GpuDisplayEffect.Default;
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
    }

    [NativeGpuFact]
    public void Cpu_fallback_scanlines_change_only_strip_rows()
    {
        using var session = Session.Open(cpuFallback: true);
        ScreenBuffer screen = Screen();
        var target = session.Target();
        int gridW = (int)Cols * session.Renderer.CellWidth, gridH = (int)Rows * session.Renderer.CellHeight;

        GpuFrameCapture plain = session.Capture(screen, GpuDisplayEffect.Default, target);
        GpuFrameCapture lined = session.Capture(screen, GpuDisplayEffect.Scanlines, target);
        Assert.Equal("cpu-composite", plain.Mode);
        Assert.Equal(GpuDisplayEffect.Scanlines, lined.Effect);
        AssertStripDifference(plain, lined, gridW, gridH, session.Renderer.Metrics.Density);
    }

    [NativeGpuFact]
    public void Shader_scanlines_follow_a_density_change()
    {
        using var session = Session.Open();
        ScreenBuffer screen = Screen();
        SdlCellMetrics next = session.Renderer.RequestPixelDensity(2f);
        var target = session.Target(next);
        // The first frame applies the density on the render thread before rendering.
        GpuFrameCapture plain = session.Capture(screen, GpuDisplayEffect.Default, target);
        GpuFrameCapture lined = session.Capture(screen, GpuDisplayEffect.Scanlines, target);
        Assert.Equal(next.PixelWidth, session.Renderer.CellWidth);
        Assert.Equal(2f, session.Renderer.Metrics.Density);
        AssertStripDifference(plain, lined, (int)Cols * next.PixelWidth, (int)Rows * next.PixelHeight, 2f);
        // Thickness 2: rows 2-3 darkened, rows 0-1 and 4-5 untouched. Cell (1,0) is a space on blue.
        int x = next.PixelWidth + 1;
        Assert.NotEqual(Px(plain, x, 2), Px(lined, x, 2));
        Assert.NotEqual(Px(plain, x, 3), Px(lined, x, 3));
        Assert.Equal(Px(plain, x, 1), Px(lined, x, 1));
        Assert.Equal(Px(plain, x, 4), Px(lined, x, 4));
    }

    [NativeGpuFact]
    public void Unavailable_scanline_pipeline_renders_Default_and_says_so()
    {
        using var session = Session.Open();
        ScreenBuffer screen = Screen();
        var target = session.Target();
        GpuFrameCapture plain = session.Capture(screen, GpuDisplayEffect.Default, target);
        session.Renderer.PipelineForTesting!.SimulateScanlineFailureForTesting = true;

        var previous = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        GpuFrameCapture first, second;
        try
        {
            first = session.Capture(screen, GpuDisplayEffect.Scanlines, target);
            second = session.Capture(screen, GpuDisplayEffect.Scanlines, target);
        }
        finally { Console.SetError(previous); }

        Assert.NotNull(session.Renderer.ScanlinesUnavailableReason);
        Assert.False(session.Renderer.PipelineForTesting!.ScanlinePipelineCreated);
        Assert.Equal(GpuDisplayEffect.Default, first.Effect);
        Assert.Equal(plain.Rgba, first.Rgba);
        Assert.Equal(plain.Rgba, second.Rgba);
        string log = stderr.ToString();
        int idx = log.IndexOf("Scanlines display effect unavailable", StringComparison.Ordinal);
        Assert.True(idx >= 0, log);
        Assert.Equal(-1, log.IndexOf("Scanlines display effect unavailable", idx + 1, StringComparison.Ordinal)); // once
    }
}
