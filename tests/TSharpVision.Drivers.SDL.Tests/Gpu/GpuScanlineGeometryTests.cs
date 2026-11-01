using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>GPU-free tests for <see cref="GpuScanlineGeometry"/>.</summary>
public sealed class GpuScanlineGeometryTests
{
    private static (int, int)[] Strips(int w, int h, float density) =>
        GpuScanlineGeometry.ComputeStrips(w, h, density).Select(s => (s.Y, s.Height)).ToArray();

    [Theory]
    [InlineData(1f, 1)]
    [InlineData(2f, 2)]
    [InlineData(1.5f, 2)]
    [InlineData(2.5f, 3)]
    [InlineData(1.49f, 1)]
    [InlineData(0.4f, 1)]
    [InlineData(0f, 1)]
    [InlineData(-2f, 1)]
    [InlineData(float.NaN, 1)]
    [InlineData(float.PositiveInfinity, 1)]
    [InlineData(3f, 3)]
    public void Thickness_is_rounded_density_at_least_one(float density, int expected) =>
        Assert.Equal(expected, GpuScanlineGeometry.Thickness(density));

    [Fact]
    public void Density_1_darkens_every_odd_row()
    {
        Assert.Equal(new[] { (1, 1), (3, 1) }, Strips(10, 4, 1f));
        Assert.Equal(new[] { (1, 1), (3, 1) }, Strips(10, 5, 1f));
        Assert.Equal(new[] { (1, 1), (3, 1), (5, 1) }, Strips(10, 6, 1f));
    }

    [Fact]
    public void Density_2_uses_two_pixel_bands_and_clips_odd_heights()
    {
        Assert.Equal(new[] { (2, 2), (6, 2) }, Strips(10, 8, 2f));
        Assert.Equal(new[] { (2, 2), (6, 1) }, Strips(10, 7, 2f));
        Assert.Equal(new[] { (2, 1) }, Strips(10, 3, 2f));
        Assert.Empty(Strips(10, 2, 2f));
    }

    [Fact]
    public void Density_1_5_rounds_to_two_pixel_bands()
    {
        Assert.Equal(Strips(10, 9, 2f), Strips(10, 9, 1.5f));
        Assert.Equal(new[] { (2, 2), (6, 2) }, Strips(10, 9, 1.5f));
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(0, 10)]
    [InlineData(10, -3)]
    [InlineData(-1, 10)]
    [InlineData(10, 1)]
    public void Degenerate_viewports_have_no_strips(int w, int h)
    {
        Assert.Empty(GpuScanlineGeometry.ComputeStrips(w, h, 1f));
        Assert.Empty(GpuScanlineGeometry.ToNdc(GpuScanlineGeometry.ComputeStrips(w, h, 1f), h));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    [InlineData(3f)]
    public void Strips_never_cover_row_zero_and_stay_inside_the_viewport(float density)
    {
        for (int h = 0; h <= 61; h++)
        {
            var strips = GpuScanlineGeometry.ComputeStrips(37, h, density);
            Assert.Equal(GpuScanlineGeometry.StripCount(h, density), strips.Count);
            int t = GpuScanlineGeometry.Thickness(density);
            for (int i = 0; i < strips.Count; i++)
            {
                Assert.Equal(t + 2 * t * i, strips[i].Y);
                Assert.InRange(strips[i].Height, 1, t);
                Assert.True(strips[i].Y + strips[i].Height <= h);
            }
            Assert.DoesNotContain(strips, s => s.Y == 0);
            // Rows not covered: every period's first band, plus nothing else.
            for (int y = 0; y < h; y++)
                Assert.Equal((y / t) % 2 == 1, strips.Any(s => y >= s.Y && y < s.Y + s.Height));

            var span = new GpuScanlineStrip[strips.Count];
            Assert.Equal(strips.Count, GpuScanlineGeometry.FillStrips(span, h, density));
            Assert.Equal(strips, span);
        }
    }

    [Fact]
    public void FillStrips_rejects_a_short_destination() =>
        Assert.Throws<ArgumentException>(() =>
            GpuScanlineGeometry.FillStrips(new GpuScanlineStrip[1], 8, 1f));

    [Fact]
    public void Ndc_rects_span_the_full_width_with_y_down_from_plus_one()
    {
        GpuNdcRect[] rects = GpuScanlineGeometry.ToNdc(GpuScanlineGeometry.ComputeStrips(10, 4, 1f), 4);
        Assert.Equal(new[] { new GpuNdcRect(-1f, 0.5f, 1f, 0f), new GpuNdcRect(-1f, -0.5f, 1f, -1f) }, rects);

        rects = GpuScanlineGeometry.ToNdc(GpuScanlineGeometry.ComputeStrips(10, 7, 2f), 7);
        Assert.Equal(2, rects.Length);
        Assert.Equal(1f - 2f * 2 / 7, rects[0].Y0, 6);
        Assert.Equal(1f - 2f * 4 / 7, rects[0].Y1, 6);
        Assert.Equal(-1f, rects[1].Y1, 6); // clipped last strip ends at the bottom edge
        Assert.All(rects, r => Assert.True(r.Y0 > r.Y1 && r.X0 == -1f && r.X1 == 1f));
    }

    [Fact]
    public void Strip_alpha_is_35_percent_black()
    {
        Assert.Equal(89, GpuScanlineGeometry.StripAlpha);
        Assert.InRange(GpuScanlineGeometry.StripAlpha / 255.0, 0.345, 0.355);
    }

    [Theory]
    [InlineData(1f, 7)]
    [InlineData(2f, 7)]
    [InlineData(1.5f, 9)]
    [InlineData(3f, 13)]
    public void Apply_darkens_only_strip_rows_with_GpuAlphaBlend_rounding(float density, int height)
    {
        const int width = 3, stride = width * 4 + 8; // padded stride
        var image = new byte[stride * height];
        for (int i = 0; i < image.Length; i++) image[i] = (byte)(i * 37 + 11);
        byte[] original = image.ToArray();

        GpuScanlineGeometry.Apply(image, width, height, stride, density);

        var strips = GpuScanlineGeometry.ComputeStrips(width, height, density);
        for (int y = 0; y < height; y++)
        {
            bool dark = strips.Any(s => y >= s.Y && y < s.Y + s.Height);
            for (int b = 0; b < stride; b++)
            {
                int o = y * stride + b;
                bool colour = b < width * 4 && b % 4 != 3;
                byte expected = dark && colour
                    ? GpuAlphaBlend.Blend(0, original[o], GpuScanlineGeometry.StripAlpha)
                    : original[o];
                Assert.Equal(expected, image[o]);
            }
        }
        Assert.NotEqual(original, image);
    }

    [Fact]
    public void Apply_value_matches_the_documented_formula()
    {
        var px = new byte[] { 200, 255, 0, 255, 200, 255, 0, 255 };
        GpuScanlineGeometry.Apply(px, 1, 2, 4, 1f);
        Assert.Equal(new byte[] { 200, 255, 0, 255, (200 * 166 + 127) / 255, (255 * 166 + 127) / 255, 0, 255 }, px);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(4, 1)]
    public void Apply_leaves_degenerate_or_single_row_images_untouched(int width, int height)
    {
        var px = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
        byte[] before = px.ToArray();
        GpuScanlineGeometry.Apply(px, width, height, 16, 1f);
        Assert.Equal(before, px);
    }

    [Fact]
    public void Apply_validates_stride_and_buffer()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuScanlineGeometry.Apply(new byte[64], 4, 2, 8, 1f));
        Assert.Throws<ArgumentException>(() => GpuScanlineGeometry.Apply(new byte[20], 4, 2, 16, 1f));
    }
}
