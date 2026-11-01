using System.Numerics;
using TSharpVision.Drivers.SDL.Gpu;
using Xunit.Abstractions;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// GPU-free tests of the ShadowMask effect: the pattern (periodicity, anchoring, density pitch),
/// neutrality, strength bounds, uniforms, the CPU reference and a readability check.
/// </summary>
public sealed class GpuShadowMaskTests
{
    private readonly ITestOutputHelper _output;
    public GpuShadowMaskTests(ITestOutputHelper output) => _output = output;

    // ─── Registration and uniforms ──────────────────────────────────────────

    [Fact]
    public void ShadowMask_is_registered_with_its_shader()
    {
        Assert.True(GpuPostEffectCatalog.TryGet(GpuDisplayEffect.ShadowMask, out GpuPostEffectSpec spec));
        Assert.Equal("TSharpVision.Drivers.SDL.Gpu.Shaders.ShadowMaskFrag.spv", spec.FragmentShaderResource);
        Assert.Equal(1u, spec.NumSamplers);
        Assert.Contains(spec.FragmentShaderResource, typeof(GpuShadowMask).Assembly.GetManifestResourceNames());
        GpuGridLayout layout = GpuGridLayout.Compute(800, 600, 80, 25, 9, 18);
        Assert.True(TerminalGpuPipeline.SelectPostEffect(GpuDisplayEffect.ShadowMask, layout, out GpuPostEffectSpec selected));
        Assert.Same(spec, selected);
    }

    [Theory]
    [InlineData(640, 480, 1f, 1)]
    [InlineData(720, 450, 1.5f, 2)]
    [InlineData(1440, 900, 2f, 2)]
    [InlineData(2160, 1350, 3f, 3)]
    public void BuildUniforms_carries_viewport_density_strength_and_pitch(int w, int h, float density, int pitch)
    {
        GpuPostUniforms u = GpuShadowMask.BuildUniforms(new GpuPostFrame(w, h, density));
        Assert.Equal(new Vector4(w, h, 1f / w, 1f / h), u.V0);
        Assert.Equal(new Vector4(density, GpuShadowMask.Strength, pitch, 0f), u.V1);
        Assert.Equal(Vector4.Zero, u.V2);
        Assert.Equal(Vector4.Zero, u.V3);
        Assert.Equal(u, GpuShadowMask.BuildUniforms(new GpuPostFrame(w, h, density))); // pure

        GpuPostUniforms baseU = GpuPostUniforms.Base(new GpuPostFrame(w, h, density));
        Assert.Equal(baseU.V0, u.V0);
        Assert.Equal(baseU.V1.X, u.V1.X);
    }

    [Fact]
    public void BuildUniforms_allocates_nothing()
    {
        var frame = new GpuPostFrame(800, 600, 2f);
        GpuShadowMask.BuildUniforms(frame);
        long before = GC.GetAllocatedBytesForCurrentThread();
        float sum = 0;
        for (int i = 0; i < 1000; i++) sum += GpuShadowMask.BuildUniforms(frame).V1.Z;
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(2000f, sum);
    }

    // ─── Pattern ────────────────────────────────────────────────────────────

    [Fact]
    public void Pattern_is_the_documented_staggered_3x2_tile()
    {
        // row 0: 1 1 D, row 1: 1 D 1
        bool[,] tile = { { false, false, true }, { false, true, false } };
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
                Assert.Equal(tile[y, x], GpuShadowMask.IsDark(x, y, 1));
        Assert.Equal(GpuShadowMask.DarkFactor, GpuShadowMask.Factor(2, 0, 1));
        Assert.Equal(1f, GpuShadowMask.Factor(0, 0, 1));
        // No two dark pixels are vertically adjacent: the gap is staggered.
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 12; x++)
                Assert.False(GpuShadowMask.IsDark(x, y, 1) && GpuShadowMask.IsDark(x, y + 1, 1));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    [InlineData(3f)]
    public void Pattern_is_periodic_with_a_density_scaled_pitch(float density)
    {
        int p = GpuShadowMask.PitchPixels(density);
        int px = GpuShadowMask.PeriodX * p, py = GpuShadowMask.PeriodY * p;
        int dark = 0;
        for (int y = 0; y < 4 * py; y++)
            for (int x = 0; x < 4 * px; x++)
            {
                bool d = GpuShadowMask.IsDark(x, y, p);
                Assert.Equal(d, GpuShadowMask.IsDark(x + px, y, p));
                Assert.Equal(d, GpuShadowMask.IsDark(x, y + py, p));
                // Constant within each pitch x pitch mask cell.
                Assert.Equal(d, GpuShadowMask.IsDark(x - x % p, y - y % p, p));
                if (d) dark++;
            }
        // Exactly 2 of 6 mask cells dark: mean factor as documented.
        Assert.Equal(16 * GpuShadowMask.DarkPerTile * p * p, dark);
        // Period is minimal: a shift by one mask cell changes the pattern.
        bool differs = false;
        for (int y = 0; y < py && !differs; y++)
            for (int x = 0; x < px && !differs; x++)
                differs = GpuShadowMask.IsDark(x, y, p) != GpuShadowMask.IsDark(x + p, y, p);
        Assert.True(differs);
    }

    [Theory]
    [InlineData(1f, 1)]
    [InlineData(1.25f, 1)]
    [InlineData(1.5f, 2)]
    [InlineData(2f, 2)]
    [InlineData(2.5f, 3)]
    [InlineData(3f, 3)]
    [InlineData(0f, 1)]
    [InlineData(-2f, 1)]
    [InlineData(float.NaN, 1)]
    [InlineData(float.PositiveInfinity, 1)]
    [InlineData(20f, GpuShadowMask.MaxPitch)]
    public void Pitch_is_max_1_round_density(float density, int expected) =>
        Assert.Equal(expected, GpuShadowMask.PitchPixels(density));

    [Fact]
    public void Pattern_is_anchored_to_device_pixels_not_cells()
    {
        // The reference takes no cell size: the same device pixel gets the same factor whatever
        // grid produced the image. Two flat images of different size (different cols x cell)
        // agree on their common area.
        var a = Flat(27, 36, 0x80, 0x80, 0x80);
        var b = Flat(40, 20, 0x80, 0x80, 0x80);
        var ma = new byte[a.Length];
        var mb = new byte[b.Length];
        GpuShadowMask.ApplyReference(a, ma, 27, 36, 1f);
        GpuShadowMask.ApplyReference(b, mb, 40, 20, 1f);
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 27; x++)
                Assert.Equal(ma[(y * 27 + x) * 4], mb[(y * 40 + x) * 4]);
    }

    [Fact]
    public void Strength_is_restrained()
    {
        Assert.InRange(GpuShadowMask.DarkFactor, 0.75f, 0.85f);
        Assert.InRange(1f - GpuShadowMask.MeanFactor, 0.01f, 0.15f);
        Assert.Equal(1f - GpuShadowMask.Strength / 3f, GpuShadowMask.MeanFactor, 6);
        // Measured over a flat white field: mean loss matches MeanFactor, nothing brighter than the source.
        foreach (float density in new[] { 1f, 2f, 3f })
        {
            int p = GpuShadowMask.PitchPixels(density), w = 6 * p * 3, h = 4 * p * 2;
            var src = Flat(w, h, 255, 255, 255);
            var dst = new byte[src.Length];
            GpuShadowMask.ApplyReference(src, dst, w, h, density);
            double mean = 0;
            for (int i = 0; i < dst.Length; i += 4)
            {
                Assert.True(dst[i] <= 255 && dst[i] >= 204, $"{dst[i]}");
                mean += dst[i];
            }
            mean /= w * h * 255.0;
            Assert.Equal(GpuShadowMask.MeanFactor, mean, 2);
        }
    }

    // ─── Neutrality ─────────────────────────────────────────────────────────

    [Fact]
    public void Mask_is_neutral_rgb_ratios_are_preserved()
    {
        var rng = new Random(12);
        const int w = 30, h = 14;
        var src = new byte[w * h * 4];
        rng.NextBytes(src);
        var dst = new byte[src.Length];
        GpuShadowMask.ApplyReference(src, dst, w, h, 1f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                float f = GpuShadowMask.Factor(x, y, 1);
                for (int c = 0; c < 3; c++)
                    Assert.True(Math.Abs(dst[o + c] - src[o + c] * f) <= 0.5f + 1e-3f,
                        $"({x},{y}) channel {c}: {src[o + c]} * {f} -> {dst[o + c]}");
                Assert.Equal(src[o + 3], dst[o + 3]);
                if (f == 1f) Assert.Equal(src.AsSpan(o, 4).ToArray(), dst.AsSpan(o, 4).ToArray());
            }
    }

    [Fact]
    public void Mono_palettes_keep_their_single_hue()
    {
        foreach (GpuPhysicalPalette palette in GpuPhysicalPalette.All.Where(p => p.Id != "Default"))
        {
            uint phosphor = palette.Argb[15];
            float hue0 = Hue(phosphor);
            foreach (uint argb in palette.Argb.Skip(1))
            {
                byte r = (byte)(argb >> 16), g = (byte)(argb >> 8), b = (byte)argb;
                byte mr = GpuShadowMask.Scale(r, GpuShadowMask.DarkFactor);
                byte mg = GpuShadowMask.Scale(g, GpuShadowMask.DarkFactor);
                byte mb = GpuShadowMask.Scale(b, GpuShadowMask.DarkFactor);
                // Zero channels stay zero, equal channels stay equal, and the hue is unchanged.
                Assert.Equal(r == 0, mr == 0);
                Assert.Equal(b == 0, mb == 0);
                if (r == b) Assert.Equal(mr, mb);
                if (r == g) Assert.Equal(mr, mg);
                uint masked = 0xFF000000u | (uint)(mr << 16) | (uint)(mg << 8) | mb;
                // The mask keeps each entry's own hue; the palette stays within a few degrees of its phosphor
                // (Amber's cream highlights sit about 4° from #FFB000's ramp, TV12g).
                Assert.True(Math.Abs(Hue(masked) - Hue(argb)) < 1f, $"{palette.Id} {argb:X8} hue {Hue(argb)} -> {Hue(masked)}");
                Assert.True(Math.Abs(Hue(masked) - hue0) < 6f, $"{palette.Id} {argb:X8} hue {Hue(masked)} vs phosphor {hue0}");
            }
        }
    }

    // ─── CPU reference ──────────────────────────────────────────────────────

    [Fact]
    public void Reference_applies_the_factor_per_pixel_and_keeps_edges_sharp()
    {
        // Left half white, right half black: masked pixels are exactly the mask factor of white or
        // black; the edge stays a one-pixel step (no resampling or blur).
        const int w = 18, h = 8;
        var src = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            byte v = (i % w) < 9 ? (byte)255 : (byte)0;
            src[i * 4] = src[i * 4 + 1] = src[i * 4 + 2] = v;
            src[i * 4 + 3] = 255;
        }
        var dst = new byte[src.Length];
        GpuShadowMask.ApplyReference(src, dst, w, h, 1f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte expected = x < 9 ? (GpuShadowMask.IsDark(x, y, 1) ? (byte)204 : (byte)255) : (byte)0;
                Assert.Equal(expected, dst[(y * w + x) * 4]);
            }
        Assert.Throws<ArgumentException>(() => GpuShadowMask.ApplyReference(src.AsSpan(0, 8), dst, w, h, 1f));
        GpuShadowMask.ApplyReference(ReadOnlySpan<byte>.Empty, Span<byte>.Empty, 0, 0, 1f);
    }

    // ─── Readability ────────────────────────────────────────────────────────

    [Fact]
    public void Text_contrast_is_essentially_unchanged()
    {
        GpuPhysicalPalette def = GpuPhysicalPalette.Default;
        var pairs = new List<(string Name, uint Fg, uint Bg)>
        {
            ("Default 0x1F white-on-blue",     def.Argb[15], def.Argb[1]),
            ("Default 0x70 black-on-lightgray", def.Argb[0], def.Argb[7]),
            ("Default 0x1E yellow-on-blue",     def.Argb[14], def.Argb[1]),
            ("Default 0x30 black-on-cyan",      def.Argb[0], def.Argb[3]),
        };
        foreach (GpuPhysicalPalette p in GpuPhysicalPalette.All.Where(p => p.Id != "Default"))
        {
            pairs.Add(($"{p.Id} 15 vs 0 (brightest vs black)", p.Argb[15], p.Argb[0]));
            pairs.Add(($"{p.Id} 15 vs 1 (brightest vs darkest lit)", p.Argb[15], p.Argb[1]));
            pairs.Add(($"{p.Id} 0x1F text", p.Argb[15], p.Argb[1]));
            pairs.Add(($"{p.Id} 0x70 text", p.Argb[0], p.Argb[7]));
        }

        // Two measures, both over the mean-of-period (linear) luminance:
        //  - the intrinsic luminance ratio Lfg/Lbg: a common multiplicative factor leaves it
        //    unchanged (up to 8-bit rounding);
        //  - the WCAG ratio (L1 + 0.05)/(L2 + 0.05): its 0.05 flare term makes it fall a little
        //    because the absolute luminance falls (~13% linear, 6.7% encoded).
        _output.WriteLine($"mean factor encoded={GpuShadowMask.MeanFactor:F4} white linear={MaskedMeanLum(0xFFFFFFFF):F4}");
        foreach (var (name, fg, bg) in pairs)
        {
            double lf = Lum(fg), lb = Lum(bg), mf = MaskedMeanLum(fg), mb = MaskedMeanLum(bg);
            double before = Wcag(lf, lb), after = Wcag(mf, mb);
            string intrinsic = "n/a (black)";
            if (Math.Min(lf, lb) > 0)
            {
                double rb = Math.Max(lf, lb) / Math.Min(lf, lb), ra = Math.Max(mf, mb) / Math.Min(mf, mb);
                intrinsic = $"{rb,6:F2} -> {ra,6:F2} ({(ra / rb - 1) * 100:+0.0;-0.0}%)";
                Assert.True(Math.Abs(ra / rb - 1) <= 0.03, $"{name}: intrinsic ratio {rb:F3} -> {ra:F3}");
            }
            _output.WriteLine($"{name,-44} WCAG {before,6:F2}:1 -> {after,6:F2}:1 ({(after / before - 1) * 100:+0.0;-0.0}%)  Lfg/Lbg {intrinsic}");
            Assert.True(after >= 0.85 * before, $"{name}: contrast {before:F2} -> {after:F2}");
            Assert.True(after <= before + 1e-9, $"{name}: contrast increased");
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static byte[] Flat(int w, int h, byte r, byte g, byte b)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { px[i * 4] = r; px[i * 4 + 1] = g; px[i * 4 + 2] = b; px[i * 4 + 3] = 255; }
        return px;
    }

    private static double Lin(byte c)
    {
        double s = c / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    private static double Lum(byte r, byte g, byte b) => 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
    private static double Lum(uint argb) => Lum((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    // Mean linear luminance over one mask period (6 pixels, 2 dark).
    private static double MaskedMeanLum(uint argb)
    {
        byte r = (byte)(argb >> 16), g = (byte)(argb >> 8), b = (byte)argb;
        double sum = 0;
        for (int y = 0; y < GpuShadowMask.PeriodY; y++)
            for (int x = 0; x < GpuShadowMask.PeriodX; x++)
            {
                float f = GpuShadowMask.Factor(x, y, 1);
                sum += Lum(GpuShadowMask.Scale(r, f), GpuShadowMask.Scale(g, f), GpuShadowMask.Scale(b, f));
            }
        return sum / (GpuShadowMask.PeriodX * GpuShadowMask.PeriodY);
    }

    private static double Wcag(double l1, double l2) => (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);

    private static float Hue(uint argb)
    {
        float r = (byte)(argb >> 16) / 255f, g = (byte)(argb >> 8) / 255f, b = (byte)argb / 255f;
        float max = MathF.Max(r, MathF.Max(g, b)), min = MathF.Min(r, MathF.Min(g, b)), d = max - min;
        if (d <= 0) return 0;
        float h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h *= 60;
        return h < 0 ? h + 360 : h;
    }
}
