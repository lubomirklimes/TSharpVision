using System.Numerics;
using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// GPU-free tests of the Curvature warp: the destination → source mapping shared by the shader and
/// the pointer hit-testing (<see cref="GpuCurvature.MapToSource(float, float, int, int, out float, out float)"/>),
/// its uniforms, and the CPU reference of the shader.
/// </summary>
public sealed class GpuCurvatureTests
{
    // Viewports: 80x25 of 9x18 cells, the same at density 2, an odd small grid, a portrait one.
    public static readonly TheoryData<int, int> Viewports = new()
    {
        { 720, 450 }, { 1440, 900 }, { 63, 54 }, { 300, 701 },
    };

    // ─── Catalog / uniforms ─────────────────────────────────────────────────

    [Fact]
    public void Curvature_is_registered_with_its_embedded_shader()
    {
        Assert.True(GpuCurvature.Available);
        Assert.True(GpuPostEffectCatalog.TryGet(GpuDisplayEffect.Curvature, out GpuPostEffectSpec spec));
        Assert.Equal("TSharpVision.Drivers.SDL.Gpu.Shaders.CurvatureFrag.spv", spec.FragmentShaderResource);
        Assert.Equal(1u, spec.NumSamplers);
        Assert.Contains(spec.FragmentShaderResource, typeof(GpuPostProcess).Assembly.GetManifestResourceNames());
        GpuGridLayout layout = GpuGridLayout.Compute(800, 600, 80, 25, 9, 18);
        Assert.True(TerminalGpuPipeline.SelectPostEffect(GpuDisplayEffect.Curvature, layout, out GpuPostEffectSpec selected));
        Assert.Same(spec, selected);
    }

    [Fact]
    public void BuildUniforms_carries_viewport_density_strength_aspect_and_edge()
    {
        GpuPostUniforms u = GpuCurvature.BuildUniforms(new GpuPostFrame(640, 480, 1f));
        Assert.Equal(new Vector4(640, 480, 1f / 640, 1f / 480), u.V0);
        Assert.Equal(new Vector4(1f, GpuCurvature.K, 1f, 0.75f), u.V1);
        Assert.Equal(new Vector4(GpuCurvature.EdgeWidthPixels, 0, 0, 0), u.V2);
        Assert.Equal(Vector4.Zero, u.V3);

        GpuPostUniforms p = GpuCurvature.BuildUniforms(new GpuPostFrame(480, 640, 2f));
        Assert.Equal(new Vector4(2f, GpuCurvature.K, 0.75f, 1f), p.V1);

        // Degenerate viewports follow Base (clamped to 1): finite, no division by zero.
        GpuPostUniforms z = GpuCurvature.BuildUniforms(new GpuPostFrame(0, 0, 1f));
        Assert.Equal(new Vector4(1, 1, 1, 1), z.V0);
        Assert.Equal(new Vector4(1f, GpuCurvature.K, 1f, 1f), z.V1);

        Assert.InRange(GpuCurvature.K, 0.04f, 0.08f);
    }

    [Fact]
    public void BuildUniforms_is_pure_and_allocation_free()
    {
        var frame = new GpuPostFrame(1440, 900, 2f);
        GpuPostUniforms first = GpuCurvature.BuildUniforms(frame);
        GpuCurvature.MapToSource(10f, 10f, 1440, 900, out _, out _);   // warm
        long before = GC.GetAllocatedBytesForCurrentThread();
        float sink = 0;
        for (int i = 0; i < 1000; i++)
        {
            GpuPostUniforms u = GpuCurvature.BuildUniforms(frame);
            GpuCurvature.MapToSource(i, i * 0.5f, 1440, 900, out float sx, out float sy);
            sink += u.V1.Y + sx + sy;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.True(sink > 0);
        Assert.Equal(first, GpuCurvature.BuildUniforms(frame));
    }

    // ─── Mapping ────────────────────────────────────────────────────────────

    [Theory, MemberData(nameof(Viewports))]
    public void Centre_maps_to_itself(int w, int h)
    {
        Assert.True(GpuCurvature.MapToSource(w / 2f, h / 2f, w, h, out float sx, out float sy));
        Assert.Equal(w / 2f, sx, 3);
        Assert.Equal(h / 2f, sy, 3);
        // Near the centre the warp is sub-pixel.
        Assert.True(GpuCurvature.MapToSource(w * 0.55f, h * 0.45f, w, h, out sx, out sy));
        Assert.True(Math.Abs(sx - w * 0.55f) < 0.05f * Math.Max(1, w / 100f));
        Assert.True(Math.Abs(sy - h * 0.45f) < 0.05f * Math.Max(1, h / 100f));
    }

    [Theory, MemberData(nameof(Viewports))]
    public void Mapping_is_monotonic_along_both_axes(int w, int h)
    {
        foreach (float fixedFraction in new[] { 0.5f, 0.2f, 0.02f })
        {
            float prev = float.NegativeInfinity;
            for (float x = 0; x <= w; x += 0.25f)
            {
                GpuCurvature.MapToSource(x, h * fixedFraction, w, h, out float sx, out _);
                Assert.True(sx >= prev, $"x={x}: {sx} < {prev}");
                prev = sx;
            }
            prev = float.NegativeInfinity;
            for (float y = 0; y <= h; y += 0.25f)
            {
                GpuCurvature.MapToSource(w * fixedFraction, y, w, h, out _, out float sy);
                Assert.True(sy >= prev, $"y={y}: {sy} < {prev}");
                prev = sy;
            }
        }
    }

    [Theory, MemberData(nameof(Viewports))]
    public void Mapping_is_symmetric_about_the_centre(int w, int h)
    {
        for (float fy = 0.05f; fy < 1f; fy += 0.1f)
            for (float fx = 0.05f; fx < 1f; fx += 0.1f)
            {
                float x = fx * w, y = fy * h;
                bool a = GpuCurvature.MapToSource(x, y, w, h, out float ax, out float ay);
                bool b = GpuCurvature.MapToSource(w - x, h - y, w, h, out float bx, out float by);
                bool c = GpuCurvature.MapToSource(w - x, y, w, h, out float cx, out _);
                Assert.Equal(a, b);
                Assert.Equal(a, c);
                if (!a) continue;
                float tol = 2e-3f * Math.Max(w, h);
                Assert.True(Math.Abs(ax - (w - bx)) < tol && Math.Abs(ay - (h - by)) < tol, $"({x},{y})");
                Assert.True(Math.Abs(ax - (w - cx)) < tol, $"mirror x ({x},{y})");
            }
    }

    [Theory, MemberData(nameof(Viewports))]
    public void Corners_and_a_thin_edge_band_are_outside_and_clamp_to_the_nearest_edge(int w, int h)
    {
        // Corners are black: outside, clamped to the grid's corners.
        Assert.False(GpuCurvature.MapToSource(0.5f, 0.5f, w, h, out float sx, out float sy));
        Assert.Equal((0f, 0f), (sx, sy));
        Assert.False(GpuCurvature.MapToSource(w - 0.5f, h - 0.5f, w, h, out sx, out sy));
        Assert.Equal((MathF.BitDecrement(w), MathF.BitDecrement(h)), (sx, sy));
        Assert.True(GpuCurvature.MapPixelToSourcePixel(w - 1, h - 1, w, h, out int px, out int py) == false);
        Assert.Equal((w - 1, h - 1), (px, py));

        // The band at the middle of each edge: outside, small (<= 3 % of the extent per side on the
        // long axis, less on the short one), and the first pixel inside it shows the grid's edge.
        float longest = Math.Max(w, h);
        float bandX = FirstInside(t => GpuCurvature.MapToSource(t, h / 2f, w, h, out _, out _), w);
        float bandY = FirstInside(t => GpuCurvature.MapToSource(w / 2f, t, w, h, out _, out _), h);
        Assert.InRange(bandX / w, 0.002f, 0.03f);
        Assert.InRange(bandY / h, 0.002f, 0.03f);
        if (w > h) Assert.True(bandX / w > bandY / h);
        if (h > w) Assert.True(bandY / h > bandX / w);
        Assert.False(GpuCurvature.MapToSource(bandX - 0.5f, h / 2f, w, h, out sx, out sy));
        Assert.Equal(0f, sx);
        Assert.True(GpuCurvature.MapToSource(bandX + 0.01f, h / 2f, w, h, out sx, out _));
        Assert.InRange(sx, 0f, 0.05f);
        _ = longest;
    }

    private static float FirstInside(Func<float, bool> inside, int extent)
    {
        for (float t = 0; t < extent; t += 1f / 64)
            if (inside(t)) return t;
        return extent;
    }

    [Fact]
    public void Letterbox_degenerate_and_non_finite_inputs_return_false_with_clamped_outputs()
    {
        const int w = 720, h = 450;
        Assert.False(GpuCurvature.MapToSource(w + 13, h / 2f, w, h, out float sx, out float sy));
        Assert.Equal(MathF.BitDecrement(w), sx);
        Assert.InRange(sy, 0f, MathF.BitDecrement(h));
        Assert.Equal(w - 1, (int)MathF.Floor(sx));
        Assert.False(GpuCurvature.MapToSource(-5, -5, w, h, out sx, out sy));
        Assert.Equal((0f, 0f), (sx, sy));
        Assert.False(GpuCurvature.MapToSource(float.NaN, 3, w, h, out sx, out sy));
        Assert.Equal((0f, 0f), (sx, sy));
        Assert.False(GpuCurvature.MapToSource(float.PositiveInfinity, 3, w, h, out sx, out sy));
        Assert.False(GpuCurvature.MapToSource(1, 1, 0, h, out sx, out sy));
        Assert.Equal((0f, 0f), (sx, sy));
        Assert.False(GpuCurvature.MapToSource(1, 1, w, -3, out _, out _));

        // Every output of any input lies in [0, w) x [0, h).
        var rng = new Random(12);
        for (int i = 0; i < 20000; i++)
        {
            float x = (float)(rng.NextDouble() * (w + 200) - 100), y = (float)(rng.NextDouble() * (h + 200) - 100);
            bool inside = GpuCurvature.MapToSource(x, y, w, h, out sx, out sy);
            Assert.InRange(sx, 0f, MathF.BitDecrement(w));
            Assert.InRange(sy, 0f, MathF.BitDecrement(h));
            Assert.True(sx < w && sy < h);
            if (x < 0 || y < 0 || x > w || y > h) Assert.False(inside);
        }
    }

    [Theory]
    [InlineData(80, 25, 9, 18)]
    [InlineData(80, 25, 18, 36)]
    [InlineData(7, 3, 9, 18)]
    [InlineData(132, 50, 8, 16)]
    [InlineData(24, 60, 9, 18)]
    public void Every_cell_is_reachable_by_some_destination_pixel(int cols, int rows, int cw, int ch)
    {
        int w = cols * cw, h = rows * ch;
        var hits = new int[cols * rows];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!GpuCurvature.MapToSource(x + 0.5f, y + 0.5f, w, h, out float sx, out float sy)) continue;
                // Count only pixels that are clearly inside their cell (>= 1 px from its border).
                float lx = sx - MathF.Floor(sx / cw) * cw, ly = sy - MathF.Floor(sy / ch) * ch;
                if (lx < 1 || lx > cw - 1 || ly < 1 || ly > ch - 1) continue;
                TPoint cell = SdlMouseTranslator.PixelToCell((int)sx, (int)sy, cw, ch);
                hits[cell.y * cols + cell.x]++;
            }
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                Assert.True(hits[r * cols + c] >= (cw - 2) * (ch - 2) / 2,
                    $"cell ({c},{r}) has only {hits[r * cols + c]} destination pixels");
    }

    [Theory]
    [InlineData(720, 450)]
    [InlineData(63, 54)]
    public void The_warp_is_resolution_independent(int w, int h)
    {
        for (float fy = 0.03f; fy < 1f; fy += 0.07f)
            for (float fx = 0.03f; fx < 1f; fx += 0.07f)
            {
                bool a = GpuCurvature.MapToSource(fx * w, fy * h, w, h, out float ax, out float ay);
                bool b = GpuCurvature.MapToSource(fx * 2 * w, fy * 2 * h, 2 * w, 2 * h, out float bx, out float by);
                if (a != b) continue;   // exactly on the image edge
                if (!a) continue;
                Assert.True(Math.Abs(2 * ax - bx) < 0.01f && Math.Abs(2 * ay - by) < 0.01f, $"({fx},{fy})");
            }
    }

    // ─── CPU reference of the shader ────────────────────────────────────────

    [Fact]
    public void Bilinear_at_a_texel_centre_is_that_texel_and_interpolates_between()
    {
        byte[] src = { 0, 0, 0, 255, 200, 100, 50, 255 };
        Assert.Equal(Vector3.Zero, GpuCurvature.Bilinear(src, 2, 1, 0.5f, 0.5f));
        Assert.Equal(new Vector3(200, 100, 50) / 255f, GpuCurvature.Bilinear(src, 2, 1, 1.5f, 0.5f));
        Vector3 mid = GpuCurvature.Bilinear(src, 2, 1, 1.0f, 0.5f);
        Assert.Equal(100f / 255f, mid.X, 4);
        Assert.Equal(new Vector3(200, 100, 50) / 255f, GpuCurvature.Bilinear(src, 2, 1, 9f, -4f));  // clamp
    }

    // Cells of distinct colours: every cell differs from its 8 neighbours.
    internal static byte[] CellImage(int cols, int rows, int cw, int ch)
    {
        int w = cols * cw, h = rows * ch;
        var img = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (r, g, b) = CellColour(x / cw, y / ch);
                int o = (y * w + x) * 4;
                img[o] = r; img[o + 1] = g; img[o + 2] = b; img[o + 3] = 255;
            }
        return img;
    }

    private static (byte, byte, byte) CellColour(int col, int row) =>
        ((byte)(40 + 20 * (col % 10)), (byte)(40 + 50 * (row % 4)), (byte)(((col + row) & 1) * 200 + 30));

    [Fact]
    public void Reference_keeps_the_centre_blackens_the_corners_and_hits_the_mapped_cell()
    {
        const int cols = 24, rows = 8, cw = 9, ch = 18;
        int w = cols * cw, h = rows * ch;
        byte[] src = CellImage(cols, rows, cw, ch);
        var dst = new byte[src.Length];
        GpuCurvature.ApplyReference(src, dst, w, h);

        Assert.Equal((0, 0, 0, 255), Px(dst, w, 0, 0));
        Assert.Equal((0, 0, 0, 255), Px(dst, w, w - 1, h - 1));
        Assert.Equal((0, 0, 0, 255), Px(dst, w, 0, h / 2));

        int checkedPixels = 0, displaced = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!GpuCurvature.MapToSource(x + 0.5f, y + 0.5f, w, h, out float sx, out float sy)) continue;
                float lx = sx - MathF.Floor(sx / cw) * cw, ly = sy - MathF.Floor(sy / ch) * ch;
                if (lx < 1 || lx > cw - 1 || ly < 1 || ly > ch - 1) continue;          // bilinear mix at cell borders
                if (sx < 1 || sy < 1 || sx > w - 1 || sy > h - 1) continue;           // antialiased image edge
                TPoint cell = SdlMouseTranslator.PixelToCell((int)sx, (int)sy, cw, ch);
                var (r, g, b) = CellColour(cell.x, cell.y);
                Assert.Equal(((byte)r, (byte)g, (byte)b, (byte)255), Px(dst, w, x, y));
                checkedPixels++;
                if (cell != SdlMouseTranslator.PixelToCell(x, y, cw, ch)) displaced++;
            }
        Assert.True(checkedPixels > w * h / 2, $"checked {checkedPixels}");
        Assert.True(displaced > 0, "the warp moved no pixel to another cell");

        // The central quarter is (nearly) the source: equal away from cell borders (where the
        // sub-pixel shift blends neighbouring cells).
        for (int y = h * 3 / 8; y < h * 5 / 8; y++)
            for (int x = w * 3 / 8; x < w * 5 / 8; x++)
            {
                if (x % cw == 0 || x % cw == cw - 1 || y % ch == 0 || y % ch == ch - 1) continue;
                var a = Px(src, w, x, y); var b = Px(dst, w, x, y);
                Assert.True(Math.Abs(a.R - b.R) <= 1 && Math.Abs(a.G - b.G) <= 1 && Math.Abs(a.B - b.B) <= 1, $"({x},{y}) {a} -> {b}");
            }
    }

    private static (byte R, byte G, byte B, byte A) Px(byte[] img, int w, int x, int y)
    {
        int o = (y * w + x) * 4;
        return (img[o], img[o + 1], img[o + 2], img[o + 3]);
    }
}
