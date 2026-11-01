using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Graphics;
using TSharpVision.Samples.TVDemo;
using Xunit;

namespace TSharpVision.Tests.Samples;

/// <summary>
/// TV12o: the generated test images, <see cref="GImage"/> fit math, and the TVDemo "Graphics gallery" and "Live chart"
/// windows against an image-recording <see cref="IPixelGraphicsDriver"/> around the scope's NullDriver, plus the
/// text-driver fallback.
/// </summary>
[Collection("NonParallel")]
public sealed class GraphicsGalleryTests
{
    private const int CellW = 8, CellH = 16;

    private static uint Pixel(PixelImage image, int x, int y)
    {
        ReadOnlySpan<byte> p = image.Pixels.Slice(y * image.Stride + x * 4, 4);
        return (uint)(p[0] << 24 | p[1] << 16 | p[2] << 8 | p[3]);
    }

    // ── Test patterns ────────────────────────────────────────────────────

    [Fact]
    public void Checkerboard_alternates_cells_starting_with_the_first_colour()
    {
        PixelImage img = TestPatterns.Checkerboard(5, 4, 2, 0x112233FF, 0xAABBCC00);
        Assert.Equal((5, 4, 20), (img.Width, img.Height, img.Stride));
        Assert.Equal(0x112233FFu, Pixel(img, 0, 0));
        Assert.Equal(0x112233FFu, Pixel(img, 1, 1));
        Assert.Equal(0xAABBCC00u, Pixel(img, 2, 0));
        Assert.Equal(0xAABBCC00u, Pixel(img, 0, 2));
        Assert.Equal(0xAABBCC00u, Pixel(img, 4, 3)); // cell (2, 1): odd sum
        Assert.Equal(0x112233FFu, Pixel(img, 4, 0)); // cell (2, 0): even sum, the clipped last column
        Assert.Throws<ArgumentOutOfRangeException>(() => TestPatterns.Checkerboard(4, 4, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TestPatterns.Checkerboard(0, 4, 1, 0, 0));
    }

    [Fact]
    public void Gradient_interpolates_all_four_channels_with_exact_ends()
    {
        PixelImage h = TestPatterns.Gradient(5, 2, 0x00000000, 0xFF8040FF);
        Assert.Equal(0x00000000u, Pixel(h, 0, 1));
        Assert.Equal(0xFF8040FFu, Pixel(h, 4, 0));
        // k = 2 of 5: (255*2 + 2) / 4 = 128, (128*2 + 2) / 4 = 64, (64*2 + 2) / 4 = 32.
        Assert.Equal(0x80402080u, Pixel(h, 2, 0));
        Assert.Equal(Pixel(h, 3, 0), Pixel(h, 3, 1)); // constant down a column

        PixelImage v = TestPatterns.Gradient(3, 4, 0xFFFFFFFF, 0x000000FF, vertical: true);
        Assert.Equal(0xFFFFFFFFu, Pixel(v, 2, 0));
        Assert.Equal(0x000000FFu, Pixel(v, 0, 3));
        Assert.Equal(Pixel(v, 0, 1), Pixel(v, 2, 1));
        Assert.Equal(0xFF0000FFu, Pixel(TestPatterns.Gradient(1, 1, 0xFF0000FF, 0), 0, 0)); // one pixel: the start colour
    }

    [Fact]
    public void Color_bars_have_eight_bars_and_an_alpha_ramp_in_the_bottom_half()
    {
        PixelImage img = TestPatterns.ColorBars(16, 8);
        for (int bar = 0; bar < 8; bar++)
        {
            Assert.Equal(TestPatterns.BarColors[bar], Pixel(img, bar * 2, 0));
            Assert.Equal(TestPatterns.BarColors[bar], Pixel(img, bar * 2 + 1, 3));
        }
        byte[] alpha = Enumerable.Range(0, 8).Select(y => (byte)Pixel(img, 5, y)).ToArray();
        Assert.Equal(new byte[] { 255, 255, 255, 255, 255, 170, 85, 0 }, alpha);
        Assert.Equal(TestPatterns.BarColors[2] & 0xFFFFFF00u, Pixel(img, 5, 7)); // RGB kept at alpha 0
        Assert.Equal(TestPatterns.BarColors[7], Pixel(TestPatterns.ColorBars(9, 1), 8, 0));
    }

    // ── GImage ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(16, 8, 0, 0, 100, 100, 0, 25, 100, 50)]   // width-limited, centred vertically
    [InlineData(8, 16, 0, 0, 100, 100, 25, 0, 50, 100)]   // height-limited, centred horizontally
    [InlineData(4, 3, 10, 20, 40, 30, 10, 20, 40, 30)]    // exact aspect fills the area
    [InlineData(3, 1, 5, 5, 10, 10, 5, 8, 10, 3)]         // 10 * 1 / 3 = 3 rows, (10 - 3) / 2 = 3
    [InlineData(1000, 1, 0, 0, 10, 10, 0, 4, 10, 1)]      // never thinner than one pixel
    [InlineData(4, 4, 7, 9, 0, 10, 7, 9, 0, 0)]           // empty area
    public void Fit_preserves_aspect_and_centres(int iw, int ih, int ax, int ay, int aw, int ah, int x, int y, int w, int h) =>
        Assert.Equal(new PixelRect(x, y, w, h), GImage.Fit(iw, ih, new PixelRect(ax, ay, aw, ah)));

    [Fact]
    public void Image_object_fits_or_stretches_into_its_area_and_follows_the_view_size()
    {
        PixelImage img = TestPatterns.Checkerboard(16, 8, 1, 0xFFFFFFFF, 0x000000FF);
        var obj = new GImage(img, GraphicsImageFilter.Linear) { Opacity = 0x80 };
        var canvas = new GraphicsCanvas();
        obj.GraphDraw(canvas, 10, 20, 200, 200);
        Assert.Equal([GraphicsPrimitive.Image(0, 10, 70, 200, 100, GraphicsImageFilter.Linear, 0x80)], canvas.Primitives.ToArray());
        Assert.Same(img, canvas.Images[0]);

        canvas.Clear();
        obj.Stretch = true;
        obj.ChangeArea(GraphConstants.GraphXMax / 2, 0, GraphConstants.GraphXMax, GraphConstants.GraphYMax);
        obj.GraphDraw(canvas, 0, 0, 400, 300);
        Assert.Equal(GraphicsPrimitive.Image(0, 200, 0, 200, 300, GraphicsImageFilter.Linear, 0x80), canvas.Primitives[0]);

        canvas.Clear();
        obj.Image = null;
        obj.GraphDraw(canvas, 0, 0, 400, 300);
        obj.Image = img;
        obj.Opacity = 0;
        obj.GraphDraw(canvas, 0, 0, 400, 300);
        Assert.Equal(0, canvas.Count);
    }

    [Fact]
    public void Two_objects_sharing_an_image_submit_it_once()
    {
        PixelImage img = TestPatterns.ColorBars(8, 4);
        var canvas = new GraphicsCanvas();
        new GImage(img).GraphDraw(canvas, 0, 0, 80, 40);
        new GImage(img, GraphicsImageFilter.Linear).GraphDraw(canvas, 0, 0, 80, 40);
        Assert.Equal(1, canvas.Images.Length);
        Assert.All(canvas.Primitives.ToArray(), p => Assert.Equal(0, p.ImageIndex));
    }

    // ── TVDemo windows with a pixel-graphics driver ──────────────────────

    private static DemoTestScope PixelScope(out ImageRecordingDriver driver)
    {
        ImageRecordingDriver? created = null;
        var scope = new DemoTestScope(wrapDriver: inner => created = new ImageRecordingDriver(inner));
        driver = created!;
        return scope;
    }

    private static T[] Windows<T>(ScriptedDemoApp app) where T : TView =>
        ScriptedDemoApp.Descendants(app.DeskTop!).OfType<T>().ToArray();

    private static PixelRect ViewPixels(TView view)
    {
        TPoint g = view.MakeGlobal(new TPoint(0, 0));
        return new PixelRect(g.x * CellW, g.y * CellH, view.size.x * CellW, view.size.y * CellH);
    }

    [Fact]
    public void System_menu_lists_both_windows_next_to_styx()
    {
        using var scope = new DemoTestScope();
        var system = TVDemoMenuNavigationTests.Items(scope.App.MenuBar!.Menu!).First();
        var names = TVDemoMenuNavigationTests.Items(system.SubMenu).Select(i => (i.Name, i.Command)).ToArray();
        int styx = Array.FindIndex(names, n => n.Command == ShowcaseCmd.Styx);
        Assert.Equal(("~G~raphics gallery", ShowcaseCmd.GraphicsGallery), names[styx + 1]);
        Assert.Equal(("Li~v~e chart", ShowcaseCmd.LiveChart), names[styx + 2]);
    }

    [Fact]
    public void Gallery_opens_with_shapes_and_the_bitmap_shown_nearest_and_linear()
    {
        using var scope = PixelScope(out var driver);
        scope.App.RunCommand(ShowcaseCmd.GraphicsGallery);
        TGraphicsGalleryDemo win = Assert.Single(Windows<TGraphicsGalleryDemo>(scope.App));
        Assert.Same(scope.App.DeskTop, win.owner);
        Assert.Equal("Graphics gallery", win.title);
        GView view = win.View;
        Assert.True(view.HasGraphicsLayer);
        Assert.Equal(1, driver.LayerCount);
        Assert.Equal(win.size.y - 3, view.size.y); // frame, frame and the caption row
        Assert.Contains("nearest | linear", ScriptedDemoApp.Text(win));

        GraphicsPrimitive[] prims = driver.Primitives(view);
        PixelImage[] images = driver.Images(view);
        Assert.Contains(prims, p => p.Kind == GraphicsPrimitiveKind.Line);
        Assert.Contains(prims, p => p.Kind == GraphicsPrimitiveKind.FillRect && (p.Rgba & 0xFF) == 0x80); // translucent
        GraphicsPrimitive[] imagePrims = prims.Where(p => p.Kind == GraphicsPrimitiveKind.Image).ToArray();
        Assert.Equal(4, imagePrims.Length);
        Assert.Equal(3, images.Length); // the colour bars are shared by the nearest and the linear panel
        Assert.All(imagePrims, p => Assert.InRange(p.ImageIndex, 0, images.Length - 1));

        GraphicsPrimitive nearest = imagePrims[0], linear = imagePrims[1];
        Assert.Equal(GraphicsImageFilter.Nearest, nearest.Filter);
        Assert.Equal(GraphicsImageFilter.Linear, linear.Filter);
        Assert.Equal(nearest.ImageIndex, linear.ImageIndex);
        Assert.Same(win.Nearest.Image, images[nearest.ImageIndex]);
        Assert.Equal(nearest.X2 - nearest.X1, linear.X2 - linear.X1); // same size, side by side
        Assert.True(linear.X1 >= nearest.X2);
        Assert.InRange((nearest.X2 - nearest.X1) - 2 * (nearest.Y2 - nearest.Y1), -2, 2); // 2:1 aspect kept

        // Shapes expanded inside the shape bounds; the clipping panel draws past the view on purpose.
        PixelRect px = ViewPixels(view);
        Assert.Contains(prims, p => p.Kind == GraphicsPrimitiveKind.FillRect && p.X2 > px.X + px.W);
        Assert.All(prims.Where(p => p.Kind == GraphicsPrimitiveKind.FillRect),
            p => Assert.True(p.X1 >= 0 && p.X2 <= GraphicsCanvas.ShapeLimit && p.Y1 >= 0));
    }

    [Fact]
    public void Gallery_closes_cleanly_reopens_and_follows_a_resize()
    {
        using var scope = PixelScope(out var driver);
        scope.App.RunCommand(ShowcaseCmd.GraphicsGallery);
        TGraphicsGalleryDemo win = Assert.Single(Windows<TGraphicsGalleryDemo>(scope.App));
        GraphicsPrimitive before = driver.Primitives(win.View).First(p => p.Kind == GraphicsPrimitiveKind.Image);

        win.Locate(new TRect(0, 0, 78, 22));
        GraphicsPrimitive after = driver.Primitives(win.View).First(p => p.Kind == GraphicsPrimitiveKind.Image);
        Assert.True(after.X2 - after.X1 > before.X2 - before.X1, "the image follows the larger view");
        Assert.Equal([ViewPixels(win.View)], driver.Clip(win.View));

        GView view = win.View;
        win.Close();
        Assert.False(view.HasGraphicsLayer);
        Assert.False(view.IsAnimating);
        Assert.Equal(0, driver.LayerCount);

        scope.App.RunCommand(ShowcaseCmd.GraphicsGallery);
        scope.App.RunCommand(ShowcaseCmd.GraphicsGallery);
        Assert.Equal(2, Windows<TGraphicsGalleryDemo>(scope.App).Length);
        Assert.Equal(2, driver.LayerCount);
        foreach (var w in Windows<TGraphicsGalleryDemo>(scope.App)) w.Close();
        Assert.Equal(0, driver.LayerCount);
    }

    [Fact]
    public void Live_chart_animates_plots_and_bars_and_stops_on_close()
    {
        using var scope = PixelScope(out var driver);
        scope.App.RunCommand(ShowcaseCmd.LiveChart);
        TLiveChartDemo win = Assert.Single(Windows<TLiveChartDemo>(scope.App));
        Assert.Equal("Live chart", win.title);
        GView view = win.View;
        Assert.True(view.HasGraphicsLayer);
        Assert.True(view.IsAnimating);

        GraphicsPrimitive[] first = driver.Primitives(view);
        Assert.Equal(2 * (TLiveChart.Samples - 1), first.Count(p => p.Kind == GraphicsPrimitiveKind.Line
            && p.Rgba is TLiveChart.SineColor or TLiveChart.NoiseColor));
        Assert.Equal(TLiveChart.Bars, first.Count(p => p.Rgba == TLiveChart.BarColor));
        PixelRect px = ViewPixels(view);
        Assert.All(first, p => Assert.True(p.X1 >= px.X && p.Y1 >= px.Y && Math.Max(p.X1, p.X2) <= px.X + px.W
            && Math.Max(p.Y1, p.Y2) <= px.Y + px.H, $"{p} leaves the view"));

        // One animation step (what a GView tick does): the plots scroll and the layer is resubmitted.
        long ticks = win.Chart.Ticks;
        int submissions = driver.Submissions;
        win.Chart.GraphProd();
        view.DrawView();
        Assert.Equal(ticks + 1, win.Chart.Ticks);
        Assert.True(driver.Submissions > submissions);
        Assert.NotEqual(first, driver.Primitives(view));

        win.Close();
        Assert.False(view.HasGraphicsLayer);
        Assert.False(view.IsAnimating);
        Assert.Equal(0, driver.LayerCount);
        TEventQueue.ClearPosted();

        scope.App.RunCommand(ShowcaseCmd.LiveChart);
        Assert.Equal(1, driver.LayerCount);
        Assert.Single(Windows<TLiveChartDemo>(scope.App)).Close();
        TEventQueue.ClearPosted();
    }

    [Fact]
    public void Live_chart_samples_stay_in_range_over_many_ticks()
    {
        var chart = new TLiveChart(new Random(3));
        var canvas = new GraphicsCanvas();
        for (int i = 0; i < 2000; i++) chart.GraphProd();
        chart.GraphDraw(canvas, 0, 0, 400, 200);
        Assert.All(canvas.Primitives.ToArray(), p => Assert.True(p.Y1 >= 0 && p.Y2 <= 200 && p.X1 >= 0 && p.X2 <= 400));
        Assert.All(Enumerable.Range(0, TLiveChart.Bars), i => Assert.InRange(chart.BarHeight(i), 0.0, 1.0));

        // Too small a view draws nothing instead of degenerate shapes.
        canvas.Clear();
        chart.GraphDraw(canvas, 0, 0, 8, 8);
        Assert.Equal(0, canvas.Count);
    }

    // ── Drivers without pixel graphics ───────────────────────────────────

    [Theory]
    [InlineData(ShowcaseCmd.GraphicsGallery, "The graphics gallery draws pixels")]
    [InlineData(ShowcaseCmd.LiveChart, "The live chart draws pixels")]
    public void Text_drivers_explain_instead_of_opening(ushort command, string message)
    {
        using var scope = new DemoTestScope();
        scope.App.DismissResult(message);
        scope.App.RunCommand(command);
        Assert.Empty(Windows<TGraphicsGalleryDemo>(scope.App));
        Assert.Empty(Windows<TLiveChartDemo>(scope.App));
    }

    // A NullDriver decorator that adds the pixel capability and records each layer's clip, primitives and images.
    private sealed class ImageRecordingDriver : IDriver, IPixelGraphicsDriver
    {
        private readonly IDriver _inner;
        private readonly Dictionary<object, (PixelRect[] Clip, GraphicsPrimitive[] Prims, PixelImage[] Images)> _layers =
            new(ReferenceEqualityComparer.Instance);
        public ImageRecordingDriver(IDriver inner) => _inner = inner;

        public int Submissions { get; private set; }
        public int LayerCount => _layers.Count;
        public PixelRect[] Clip(object owner) => _layers[owner].Clip;
        public GraphicsPrimitive[] Primitives(object owner) => _layers[owner].Prims;
        public PixelImage[] Images(object owner) => _layers[owner].Images;

        public int CellPixelWidth => CellW;
        public int CellPixelHeight => CellH;
        public void SetGraphicsLayer(object owner, ReadOnlySpan<PixelRect> clip, ReadOnlySpan<GraphicsPrimitive> primitives,
            ReadOnlySpan<PixelImage> images = default)
        {
            ArgumentNullException.ThrowIfNull(owner);
            Submissions++;
            _layers[owner] = (clip.ToArray(), primitives.ToArray(), images.ToArray());
        }
        public void RemoveGraphicsLayer(object owner)
        {
            ArgumentNullException.ThrowIfNull(owner);
            _layers.Remove(owner);
        }

        public void Initialize() => _inner.Initialize();
        public void Suspend() => _inner.Suspend();
        public void Resume() => _inner.Resume();
        public void Shutdown() => _inner.Shutdown();
        public ushort GetCols() => _inner.GetCols();
        public ushort GetRows() => _inner.GetRows();
        public TDisplay.SM GetScreenMode() => _inner.GetScreenMode();
        public void SetScreenMode(TDisplay.SM mode) => _inner.SetScreenMode(mode);
        public ScreenBuffer AllocateScreenBuffer() => _inner.AllocateScreenBuffer();
        public void ClearScreen(ushort cols, ushort rows) => _inner.ClearScreen(cols, rows);
        public ushort GetCursorType() => _inner.GetCursorType();
        public void SetCursorType(ushort cursorType) => _inner.SetCursorType(cursorType);
        public void SetCaretPosition(int x, int y) => _inner.SetCaretPosition(x, y);
        public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf) => _inner.WriteBuf(x, y, w, h, buf);
        public void MakeBeep() => _inner.MakeBeep();
        public void PumpMessages() => _inner.PumpMessages();
        public bool ReadKeyEvent(out TEvent ev) => _inner.ReadKeyEvent(out ev);
        public bool SupportsMouse => _inner.SupportsMouse;
        public bool SupportsTrueColor => _inner.SupportsTrueColor;
        public bool SupportsGraphics => true;
        public KeyboardCapabilities KeyboardCapabilities => _inner.KeyboardCapabilities;
    }
}
