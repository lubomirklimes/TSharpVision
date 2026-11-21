using System.Runtime.InteropServices;
using TSharpVision.Config;
using TSharpVision.Drivers.SDL.Gpu;
using TSharpVision.Drivers.SDL.Renderer;
using TSharpVision.Drivers.SDL.Rendering.Fonts;
using static TSharpVision.Drivers.SDL.Tests.Unicode.UnicodeTestText;

namespace TSharpVision.Drivers.SDL.Tests.Unicode;

/// <summary>
/// TV13m through both production renderers without a window: <see cref="SDLRenderer"/> on an SDL software renderer
/// over a surface (read back pixel for pixel) and the <see cref="SDLGpuRenderer"/> CPU-compositing path. Wide clusters
/// span their cells, trails get no glyph of their own and their background never covers the lead's glyph, colour
/// emoji keep their colours under any foreground, monochrome clusters are tinted, and the SDL_Renderer texture cache is
/// keyed without the foreground colour.
/// </summary>
[Collection("SDL initialization")]
public sealed class SdlClusterRendererTests
{
    [Fact]
    public void Both_drivers_report_wide_cells_and_cluster_text()
    {
        using var classic = new SDLDriver();
        using var gpu = new SDLGpuDriver();
        foreach (IDriver driver in new IDriver[] { classic, gpu })
        {
            Assert.True(driver.TextCapabilities.HasFlag(TextCapabilities.WideCells));
            Assert.True(driver.TextCapabilities.HasFlag(TextCapabilities.ClusterText));
            // ColorGlyphs only once a renderer has loaded a colour emoji font.
            Assert.False(driver.TextCapabilities.HasFlag(TextCapabilities.ColorGlyphs));
        }
    }

    private sealed class Software : IDisposable
    {
        public IntPtr Surface, Handle;
        public SDLRenderer Renderer = null!;
        public int Width, Height;

        public static Software Open(uint cols, uint rows, float pointSize = 16)
        {
            var s = new Software();
            // A throwaway renderer first, only to learn the cell size for this font.
            s.Surface = SDL3.SDL.CreateSurface(64, 64, SDL3.SDL.PixelFormat.ARGB8888);
            s.Handle = SDL3.SDL.CreateSoftwareRenderer(s.Surface);
            Assert.True(s.Handle != IntPtr.Zero, SDL3.SDL.GetError());
            var probe = new SDLRenderer(s.Handle, null, (int)pointSize, FontFitMode.Condense, 1f);
            int cw = probe.CellWidth, ch = probe.CellHeight;
            probe.Dispose();
            SDL3.SDL.DestroyRenderer(s.Handle);
            SDL3.SDL.DestroySurface(s.Surface);

            s.Width = (int)cols * cw;
            s.Height = (int)rows * ch;
            s.Surface = SDL3.SDL.CreateSurface(s.Width, s.Height, SDL3.SDL.PixelFormat.ARGB8888);
            s.Handle = SDL3.SDL.CreateSoftwareRenderer(s.Surface);
            s.Renderer = new SDLRenderer(s.Handle, null, (int)pointSize, FontFitMode.Condense, 1f);
            s.Renderer.SetCursor(-1, -1, 0);
            return s;
        }

        // RGBA in memory order, from the ARGB8888 target surface.
        public byte[] Render(ScreenBuffer screen)
        {
            Renderer.Render(screen, 0, 0, screen.Width, screen.Height);
            SDL3.SDL.Surface s = Marshal.PtrToStructure<SDL3.SDL.Surface>(Surface);
            var raw = new byte[s.Pitch * s.Height];
            Marshal.Copy(s.Pixels, raw, 0, raw.Length);
            var rgba = new byte[Width * Height * 4];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int si = y * s.Pitch + x * 4, di = (y * Width + x) * 4;
                    rgba[di] = raw[si + 2]; rgba[di + 1] = raw[si + 1]; rgba[di + 2] = raw[si]; rgba[di + 3] = 255;
                }
            return rgba;
        }

        public void Dispose()
        {
            Renderer?.Dispose();
            if (Handle != IntPtr.Zero) SDL3.SDL.DestroyRenderer(Handle);
            if (Surface != IntPtr.Zero) SDL3.SDL.DestroySurface(Surface);
        }
    }

    private static (byte R, byte G, byte B) Px(byte[] rgba, int width, int x, int y)
    {
        int o = (y * width + x) * 4;
        return (rgba[o], rgba[o + 1], rgba[o + 2]);
    }

    private static bool Chromatic((byte R, byte G, byte B) p) =>
        Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) > 48;

    // Pixels of cells [col, col + cells) in row 0 that differ from the background colour.
    private static int Ink(byte[] rgba, int width, int cw, int ch, int col, int cells, (byte, byte, byte) bg)
    {
        int n = 0;
        for (int y = 0; y < ch; y++)
            for (int x = col * cw; x < (col + cells) * cw; x++)
                if (Px(rgba, width, x, y) != bg) n++;
        return n;
    }

    private static (byte, byte, byte) Rgb(uint argb) => ((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    [SystemFontsFact]
    public void Sdl_renderer_draws_an_emoji_across_its_trail_in_its_own_colours()
    {
        using var sw = Software.Open(6, 1);
        int cw = sw.Renderer.CellWidth, ch = sw.Renderer.CellHeight;
        ScreenBuffer screen = Blank(6, 1, 0x1F);   // white on blue
        PutCluster(screen, 1, 0, Grinning, 2, 0x1F);
        sw.Renderer.Trace = new SdlRenderTrace();
        byte[] px = sw.Render(screen);
        var blue = Rgb(SdlPalette.DecodeAttr(0x1F).bg);

        SdlRenderOp op = Assert.Single(sw.Renderer.Trace.Operations, o => o.Kind == SdlRenderOpKind.Cluster);
        Assert.Equal((cw, 0, 2 * cw, ch), (op.X, op.Y, op.Width, op.Height));
        Assert.DoesNotContain(sw.Renderer.Trace.Operations, o => o.Kind == SdlRenderOpKind.Glyph);

        // Ink on both the lead and the trail cell (the trail's background did not cover the glyph), none outside.
        Assert.True(Ink(px, sw.Width, cw, ch, 1, 1, blue) > 10);
        Assert.True(Ink(px, sw.Width, cw, ch, 2, 1, blue) > 10);
        Assert.Equal(0, Ink(px, sw.Width, cw, ch, 0, 1, blue));
        Assert.Equal(0, Ink(px, sw.Width, cw, ch, 3, 3, blue));

        // Intrinsic colour: yellow-ish face pixels exist although the foreground is white.
        int warm = 0;
        for (int y = 0; y < ch; y++)
            for (int x = cw; x < 3 * cw; x++)
            {
                var p = Px(px, sw.Width, x, y);
                if (Chromatic(p) && p.R > 150 && p.G > 100 && p.B < 120) warm++;
            }
        Assert.True(warm > 10, $"only {warm} warm pixels");
    }

    [SystemFontsFact]
    public void Sdl_renderer_tints_monochrome_clusters_and_caches_without_the_colour()
    {
        using var sw = Software.Open(8, 1);
        int cw = sw.Renderer.CellWidth, ch = sw.Renderer.CellHeight;
        ScreenBuffer screen = Blank(8, 1, 0x00);
        PutCluster(screen, 0, 0, Kan, 2, 0x0C);    // light red
        PutCluster(screen, 2, 0, Kan, 2, 0x0A);    // light green
        PutCluster(screen, 4, 0, Grinning, 2, 0x0C);
        PutCluster(screen, 6, 0, Grinning, 2, 0x0A);
        byte[] px = sw.Render(screen);
        Assert.Equal(2, sw.Renderer.ClusterTexturesCreated);   // one per (cells, text), not per colour
        sw.Render(screen);
        Assert.Equal(2, sw.Renderer.ClusterTexturesCreated);

        var red = Rgb(SdlPalette.DecodeAttr(0x0C).fg);
        var green = Rgb(SdlPalette.DecodeAttr(0x0A).fg);
        bool Has(int col, (byte, byte, byte) colour)
        {
            for (int y = 0; y < ch; y++)
                for (int x = col * cw; x < (col + 2) * cw; x++)
                {
                    var p = Px(px, sw.Width, x, y);
                    if (Math.Abs(p.R - colour.Item1) <= 2 && Math.Abs(p.G - colour.Item2) <= 2 && Math.Abs(p.B - colour.Item3) <= 2)
                        return true;
                }
            return false;
        }
        Assert.True(Has(0, red));
        Assert.True(Has(2, green));
        Assert.False(Has(0, green));
        // The emoji in red and in green cells is the same picture.
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < 2 * cw; x++)
                Assert.Equal(Px(px, sw.Width, 4 * cw + x, y), Px(px, sw.Width, 6 * cw + x, y));
    }

    [SystemFontsFact]
    public void Sdl_renderer_clips_a_wide_cluster_at_the_right_edge()
    {
        using var sw = Software.Open(4, 1);
        int cw = sw.Renderer.CellWidth, ch = sw.Renderer.CellHeight;
        ScreenBuffer screen = Blank(4, 1, 0x07);
        PutCluster(screen, 3, 0, Kan, 2, 0x07, trails: 0);
        sw.Renderer.Trace = new SdlRenderTrace();
        sw.Render(screen);
        SdlRenderOp op = Assert.Single(sw.Renderer.Trace.Operations, o => o.Kind == SdlRenderOpKind.Cluster);
        Assert.Equal((3 * cw, cw), (op.X, op.Width));
    }

    [SystemFontsFact]
    public void Gpu_cpu_composite_matches_the_cluster_rules()
    {
        using var renderer = new SDLGpuRenderer(IntPtr.Zero, new SDLGpuOptions());
        renderer.LoadFontForTesting();
        renderer.SetCursor(-1, -1, 0);
        int cw = renderer.CellWidth, ch = renderer.CellHeight;
        ScreenBuffer screen = Blank(16, 1, 0x1F);
        Write(screen, 0, 0, Sample1, 0x1F);
        byte[] px = renderer.CompositeForTesting(screen, 16, 1);
        int width = 16 * cw;
        var blue = Rgb(GpuPhysicalPalette.Default.Argb[1]);

        Assert.NotNull(renderer.ClustersForTesting);
        Assert.True(renderer.ColorGlyphsAvailable);
        // Every two-cell cluster inks both of its cells.
        foreach (int col in new[] { 1, 4, 7, 10 })
        {
            Assert.True(Ink(px, width, cw, ch, col, 1, blue) > 5, $"lead {col}");
            Assert.True(Ink(px, width, cw, ch, col + 1, 1, blue) > 5, $"trail {col + 1}");
        }
        int chromatic = 0;
        for (int y = 0; y < ch; y++)
            for (int x = cw; x < 3 * cw; x++)
                if (Chromatic(Px(px, width, x, y)) && Px(px, width, x, y) != blue) chromatic++;
        Assert.True(chromatic > 10);
        Assert.Equal(0, Ink(px, width, cw, ch, 13, 3, blue));
    }
}
