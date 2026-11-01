using System.Numerics;

namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>
/// ShadowMask display effect: a fine CRT phosphor mask drawn by <c>ShadowMaskFrag.hlsl</c> as a
/// post-process over the offscreen grid texture.
/// <para>
/// The mask is anchored to device pixels (the grid viewport starts at device pixel 0,0), not to
/// cells: it does not move when the font or cell size changes. It is a 3 x 2 tile of mask cells,
/// each mask cell <see cref="PitchPixels"/> x <see cref="PitchPixels"/> device pixels
/// (<c>pitch = max(1, round(density))</c>, so the pattern stays fine but visible on HiDPI),
/// with a staggered dark gap:
/// <code>
/// m      = (x / pitch, y / pitch)              integer division
/// row 0: 1 1 D        (m.y % 2 == 0: dark at m.x % 3 == 2)
/// row 1: 1 D 1        (m.y % 2 == 1: dark at m.x % 3 == 1)
/// out    = src * (dark ? DarkFactor : 1)       DarkFactor = 1 - Strength = 0.8
/// </code>
/// The mask is deliberately neutral: every channel of a pixel is multiplied by the same factor and
/// there are no coloured (RGB) subpixels, so Green/Amber/Grayscale stay single-hue and Default
/// colours keep their hue. Two of the six tile pixels are dark, so the mean brightness factor over
/// a period is <see cref="MeanFactor"/> = 1 - Strength/3 ≈ 0.933 (6.7% loss). No brightness
/// compensation is applied: any gain above 1 would clip full-white text. The source is read at
/// texel centres, so the image is never resampled or blurred. <see cref="ApplyReference"/> is the
/// same math on the CPU, for tests.
/// </para>
/// </summary>
internal static class GpuShadowMask
{
    /// <summary>True once <c>ShadowMaskFrag.spv</c> exists and the effect is ready to register.</summary>
    internal const bool Available = true;

    /// <summary>How much a dark mask pixel is dimmed (0 = no mask, 1 = black gaps).</summary>
    public const float Strength = 0.2f;

    /// <summary>Factor of a dark mask pixel: <c>1 - Strength</c>.</summary>
    public const float DarkFactor = 1f - Strength;

    /// <summary>Tile width in mask cells.</summary>
    public const int PeriodX = 3;

    /// <summary>Tile height in mask cells.</summary>
    public const int PeriodY = 2;

    /// <summary>Dark pixels per tile (one per row).</summary>
    public const int DarkPerTile = 2;

    /// <summary>Mean brightness factor over one period: <c>1 - Strength * DarkPerTile / (PeriodX * PeriodY)</c>.</summary>
    public const float MeanFactor = 1f - Strength * DarkPerTile / (PeriodX * PeriodY);

    /// <summary>Largest mask cell size in device pixels (density 8 and above).</summary>
    public const int MaxPitch = 8;

    /// <summary>
    /// Device pixels per mask cell at <paramref name="density"/>: <c>max(1, round(density))</c>
    /// (midpoint away from zero; non-finite or non-positive → 1; at most <see cref="MaxPitch"/>).
    /// </summary>
    public static int PitchPixels(float density)
    {
        if (!float.IsFinite(density) || density <= 0f) return 1;
        return Math.Clamp((int)MathF.Round(density, MidpointRounding.AwayFromZero), 1, MaxPitch);
    }

    /// <summary>True when device pixel (<paramref name="x"/>, <paramref name="y"/>) is a dark mask pixel (x, y ≥ 0).</summary>
    public static bool IsDark(int x, int y, int pitch)
    {
        if (pitch < 1) pitch = 1;
        int mx = x / pitch, my = y / pitch;
        int darkColumn = (my % PeriodY) == 0 ? 2 : 1;
        return mx % PeriodX == darkColumn;
    }

    /// <summary>Mask factor of device pixel (<paramref name="x"/>, <paramref name="y"/>): <see cref="DarkFactor"/> or 1.</summary>
    public static float Factor(int x, int y, int pitch) => IsDark(x, y, pitch) ? DarkFactor : 1f;

    /// <summary>
    /// Uniforms for <c>ShadowMaskFrag.hlsl</c>: V0 = viewport (w, h, 1/w, 1/h); V1 = (density,
    /// Strength, pitch px, 0); V2 = V3 = 0. Pure and allocation-free.
    /// </summary>
    public static GpuPostUniforms BuildUniforms(in GpuPostFrame frame)
    {
        GpuPostUniforms u = GpuPostUniforms.Base(frame);
        u.V1 = new Vector4(frame.Density, Strength, PitchPixels(frame.Density), 0f);
        return u;
    }

    /// <summary>One channel through the mask: <c>round(c * factor)</c>, as the GPU's UNORM write.</summary>
    public static byte Scale(byte channel, float factor) => (byte)MathF.Round(channel / 255f * factor * 255f);

    /// <summary>
    /// CPU reference of the shader: reads <paramref name="source"/> (tightly packed RGBA8,
    /// <paramref name="width"/> x <paramref name="height"/>, origin = device pixel 0,0), writes the
    /// masked image to <paramref name="destination"/> (same layout; alpha copied).
    /// </summary>
    public static void ApplyReference(ReadOnlySpan<byte> source, Span<byte> destination, int width, int height, float density)
    {
        if (width <= 0 || height <= 0) return;
        int pixels = checked(width * height);
        if (source.Length < pixels * 4) throw new ArgumentException("Source is too short.", nameof(source));
        if (destination.Length < pixels * 4) throw new ArgumentException("Destination is too short.", nameof(destination));

        int pitch = PitchPixels(density);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int o = (y * width + x) * 4;
                float f = Factor(x, y, pitch);
                destination[o]     = Scale(source[o], f);
                destination[o + 1] = Scale(source[o + 1], f);
                destination[o + 2] = Scale(source[o + 2], f);
                destination[o + 3] = source[o + 3];
            }
    }
}
