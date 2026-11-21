using System.Runtime.InteropServices;
using TSharpVision.Config;
using TSharpVision.Drivers.SDL.Gpu;
using TSharpVision.Drivers.SDL.Tests.Gpu;
using Xunit.Abstractions;
using static TSharpVision.Drivers.SDL.Tests.Unicode.UnicodeTestText;

namespace TSharpVision.Drivers.SDL.Tests.Unicode;

/// <summary>
/// TV13m visual gate on a real device (opt-in, <see cref="NativeGpuFactAttribute"/>: <c>TSV_NATIVE_GPU_TESTS=1</c>,
/// hidden window): the TV13h samples 1-4 at 16 pt and 24 pt through the SDL GPU shader pipeline (capture seam: the
/// presented frame rerendered into a readable texture) and through SDL_Renderer on the platform's default renderer
/// (read back before present). Asserts the structural oracles (cluster quads, colour pixels, trail ink, edges); when
/// <c>TSV_U3_SCREENSHOT_DIR</c> is set it also writes each frame as a PNG for visual review.
/// </summary>
[Collection("SDL initialization")]
public sealed class SdlUnicodeNativeTests
{
    private const uint Cols = 24, Rows = 6;
    private readonly ITestOutputHelper _output;
    public SdlUnicodeNativeTests(ITestOutputHelper output) => _output = output;

    // Row 0: sample 1. Row 1: sample 2 (e + combining acute) next to a plain e. Row 2: sample 3 (CJK).
    // Row 3: sample 4 - a tab then a wide cluster, and a wide cluster cut at the right edge.
    // Row 4: VS15 / VS16 heart, technologist, keycap. Row 5: colour under different foregrounds.
    private static ScreenBuffer Samples()
    {
        ScreenBuffer screen = Blank(Cols, Rows, 0x17);
        Write(screen, 0, 0, Sample1, 0x1F);
        Write(screen, 1, 0, Sample2 + " e " + Sample2 + Sample2, 0x1E);
        Write(screen, 2, 0, Sample3 + " " + Sample3, 0x1A);
        Write(screen, 3, 0, "\t" + Kan + Grinning, 0x70);
        PutCluster(screen, Cols - 1, 3, Grinning, 2, 0x4F, trails: 0);   // half-clipped at the right edge
        Write(screen, 4, 0, HeartText + " " + HeartEmoji + " " + Technologist + " " + Keycap1, 0x1F);
        Write(screen, 5, 0, Grinning, 0x0C);
        Write(screen, 5, 2, Grinning, 0x0A);
        Write(screen, 5, 4, Kan, 0x0C);
        Write(screen, 5, 6, Kan, 0x0A);
        return screen;
    }

    private void Save(string name, int width, int height, byte[] rgba)
    {
        string? dir = Environment.GetEnvironmentVariable("TSV_U3_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name + ".png");
        SavePng(path, width, height, rgba);
        _output.WriteLine($"saved {path}");
    }

    private static int Chromatic(byte[] rgba, int width, int x0, int y0, int x1, int y1)
    {
        int n = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int o = (y * width + x) * 4;
                int max = Math.Max(rgba[o], Math.Max(rgba[o + 1], rgba[o + 2]));
                int min = Math.Min(rgba[o], Math.Min(rgba[o + 1], rgba[o + 2]));
                if (max - min > 48 && !(rgba[o + 2] > 120 && rgba[o] < 40 && rgba[o + 1] < 40)) n++;   // not the blue background
            }
        return n;
    }

    [NativeGpuFact]
    public void Gpu_shader_pipeline_draws_the_samples() { RunGpu(16); RunGpu(24); }

    private void RunGpu(int pointSize)
    {
        SDL3.SDL.SetHint(SDL3.SDL.Hints.QuitOnLastWindowClose, "0");
        Assert.True(SDL3.SDL.Init(SDL3.SDL.InitFlags.Video), SDL3.SDL.GetError());
        IntPtr window = SDL3.SDL.CreateWindow("TSV U3 native", 640, 480, SDL3.SDL.WindowFlags.Hidden);
        Assert.True(window != IntPtr.Zero, SDL3.SDL.GetError());
        var renderer = new SDLGpuRenderer(window, SDLGpuOptions.FromEnvironment(), null, pointSize);
        try
        {
            renderer.Initialize();
            Assert.True(renderer.ShaderPipelineActive);
            Assert.True(renderer.PipelineForTesting!.ClusterPassActive, renderer.PipelineForTesting.ClusterPassUnavailableReason);
            renderer.StartRenderThread();
            int cw = renderer.CellWidth, ch = renderer.CellHeight;
            uint w = (uint)(cw * Cols), h = (uint)(ch * Rows);
            ScreenBuffer screen = Samples();
            var pending = renderer.RequestCaptureForTesting(w, h, TimeSpan.FromSeconds(10));
            renderer.Render(screen, 0, 0, Cols, Rows);
            Assert.True(pending.Wait(TimeSpan.FromSeconds(20)), "capture timed out");
            GpuFrameCapture c = pending.Result;
            _output.WriteLine($"gpu {pointSize}pt backend={c.Backend} cell={cw}x{ch} clusters={renderer.PipelineForTesting.LastClusterQuadCount}");
            Save($"gpu-{c.Backend}-{pointSize}pt", c.Width, c.Height, c.Rgba);

            Assert.True(renderer.PipelineForTesting.LastClusterQuadCount >= 15);
            Assert.True(Chromatic(c.Rgba, c.Width, cw, 0, 3 * cw, ch) > 10, "the grinning face is not in colour");
            Assert.True(Chromatic(c.Rgba, c.Width, 10 * cw, 0, 12 * cw, ch) > 10, "the family is not in colour");
            // Emoji under red and green foregrounds are identical pictures.
            for (int y = 5 * ch; y < 6 * ch; y++)
                for (int x = 0; x < 2 * cw; x++)
                {
                    int a = (y * c.Width + x) * 4, b = (y * c.Width + x + 2 * cw) * 4;
                    Assert.Equal(c.Rgba.AsSpan(a, 3).ToArray(), c.Rgba.AsSpan(b, 3).ToArray());
                }
        }
        finally
        {
            renderer.Dispose();
            SDL3.SDL.DestroyWindow(window);
            SDL3.SDL.Quit();
        }
    }

    [NativeGpuFact]
    public void Recording_a_cluster_frame_allocates_no_managed_memory()
    {
        SDL3.SDL.SetHint(SDL3.SDL.Hints.QuitOnLastWindowClose, "0");
        Assert.True(SDL3.SDL.Init(SDL3.SDL.InitFlags.Video), SDL3.SDL.GetError());
        IntPtr window = SDL3.SDL.CreateWindow("TSV U3 alloc", 640, 480, SDL3.SDL.WindowFlags.Hidden);
        var renderer = new SDLGpuRenderer(window, SDLGpuOptions.FromEnvironment(), null, 16);
        IntPtr tex = IntPtr.Zero;
        try
        {
            renderer.Initialize();   // no render thread: this test thread records the frames
            TerminalGpuPipeline pipeline = renderer.PipelineForTesting!;
            IntPtr device = renderer.DeviceForTesting;
            uint w = (uint)(renderer.CellWidth * Cols), h = (uint)(renderer.CellHeight * Rows);
            var texInfo = new SDL3.SDL.GPUTextureCreateInfo
            {
                Type = SDL3.SDL.GPUTextureType.TextureType2D, Format = SDL3.SDL.GetGPUSwapchainTextureFormat(device, window),
                Usage = SDL3.SDL.GPUTextureUsageFlags.ColorTarget, Width = w, Height = h,
                LayerCountOrDepth = 1, NumLevels = 1, SampleCount = SDL3.SDL.GPUSampleCount.SampleCount1,
            };
            tex = SDL3.SDL.CreateGPUTexture(device, in texInfo);
            Assert.True(tex != IntPtr.Zero, SDL3.SDL.GetError());
            ScreenBuffer screen = Samples();
            IntPtr cmd = SDL3.SDL.AcquireGPUCommandBuffer(device);
            for (int warm = 0; warm < 2; warm++)
                pipeline.RenderFrame(cmd, tex, w, h, screen, Cols, Rows, -1, -1, 0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            var (build, record) = pipeline.RenderFrame(cmd, tex, w, h, screen, Cols, Rows, -1, -1, 0);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            SDL3.SDL.SubmitGPUCommandBuffer(cmd);
            SDL3.SDL.WaitForGPUIdle(device);
            _output.WriteLine($"clusters={pipeline.LastClusterQuadCount} allocatedBytes={allocated} " +
                $"vertexBuild={build.TotalMilliseconds:0.000}ms record={record.TotalMilliseconds:0.000}ms");
            Assert.True(pipeline.LastClusterQuadCount >= 15);
            Assert.Equal(0, allocated);
        }
        finally
        {
            if (tex != IntPtr.Zero) SDL3.SDL.ReleaseGPUTexture(renderer.DeviceForTesting, tex);
            renderer.Dispose();
            SDL3.SDL.DestroyWindow(window);
            SDL3.SDL.Quit();
        }
    }

    [NativeGpuFact]
    public void Without_the_cluster_pass_leads_fall_back_to_their_character()
    {
        SDL3.SDL.SetHint(SDL3.SDL.Hints.QuitOnLastWindowClose, "0");
        Assert.True(SDL3.SDL.Init(SDL3.SDL.InitFlags.Video), SDL3.SDL.GetError());
        IntPtr window = SDL3.SDL.CreateWindow("TSV U3 fallback", 640, 480, SDL3.SDL.WindowFlags.Hidden);
        TerminalGpuPipeline.SimulateClusterPassFailureForTesting = true;
        var renderer = new SDLGpuRenderer(window, SDLGpuOptions.FromEnvironment(), null, 16);
        try
        {
            renderer.Initialize();
            TerminalGpuPipeline.SimulateClusterPassFailureForTesting = false;
            Assert.True(renderer.ShaderPipelineActive);
            Assert.False(renderer.PipelineForTesting!.ClusterPassActive);
            Assert.NotNull(renderer.PipelineForTesting.ClusterPassUnavailableReason);
            renderer.StartRenderThread();
            uint w = (uint)(renderer.CellWidth * Cols), h = (uint)(renderer.CellHeight * Rows);
            var pending = renderer.RequestCaptureForTesting(w, h, TimeSpan.FromSeconds(10));
            renderer.Render(Samples(), 0, 0, Cols, Rows);
            Assert.True(pending.Wait(TimeSpan.FromSeconds(20)), "capture timed out");
            Assert.Equal(0, renderer.PipelineForTesting.LastClusterQuadCount);
        }
        finally
        {
            TerminalGpuPipeline.SimulateClusterPassFailureForTesting = false;
            renderer.Dispose();
            SDL3.SDL.DestroyWindow(window);
            SDL3.SDL.Quit();
        }
    }

    [NativeGpuFact]
    public void Sdl_renderer_draws_the_samples() { RunRenderer(16); RunRenderer(24); }

    private void RunRenderer(int pointSize)
    {
        SDL3.SDL.SetHint(SDL3.SDL.Hints.QuitOnLastWindowClose, "0");
        Assert.True(SDL3.SDL.Init(SDL3.SDL.InitFlags.Video), SDL3.SDL.GetError());
        IntPtr window = SDL3.SDL.CreateWindow("TSV U3 native renderer", 1200, 600, SDL3.SDL.WindowFlags.Hidden);
        IntPtr handle = SDL3.SDL.CreateRenderer(window, (string?)null);
        Assert.True(handle != IntPtr.Zero, SDL3.SDL.GetError());
        var renderer = new SDLRenderer(handle, null, pointSize, FontFitMode.Condense, 1f);
        try
        {
            renderer.SetCursor(-1, -1, 0);
            int cw = renderer.CellWidth, ch = renderer.CellHeight;
            byte[]? rgba = null;
            int width = (int)Cols * cw, height = (int)Rows * ch;
            renderer.BeforePresentForTesting = r =>
            {
                var rect = new SDL3.SDL.Rect { X = 0, Y = 0, W = width, H = height };
                IntPtr surface = SDL3.SDL.RenderReadPixels(r, rect);
                IntPtr abgr = SDL3.SDL.ConvertSurface(surface, SDL3.SDL.PixelFormat.ABGR8888);
                SDL3.SDL.DestroySurface(surface);
                SDL3.SDL.Surface s = Marshal.PtrToStructure<SDL3.SDL.Surface>(abgr);
                rgba = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                    Marshal.Copy(s.Pixels + y * s.Pitch, rgba, y * width * 4, width * 4);
                SDL3.SDL.DestroySurface(abgr);
            };
            renderer.Render(Samples(), 0, 0, Cols, Rows);
            Assert.NotNull(rgba);
            string name = SDL3.SDL.GetRendererName(handle) ?? "renderer";
            _output.WriteLine($"sdl_renderer {pointSize}pt driver={name} cell={cw}x{ch}");
            Save($"renderer-{name}-{pointSize}pt", width, height, rgba!);
            Assert.True(Chromatic(rgba!, width, cw, 0, 3 * cw, ch) > 10, "the grinning face is not in colour");
        }
        finally
        {
            renderer.Dispose();
            SDL3.SDL.DestroyRenderer(handle);
            SDL3.SDL.DestroyWindow(window);
            SDL3.SDL.Quit();
        }
    }
}
