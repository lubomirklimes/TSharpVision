using System.Runtime.InteropServices;
using TSharpVision.Config;
using TSharpVision.Drivers.SDL.Graphics;
using TSharpVision.Drivers.SDL.Tests.Gpu;
using Xunit.Abstractions;

namespace TSharpVision.Drivers.SDL.Tests.Graphics;

/// <summary>
/// TV12o pixel graphics through the classic SDL_Renderer driver on a real renderer (opt-in,
/// <see cref="NativeGpuFactAttribute"/>, hidden window): the production <see cref="SDLRenderer"/> renders cells plus
/// lines, rectangles and images, the <see cref="SDLRenderer.BeforePresentForTesting"/> seam reads the target back
/// (SDL_RenderReadPixels), and the result is compared with the CPU reference (cell backgrounds +
/// <see cref="PixelGraphicsRasterizer"/>). Opaque colours and nearest images at integer scale must match exactly;
/// bilinear and translucent content within a small tolerance. The screen uses only spaces, so no glyph enters.
/// </summary>
[Collection("SDL initialization")]
public sealed class SdlRendererGraphicsNativeTests
{
    private const uint Cols = 10, Rows = 4;

    private readonly ITestOutputHelper _output;
    public SdlRendererGraphicsNativeTests(ITestOutputHelper output) => _output = output;

    private static ScreenBuffer Screen()
    {
        var screen = new ScreenBuffer((ushort)Cols, (ushort)Rows);
        for (uint row = 0; row < Rows; row++)
            for (uint col = 0; col < Cols; col++)
                screen.SetChar(col, row, new TScreenChar(' ', (TColorAttr)(byte)(((col + 2 * row) % 8) << 4)));
        return screen;
    }

    private static PixelImage Checker(int w, int h, bool translucent = false) => PixelImage.Create(w, h, px =>
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                px[o] = (byte)(x * 255 / Math.Max(1, w - 1));
                px[o + 1] = (byte)(y * 255 / Math.Max(1, h - 1));
                px[o + 2] = (byte)((x + y) % 2 == 0 ? 220 : 30);
                px[o + 3] = translucent ? (byte)(60 + (x * 37 + y * 11) % 190) : (byte)255;
            }
    });

    private sealed class Session : IDisposable
    {
        public IntPtr Window, Handle;
        public SDLRenderer Renderer = null!;

        public static Session Open(string? driver, float density)
        {
            SDL3.SDL.SetHint(SDL3.SDL.Hints.QuitOnLastWindowClose, "0");
            Assert.True(SDL3.SDL.Init(SDL3.SDL.InitFlags.Video), SDL3.SDL.GetError());
            var s = new Session();
            s.Window = SDL3.SDL.CreateWindow("TSV classic graphics native test", 640, 480, SDL3.SDL.WindowFlags.Hidden);
            Assert.True(s.Window != IntPtr.Zero, SDL3.SDL.GetError());
            s.Handle = SDL3.SDL.CreateRenderer(s.Window, driver);
            Assert.True(s.Handle != IntPtr.Zero, SDL3.SDL.GetError());
            s.Renderer = new SDLRenderer(s.Handle, null, null, FontFitMode.Condense, density);
            s.Renderer.SetCursor(-1, -1, 0);
            return s;
        }

        public string Name => SDL3.SDL.GetRendererName(Handle) ?? "unknown";

        public (int W, int H, byte[] Rgba) Capture(ScreenBuffer screen, PixelGraphicsFrame graphics)
        {
            Renderer.GraphicsFrame = graphics;
            (int, int, byte[])? result = null;
            Renderer.BeforePresentForTesting = handle => result = ReadBack(handle);
            try { Renderer.Render(screen, 0, 0, Cols, Rows); }
            finally { Renderer.BeforePresentForTesting = null; }
            return result ?? throw new InvalidOperationException("no frame was read back");
        }

        public void Dispose()
        {
            Renderer?.Dispose();
            if (Handle != IntPtr.Zero) SDL3.SDL.DestroyRenderer(Handle);
            if (Window != IntPtr.Zero) SDL3.SDL.DestroyWindow(Window);
            SDL3.SDL.Quit();
        }
    }

    private static (int, int, byte[]) ReadBack(IntPtr handle)
    {
        IntPtr surface = SDL3.SDL.RenderReadPixels(handle, null);
        Assert.True(surface != IntPtr.Zero, SDL3.SDL.GetError());
        try
        {
            IntPtr converted = SDL3.SDL.ConvertSurface(surface, SDL3.SDL.PixelFormat.ABGR8888);
            Assert.True(converted != IntPtr.Zero, SDL3.SDL.GetError());
            try
            {
                var s = Marshal.PtrToStructure<SDL3.SDL.Surface>(converted);
                var rgba = new byte[s.Width * s.Height * 4];
                for (int y = 0; y < s.Height; y++) Marshal.Copy(s.Pixels + y * s.Pitch, rgba, y * s.Width * 4, s.Width * 4);
                return (s.Width, s.Height, rgba);
            }
            finally { SDL3.SDL.DestroySurface(converted); }
        }
        finally { SDL3.SDL.DestroySurface(surface); }
    }

    // Cell backgrounds (black outside the grid) and then the CPU reference graphics, at the renderer's scale.
    private static byte[] Oracle(SDLRenderer renderer, int width, int height, ScreenBuffer screen, PixelGraphicsFrame graphics)
    {
        var rgba = new byte[width * height * 4];
        for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
        int cw = renderer.CellWidth, ch = renderer.CellHeight;
        for (uint row = 0; row < Rows; row++)
            for (uint col = 0; col < Cols; col++)
            {
                var (_, bg) = SdlPalette.DecodeAttr((byte)(screen.GetChar(col, row).Attr & 0xFF));
                for (int y = (int)row * ch; y < (int)(row + 1) * ch && y < height; y++)
                    for (int x = (int)col * cw; x < (int)(col + 1) * cw && x < width; x++)
                    {
                        int o = (y * width + x) * 4;
                        rgba[o] = (byte)(bg >> 16); rgba[o + 1] = (byte)(bg >> 8); rgba[o + 2] = (byte)bg;
                    }
            }
        var scale = new PixelScale(renderer.Metrics.LogicalWidth, renderer.Metrics.LogicalHeight, cw, ch);
        PixelGraphicsRasterizer.Rasterize(rgba, width, height, width * 4, graphics, scale, new());
        return rgba;
    }

    // Compares RGB (the window target's alpha is backend-defined); returns the largest channel difference.
    private int Compare((int W, int H, byte[] Rgba) capture, byte[] oracle, int tolerance, string label)
    {
        int worst = 0, differing = 0;
        string? first = null;
        for (int i = 0; i < capture.Rgba.Length; i += 4)
            for (int c = 0; c < 3; c++)
            {
                int d = Math.Abs(capture.Rgba[i + c] - oracle[i + c]);
                if (d == 0) continue;
                differing++;
                worst = Math.Max(worst, d);
                if (d > tolerance && first == null)
                {
                    int p = i / 4;
                    first = $"{label}: pixel ({p % capture.W},{p / capture.W}) channel {c} is {capture.Rgba[i + c]}, reference {oracle[i + c]}";
                }
            }
        _output.WriteLine($"{label}: {capture.W}x{capture.H}, differing channels {differing}, worst {worst}");
        Assert.True(first == null, first);
        return worst;
    }

    private static PixelGraphicsFrame Opaque(int cw, int ch, PixelImage pixelArt, PixelImage photo)
    {
        var layers = new PixelGraphicsLayers();
        layers.Set(new object(),
            [new(cw, ch / 2, 4 * cw, 3 * ch), new(6 * cw, 0, 3 * cw, 4 * ch)],
            [
                GraphicsPrimitive.FillRect(0, ch + 3, (int)Cols * cw, ch, 0x0000AAFF),
                GraphicsPrimitive.Image(0, cw + 2, ch / 2 + 1, pixelArt.Width * 3, pixelArt.Height * 2),  // integer 3x2
                GraphicsPrimitive.Image(1, 6 * cw - 5, 3, photo.Width, photo.Height),                    // 1:1, clipped
                GraphicsPrimitive.Line(0, 0, (int)Cols * cw - 1, (int)Rows * ch - 1, 0xFFFFFFFF),
                GraphicsPrimitive.Line((int)Cols * cw - 1, ch, 0, 3 * ch - 1, 0x55FF55FF),
                GraphicsPrimitive.FillRect(7 * cw, 2 * ch, cw, 4, 0xFFFF00FF),                           // covers the photo
            ],
            [pixelArt, photo]);
        return layers.Frame;
    }

    private static PixelGraphicsFrame Blended(int cw, int ch, PixelImage pixelArt, PixelImage translucent)
    {
        var layers = new PixelGraphicsLayers();
        layers.Set(new object(),
            [new(0, 0, (int)Cols * cw, (int)Rows * ch)],
            [
                GraphicsPrimitive.Image(0, 3, 2, 5 * cw, 3 * ch, GraphicsImageFilter.Linear),
                GraphicsPrimitive.Image(1, 4 * cw, 5, 3 * cw + 7, 2 * ch + 3, GraphicsImageFilter.Nearest, 160),
                GraphicsPrimitive.Image(1, cw, ch, 2 * cw, ch, GraphicsImageFilter.Linear, 90),
                GraphicsPrimitive.FillRect(2 * cw, 0, 5 * cw, ch, 0xFF000080),
                GraphicsPrimitive.Line(0, (int)Rows * ch - 1, (int)Cols * cw - 1, 0, 0x00FFFF60),
            ],
            [pixelArt, translucent]);
        return layers.Frame;
    }

    [NativeGpuFact]
    public void Default_renderer_draws_lines_rectangles_and_images_like_the_cpu_reference() => Qualify(null, 1f);

    [NativeGpuFact]
    public void Default_renderer_scales_graphics_with_the_pixel_density() => Qualify(null, 2f);

    [NativeGpuFact]
    public void Software_renderer_draws_lines_rectangles_and_images_like_the_cpu_reference() => Qualify("software", 1f);

    [NativeGpuFact]
    public void Every_available_renderer_driver_draws_like_the_cpu_reference()
    {
        Assert.True(SDL3.SDL.Init(SDL3.SDL.InitFlags.Video), SDL3.SDL.GetError());
        var drivers = new List<string>();
        for (int i = 0; i < SDL3.SDL.GetNumRenderDrivers(); i++)
            if (SDL3.SDL.GetRenderDriver(i) is { } name) drivers.Add(name);
        SDL3.SDL.Quit();
        _output.WriteLine("drivers: " + string.Join(", ", drivers));
        foreach (string driver in drivers)
        {
            if (driver is "gpu" or "offscreen") { _output.WriteLine($"{driver}: not a window renderer here, skipped"); continue; }
            if (!CanCreate(driver)) { _output.WriteLine($"{driver}: unavailable on this machine"); continue; }
            Qualify(driver, 1f);
        }
    }

    private static bool CanCreate(string driver)
    {
        Assert.True(SDL3.SDL.Init(SDL3.SDL.InitFlags.Video), SDL3.SDL.GetError());
        IntPtr window = SDL3.SDL.CreateWindow("probe", 64, 64, SDL3.SDL.WindowFlags.Hidden);
        IntPtr renderer = window == IntPtr.Zero ? IntPtr.Zero : SDL3.SDL.CreateRenderer(window, driver);
        if (renderer != IntPtr.Zero) SDL3.SDL.DestroyRenderer(renderer);
        if (window != IntPtr.Zero) SDL3.SDL.DestroyWindow(window);
        SDL3.SDL.Quit();
        return renderer != IntPtr.Zero;
    }

    private void Qualify(string? driver, float density)
    {
        using var session = Session.Open(driver, density);
        SDLRenderer r = session.Renderer;
        int cw = r.Metrics.LogicalWidth, ch = r.Metrics.LogicalHeight;
        _output.WriteLine($"renderer={session.Name} cell={cw}x{ch} logical, {r.CellWidth}x{r.CellHeight} device");
        ScreenBuffer screen = Screen();
        PixelImage pixelArt = Checker(5, 4), photo = Checker(2 * cw, ch + 5);

        var before = session.Capture(screen, PixelGraphicsFrame.Empty);
        Assert.Equal(0, r.LastGraphicsDrawCalls);

        PixelGraphicsFrame opaque = Opaque(cw, ch, pixelArt, photo);
        var drawn = session.Capture(screen, opaque);
        Assert.NotEqual(before.Rgba, drawn.Rgba);
        Compare(drawn, Oracle(r, drawn.W, drawn.H, screen, opaque), 0, $"{session.Name} opaque x{density}");
        Assert.Equal(2, r.ImageTexturesForTesting.Uploads);
        // One batch per primitive and clip rectangle it reaches: fill 2, pixel art 1, photo 1, lines 2 + 2, fill 1.
        Assert.Equal(9, r.LastGraphicsDrawCalls);

        PixelGraphicsFrame blended = Blended(cw, ch, pixelArt, Checker(7, 5, translucent: true));
        var mixed = session.Capture(screen, blended);
        Compare(mixed, Oracle(r, mixed.W, mixed.H, screen, blended), session.Name == "software" ? 6 : 3, $"{session.Name} blended x{density}");

        // Texture lifecycle: unreferenced textures are released; a device reset drops textures and text continues.
        Assert.Equal(2, r.ImageTexturesForTesting.Count); // pixelArt kept, photo released, translucent uploaded
        r.HandleDeviceReset();
        Assert.Equal(0, r.ImageTexturesForTesting.Count);
        var again = session.Capture(screen, opaque);
        Compare(again, Oracle(r, again.W, again.H, screen, opaque), 0, $"{session.Name} after reset x{density}");

        // Without layers the frame is exactly the text-only frame and no texture is kept.
        var after = session.Capture(screen, PixelGraphicsFrame.Empty);
        Assert.Equal(before.Rgba, after.Rgba);
        Assert.Equal(0, r.ImageTexturesForTesting.Count);
    }
}
