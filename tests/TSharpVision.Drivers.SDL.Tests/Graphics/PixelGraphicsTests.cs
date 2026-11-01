using System.Reflection;
using TSharpVision.Drivers.SDL.Gpu;
using TSharpVision.Drivers.SDL.Graphics;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Drivers.SDL.Tests.Graphics;

/// <summary>
/// TV12n pixel graphics in the SDL drivers without a device: the shared primitive geometry, the layer store, the
/// software rasteriser, the GPU vertex build and the CPU-composite fallback, and the driver capability surface.
/// </summary>
public sealed class PixelGraphicsTests
{
    private const uint Red = 0xFF0000FF, Green = 0x00FF00FF;

    private static HashSet<(int, int)> Pixels(IEnumerable<DeviceRect> rects)
    {
        var set = new HashSet<(int, int)>();
        foreach (DeviceRect r in rects)
            for (int y = r.Y0; y < r.Y1; y++)
                for (int x = r.X0; x < r.X1; x++)
                    Assert.True(set.Add((x, y)), $"pixel ({x},{y}) covered twice");
        return set;
    }

    private static List<DeviceRect> Rects(GraphicsPrimitive p, PixelScale? scale = null)
    {
        var list = new List<DeviceRect>();
        PixelGraphicsGeometry.AppendRects(p, scale ?? PixelScale.Identity(8, 16), list);
        return list;
    }

    // ── Geometry ─────────────────────────────────────────────────────────

    [Fact]
    public void Horizontal_and_vertical_lines_are_single_runs_including_both_end_points()
    {
        Assert.Equal([new DeviceRect(2, 5, 11, 6)], Rects(GraphicsPrimitive.Line(2, 5, 10, 5, Red)));
        Assert.Equal([new DeviceRect(2, 5, 11, 6)], Rects(GraphicsPrimitive.Line(10, 5, 2, 5, Red)));
        Assert.Equal([new DeviceRect(3, 1, 4, 9)], Rects(GraphicsPrimitive.Line(3, 8, 3, 1, Red)));
        Assert.Equal([new DeviceRect(4, 4, 5, 5)], Rects(GraphicsPrimitive.Line(4, 4, 4, 4, Red)));
    }

    [Theory]
    [InlineData(0, 0, 7, 7)]
    [InlineData(0, 0, 13, 4)]
    [InlineData(3, 9, 1, -6)]
    [InlineData(-5, 2, 20, 11)]
    public void Lines_cover_a_connected_path_between_the_end_points(int x1, int y1, int x2, int y2)
    {
        var pixels = Pixels(Rects(GraphicsPrimitive.Line(x1, y1, x2, y2, Red)));
        Assert.Contains((x1, y1), pixels);
        Assert.Contains((x2, y2), pixels);
        // One pixel per step along the major axis, 8-connected, monotonic.
        int major = Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1));
        Assert.Equal(major + 1, pixels.Count);
        bool xMajor = Math.Abs(x2 - x1) >= Math.Abs(y2 - y1);
        Assert.Equal(pixels.Count, pixels.Select(p => xMajor ? p.Item1 : p.Item2).Distinct().Count());
        // Same pixel set in either direction.
        Assert.Equal(pixels, Pixels(Rects(GraphicsPrimitive.Line(x2, y2, x1, y1, Red))));
    }

    [Fact]
    public void Scaling_maps_logical_cells_exactly_onto_device_cells()
    {
        var scale = new PixelScale(8, 16, 16, 32);
        Assert.Equal([new DeviceRect(16, 32, 32, 64)], Rects(GraphicsPrimitive.FillRect(8, 16, 8, 16, Red), scale));
        // A logical pixel becomes its 2x2 device area, so a line is two device pixels thick.
        Assert.Equal([new DeviceRect(0, 2, 8, 4)], Rects(GraphicsPrimitive.Line(0, 1, 3, 1, Red), scale));
        // Fractional density: cell boundaries still land on the device cell boundaries.
        var fractional = new PixelScale(8, 16, 12, 24);
        Assert.Equal(36, fractional.X(24));
        Assert.Equal(48, fractional.Y(32));
    }

    [Fact]
    public void Degenerate_primitives_produce_nothing()
    {
        Assert.Empty(Rects(GraphicsPrimitive.FillRect(3, 3, 0, 5, Red)));
        Assert.Empty(Rects(new GraphicsPrimitive(GraphicsPrimitiveKind.FillRect, 5, 5, 2, 9, Red)));
        Assert.Empty(Rects(GraphicsPrimitive.Line(0, 0, GraphicsPrimitive.MaxLineExtent + 1, 0, Red)));
        Assert.Equal(GraphicsPrimitive.MaxLineExtent, PixelGraphicsGeometry.MaxLineLength);
    }

    // ── Layer store ───────────────────────────────────────────────────────

    [Fact]
    public void Layers_are_per_owner_ordered_and_unchanged_resubmissions_are_free()
    {
        var layers = new PixelGraphicsLayers();
        object a = new(), b = new();
        PixelRect[] clip = [new(0, 0, 10, 10), new(5, 5, 0, 3)];
        GraphicsPrimitive[] prims = [GraphicsPrimitive.Line(0, 0, 9, 9, Red)];

        Assert.True(layers.Set(a, clip, prims));
        PixelGraphicsFrame first = layers.Frame;
        Assert.Single(first.Layers[0].Clip); // the empty rectangle is dropped
        Assert.False(layers.Set(a, [new(0, 0, 10, 10)], prims)); // same visible content
        Assert.Same(first, layers.Frame);

        Assert.True(layers.Set(b, [new(20, 0, 4, 4)], [GraphicsPrimitive.FillRect(20, 0, 4, 4, Green)]));
        Assert.Equal(2, layers.Frame.Layers.Length);
        Assert.Equal(2, layers.Frame.PrimitiveCount);
        Assert.True(layers.Set(a, [new(0, 0, 10, 10)], [GraphicsPrimitive.Line(0, 9, 9, 0, Red)]));
        Assert.Equal(GraphicsPrimitive.Line(0, 9, 9, 0, Red), layers.Frame.Layers[0].Primitives[0]); // order kept

        Assert.True(layers.Remove(a));
        Assert.False(layers.Remove(a));
        Assert.Single(layers.Frame.Layers);
        Assert.True(layers.Clear());
        Assert.Same(PixelGraphicsFrame.Empty, layers.Frame);
        Assert.Throws<ArgumentNullException>(() => layers.Set(null!, clip, prims));
    }

    [Fact]
    public void A_layer_without_visible_clip_draws_nothing()
    {
        var layers = new PixelGraphicsLayers();
        layers.Set(new object(), [], [GraphicsPrimitive.FillRect(0, 0, 4, 4, Red)]);
        Assert.True(layers.Frame.IsEmpty);
    }

    // ── Rasteriser ─────────────────────────────────────────────────────────

    private static PixelGraphicsFrame Frame(PixelRect[] clip, params GraphicsPrimitive[] prims)
    {
        var layers = new PixelGraphicsLayers();
        layers.Set(new object(), clip, prims);
        return layers.Frame;
    }

    [Fact]
    public void Rasteriser_draws_only_inside_the_clip_region_and_the_buffer()
    {
        const int W = 16, H = 8;
        var buffer = new byte[W * H * 4];
        PixelGraphicsFrame frame = Frame([new(0, 0, 4, 8), new(12, 0, 8, 8)],
            GraphicsPrimitive.FillRect(-3, 2, 30, 2, Red));
        PixelGraphicsRasterizer.Rasterize(buffer, W, H, W * 4, frame, PixelScale.Identity(8, 16), new());

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                bool expected = y is 2 or 3 && (x < 4 || x >= 12);
                int o = (y * W + x) * 4;
                Assert.Equal(expected ? (byte)255 : (byte)0, buffer[o]);
                Assert.Equal(expected ? (byte)255 : (byte)0, buffer[o + 3]);
            }
    }

    [Fact]
    public void Rasteriser_blends_translucent_colours_source_over()
    {
        var buffer = new byte[4];
        buffer[0] = 0; buffer[1] = 0; buffer[2] = 200; buffer[3] = 255;
        PixelGraphicsRasterizer.Rasterize(buffer, 1, 1, 4, Frame([new(0, 0, 1, 1)],
            GraphicsPrimitive.FillRect(0, 0, 1, 1, 0xFF000080)), PixelScale.Identity(8, 16), new());
        Assert.Equal(128, buffer[0]);
        Assert.Equal(0, buffer[1]);
        Assert.Equal(100, buffer[2]);
        Assert.Equal(255, buffer[3]);
    }

    // ── Driver capability ──────────────────────────────────────────────────

    [Fact]
    public void Both_sdl_drivers_expose_pixel_graphics_and_text_drivers_do_not()
    {
        Assert.True(typeof(IPixelGraphicsDriver).IsAssignableFrom(typeof(SDLGpuDriver)));
        Assert.True(typeof(IPixelGraphicsDriver).IsAssignableFrom(typeof(SDLDriver)));
        Assert.False(typeof(IPixelGraphicsDriver).IsAssignableFrom(typeof(NullDriver)));
        // The mandatory contract is unchanged: the capability is a separate optional interface.
        Assert.DoesNotContain(typeof(IPixelGraphicsDriver), typeof(IDriver).GetInterfaces());
    }

    [Fact]
    public void Gpu_driver_reports_logical_cell_pixels_and_stores_layers_per_owner()
    {
        using var driver = new SDLGpuDriver();
        driver.AttachForTest(SdlCellMetrics.ForDensity(9, 18, 2f), SdlDisplayScale.Identity, 40, 12);
        try
        {
            Assert.Equal((9, 18), (driver.CellPixelWidth, driver.CellPixelHeight));
            object owner = new();
            driver.SetGraphicsLayer(owner, [new(0, 0, 9, 18)], [GraphicsPrimitive.Line(0, 0, 8, 17, Red)]);
            Assert.Equal(1, driver.GraphicsLayerCount);
            Assert.Equal(1, driver.GraphicsFrameForTesting.PrimitiveCount);
            driver.RemoveGraphicsLayer(owner);
            driver.RemoveGraphicsLayer(owner);
            Assert.Equal(0, driver.GraphicsLayerCount);
            Assert.Throws<ArgumentNullException>(() => driver.RemoveGraphicsLayer(null!));
        }
        finally { DetachForTest(driver); }
    }

    [Fact]
    public void Classic_driver_reports_logical_cell_pixels_and_stores_layers()
    {
        var driver = new SDLDriver();
        Assert.Equal(12, driver.CellPixelWidth); // headless default metrics (12 x 26)
        Assert.Equal(26, driver.CellPixelHeight);
        object owner = new();
        driver.SetGraphicsLayer(owner, [new(0, 0, 12, 26)], [GraphicsPrimitive.FillRect(0, 0, 12, 26, Red)]);
        Assert.Equal(1, driver.GraphicsFrameForTesting.PrimitiveCount);
        driver.RemoveGraphicsLayer(owner);
        Assert.True(driver.GraphicsFrameForTesting.IsEmpty);
    }

    private static void DetachForTest(SDLGpuDriver driver) =>
        typeof(SDLGpuDriver).GetField("_attached", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(driver, false);
}
