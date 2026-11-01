using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using TSharpVision.Drivers.SDL.Gpu;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// GPU-free tests of the post-process infrastructure (catalog, uniform layout, the post decision)
/// and of the Glow math through its CPU reference, plus the CPU-fallback behaviour of post effects.
/// </summary>
public sealed class GpuPostProcessTests
{
    // ─── Catalog ────────────────────────────────────────────────────────────

    [Fact]
    public void Catalog_has_Glow_and_nothing_for_Default_or_Scanlines()
    {
        Assert.True(GpuPostEffectCatalog.TryGet(GpuDisplayEffect.Glow, out GpuPostEffectSpec glow));
        Assert.Equal("TSharpVision.Drivers.SDL.Gpu.Shaders.GlowFrag.spv", glow.FragmentShaderResource);
        Assert.Equal(1u, glow.NumSamplers);
        Assert.False(GpuPostEffectCatalog.TryGet(GpuDisplayEffect.Default, out _));
        Assert.False(GpuPostEffectCatalog.TryGet(GpuDisplayEffect.Scanlines, out _));
    }

    [Fact]
    public void Every_catalog_entry_is_a_post_effect_with_an_embedded_shader()
    {
        Assembly assembly = typeof(GpuPostProcess).Assembly;
        string[] resources = assembly.GetManifestResourceNames();
        Assert.Contains(GpuPostProcess.VertexShaderResource, resources);
        foreach (GpuDisplayEffect effect in Enum.GetValues<GpuDisplayEffect>())
        {
            if (!GpuPostEffectCatalog.TryGet(effect, out GpuPostEffectSpec spec)) continue;
            Assert.True(GpuDisplayEffects.IsPostProcess(effect), $"{effect} is in the catalog but not a post effect");
            Assert.Contains(spec.FragmentShaderResource, resources);
            Assert.InRange(spec.NumSamplers, 0u, 1u);
            GpuPostUniforms u = spec.BuildUniforms(new GpuPostFrame(640, 480, 1f));
            Assert.Equal(new Vector4(640, 480, 1f / 640, 1f / 480), u.V0);
            Assert.Equal(1f, u.V1.X);
        }
    }

    [Fact]
    public void Glow_is_selected_only_for_a_renderable_viewport_and_never_for_Default_or_Scanlines()
    {
        GpuGridLayout layout = GpuGridLayout.Compute(800, 600, 80, 25, 9, 18);
        GpuGridLayout empty  = GpuGridLayout.Compute(0, 0, 80, 25, 9, 18);
        Assert.False(TerminalGpuPipeline.SelectPostEffect(GpuDisplayEffect.Default, layout, out _));
        Assert.False(TerminalGpuPipeline.SelectPostEffect(GpuDisplayEffect.Scanlines, layout, out _));
        Assert.True(TerminalGpuPipeline.SelectPostEffect(GpuDisplayEffect.Glow, layout, out GpuPostEffectSpec spec));
        Assert.Same(spec, GetSpec(GpuDisplayEffect.Glow));
        Assert.False(TerminalGpuPipeline.SelectPostEffect(GpuDisplayEffect.Glow, empty, out _));
        // Effects not (yet) registered render Default without error.
        foreach (var effect in new[] { GpuDisplayEffect.Curvature, GpuDisplayEffect.ShadowMask })
            Assert.Equal(GpuPostEffectCatalog.TryGet(effect, out _),
                TerminalGpuPipeline.SelectPostEffect(effect, layout, out _));
    }

    private static GpuPostEffectSpec GetSpec(GpuDisplayEffect e)
    {
        Assert.True(GpuPostEffectCatalog.TryGet(e, out GpuPostEffectSpec s));
        return s;
    }

    // ─── Uniform layout ─────────────────────────────────────────────────────

    [Fact]
    public void Uniforms_are_64_bytes_of_four_float4()
    {
        Assert.Equal(64, Marshal.SizeOf<GpuPostUniforms>());
        Assert.Equal(64, Unsafe.SizeOf());
        Assert.Equal(GpuPostUniforms.SizeInBytes, Marshal.SizeOf<GpuPostUniforms>());
        Assert.Equal(0,  (int)Marshal.OffsetOf<GpuPostUniforms>(nameof(GpuPostUniforms.V0)));
        Assert.Equal(16, (int)Marshal.OffsetOf<GpuPostUniforms>(nameof(GpuPostUniforms.V1)));
        Assert.Equal(32, (int)Marshal.OffsetOf<GpuPostUniforms>(nameof(GpuPostUniforms.V2)));
        Assert.Equal(48, (int)Marshal.OffsetOf<GpuPostUniforms>(nameof(GpuPostUniforms.V3)));
        Assert.Equal(4, typeof(GpuPostUniforms).GetFields().Count(f => !f.IsStatic && f.FieldType == typeof(Vector4)));
    }

    private static class Unsafe
    {
        public static unsafe int SizeOf() => sizeof(GpuPostUniforms);
    }

    [Fact]
    public void Base_uniforms_carry_viewport_and_density()
    {
        GpuPostUniforms u = GpuPostUniforms.Base(new GpuPostFrame(1280, 720, 2f));
        Assert.Equal(new Vector4(1280, 720, 1f / 1280, 1f / 720), u.V0);
        Assert.Equal(new Vector4(2f, 0, 0, 0), u.V1);
        Assert.Equal(Vector4.Zero, u.V2);
        Assert.Equal(Vector4.Zero, u.V3);
        // A degenerate viewport never divides by zero.
        GpuPostUniforms z = GpuPostUniforms.Base(new GpuPostFrame(0, 0, 1f));
        Assert.True(float.IsFinite(z.V0.Z) && float.IsFinite(z.V0.W));
    }

    [Fact]
    public void Glow_uniforms_pack_strength_radius_and_thresholds()
    {
        GpuPostUniforms u = GpuGlow.BuildUniforms(new GpuPostFrame(100, 50, 1f));
        Assert.Equal(new Vector4(100, 50, 0.01f, 0.02f), u.V0);
        Assert.Equal(new Vector4(1f, GpuGlow.Strength, 2.5f, GpuGlow.ThresholdLow), u.V1);
        Assert.Equal(new Vector4(GpuGlow.ThresholdHigh, 0, 0, 0), u.V2);
        Assert.Equal(Vector4.Zero, u.V3);
        Assert.Equal(5f, GpuGlow.BuildUniforms(new GpuPostFrame(100, 50, 2f)).V1.Z);
    }

    // ─── Glow parameters ────────────────────────────────────────────────────

    [Theory]
    [InlineData(1f, 2.5f)]
    [InlineData(2f, 5f)]
    [InlineData(1.5f, 3.75f)]
    [InlineData(3f, 7.5f)]
    [InlineData(4f, 8f)]          // capped
    [InlineData(0.25f, 1f)]       // floor
    [InlineData(0f, 2.5f)]        // invalid → density 1
    [InlineData(float.NaN, 2.5f)]
    public void Glow_radius_scales_with_density_within_bounds(float density, float radius) =>
        Assert.Equal(radius, GpuGlow.RadiusPixels(density));

    [Fact]
    public void Glow_constants_are_restrained()
    {
        Assert.InRange(GpuGlow.Strength, 0.2f, 0.8f);
        Assert.True(GpuGlow.ThresholdLow < GpuGlow.ThresholdHigh);
        Assert.InRange(GpuGlow.TapCount, 25, 40);
        Assert.Equal(8f, GpuGlow.MaxRadius);
        Assert.Equal(3, GpuGlow.ReachPixels(1f));
        Assert.Equal(5, GpuGlow.ReachPixels(2f));
        Assert.Equal(8, GpuGlow.ReachPixels(10f));
    }

    [Fact]
    public void Glow_taps_are_a_normalised_symmetric_unit_disc()
    {
        float sum = 0, sx = 0, sy = 0;
        for (int i = 0; i < GpuGlow.TapCount; i++)
        {
            var (x, y, w) = GpuGlow.Tap(i);
            Assert.True(x * x + y * y <= 1.0001f);
            Assert.True(w > 0);
            sum += w; sx += x * w; sy += y * w;
        }
        Assert.InRange(sum, 0.9999f, 1.0f);   // ≤ 1: a uniform field gains nothing
        Assert.InRange(sx, -1e-5f, 1e-5f);
        Assert.InRange(sy, -1e-5f, 1e-5f);
        Assert.Equal((0f, 0f), (GpuGlow.Tap(0).X, GpuGlow.Tap(0).Y));
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuGlow.Tap(GpuGlow.TapCount));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    [InlineData(3f)]
    public void Glow_tap_rounding_has_no_ties_at_common_densities(float density)
    {
        // Shader and CPU both compute floor(t * r + 0.5); keeping away from .5 ties means float
        // evaluation order cannot move a tap by one pixel between them.
        float r = GpuGlow.RadiusPixels(density);
        for (int i = 0; i < GpuGlow.TapCount; i++)
        {
            var (x, y, _) = GpuGlow.Tap(i);
            foreach (float v in new[] { x * r + 0.5f, y * r + 0.5f })
            {
                float frac = v - MathF.Floor(v);
                if (MathF.Abs(v - MathF.Round(v)) < 1e-6f) continue; // exact integer (e.g. 0.5 * 0 + 0.5 is not)
                Assert.True(frac > 1e-3f && frac < 1 - 1e-3f, $"tap {i} at density {density}: {v}");
            }
        }
    }

    // ─── Glow CPU reference ─────────────────────────────────────────────────

    private static byte[] Field(int w, int h, (byte R, byte G, byte B) c)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { px[i * 4] = c.R; px[i * 4 + 1] = c.G; px[i * 4 + 2] = c.B; px[i * 4 + 3] = 255; }
        return px;
    }

    private static void Set(byte[] px, int w, int x, int y, (byte R, byte G, byte B) c)
    {
        int o = (y * w + x) * 4;
        px[o] = c.R; px[o + 1] = c.G; px[o + 2] = c.B; px[o + 3] = 255;
    }

    private static byte[] Glow(byte[] src, int w, int h, float density = 1f)
    {
        var dst = new byte[src.Length];
        GpuGlow.ApplyReference(src, dst, w, h, density);
        return dst;
    }

    private static readonly (byte, byte, byte) White = (255, 255, 255), Black = (0, 0, 0), Blue = (0, 0, 0xAA),
        LightGray = (0xAA, 0xAA, 0xAA), Cyan = (0x00, 0xAA, 0xAA), Yellow = (0xFF, 0xFF, 0x55), Red = (0xAA, 0, 0);

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(3f)]
    public void A_single_bright_pixel_gets_a_halo_limited_to_the_reach(float density)
    {
        const int w = 41, h = 41, cx = 20, cy = 20;
        byte[] src = Field(w, h, Black);
        Set(src, w, cx, cy, White);
        byte[] dst = Glow(src, w, h, density);
        int reach = GpuGlow.ReachPixels(density);

        int lit = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                int d = Math.Max(Math.Abs(x - cx), Math.Abs(y - cy));
                if (d == 0) { Assert.Equal(src.AsSpan(o, 4).ToArray(), dst.AsSpan(o, 4).ToArray()); continue; }
                if (d > reach) Assert.True(src.AsSpan(o, 4).SequenceEqual(dst.AsSpan(o, 4)), $"({x},{y}) beyond reach changed");
                else if (dst[o] > 0) lit++;
                Assert.Equal(dst[o], dst[o + 1]); // a white source glows white (neutral)
            }
        Assert.True(lit >= 8, $"only {lit} halo pixels");
        // The halo falls off: the brightest halo pixel is near the source, the outer ring is dimmer.
        // (With a fixed tap count a single pixel's halo is sparse at large radii; strokes fill it.)
        int Max(Func<int, bool> ring)
        {
            int m = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (ring(Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)))) m = Math.Max(m, dst[(y * w + x) * 4]);
            return m;
        }
        Assert.True(Max(d => d >= 1 && d <= 2) > Max(d => d == reach));
    }

    [Fact]
    public void Dark_areas_far_from_glyphs_are_unchanged()
    {
        const int w = 60, h = 30;
        byte[] src = Field(w, h, Blue);
        for (int y = 5; y < 15; y++) for (int x = 3; x < 6; x++) Set(src, w, x, y, White); // a stroke
        byte[] dst = Glow(src, w, h);
        int reach = GpuGlow.ReachPixels(1f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool near = x >= 3 - reach && x < 6 + reach && y >= 5 - reach && y < 15 + reach;
                int o = (y * w + x) * 4;
                if (!near) Assert.True(src.AsSpan(o, 4).SequenceEqual(dst.AsSpan(o, 4)), $"({x},{y}) changed");
            }
        // Next to the stroke the blue background visibly lightens (a real halo, not a no-op).
        int n = (10 * w + 6) * 4;
        Assert.True(dst[n] - src[n] >= 20, $"halo next to the stroke only +{dst[n] - src[n]}");
    }

    public static TheoryData<byte, byte, byte> UniformColours => new()
    {
        { 0, 0, 0 }, { 255, 255, 255 }, { 0xAA, 0xAA, 0xAA }, { 0x55, 0xFF, 0xFF },
        { 0xFF, 0xFF, 0x55 }, { 0, 0, 0xAA }, { 0x33, 0xFF, 0x33 }, { 0xFF, 0xB0, 0x00 },
    };

    [Theory]
    [MemberData(nameof(UniformColours))]
    public void A_uniform_field_is_not_tinted(byte r, byte g, byte b)
    {
        byte[] src = Field(16, 12, (r, g, b));
        Assert.Equal(src, Glow(src, 16, 12));
    }

    [Fact]
    public void Glyph_cores_keep_their_brightness_and_nothing_gets_darker()
    {
        const int w = 48, h = 32;
        byte[] src = Field(w, h, Blue);
        var rng = new Random(12);
        (byte, byte, byte)[] inks = { White, Yellow, LightGray, Cyan, Red, Black };
        for (int i = 0; i < 300; i++) Set(src, w, rng.Next(w), rng.Next(h), inks[rng.Next(inks.Length)]);
        byte[] dst = Glow(src, w, h);
        for (int i = 0; i < src.Length; i++) Assert.True(dst[i] >= src[i], $"byte {i} darker: {src[i]} -> {dst[i]}");
        // A white pixel stays white; a bounded gain everywhere.
        for (int i = 0; i < src.Length; i++)
            Assert.True(dst[i] - src[i] <= MathF.Ceiling(GpuGlow.Strength * 255), $"byte {i} gained {dst[i] - src[i]}");
    }

    [Fact]
    public void Dim_ink_below_the_threshold_does_not_glow()
    {
        const int w = 20, h = 20;
        byte[] src = Field(w, h, Black);
        for (int y = 5; y < 15; y++) Set(src, w, 10, y, Red);   // luma 0.2
        Assert.Equal(src, Glow(src, w, h));
    }

    [Fact]
    public void Edges_clamp_like_the_sampler()
    {
        const int w = 8, h = 6;
        byte[] src = Field(w, h, Black);
        Set(src, w, 0, 0, White);
        byte[] dst = Glow(src, w, h);
        Assert.True(dst[4] > 0);                       // (1,0) lit
        Assert.Equal(src.AsSpan(0, 4).ToArray(), dst.AsSpan(0, 4).ToArray());
        Assert.Throws<ArgumentException>(() => GpuGlow.ApplyReference(src, new byte[4], w, h, 1f));
    }

    // ─── CPU-compositing fallback ───────────────────────────────────────────

    private const uint Cols = 4, Rows = 3;

    internal static ScreenBuffer Screen()
    {
        var screen = new ScreenBuffer((ushort)Cols, (ushort)Rows);
        byte[] attrs = { 0x1F, 0x4E, 0x6F, 0x70, 0x1E, 0x0F, 0x5A, 0x89, 0x2F, 0xF0, 0x3C, 0x71 };
        for (int i = 0; i < attrs.Length; i++)
            screen.SetChar((uint)(i % Cols), (uint)(i / Cols),
                new TScreenChar(i % 3 == 0 ? '█' : i % 3 == 1 ? '▒' : ' ', (TColorAttr)attrs[i]));
        return screen;
    }

    internal static SDLGpuRenderer HeadlessRenderer()
    {
        var renderer = new SDLGpuRenderer(IntPtr.Zero, new SDLGpuOptions());
        typeof(SDLGpuRenderer).GetField("_generatedGlyphs", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(renderer, new CellGlyphBitmapCache(renderer.CellWidth, renderer.CellHeight));
        return renderer;
    }

    [Theory]
    [InlineData("Default", "Default")]
    [InlineData("Scanlines", "Scanlines")]
    [InlineData("Glow", "Default")]
    [InlineData("Curvature", "Default")]
    [InlineData("ShadowMask", "Default")]
    public void Cpu_fallback_effect_mapping(string requested, string drawn)
    {
        Assert.True(GpuDisplayEffects.TryParse(requested, out GpuDisplayEffect r));
        Assert.Equal(drawn, GpuDisplayEffects.Id(SDLGpuRenderer.CpuFallbackEffect(r)));
    }

    [Fact]
    public void A_new_renderer_presents_Default_and_has_no_post_failure()
    {
        using var renderer = HeadlessRenderer();
        Assert.Equal(GpuDisplayEffect.Default, renderer.PresentedEffect);
        Assert.Null(renderer.PostProcessUnavailableReason);
        renderer.DisplayEffect = GpuDisplayEffect.Glow;
        Assert.Equal(GpuDisplayEffect.Default, renderer.PresentedEffect); // nothing presented yet
    }

    [Fact]
    public void RenderFrame_signature_is_unchanged_and_pipeline_reports_what_it_drew()
    {
        MethodInfo m = typeof(TerminalGpuPipeline).GetMethod("RenderFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(13, m.GetParameters().Length);
        Assert.Equal(typeof((TimeSpan, TimeSpan)), m.ReturnType);
        PropertyInfo drawn = typeof(TerminalGpuPipeline).GetProperty("LastDrawnEffect", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(typeof(GpuDisplayEffect), drawn.PropertyType);
        // Presented effect is published to other threads.
        FieldInfo presented = typeof(SDLGpuRenderer).GetField("_presentedEffect", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Contains(typeof(System.Runtime.CompilerServices.IsVolatile), presented.GetRequiredCustomModifiers());
    }
}

/// <summary>Captures stderr, so it runs in the non-parallel collection (other tests swap Console.Error too).</summary>
[Collection("SDL initialization")]
public sealed class GpuPostProcessFallbackLogTests
{
    private const uint Cols = 4, Rows = 3;

    private static ScreenBuffer Screen() => GpuPostProcessTests.Screen();

    internal static SDLGpuRenderer HeadlessRenderer() => GpuPostProcessTests.HeadlessRenderer();

    [Fact]
    public void Cpu_fallback_renders_post_effects_as_Default_and_logs_once()
    {
        ScreenBuffer screen = Screen();
        using var renderer = HeadlessRenderer();
        byte[] plain = renderer.CompositeForTesting(screen, Cols, Rows).ToArray();

        var previous = Console.Error;
        using var stderr = new StringWriter();
        Console.SetError(stderr);
        try
        {
            renderer.DisplayEffect = GpuDisplayEffect.Glow;
            Assert.Equal(plain, renderer.CompositeForTesting(screen, Cols, Rows));
            Assert.Equal(plain, renderer.CompositeForTesting(screen, Cols, Rows));
            renderer.DisplayEffect = GpuDisplayEffect.Curvature;
            Assert.Equal(plain, renderer.CompositeForTesting(screen, Cols, Rows));
        }
        finally { Console.SetError(previous); }

        string log = stderr.ToString();
        const string line = "display effect needs the shader pipeline";
        int idx = log.IndexOf(line, StringComparison.Ordinal);
        Assert.True(idx >= 0, log);
        Assert.Contains("Glow", log);
        Assert.Equal(-1, log.IndexOf(line, idx + 1, StringComparison.Ordinal));

        // Scanlines is still applied by the fallback.
        renderer.DisplayEffect = GpuDisplayEffect.Scanlines;
        Assert.NotEqual(plain, renderer.CompositeForTesting(screen, Cols, Rows));
    }
}
