using System.Diagnostics;
using TSharpVision.Drivers.SDL.Gpu;
using TSharpVision.Drivers.SDL.Graphics;
using Xunit.Abstractions;

namespace TSharpVision.Drivers.SDL.Tests.Graphics;

/// <summary>Runs only with <c>TSV_GRAPHICS_BENCH=1</c>: bounded, deterministic graphics microbenchmarks.</summary>
public sealed class GraphicsBenchFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "TSV_GRAPHICS_BENCH";

    public GraphicsBenchFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) != "1")
            Skip = $"Graphics microbenchmark: set {EnvironmentVariable}=1.";
    }
}

/// <summary>
/// TV12o microbenchmarks of the device-independent graphics work per presented frame: layer store copy, image cache
/// bookkeeping and the instanced GPU build (<see cref="GpuGraphicsBatch"/>). Scenarios are fixed (seeded) so
/// before/after runs are comparable (the TV12n baseline in <c>reports/TV12o-bench-before.md</c> used the same first
/// five scenarios with the quad-per-device-rectangle build). Each scenario is measured over <see cref="Iterations"/>
/// frames after <see cref="Warmup"/> warm-up frames; reported: median and p95 CPU time per frame, allocated bytes
/// per frame (process-wide, precise; run the benchmark alone), the median of the GPU part alone (image cache frame +
/// instance build, without the layer store copy), primitives, GPU instances, GPU upload bytes per frame
/// (instances plus image pixels uploaded that frame: an unchanged image uploads nothing) and GPU draw calls. Writes a
/// Markdown table to the test output and, when <c>TSV_GRAPHICS_BENCH_OUT</c> names a file, appends it there.
/// </summary>
public sealed class GraphicsBenchmarkTests
{
    private const int Warmup = 20, Iterations = 200;
    private const int GridW = 640, GridH = 400; // 80 x 25 cells of 8 x 16

    private readonly ITestOutputHelper _output;
    public GraphicsBenchmarkTests(ITestOutputHelper output) => _output = output;

    internal sealed record Scenario(string Name, PixelRect[] Clip, GraphicsPrimitive[] Primitives, PixelImage[]? Images = null);

    internal static IEnumerable<Scenario> Scenarios()
    {
        PixelRect[] full = [new(0, 0, GridW, GridH)];
        yield return new Scenario("no graphics (baseline)", [], []);

        // STYX: 16 lines inside a 34 x 12 window client area (32 x 10 cells).
        var rnd = new Random(12);
        var styxClip = new PixelRect(6 * 8, 5 * 16, 32 * 8, 10 * 16);
        var styx = new GraphicsPrimitive[16];
        for (int i = 0; i < styx.Length; i++)
            styx[i] = GraphicsPrimitive.Line(
                styxClip.X + rnd.Next(styxClip.W), styxClip.Y + rnd.Next(styxClip.H),
                styxClip.X + rnd.Next(styxClip.W), styxClip.Y + rnd.Next(styxClip.H), 0x55FFFFFF);
        yield return new Scenario("sparse STYX lines (16)", [styxClip], styx);

        // Dense chart: 4 series x 600 segments across the full grid.
        var chart = new List<GraphicsPrimitive>();
        for (int s = 0; s < 4; s++)
        {
            int prevX = 0, prevY = GridH / 2;
            for (int i = 1; i <= 600; i++)
            {
                int x = i * (GridW - 1) / 600;
                int y = (int)(GridH / 2 + Math.Sin(i * 0.07 + s) * (GridH / 2 - 10) * (0.4 + 0.15 * s));
                chart.Add(GraphicsPrimitive.Line(prevX, prevY, x, y, 0xFF5555FF + (uint)s * 0x00220000));
                (prevX, prevY) = (x, y);
            }
        }
        yield return new Scenario("dense line chart (2400 segments)", full, chart.ToArray());

        // Full-frame bitmap drawn the only way TV12n allows: one FillRect per pixel (320 x 200 image).
        var bitmap = new GraphicsPrimitive[320 * 200];
        for (int y = 0; y < 200; y++)
            for (int x = 0; x < 320; x++)
                bitmap[y * 320 + x] = GraphicsPrimitive.FillRect(x * 2, y * 2, 2, 2, (uint)((x << 24) | (y << 16) | 0x80FF));
        yield return new Scenario("full-frame bitmap as FillRects (64000)", full, bitmap);

        // Occlusion: the STYX lines under a region split into 8 clip rectangles by overlapping windows.
        var occluded = new PixelRect[8];
        for (int i = 0; i < occluded.Length; i++)
            occluded[i] = new PixelRect(styxClip.X + i * 32, styxClip.Y + (i % 2) * 16, 24, styxClip.H - 32);
        yield return new Scenario("STYX lines, 8 clip rectangles", occluded, styx);

        // The same 320 x 200 picture as one Image primitive (TV12o), scaled 2x into the full grid.
        PixelImage picture = PixelImage.Create(320, 200, px =>
        {
            for (int y = 0; y < 200; y++)
                for (int x = 0; x < 320; x++)
                {
                    int o = (y * 320 + x) * 4;
                    (px[o], px[o + 1], px[o + 2], px[o + 3]) = ((byte)x, (byte)y, 0x80, 0xFF);
                }
        });
        yield return new Scenario("full-frame image (320x200, one Image primitive)", full,
            [GraphicsPrimitive.Image(0, 0, 0, GridW, GridH)], [picture]);
    }

    internal readonly record struct Result(
        string Name, double MedianUs, double P95Us, double BuildMedianUs, long AllocatedBytes, int Primitives, int Instances,
        long UploadBytes, int DrawCalls);

    // Texture creation without a device: the cache's bookkeeping is what the frame pays for.
    private sealed class NoDeviceTextures : IGpuImageTextureBackend
    {
        private int _next = 1;
        public IntPtr CreateTexture(int width, int height) => _next++;
        public void ReleaseTexture(IntPtr texture) { }
    }

    // After (TV12o): store copy + image cache frame + one 32-byte instance per primitive.
    internal static Result Measure(Scenario s)
    {
        var clips = new List<DeviceRect>();
        var draws = new List<GpuGraphicsDraw>();
        var layers = new PixelGraphicsLayers();
        var images = new GpuImageCache(new NoDeviceTextures());
        object owner = new();
        var instances = new GpuGraphicsInstance[TerminalGpuPipeline.MaxGraphicsInstances];
        GpuGraphicsBuildStats stats = default;
        long uploadBytes = 0, buildTicks = 0;
        PixelScale scale = PixelScale.Identity(8, 16);

        void Frame(int i)
        {
            // A changed layer every frame (as an animation does): the store copies, then the renderer builds.
            // For the image scenario only the (ignored) RGB bytes change: the image itself is unchanged.
            var prims = s.Primitives;
            if (prims.Length > 0) prims[0] = prims[0] with { Rgba = ((uint)i << 8) | 0xFF };
            layers.Set(owner, s.Clip, prims, s.Images);
            PixelGraphicsFrame frame = layers.Frame;
            long b0 = Stopwatch.GetTimestamp();
            images.BeginFrame(frame);
            int capacity = Math.Min(frame.PrimitiveCount, instances.Length);
            stats = GpuGraphicsBatch.Build(instances.AsSpan(0, capacity), frame, scale, GridW, GridH, images, clips, draws);
            uploadBytes = (long)stats.Instances * GpuGraphicsInstance.Size + images.PendingBytes;
            images.MarkUploaded();
            buildTicks = Stopwatch.GetTimestamp() - b0;
        }

        for (int i = 0; i < Warmup; i++) Frame(i);
        var times = new double[Iterations];
        var buildTimes = new double[Iterations];
        long before = GC.GetTotalAllocatedBytes(precise: true);
        for (int i = 0; i < Iterations; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            Frame(Warmup + i);
            times[i] = Stopwatch.GetElapsedTime(t0).TotalMicroseconds;
            buildTimes[i] = buildTicks * 1e6 / Stopwatch.Frequency;
        }
        long allocated = (GC.GetTotalAllocatedBytes(precise: true) - before) / Iterations;
        Array.Sort(times);
        Array.Sort(buildTimes);
        Assert.True(s.Primitives.Length == 0 || layers.Frame.Layers.Length == 1);
        return new Result(s.Name, times[Iterations / 2], times[Iterations * 95 / 100], buildTimes[Iterations / 2], allocated,
            s.Primitives.Length, stats.Instances, uploadBytes, draws.Count);
    }

    [GraphicsBenchFact]
    public void Report_graphics_frame_costs()
    {
        var lines = new List<string>
        {
            $"Environment: {Environment.OSVersion}, {Environment.ProcessorCount} logical CPUs, .NET {Environment.Version}, " +
            $"{(Debugger.IsAttached ? "debugger" : "no debugger")}, Release={!IsDebugBuild()}; {Warmup} warm-up + {Iterations} measured frames.",
            "",
            "| Scenario | median µs/frame | p95 µs | GPU build median µs | alloc B/frame | primitives | GPU instances | GPU upload B/frame | GPU draws |",
            "|---|---:|---:|---:|---:|---:|---:|---:|---:|",
        };
        foreach (Scenario s in Scenarios())
        {
            Result r = Measure(s);
            lines.Add($"| {r.Name} | {r.MedianUs:F1} | {r.P95Us:F1} | {r.BuildMedianUs:F1} | {r.AllocatedBytes} | {r.Primitives} | {r.Instances} | {r.UploadBytes} | {r.DrawCalls} |");
        }
        foreach (string l in lines) _output.WriteLine(l);
        if (Environment.GetEnvironmentVariable("TSV_GRAPHICS_BENCH_OUT") is { Length: > 0 } path)
            File.AppendAllLines(path, lines);
    }

    private static bool IsDebugBuild() =>
        typeof(GraphicsBenchmarkTests).Assembly.GetCustomAttributes(false)
            .OfType<DebuggableAttribute>().Any(d => d.IsJITOptimizerDisabled);
}
