// True HiDPI / Retina rendering (MACOS-2D): the logical-vs-drawable scale model shared by SDLDriver and
// SDLGpuDriver, the density-aware glyph inputs, and the drivers' grid, mouse and density-change
// handling. Everything is headless: real platform scales (macOS 2×, Wayland 1.5×, Windows/X11 1×) are
// fed in as synthetic SdlDisplayScale values, so no window, renderer or font is opened.
using TSharpVision.Constants;
using TSharpVision.Drivers.SDL.Gpu;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;
using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Drivers.SDL.Tests;

public sealed class SdlHiDpiScaleModelTests
{
    // TVDemo's measured configuration on macOS: Menlo 20pt -> 12x24 logical cells, 120x37 grid.
    private const int CellW = 12;
    private const int CellH = 24;
    private const int Cols  = 120;
    private const int Rows  = 37;

    // ── 1. 1× window: logical == drawable ─────────────────────────────────────

    [Fact]
    public void OneX_window_has_identical_logical_and_drawable_sizes()
    {
        var scale   = SdlDisplayScale.Create(1440, 888, 1440, 888, 1f);
        var metrics = SdlCellMetrics.ForDensity(CellW, CellH, scale.Density);

        Assert.Equal(1f, scale.ScaleX);
        Assert.Equal(1f, scale.ScaleY);
        Assert.True(metrics.IsUnscaled);
        Assert.Equal((CellW, CellH), (metrics.PixelWidth, metrics.PixelHeight));
        Assert.Equal((1440, 888), metrics.WindowSizeForGrid(Cols, Rows, scale));
        Assert.Equal(((ushort)Cols, (ushort)Rows), metrics.GridForDrawable(1440, 888));
    }

    // ── 2. 2× window: drawable doubles, grid stays ────────────────────────────

    [Fact]
    public void TwoX_window_doubles_the_drawable_but_keeps_the_grid()
    {
        // The owner's MacBook: NSWindow backingScaleFactor 2, window 1440x888 points.
        var scale   = SdlDisplayScale.Create(1440, 888, 2880, 1776, 2f);
        var metrics = SdlCellMetrics.ForDensity(CellW, CellH, scale.Density);

        Assert.Equal((24, 48), (metrics.PixelWidth, metrics.PixelHeight));
        Assert.Equal(40f, metrics.RasterPointSize(20));                  // fontSize 20 keeps its visual size
        Assert.Equal((1440, 888), metrics.WindowSizeForGrid(Cols, Rows, scale)); // window points unchanged
        Assert.Equal(((ushort)Cols, (ushort)Rows), metrics.GridForDrawable(2880, 1776));
    }

    // ── 3. Non-integer densities ──────────────────────────────────────────────

    [Theory]
    [InlineData(1.25f, 15, 30)]
    [InlineData(1.5f,  18, 36)]
    [InlineData(1.75f, 21, 42)]
    [InlineData(2.0f,  24, 48)]
    [InlineData(3.0f,  36, 72)]
    public void Non_integer_densities_keep_one_integral_cell_size_and_the_grid(float density, int pixelW, int pixelH)
    {
        var metrics = SdlCellMetrics.ForDensity(CellW, CellH, density);
        Assert.Equal((pixelW, pixelH), (metrics.PixelWidth, metrics.PixelHeight));

        // The window is sized from the drawable cells; whatever drawable the platform rounds that
        // window to, the grid comes back unchanged.
        var probe = SdlDisplayScale.Create(1000, 1000, (int)MathF.Round(1000 * density), (int)MathF.Round(1000 * density), density);
        var (winW, winH) = metrics.WindowSizeForGrid(Cols, Rows, probe);
        int drawW = (int)MathF.Round(winW * density);
        int drawH = (int)MathF.Round(winH * density);
        Assert.Equal(((ushort)Cols, (ushort)Rows), metrics.GridForDrawable(drawW, drawH));

        // No drift: every cell is the same size, so the last cell ends exactly at cols * cell.
        Assert.True(drawW - Cols * metrics.PixelWidth is >= 0 and < 2 * 12);
        Assert.True(drawH - Rows * metrics.PixelHeight is >= 0 and < 2 * 24);
    }

    [Fact]
    public void Odd_logical_cells_round_to_one_cell_size_for_the_whole_grid()
    {
        // 13 * 1.25 = 16.25 -> 16. The window is sized for 16-pixel cells, so the grid holds.
        var metrics = SdlCellMetrics.ForDensity(13, 25, 1.25f);
        Assert.Equal((16, 31), (metrics.PixelWidth, metrics.PixelHeight));

        var scale = SdlDisplayScale.Create(1000, 1000, 1250, 1250, 1.25f);
        var (winW, winH) = metrics.WindowSizeForGrid(Cols, Rows, scale);
        Assert.Equal((1536, 918), (winW, winH));   // ceil(120*16/1.25), ceil(37*31/1.25)
        Assert.Equal(((ushort)Cols, (ushort)Rows),
            metrics.GridForDrawable((int)MathF.Round(winW * 1.25f), (int)MathF.Round(winH * 1.25f)));
    }

    // ── 7. Mouse coordinate mapping ───────────────────────────────────────────

    [Theory]
    [InlineData(0f,      0f,     0,   0)]
    [InlineData(11.9f,   23.9f,  0,   0)]
    [InlineData(12f,     24f,    1,   1)]
    [InlineData(90.5f,   12f,    7,   0)]   // "File" in TVDemo's menu bar
    [InlineData(1439.5f, 887.5f, 119, 36)]
    public void Window_coordinates_map_to_the_same_cell_at_1x_and_2x(float x, float y, int col, int row)
    {
        foreach (float density in new[] { 1f, 2f })
        {
            var scale   = SdlDisplayScale.Create(1440, 888, (int)(1440 * density), (int)(888 * density), density);
            var metrics = SdlCellMetrics.ForDensity(CellW, CellH, density);
            var (px, py) = scale.WindowToPixel(x, y);
            TPoint cell = SdlMouseTranslator.PixelToCell(px, py, metrics.PixelWidth, metrics.PixelHeight);
            Assert.Equal((col, row), (cell.x, cell.y));
        }
    }

    [Fact]
    public void Mouse_mapping_at_a_fractional_density_follows_the_drawn_cells()
    {
        // 1.5×: drawable cell 18x36. Window x 11.99 -> pixel 17 (cell 0); x 12 -> pixel 18 (cell 1).
        var scale   = SdlDisplayScale.Create(1440, 888, 2160, 1332, 1.5f);
        var metrics = SdlCellMetrics.ForDensity(CellW, CellH, 1.5f);
        var (p0, _) = scale.WindowToPixel(11.99f, 0);
        var (p1, _) = scale.WindowToPixel(12f, 0);
        Assert.Equal(0, SdlMouseTranslator.PixelToCell(p0, 0, metrics.PixelWidth, metrics.PixelHeight).x);
        Assert.Equal(1, SdlMouseTranslator.PixelToCell(p1, 0, metrics.PixelWidth, metrics.PixelHeight).x);
    }

    // ── 13. Platform baselines and density normalisation ──────────────────────

    [Theory]
    [InlineData(1440, 888, 1440, 888, 1f,    1f)]  // Windows (DPI-aware: window == pixels), X11, standard macOS
    [InlineData(1440, 888, 2880, 1776, 2f,   2f)]  // macOS Retina, Wayland 2×
    [InlineData(1440, 888, 2160, 1332, 1.5f, 1.5f)] // Wayland fractional 1.5×
    [InlineData(1440, 888, 2880, 1776, 0f,   2f)]  // density unavailable: measured ratio
    [InlineData(0,    0,   0,    0,    0f,   1f)]  // nothing known: 1×
    [InlineData(1440, 888, 2880, 1776, 1.9999f, 2f)] // float noise snaps to the integer
    public void Density_is_taken_from_SDL_then_the_measured_ratio_then_1(
        int winW, int winH, int pixW, int pixH, float reported, float expected)
    {
        Assert.Equal(expected, SdlDisplayScale.Create(winW, winH, pixW, pixH, reported).Density);
    }

    [Theory]
    [InlineData(float.NaN, 1f)]
    [InlineData(-2f, 1f)]
    [InlineData(float.PositiveInfinity, 1f)]
    [InlineData(1.25f, 1.25f)]
    [InlineData(1.004f, 1f)]
    [InlineData(100f, 8f)]
    public void NormalizeDensity_rejects_garbage_and_keeps_real_fractions(float input, float expected) =>
        Assert.Equal(expected, SdlDisplayScale.NormalizeDensity(input));

    // ── 12. 1× behaviour unchanged ────────────────────────────────────────────

    [Theory]
    [InlineData(11, 20)]
    [InlineData(12, 24)]
    [InlineData(14, 26)]
    public void OneX_metrics_match_the_previous_cell_model(int w, int h)
    {
        var metrics = SdlCellMetrics.ForDensity(w, h, 1f);
        Assert.Equal((w, h), (metrics.PixelWidth, metrics.PixelHeight));
        Assert.Equal(SdlCellMetrics.LogicalUnderlineHeight, metrics.UnderlineHeight);
        Assert.Equal(1, metrics.ShadingDotSize);
        // Same answer as the pre-HiDPI grid helper.
        Assert.Equal(SDLDriver.CalculateGridSize(1920, 1080, w, h), metrics.GridForDrawable(1920, 1080));
    }

    [Fact]
    public void Underline_cursor_scales_with_density()
    {
        Assert.Equal(2, SdlCellMetrics.ForDensity(CellW, CellH, 1f).UnderlineHeight);
        Assert.Equal(3, SdlCellMetrics.ForDensity(CellW, CellH, 1.5f).UnderlineHeight);
        Assert.Equal(4, SdlCellMetrics.ForDensity(CellW, CellH, 2f).UnderlineHeight);
    }
}

public sealed class SdlHiDpiGlyphTests
{
    // ── 10. Glyph atlas scales appropriately ──────────────────────────────────

    [Theory]
    [InlineData(12, 24, 1024)]  // 1×: the atlas it always had
    [InlineData(11, 20, 1024)]
    [InlineData(24, 48, 2048)]  // 2×
    [InlineData(18, 36, 2048)]  // 1.5×
    [InlineData(36, 72, 4096)]  // 3×
    [InlineData(200, 400, 4096)] // capped
    public void Atlas_grows_with_the_drawable_cell(int cellW, int cellH, int expected) =>
        Assert.Equal(expected, TerminalAtlas.ChooseSize(cellW, cellH));

    [Fact]
    public void Atlas_keeps_its_1x_slot_count_at_2x()
    {
        int OneX(int w, int h) { int s = TerminalAtlas.ChooseSize(w, h); return (s / w) * (s / h); }
        Assert.Equal(OneX(12, 24), OneX(24, 48));
        Assert.True(OneX(24, 48) >= TerminalAtlas.TargetSlots);
    }

    [Fact]
    public void Generated_glyphs_are_drawable_cell_sized_at_2x()
    {
        var metrics = SdlCellMetrics.ForDensity(12, 24, 2f);
        var gen = new Cp437GlyphGenerator(metrics.PixelWidth, metrics.PixelHeight,
            Cp437GlyphGeneratorOptions.Default with { ShadingDotSize = metrics.ShadingDotSize });
        foreach (char ch in "░▒▓═║╔╗╚╝█▀▄")
        {
            AlphaBitmap b = gen.Generate(ch, gen.PhaseForCell(3, 5));
            Assert.Equal((24, 48), (b.Width, b.Height));
        }
    }

    // ── Desktop dither keeps its 1× dot pitch, rendered natively ───────────────

    [Theory]
    [InlineData('░')]
    [InlineData('▒')]
    [InlineData('▓')]
    public void Shading_at_2x_is_the_1x_pattern_with_two_pixel_dots(char ch)
    {
        var oneX = new Cp437GlyphGenerator(12, 24);
        var twoX = new Cp437GlyphGenerator(24, 48, Cp437GlyphGeneratorOptions.Default with { ShadingDotSize = 2 });

        for (int col = 0; col < 3; col++)
        for (int row = 0; row < 3; row++)
        {
            AlphaBitmap small = oneX.Generate(ch, oneX.PhaseForCell(col, row));
            AlphaBitmap large = twoX.Generate(ch, twoX.PhaseForCell(col, row));
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 24; x++)
                    Assert.Equal(small[x / 2, y / 2], large[x, y]);
        }
    }

    [Fact]
    public void Shading_dot_falls_back_to_one_pixel_when_the_cell_is_not_divisible()
    {
        // 25x50 cells cannot hold whole 2-pixel dots; a 1-pixel dither keeps the pattern continuous.
        var gen = new Cp437GlyphGenerator(25, 50, Cp437GlyphGeneratorOptions.Default with { ShadingDotSize = 2 });
        Assert.Equal(1, gen.ShadingDotSize);
        Assert.Equal(GlyphPhase.ForCell(1, 1, 25, 50), gen.PhaseForCell(1, 1));
        Assert.Equal(1, SdlCellMetrics.ForDensity(13, 25, 1.9f).ShadingDotSize); // 25x48: 25 is odd
    }

    [Fact]
    public void Shading_pattern_is_continuous_across_cells_at_2x()
    {
        var gen = new Cp437GlyphGenerator(24, 48, Cp437GlyphGeneratorOptions.Default with { ShadingDotSize = 2 });
        // Absolute pixel (X, Y) must carry ink iff dot (X/2, Y/2) is a ░ dot (both coordinates even).
        for (int col = 0; col < 4; col++)
        for (int row = 0; row < 2; row++)
        {
            AlphaBitmap b = gen.Generate('░', gen.PhaseForCell(col, row));
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 24; x++)
                {
                    int dotX = (col * 24 + x) / 2, dotY = (row * 48 + y) / 2;
                    bool ink = dotX % 2 == 0 && dotY % 2 == 0;
                    Assert.Equal(ink ? 255 : 0, (int)b[x, y]);
                }
        }
    }

    [Fact]
    public void Default_generator_phase_is_the_classic_pixel_phase()
    {
        var gen = new Cp437GlyphGenerator(11, 20);
        Assert.Equal(1, gen.ShadingDotSize);
        for (int c = 0; c < 5; c++)
            Assert.Equal(GlyphPhase.ForCell(c, c, 11, 20), gen.PhaseForCell(c, c));
    }

    // ── 11. Box / frame geometry stays aligned at 2× ──────────────────────────

    [Fact]
    public void Double_frame_rails_join_across_cells_at_2x()
    {
        var gen = new Cp437GlyphGenerator(24, 48);
        AlphaBitmap corner = gen.Generate('╔');
        AlphaBitmap top    = gen.Generate('═');
        AlphaBitmap side   = gen.Generate('║');

        // ═ rails run edge to edge on the same rows the ╔ arm leaves on.
        for (int y = 0; y < 48; y++)
        {
            bool rail = top[0, y] == 255;
            Assert.Equal(rail, top[23, y] == 255);
            Assert.Equal(rail, corner[23, y] == 255);
        }
        // ║ rails run top to bottom on the same columns the ╔ arm leaves on.
        for (int x = 0; x < 24; x++)
        {
            bool rail = side[x, 0] == 255;
            Assert.Equal(rail, side[x, 47] == 255);
            Assert.Equal(rail, corner[x, 47] == 255);
        }
        // Strokes are two drawable pixels — one logical pixel — thick, not a blurred single pixel.
        Assert.Equal(2, gen.Geometry.DoubleStrokeThickness);
        Assert.Equal(2, gen.Geometry.SingleStrokeThickness);
    }

    [Fact]
    public void Gpu_layout_at_2x_is_pixel_exact_for_the_whole_grid()
    {
        var metrics = SdlCellMetrics.ForDensity(12, 24, 2f);
        GpuGridLayout layout = GpuGridLayout.Compute(2880, 1776, 120, 37, metrics.PixelWidth, metrics.PixelHeight);
        Assert.True(layout.IsPixelExact);
        Assert.False(layout.IsClipped);
        Assert.Equal((0, 0), (layout.RemainderX, layout.RemainderY));
        Assert.Equal((2880, 1776), (layout.ViewportWidth, layout.ViewportHeight));
    }
}

/// <summary>Driver-level HiDPI behaviour, driven with synthetic scales (no window).</summary>
[Collection("SDL initialization")]
public sealed class SdlHiDpiDriverTests
{
    private static readonly SdlDisplayScale Retina = SdlDisplayScale.Create(1440, 888, 2880, 1776, 2f);
    private static readonly SdlDisplayScale Standard = SdlDisplayScale.Create(1440, 888, 1440, 888, 1f);

    private static SDLDriver Sdl(float density, SdlDisplayScale scale)
    {
        var d = new SDLDriver();
        d.AttachForTest(SdlCellMetrics.ForDensity(12, 24, density), scale, 120, 37);
        return d;
    }

    private static SDLGpuDriver Gpu(float density, SdlDisplayScale scale)
    {
        var d = new SDLGpuDriver();
        d.AttachForTest(SdlCellMetrics.ForDensity(12, 24, density), scale, 120, 37);
        return d;
    }

    // ── 4/5/6. Output and grid sizing on both drivers ─────────────────────────

    [Fact]
    public void Both_drivers_keep_the_120x37_grid_on_a_2x_drawable()
    {
        var sdl = Sdl(2f, Retina);
        sdl.ApplyDisplayScale(Retina);
        Assert.Equal((120, 37), (sdl.GetCols(), sdl.GetRows()));
        Assert.Equal((24, 48), (sdl.CellMetrics.PixelWidth, sdl.CellMetrics.PixelHeight));

        var gpu = Gpu(2f, Retina);
        gpu.ApplyDisplayScale(Retina);
        Assert.Equal((120, 37), (gpu.GetCols(), gpu.GetRows()));
        Assert.Equal((24, 48), (gpu.CellMetrics.PixelWidth, gpu.CellMetrics.PixelHeight));
    }

    // ── 8. Resize under HiDPI ─────────────────────────────────────────────────

    [Fact]
    public void Resize_under_HiDPI_derives_the_grid_from_drawable_pixels()
    {
        // Window resized to 1207x683 points on the Retina display: drawable 2414x1366.
        var resized = SdlDisplayScale.Create(1207, 683, 2414, 1366, 2f);

        var sdl = Sdl(2f, Retina);
        sdl.ApplyDisplayScale(resized);
        Assert.Equal((100, 28), (sdl.GetCols(), sdl.GetRows()));
        Assert.Equal((100, 28), (TScreen.ScreenWidth, TScreen.ScreenHeight));

        var gpu = Gpu(2f, Retina);
        gpu.ApplyDisplayScale(resized);
        Assert.Equal((100, 28), (gpu.GetCols(), gpu.GetRows()));
    }

    // ── 7. Mouse mapping through the drivers ──────────────────────────────────

    [Fact]
    public void Both_drivers_hit_the_same_cells_at_1x_and_2x()
    {
        foreach (var (density, scale) in new[] { (1f, Standard), (2f, Retina) })
        {
            var sdl = Sdl(density, scale);
            var gpu = Gpu(density, scale);
            foreach (var (x, y, col, row) in new[] { (90f, 40f, 7, 1), (719.5f, 443.9f, 59, 18), (1439.9f, 887.9f, 119, 36) })
            {
                Assert.Equal((col, row), (sdl.WindowToCellForTest(x, y).x, sdl.WindowToCellForTest(x, y).y));
                Assert.Equal((col, row), (gpu.WindowToCellForTest(x, y).x, gpu.WindowToCellForTest(x, y).y));
            }
        }
    }

    // ── 9. Density change triggers a resource refresh ─────────────────────────

    [Fact]
    public void SDLDriver_density_change_rebuilds_metrics_and_redraws_without_changing_the_grid()
    {
        // Window dragged from the Retina panel to a 1× monitor: same points, half the pixels.
        var sdl = Sdl(2f, Retina);
        sdl.ClearDirtyForTest();
        sdl.ApplyDisplayScale(Standard);

        Assert.Equal(1f, sdl.CellMetrics.Density);
        Assert.Equal((12, 24), (sdl.CellMetrics.PixelWidth, sdl.CellMetrics.PixelHeight));
        Assert.Equal((120, 37), (sdl.GetCols(), sdl.GetRows()));
        Assert.True(sdl.IsDirty);
        Assert.True(sdl.PendingReasons.HasFlag(SDLDriver.SdlDirtyReason.WindowEvent));
    }

    [Fact]
    public void An_unchanged_scale_does_not_force_a_redraw()
    {
        // No continuous rendering: a repeated size/scale event with nothing new stays idle.
        var sdl = Sdl(2f, Retina);
        sdl.ClearDirtyForTest();
        sdl.ApplyDisplayScale(Retina);
        Assert.False(sdl.IsDirty);
    }

    [Fact]
    public void GpuRenderer_density_request_is_deferred_to_the_render_thread()
    {
        var renderer = new SDLGpuRenderer(IntPtr.Zero, new SDLGpuOptions());
        Set(renderer, "_logicalCellWidth", 12);
        Set(renderer, "_logicalCellHeight", 24);
        Set(renderer, "_activeDensity", 2f);
        Set(renderer, "_requestedDensity", 2f);

        SdlCellMetrics next = renderer.RequestPixelDensity(1f);

        Assert.Equal((12, 24), (next.PixelWidth, next.PixelHeight));
        Assert.Equal(1f, Get<float>(renderer, "_requestedDensity"));
        Assert.Equal(2f, Get<float>(renderer, "_activeDensity")); // nothing rasterised on the caller's thread
        Assert.Equal(0, renderer.DensityRebuilds);
        renderer.Dispose(TimeSpan.Zero);
    }

    [Fact]
    public void GpuDriver_density_change_requests_the_new_density_and_keeps_the_grid()
    {
        var gpu = Gpu(2f, Retina);
        gpu.ApplyDisplayScale(Standard);
        Assert.Equal(1f, gpu.CellMetrics.Density);
        Assert.Equal((120, 37), (gpu.GetCols(), gpu.GetRows()));

        gpu.ApplyDisplayScale(Retina);
        Assert.Equal(2f, gpu.CellMetrics.Density);
        Assert.Equal((120, 37), (gpu.GetCols(), gpu.GetRows()));
    }

    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(target, value);

    private static T Get<T>(object target, string field) =>
        (T)target.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(target)!;
}
