namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>A horizontal strip of device-pixel rows, <c>[Y, Y + Height)</c>, across the full viewport width.</summary>
internal readonly record struct GpuScanlineStrip(int Y, int Height);

/// <summary>A rectangle in the grid viewport's clip space (<c>Y0</c> is the top edge, <c>Y0 &gt; Y1</c>).</summary>
internal readonly record struct GpuNdcRect(float X0, float Y0, float X1, float Y1);

/// <summary>
/// Geometry of the <see cref="GpuDisplayEffect.Scanlines"/> effect. GPU-free and allocation-light,
/// so it is unit-testable and shared by the shader path and the CPU-compositing fallback.
/// <para>
/// Geometry: in device pixels of the grid viewport, strip thickness is
/// <c>max(1, round(density))</c> (midpoint away from zero; non-finite or non-positive densities
/// count as 1) and the period is <c>2 * thickness</c>. Each period's second band is darkened: strips
/// start at <c>y = thickness</c>, so row 0 is never darkened, and the last strip is clipped to the
/// viewport height. Because the geometry is recomputed from the viewport height and density each
/// frame, HiDPI and resize stay pixel exact.
/// </para>
/// <para>
/// Strength: strips are black at <see cref="StripAlpha"/> = 89/255 (about 0.35), blended
/// source-over, i.e. <c>dst = dst * (255 - 89) / 255</c>.
/// </para>
/// <para>
/// Where it plugs in: <c>TerminalGpuPipeline.RenderFrame</c>, after the glyph pass and inside the
/// same render pass (same grid viewport), binds a third pipeline that reuses the Bg vertex/fragment
/// SPIR-V (<c>BgVert.spv</c>/<c>BgFrag.spv</c>, <c>BgVertex</c> layout) with the glyph pass's
/// source-over blend state (<c>TerminalGpuPipeline.SourceOverBlendState</c>, including the
/// <c>_enableBlend</c> byte-offset-25 hack), and draws the strips as quads
/// (<c>TerminalGpuPipeline.BuildScanlineVertices</c>, same clip space as <see cref="ToNdc"/>) with
/// colour <c>(0, 0, 0, StripAlpha)</c>. The pipeline and strip buffer are created lazily on first
/// Scanlines use and the strips are rebuilt only when the viewport height or density changes, so
/// <see cref="GpuDisplayEffect.Default"/> records exactly the commands it recorded before effects
/// existed. The CPU-compositing fallback (presentation and capture) calls <see cref="Apply"/> on
/// the composited pixels before upload.
/// </para>
/// </summary>
internal static class GpuScanlineGeometry
{
    /// <summary>Strip alpha: 0.35 black, rounded to 89 of 255.</summary>
    public const byte StripAlpha = 89;

    /// <summary>Strip thickness in device pixels for <paramref name="density"/>.</summary>
    public static int Thickness(float density)
    {
        if (!float.IsFinite(density) || density <= 0f) return 1;
        double rounded = Math.Round((double)density, MidpointRounding.AwayFromZero);
        return rounded >= int.MaxValue / 4 ? int.MaxValue / 4 : Math.Max(1, (int)rounded);
    }

    /// <summary>Number of strips for a viewport <paramref name="viewportHeight"/> rows high.</summary>
    public static int StripCount(int viewportHeight, float density)
    {
        if (viewportHeight <= 0) return 0;
        int t = Thickness(density);
        if (viewportHeight <= t) return 0;
        long period = 2L * t;
        return (int)((viewportHeight - t + period - 1) / period);
    }

    /// <summary>
    /// The strips to darken for a viewport of <paramref name="viewportWidth"/> x
    /// <paramref name="viewportHeight"/> device pixels. Empty when either side is not positive.
    /// </summary>
    public static IReadOnlyList<GpuScanlineStrip> ComputeStrips(int viewportWidth, int viewportHeight, float density)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0) return Array.Empty<GpuScanlineStrip>();
        int count = StripCount(viewportHeight, density);
        if (count == 0) return Array.Empty<GpuScanlineStrip>();
        var strips = new GpuScanlineStrip[count];
        FillStrips(strips, viewportHeight, density);
        return strips;
    }

    /// <summary>
    /// Writes the strips into <paramref name="destination"/> (at least <see cref="StripCount"/>
    /// long) without allocating, and returns how many were written.
    /// </summary>
    public static int FillStrips(Span<GpuScanlineStrip> destination, int viewportHeight, float density)
    {
        int count = StripCount(viewportHeight, density);
        if (destination.Length < count)
            throw new ArgumentException("Destination is too short for the strip count.", nameof(destination));
        int t = Thickness(density);
        int y = t;
        for (int i = 0; i < count; i++, y += 2 * t)
            destination[i] = new GpuScanlineStrip(y, Math.Min(t, viewportHeight - y));
        return count;
    }

    /// <summary>
    /// Converts strips to rects in the full-viewport clip space <c>TerminalGpuPipeline</c> uses:
    /// x spans -1..1, y = 1 at the top row and -1 at the bottom of a viewport
    /// <paramref name="viewportHeight"/> rows high.
    /// </summary>
    public static GpuNdcRect[] ToNdc(IReadOnlyList<GpuScanlineStrip> strips, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(strips);
        if (strips.Count == 0 || viewportHeight <= 0) return Array.Empty<GpuNdcRect>();
        float invH = 2f / viewportHeight;
        var rects = new GpuNdcRect[strips.Count];
        for (int i = 0; i < rects.Length; i++)
        {
            GpuScanlineStrip s = strips[i];
            rects[i] = new GpuNdcRect(-1f, 1f - s.Y * invH, 1f, 1f - (s.Y + s.Height) * invH);
        }
        return rects;
    }

    /// <summary>
    /// CPU equivalent for the compositing fallback: darkens the strip rows of an RGBA8 image in
    /// place, <c>c = (c * (255 - StripAlpha) + 127) / 255</c> per colour channel (the same rounding
    /// as <see cref="GpuAlphaBlend.Blend"/> with a black source). Alpha bytes are left unchanged;
    /// rows outside the strips are not touched.
    /// </summary>
    public static void Apply(Span<byte> rgba, int width, int height, int strideBytes, float density)
    {
        if (width <= 0 || height <= 0) return;
        if (strideBytes < width * 4)
            throw new ArgumentOutOfRangeException(nameof(strideBytes), "Stride is shorter than a row.");
        if ((long)strideBytes * (height - 1) + width * 4L > rgba.Length)
            throw new ArgumentException("Buffer is too small for the image.", nameof(rgba));

        int t = Thickness(density);
        for (int y = t; y < height; y += 2 * t)
        {
            int end = Math.Min(height, y + t);
            for (int row = y; row < end; row++)
            {
                Span<byte> line = rgba.Slice(row * strideBytes, width * 4);
                for (int x = 0; x < line.Length; x += 4)
                {
                    line[x]     = GpuAlphaBlend.Blend(0, line[x],     StripAlpha);
                    line[x + 1] = GpuAlphaBlend.Blend(0, line[x + 1], StripAlpha);
                    line[x + 2] = GpuAlphaBlend.Blend(0, line[x + 2], StripAlpha);
                }
            }
        }
    }
}
