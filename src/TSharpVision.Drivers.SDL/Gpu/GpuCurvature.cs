using System.Numerics;

namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>
/// CRT curvature display effect: a restrained barrel warp drawn by <c>CurvatureFrag.hlsl</c> as a
/// post-process over the offscreen grid texture. The warp is a destination → source mapping (each
/// output pixel samples the grid at <c>f(dst)</c>), defined in normalised viewport coordinates:
/// <code>
/// u      = dst * (1/w, 1/h)                 dst in viewport device px (pixel i covers [i, i+1))
/// c      = 2u - 1                           centred, -1..1 across the viewport
/// e      = c * Aspect                       Aspect = (w, h) / max(w, h): the warp is round in physical pixels
/// g      = 1 + K * |e|^2                    K = <see cref="K"/>
/// src    = (c * g * 0.5 + 0.5) * (w, h)     source position in grid device px
/// d      = min(src.x, w - src.x, src.y, h - src.y)
/// out    = bilinear(grid, src) * saturate(d / Edge + 0.5)   (Edge = <see cref="EdgeWidthPixels"/>)
/// </code>
/// Because <c>g ≥ 1</c> everywhere, every grid point is shown (no cell is cut off, no scale factor
/// is needed): the image shrinks slightly towards the edges and the uncovered band — widest at the
/// corners, about 2.3 % of the width at the middle of the long sides for K = 0.05 — is black.
/// The formula is in normalised coordinates, so it scales with the viewport (resize, HiDPI) and is
/// independent of density. <see cref="MapToSource(float, float, int, int, out float, out float)"/> is
/// the same mapping on the CPU (float, same operation order, same constants via
/// <see cref="BuildUniforms"/>), used for pointer hit-testing; <see cref="ApplyReference"/> is the
/// whole shader on the CPU, for tests.
/// </summary>
internal static class GpuCurvature
{
    /// <summary>True once <c>CurvatureFrag.spv</c> exists and the effect is ready to register.</summary>
    internal const bool Available = true;

    /// <summary>Barrel strength: the sample radius grows by <c>K * |e|^2</c> (|e| = 1 at the middle of the long sides).</summary>
    public const float K = 0.05f;

    /// <summary>Width of the antialiased image edge, in source device pixels.</summary>
    public const float EdgeWidthPixels = 1f;

    /// <summary>
    /// Uniforms for <c>CurvatureFrag.hlsl</c>: V0 = viewport (w, h, 1/w, 1/h); V1 = (density, K,
    /// aspect x, aspect y) with aspect = (w, h) / max(w, h); V2 = (<see cref="EdgeWidthPixels"/>, 0, 0, 0);
    /// V3 = 0. Pure and allocation-free.
    /// </summary>
    public static GpuPostUniforms BuildUniforms(in GpuPostFrame frame)
    {
        GpuPostUniforms u = GpuPostUniforms.Base(frame);
        float longest = Math.Max(u.V0.X, u.V0.Y);
        u.V1 = new Vector4(frame.Density, K, u.V0.X / longest, u.V0.Y / longest);
        u.V2 = new Vector4(EdgeWidthPixels, 0f, 0f, 0f);
        return u;
    }

    // The shader's mapping, unclamped: source position in device px and the signed distance d to
    // the image edge (positive inside). Same operations, same order as CurvatureFrag.hlsl.
    private static void Warp(in GpuPostUniforms u, float x, float y, out float sx, out float sy, out float d)
    {
        float cx = x * u.V0.Z * 2f - 1f;
        float cy = y * u.V0.W * 2f - 1f;
        float ex = cx * u.V1.Z;
        float ey = cy * u.V1.W;
        float g  = 1f + u.V1.Y * (ex * ex + ey * ey);
        sx = (cx * g * 0.5f + 0.5f) * u.V0.X;
        sy = (cy * g * 0.5f + 0.5f) * u.V0.Y;
        d  = MathF.Min(MathF.Min(sx, u.V0.X - sx), MathF.Min(sy, u.V0.Y - sy));
    }

    /// <summary>
    /// The shader's destination → source mapping. (<paramref name="x"/>, <paramref name="y"/>) is a
    /// continuous position in viewport device pixels (pixel i covers [i, i+1), its centre is i + 0.5;
    /// the viewport is the grid, top-left anchored, <paramref name="width"/> x <paramref name="height"/>).
    /// The pixel drawn at that position shows the grid at (<paramref name="sourceX"/>,
    /// <paramref name="sourceY"/>), so the cell under the pointer is
    /// <c>PixelToCell(floor(sourceX), floor(sourceY))</c>. Returns false when the position maps
    /// outside the grid image (black corners and edge bands, the letterbox, non-finite input); the
    /// outputs are then clamped to the nearest point of [0, width) x [0, height). Outputs are always in
    /// that range. A non-positive size returns false with (0, 0).
    /// </summary>
    internal static bool MapToSource(float x, float y, int width, int height, out float sourceX, out float sourceY)
    {
        if (width <= 0 || height <= 0)
        {
            sourceX = sourceY = 0f;
            return false;
        }
        GpuPostUniforms u = BuildUniforms(new GpuPostFrame(width, height, 1f));
        return MapToSource(in u, x, y, out sourceX, out sourceY);
    }

    /// <summary><see cref="MapToSource(float, float, int, int, out float, out float)"/> with prebuilt uniforms (V0 = the viewport).</summary>
    internal static bool MapToSource(in GpuPostUniforms uniforms, float x, float y, out float sourceX, out float sourceY)
    {
        float w = uniforms.V0.X, h = uniforms.V0.Y;
        if (!float.IsFinite(x) || !float.IsFinite(y))
        {
            sourceX = sourceY = 0f;
            return false;
        }
        Warp(in uniforms, x, y, out float sx, out float sy, out float d);
        sourceX = Math.Clamp(sx, 0f, MathF.BitDecrement(w));
        sourceY = Math.Clamp(sy, 0f, MathF.BitDecrement(h));
        return d >= 0f;
    }

    /// <summary>
    /// Integer form for pointer mapping: the grid pixel shown by device pixel
    /// (<paramref name="pixelX"/>, <paramref name="pixelY"/>) of the viewport, i.e.
    /// <c>floor(MapToSource(pixel + 0.5))</c>; clamped into the grid, false outside the image.
    /// </summary>
    internal static bool MapPixelToSourcePixel(int pixelX, int pixelY, int width, int height, out int sourcePixelX, out int sourcePixelY)
    {
        bool inside = MapToSource(pixelX + 0.5f, pixelY + 0.5f, width, height, out float sx, out float sy);
        sourcePixelX = (int)MathF.Floor(sx);
        sourcePixelY = (int)MathF.Floor(sy);
        return inside;
    }

    /// <summary>
    /// CPU reference of the shader: reads <paramref name="source"/> (tightly packed RGBA8, the grid,
    /// <paramref name="width"/> x <paramref name="height"/>), writes the warped image to
    /// <paramref name="destination"/> (same layout, alpha 255). Each destination pixel centre is
    /// mapped with the shader's math and sampled bilinearly with clamp-to-edge (texel centres at
    /// i + 0.5), then faded by the antialiased edge.
    /// </summary>
    public static void ApplyReference(ReadOnlySpan<byte> source, Span<byte> destination, int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        int pixels = checked(width * height);
        if (source.Length < pixels * 4) throw new ArgumentException("Source is too short.", nameof(source));
        if (destination.Length < pixels * 4) throw new ArgumentException("Destination is too short.", nameof(destination));

        GpuPostUniforms u = BuildUniforms(new GpuPostFrame(width, height, 1f));
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Warp(in u, x + 0.5f, y + 0.5f, out float sx, out float sy, out float d);
                float a = Math.Clamp(d / u.V2.X + 0.5f, 0f, 1f);
                Vector3 rgb = Bilinear(source, width, height, sx, sy) * a * 255f;
                int o = (y * width + x) * 4;
                destination[o]     = (byte)MathF.Round(rgb.X);
                destination[o + 1] = (byte)MathF.Round(rgb.Y);
                destination[o + 2] = (byte)MathF.Round(rgb.Z);
                destination[o + 3] = 255;
            }
    }

    /// <summary>Bilinear, clamp-to-edge sample (components 0..1) at source position (<paramref name="sx"/>, <paramref name="sy"/>) in px.</summary>
    public static Vector3 Bilinear(ReadOnlySpan<byte> source, int width, int height, float sx, float sy)
    {
        float tx = sx - 0.5f, ty = sy - 0.5f;
        float fx0 = MathF.Floor(tx), fy0 = MathF.Floor(ty);
        float fx = tx - fx0, fy = ty - fy0;
        int x0 = (int)fx0, y0 = (int)fy0;
        Vector3 c00 = Texel(source, width, height, x0, y0);
        Vector3 c10 = Texel(source, width, height, x0 + 1, y0);
        Vector3 c01 = Texel(source, width, height, x0, y0 + 1);
        Vector3 c11 = Texel(source, width, height, x0 + 1, y0 + 1);
        return Vector3.Lerp(Vector3.Lerp(c00, c10, fx), Vector3.Lerp(c01, c11, fx), fy);
    }

    private static Vector3 Texel(ReadOnlySpan<byte> source, int width, int height, int x, int y)
    {
        x = Math.Clamp(x, 0, width - 1);
        y = Math.Clamp(y, 0, height - 1);
        int o = (y * width + x) * 4;
        return new Vector3(source[o], source[o + 1], source[o + 2]) / 255f;
    }
}
