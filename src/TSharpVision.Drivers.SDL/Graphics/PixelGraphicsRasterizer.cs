namespace TSharpVision.Drivers.SDL.Graphics;

/// <summary>
/// Software rendering of a <see cref="PixelGraphicsFrame"/> into a tightly addressed RGBA8 buffer (byte order
/// R, G, B, A), used by the SDL GPU driver's CPU-composite fallback and as the oracle of the native GPU and
/// SDL_Renderer tests. Primitives are drawn in submission order across kinds, each clipped to every clip rectangle
/// of its layer (scaled to device pixels) and to the buffer.
/// </summary>
/// <remarks>
/// <para><b>Blending</b> (straight alpha, source-over, integer): <c>c = (s * a + d * (255 - a) + 127) / 255</c> per
/// colour channel and <c>alpha = a + (d * (255 - a) + 127) / 255</c>; a = 255 stores the source, a = 0 changes nothing.</para>
/// <para><b>Images.</b> Destination D = <see cref="PixelScale.Rect"/> of the primitive's <c>[X1, X2) x [Y1, Y2)</c>,
/// W x H device pixels, image w x h texels, k = px - D.X0 (likewise for rows).
/// Nearest: texel <c>((2k + 1) * w) / (2W)</c> (the texel under the pixel centre, exact integer arithmetic in 64 bits).
/// Linear: <c>u = (k + 0.5) * w / W - 0.5</c> in double precision, the four texels around u, v with clamp-to-edge,
/// weights from the fractional parts, every channel (straight alpha) interpolated separately and rounded half up.
/// The effective alpha is <c>(texelAlpha * opacity + 127) / 255</c> (opacity = alpha byte of the primitive colour),
/// then blended as above. An image index outside the layer's images draws nothing. Coordinates are handled in
/// 64 bits, so huge destination rectangles are clipped without overflow; work is proportional to the visible
/// pixels and nothing is allocated.</para>
/// </remarks>
internal static class PixelGraphicsRasterizer
{
    // Draws every layer, each primitive clipped to each of its layer's rectangles and the buffer.
    // rects is a reusable scratch list, cleared per primitive.
    internal static void Rasterize(
        Span<byte> rgba, int width, int height, int stride,
        PixelGraphicsFrame frame, PixelScale scale, List<DeviceRect> rects)
    {
        if (frame.IsEmpty || width <= 0 || height <= 0) return;
        var bounds = new DeviceRect(0, 0, width, height);
        foreach (PixelGraphicsLayer layer in frame.Layers)
        {
            if (layer.Clip.Length == 0) continue;
            foreach (GraphicsPrimitive primitive in layer.Primitives)
            {
                if (primitive.Kind == GraphicsPrimitiveKind.Image)
                {
                    DrawImagePrimitive(rgba, stride, layer, primitive, scale, bounds);
                    continue;
                }
                if ((byte)primitive.Rgba == 0) continue;
                rects.Clear();
                if (PixelGraphicsGeometry.AppendRects(primitive, scale, rects) == 0) continue;
                foreach (PixelRect clip in layer.Clip)
                {
                    DeviceRect c = ClipRect(clip, scale).Intersect(bounds);
                    if (c.IsEmpty) continue;
                    foreach (DeviceRect r in rects)
                    {
                        DeviceRect d = r.Intersect(c);
                        if (!d.IsEmpty) Fill(rgba, stride, d, primitive.Rgba);
                    }
                }
            }
        }
    }

    /// <summary>A layer clip rectangle in device pixels (the right/bottom edge is summed in 64 bits, so it cannot wrap).</summary>
    internal static DeviceRect ClipRect(PixelRect clip, PixelScale scale) => new(
        scale.X(clip.X), scale.Y(clip.Y),
        scale.X((int)Math.Min(int.MaxValue, (long)clip.X + clip.W)),
        scale.Y((int)Math.Min(int.MaxValue, (long)clip.Y + clip.H)));

    /// <summary>The device destination rectangle of an image primitive; empty when the logical rectangle is empty.</summary>
    internal static DeviceRect ImageDestination(in GraphicsPrimitive primitive, PixelScale scale) =>
        primitive.X2 > primitive.X1 && primitive.Y2 > primitive.Y1
            ? scale.Rect(primitive.X1, primitive.Y1, primitive.X2, primitive.Y2)
            : default;

    private static void DrawImagePrimitive(
        Span<byte> rgba, int stride, PixelGraphicsLayer layer, in GraphicsPrimitive primitive, PixelScale scale, DeviceRect bounds)
    {
        byte opacity = (byte)primitive.Rgba;
        PixelImage? image = layer.ImageOf(primitive);
        if (image == null || opacity == 0) return;
        DeviceRect dest = ImageDestination(primitive, scale);
        if (dest.IsEmpty) return;
        foreach (PixelRect clip in layer.Clip)
        {
            DeviceRect visible = dest.Intersect(ClipRect(clip, scale).Intersect(bounds));
            if (!visible.IsEmpty) DrawImage(rgba, stride, dest, visible, image, primitive.Filter, opacity);
        }
    }

    /// <summary>
    /// Draws the part <paramref name="visible"/> (inside <paramref name="dest"/> and the buffer) of
    /// <paramref name="image"/> scaled into <paramref name="dest"/>, with the sampling and blending documented on
    /// the type.
    /// </summary>
    internal static void DrawImage(Span<byte> rgba, int stride, DeviceRect dest, DeviceRect visible,
        PixelImage image, GraphicsImageFilter filter, byte opacity)
    {
        if (filter == GraphicsImageFilter.Linear) DrawLinear(rgba, stride, dest, visible, image, opacity);
        else DrawNearest(rgba, stride, dest, visible, image, opacity);
    }

    private static void DrawNearest(Span<byte> rgba, int stride, DeviceRect dest, DeviceRect visible, PixelImage image, byte opacity)
    {
        ReadOnlySpan<byte> src = image.Pixels;
        int w = image.Width, h = image.Height, srcStride = image.Stride;
        long denX = 2 * ((long)dest.X1 - dest.X0), denY = 2 * ((long)dest.Y1 - dest.Y0);
        // The column texel ((2k + 1) w) / denX is stepped exactly: the numerator grows by 2w per pixel, kept as
        // quotient + remainder (0 <= r < denX).
        long stepQ = 2L * w / denX, stepR = 2L * w % denX;
        long numX0 = (2 * ((long)visible.X0 - dest.X0) + 1) * w;
        long startQ = numX0 / denX, startR = numX0 % denX;
        int count = visible.X1 - visible.X0;
        for (int py = visible.Y0; py < visible.Y1; py++)
        {
            int ty = (int)((2 * ((long)py - dest.Y0) + 1) * h / denY);
            ReadOnlySpan<byte> texRow = src.Slice(ty * srcStride, srcStride);
            Span<byte> row = rgba.Slice(py * stride + visible.X0 * 4, count * 4);
            long q = startQ, r = startR;
            for (int o = 0; o < row.Length; o += 4)
            {
                int t = (int)q * 4;
                byte a = texRow[t + 3];
                if (a != 0)
                    Blend(row, o, texRow[t], texRow[t + 1], texRow[t + 2], opacity == 255 ? a : Mul(a, opacity));
                q += stepQ;
                r += stepR;
                if (r >= denX) { r -= denX; q++; }
            }
        }
    }

    private static void DrawLinear(Span<byte> rgba, int stride, DeviceRect dest, DeviceRect visible, PixelImage image, byte opacity)
    {
        ReadOnlySpan<byte> src = image.Pixels;
        int w = image.Width, h = image.Height, srcStride = image.Stride;
        double sx = (double)w / ((long)dest.X1 - dest.X0), sy = (double)h / ((long)dest.Y1 - dest.Y0);
        int count = visible.X1 - visible.X0;
        for (int py = visible.Y0; py < visible.Y1; py++)
        {
            double v = ((double)((long)py - dest.Y0) + 0.5) * sy - 0.5;
            double vFloor = Math.Floor(v);
            double fy = v - vFloor;
            int y0 = ClampIndex(vFloor, h), y1 = ClampIndex(vFloor + 1, h);
            ReadOnlySpan<byte> row0 = src.Slice(y0 * srcStride, srcStride), row1 = src.Slice(y1 * srcStride, srcStride);
            Span<byte> row = rgba.Slice(py * stride + visible.X0 * 4, count * 4);
            long k = (long)visible.X0 - dest.X0;
            for (int o = 0; o < row.Length; o += 4, k++)
            {
                double u = ((double)k + 0.5) * sx - 0.5;
                double uFloor = Math.Floor(u);
                double fx = u - uFloor;
                int t0 = ClampIndex(uFloor, w) * 4, t1 = ClampIndex(uFloor + 1, w) * 4;
                double w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
                byte a = Sample(row0, row1, t0, t1, 3, w00, w10, w01, w11);
                if (a == 0) continue;
                Blend(row, o,
                    Sample(row0, row1, t0, t1, 0, w00, w10, w01, w11),
                    Sample(row0, row1, t0, t1, 1, w00, w10, w01, w11),
                    Sample(row0, row1, t0, t1, 2, w00, w10, w01, w11),
                    opacity == 255 ? a : Mul(a, opacity));
            }
        }
    }

    // Bilinear of one channel, rounded half up; the weights sum to 1, so the result stays within 0..255.
    private static byte Sample(ReadOnlySpan<byte> row0, ReadOnlySpan<byte> row1, int t0, int t1, int channel,
        double w00, double w10, double w01, double w11)
    {
        double value = row0[t0 + channel] * w00 + row0[t1 + channel] * w10 + row1[t0 + channel] * w01 + row1[t1 + channel] * w11;
        return (byte)Math.Clamp((int)(value + 0.5), 0, 255);
    }

    // Clamp-to-edge texel index from a floored coordinate (a double, so no coordinate can overflow).
    private static int ClampIndex(double index, int size) =>
        index <= 0 ? 0 : index >= size - 1 ? size - 1 : (int)index;

    private static byte Mul(byte a, byte b) => (byte)((a * b + 127) / 255);

    private static void Blend(Span<byte> row, int o, byte sr, byte sg, byte sb, byte sa)
    {
        if (sa == 0) return;
        if (sa == 255)
        {
            row[o] = sr; row[o + 1] = sg; row[o + 2] = sb; row[o + 3] = 255;
            return;
        }
        int inv = 255 - sa;
        row[o]     = (byte)((sr * sa + row[o]     * inv + 127) / 255);
        row[o + 1] = (byte)((sg * sa + row[o + 1] * inv + 127) / 255);
        row[o + 2] = (byte)((sb * sa + row[o + 2] * inv + 127) / 255);
        row[o + 3] = (byte)(sa + (row[o + 3] * inv + 127) / 255);
    }

    private static void Fill(Span<byte> rgba, int stride, DeviceRect r, uint color)
    {
        byte sr = (byte)(color >> 24), sg = (byte)(color >> 16), sb = (byte)(color >> 8), sa = (byte)color;
        if (sa == 0) return;
        for (int y = r.Y0; y < r.Y1; y++)
        {
            Span<byte> row = rgba.Slice(y * stride + r.X0 * 4, (r.X1 - r.X0) * 4);
            for (int o = 0; o < row.Length; o += 4) Blend(row, o, sr, sg, sb, sa);
        }
    }
}
