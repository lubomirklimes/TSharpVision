namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>
/// 8-bit source-over alpha blending for the GPU renderer's CPU-compositing fallback.
/// <para>
/// Divides by 255 rather than shifting by 8: a shift darkens by up to one part in 256 and would
/// not match the SDL_Renderer and GPU-shader paths, which blend correctly.
/// </para>
/// </summary>
internal static class GpuAlphaBlend
{
    /// <summary>
    /// Blends <paramref name="src"/> over <paramref name="dst"/> with coverage
    /// <paramref name="alpha"/>, dividing by 255 with round-to-nearest.
    /// <para>
    /// Exact at both endpoints: <c>alpha == 0</c> returns <paramref name="dst"/> unchanged and
    /// <c>alpha == 255</c> returns <paramref name="src"/> exactly.
    /// </para>
    /// </summary>
    internal static byte Blend(byte src, byte dst, byte alpha)
    {
        // Max intermediate: 255 * 255 + 255 * 255 + 127 = 130177, comfortably inside int.
        int weighted = src * alpha + dst * (255 - alpha);
        return (byte)((weighted + 127) / 255);
    }
}
