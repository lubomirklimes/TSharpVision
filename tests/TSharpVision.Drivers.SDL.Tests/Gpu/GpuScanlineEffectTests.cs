using System.Reflection;
using TSharpVision.Drivers.SDL.Gpu;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// GPU-free tests of the Scanlines display effect wiring: the strip vertex build the shader pass
/// uploads, the source-compatible <c>RenderFrame</c> signature, the shared blend state, and the
/// CPU-compositing fallback (presentation and capture compositing).
/// </summary>
public sealed class GpuScanlineEffectTests
{
    // ─── Strip vertex build (shader pass) ───────────────────────────────────

    public static TheoryData<int, float> Viewports => new()
    {
        { 400, 1f }, { 401, 1f }, { 7, 1f }, { 1, 1f }, { 2, 1f },
        { 800, 2f }, { 801, 2f }, { 803, 2f }, { 5, 2f },
        { 600, 1.5f }, { 601, 1.5f }, { 3, 1.5f },
        { 1080, 2.5f }, { 37, 3f },
    };

    [Theory]
    [MemberData(nameof(Viewports))]
    public void Strip_vertices_cover_exactly_the_geometry_strips(int height, float density)
    {
        var strips = GpuScanlineGeometry.ComputeStrips(1000, height, density);
        int capacity = TerminalGpuPipeline.ScanlineVertexCapacity(height, density);
        Assert.Equal(strips.Count * 6, capacity);

        var vertices = new TerminalGpuPipeline.BgVertex[capacity + 6];
        vertices[capacity].X = 123f; // sentinel past the end
        int written = TerminalGpuPipeline.BuildScanlineVertices(vertices, height, density);
        Assert.Equal(capacity, written);
        Assert.Equal(123f, vertices[capacity].X);

        GpuNdcRect[] ndc = GpuScanlineGeometry.ToNdc(strips, height);
        for (int i = 0; i < strips.Count; i++)
        {
            ReadOnlySpan<TerminalGpuPipeline.BgVertex> q = vertices.AsSpan(i * 6, 6);
            // Same triangle order and winding as the cell quads: TL, TR, BL / TR, BR, BL.
            Assert.Equal((-1f, ndc[i].Y0), (q[0].X, q[0].Y));
            Assert.Equal(( 1f, ndc[i].Y0), (q[1].X, q[1].Y));
            Assert.Equal((-1f, ndc[i].Y1), (q[2].X, q[2].Y));
            Assert.Equal(( 1f, ndc[i].Y0), (q[3].X, q[3].Y));
            Assert.Equal(( 1f, ndc[i].Y1), (q[4].X, q[4].Y));
            Assert.Equal((-1f, ndc[i].Y1), (q[5].X, q[5].Y));
            foreach (var v in q)
                Assert.Equal(((byte)0, (byte)0, (byte)0, GpuScanlineGeometry.StripAlpha), (v.R, v.G, v.B, v.A));

            // Back to device pixels through the viewport transform: the strip's exact rows.
            Assert.Equal(strips[i].Y, (1f - q[0].Y) / 2f * height, 3);
            Assert.Equal(strips[i].Y + strips[i].Height, (1f - q[2].Y) / 2f * height, 3);
        }
    }

    [Fact]
    public void Strip_vertices_for_known_viewports()
    {
        // density 1, odd height 5: rows 1 and 3.
        Assert.Equal(new[] { (1, 2), (3, 4) }, Rows(5, 1f));
        // density 2, height 7: rows 2-3 and 6 (last strip clipped to the viewport).
        Assert.Equal(new[] { (2, 4), (6, 7) }, Rows(7, 2f));
        // density 1.5 rounds to 2 (away from zero): same as density 2.
        Assert.Equal(Rows(9, 2f), Rows(9, 1.5f));
        Assert.Equal(new[] { (2, 4), (6, 8) }, Rows(9, 1.5f));
        // Too small for any strip.
        Assert.Empty(Rows(1, 1f));
        Assert.Empty(Rows(2, 2f));
        Assert.Empty(Rows(0, 1f));
    }

    private static (int, int)[] Rows(int height, float density)
    {
        var v = new TerminalGpuPipeline.BgVertex[TerminalGpuPipeline.ScanlineVertexCapacity(height, density)];
        int n = TerminalGpuPipeline.BuildScanlineVertices(v, height, density);
        var rows = new List<(int, int)>();
        for (int i = 0; i < n; i += 6)
            rows.Add(((int)MathF.Round((1f - v[i].Y) / 2f * height), (int)MathF.Round((1f - v[i + 2].Y) / 2f * height)));
        return rows.ToArray();
    }

    [Fact]
    public void Short_destination_is_rejected()
    {
        var v = new TerminalGpuPipeline.BgVertex[TerminalGpuPipeline.ScanlineVertexCapacity(10, 1f) - 1];
        Assert.Throws<ArgumentException>(() => TerminalGpuPipeline.BuildScanlineVertices(v, 10, 1f));
    }

    [Fact]
    public unsafe void Scanline_blend_state_is_the_glyph_source_over_state_with_blending_enabled()
    {
        SDL3.SDL.GPUColorTargetBlendState s = TerminalGpuPipeline.SourceOverBlendState();
        Assert.Equal(SDL3.SDL.GPUBlendFactor.SrcAlpha, s.SrcColorBlendFactor);
        Assert.Equal(SDL3.SDL.GPUBlendFactor.OneMinusSrcAlpha, s.DstColorBlendFactor);
        Assert.Equal(SDL3.SDL.GPUBlendOp.Add, s.ColorBlendOp);
        Assert.Equal(SDL3.SDL.GPUBlendFactor.One, s.SrcAlphaBlendFactor);
        Assert.Equal(SDL3.SDL.GPUBlendFactor.OneMinusSrcAlpha, s.DstAlphaBlendFactor);
        Assert.Equal(SDL3.SDL.GPUBlendOp.Add, s.AlphaBlendOp);
        Assert.Equal(1, ((byte*)&s)[25]); // _enableBlend
    }

    [Fact]
    public void RenderFrame_keeps_its_existing_call_shape_with_optional_effect_and_density()
    {
        MethodInfo m = typeof(TerminalGpuPipeline).GetMethod("RenderFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ParameterInfo[] p = m.GetParameters();
        Assert.Equal(13, p.Length);
        Assert.Equal(typeof(GpuPhysicalPalette), p[10].ParameterType);
        Assert.True(p[10].IsOptional);
        Assert.Equal(typeof(GpuDisplayEffect), p[11].ParameterType);
        Assert.True(p[11].IsOptional);
        Assert.Equal(GpuDisplayEffect.Default, (GpuDisplayEffect)p[11].DefaultValue!);
        Assert.Equal(typeof(float), p[12].ParameterType);
        Assert.Equal(1f, (float)p[12].DefaultValue!);
    }

    // ─── CPU-compositing fallback ───────────────────────────────────────────

    private const uint Cols = 4, Rows2 = 3;

    private static ScreenBuffer Screen()
    {
        var screen = new ScreenBuffer((ushort)Cols, (ushort)Rows2);
        byte[] attrs = { 0x17, 0x4E, 0x6F, 0x70, 0x1E, 0x06, 0x5A, 0x89, 0x2F, 0xF0, 0x3C, 0x71 };
        for (int i = 0; i < attrs.Length; i++)
            screen.SetChar((uint)(i % Cols), (uint)(i / Cols),
                new TScreenChar(i % 3 == 0 ? '█' : i % 3 == 1 ? '▒' : ' ', (TColorAttr)attrs[i]));
        return screen;
    }

    private static SDLGpuRenderer HeadlessRenderer(float? density = null)
    {
        var renderer = new SDLGpuRenderer(IntPtr.Zero, new SDLGpuOptions());
        typeof(SDLGpuRenderer).GetField("_generatedGlyphs", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(renderer, new CellGlyphBitmapCache(renderer.CellWidth, renderer.CellHeight));
        if (density is float d)
            // Only the effect reads the density; the composited cell size stays 8x16.
            typeof(SDLGpuRenderer).GetField("_metrics", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(renderer, SdlCellMetrics.ForDensity(8, 16, d));
        return renderer;
    }

    [Theory]
    [InlineData(-1, -1, (ushort)0)]
    [InlineData(1, 1, (ushort)100)]
    [InlineData(2, 2, (ushort)1)]
    public void Default_fallback_output_is_unchanged_by_toggling_the_effect(int cx, int cy, ushort type)
    {
        ScreenBuffer screen = Screen();
        using var fresh = HeadlessRenderer();
        fresh.SetCursor(cx, cy, type);
        byte[] baseline = fresh.CompositeForTesting(screen, Cols, Rows2).ToArray();

        using var toggled = HeadlessRenderer();
        toggled.SetCursor(cx, cy, type);
        toggled.DisplayEffect = GpuDisplayEffect.Scanlines;
        Assert.NotEqual(baseline, toggled.CompositeForTesting(screen, Cols, Rows2));
        toggled.DisplayEffect = GpuDisplayEffect.Default;
        Assert.Equal(baseline, toggled.CompositeForTesting(screen, Cols, Rows2));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(1.5f)]
    [InlineData(3f)]
    public void Scanlines_fallback_darkens_exactly_the_strip_rows(float density)
    {
        ScreenBuffer screen = Screen();
        using var renderer = HeadlessRenderer(density);
        renderer.SetCursor(1, 0, 100);
        byte[] plain = renderer.CompositeForTesting(screen, Cols, Rows2).ToArray();
        renderer.DisplayEffect = GpuDisplayEffect.Scanlines;
        byte[] lined = renderer.CompositeForTesting(screen, Cols, Rows2).ToArray();

        int width = (int)Cols * renderer.CellWidth, height = (int)Rows2 * renderer.CellHeight;
        var stripRows = new HashSet<int>();
        foreach (GpuScanlineStrip s in GpuScanlineGeometry.ComputeStrips(width, height, density))
            for (int y = s.Y; y < s.Y + s.Height; y++) stripRows.Add(y);
        int t = GpuScanlineGeometry.Thickness(density);
        Assert.DoesNotContain(0, stripRows);
        Assert.Contains(t, stripRows);

        int changedRows = 0;
        for (int y = 0; y < height; y++)
        {
            bool strip = stripRows.Contains(y);
            bool rowChanged = false;
            for (int x = 0; x < width; x++)
            {
                int o = (y * width + x) * 4;
                for (int c = 0; c < 3; c++)
                {
                    byte expected = strip ? (byte)((plain[o + c] * (255 - GpuScanlineGeometry.StripAlpha) + 127) / 255) : plain[o + c];
                    Assert.Equal(expected, lined[o + c]);
                    rowChanged |= lined[o + c] != plain[o + c];
                }
                Assert.Equal(plain[o + 3], lined[o + 3]);
            }
            if (rowChanged) changedRows++;
            if (!strip) Assert.True(plain.AsSpan(y * width * 4, width * 4).SequenceEqual(lined.AsSpan(y * width * 4, width * 4)));
        }
        Assert.Equal(stripRows.Count, changedRows); // every strip row has non-black pixels here
    }

    [Fact]
    public void Capture_compositing_uses_the_presented_frame_effect()
    {
        ScreenBuffer screen = Screen();
        using var renderer = HeadlessRenderer();
        renderer.DisplayEffect = GpuDisplayEffect.Scanlines;
        byte[] expected = renderer.CompositeForTesting(screen, Cols, Rows2).ToArray();
        renderer.DisplayEffect = GpuDisplayEffect.Default;
        byte[] plain = renderer.CompositeForTesting(screen, Cols, Rows2).ToArray();

        var pending = renderer.RequestCaptureForTesting(64, 64, TimeSpan.FromSeconds(1));
        renderer.Render(screen, 0, 0, Cols, Rows2);
        Assert.True(renderer.CaptureFramePinned(pending));
        object request = Field<object>(renderer, "_captureRequest");
        object pinned = request.GetType().GetField("Pinned")!.GetValue(request)!;
        MethodInfo composite = typeof(SDLGpuRenderer).GetMethod("CompositeCapturedFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo frameEffect = typeof(SDLGpuRenderer).GetField("_frameEffect", BindingFlags.Instance | BindingFlags.NonPublic)!;

        // The render loop stores the effect it read for the presented frame; capture follows it.
        frameEffect.SetValue(renderer, GpuDisplayEffect.Scanlines);
        composite.Invoke(renderer, new[] { pinned });
        Assert.Equal(expected, Field<byte[]>(renderer, "_cpuPixels"));

        frameEffect.SetValue(renderer, GpuDisplayEffect.Default);
        composite.Invoke(renderer, new[] { pinned });
        Assert.Equal(plain, Field<byte[]>(renderer, "_cpuPixels"));
        renderer.CancelDiagnosticCapture(pending);
    }

    [Fact]
    public void Toggling_the_effect_rebuilds_no_glyphs_or_fonts()
    {
        ScreenBuffer screen = Screen();
        using var renderer = HeadlessRenderer();
        renderer.CompositeForTesting(screen, Cols, Rows2);
        var cache = Field<System.Collections.IDictionary>(renderer, "_glyphCache");
        var generated = Field<object>(renderer, "_generatedGlyphs");
        var entries = cache.Keys.Cast<object>().Select(k => (k, cache[k])).ToArray();

        renderer.DisplayEffect = GpuDisplayEffect.Scanlines;
        renderer.CompositeForTesting(screen, Cols, Rows2);
        renderer.DisplayEffect = GpuDisplayEffect.Default;
        renderer.CompositeForTesting(screen, Cols, Rows2);

        Assert.Equal(0, renderer.DensityRebuilds);
        Assert.Same(generated, Field<object>(renderer, "_generatedGlyphs"));
        Assert.Equal(entries.Length, cache.Count);
        foreach (var (key, value) in entries) Assert.Same(value, cache[key]);
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
}
