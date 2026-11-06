using System.Numerics;

namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>
/// Glow display effect: a localized phosphor halo around bright pixels (in practice bright glyph
/// strokes), drawn by <c>GlowFrag.hlsl</c> as a post-process over the offscreen grid texture.
/// <code>
/// bright(c) = c * smoothstep(ThresholdLow, ThresholdHigh, luma601(c))
/// halo(p)   = max(sum_i Weight_i * bright(src(p + round(Tap_i * radius))) - bright(src(p)), 0)
/// out(p)    = saturate(src(p) + Strength * halo(p))
/// </code>
/// A pixel only gains light from brighter neighbours, so: no pixel gets darker (glyph cores keep
/// their brightness), uniform areas are unchanged (no global tint), and pixels farther than
/// <see cref="ReachPixels"/> (Chebyshev distance) from every bright pixel are exactly unchanged.
/// The tap count is fixed (<see cref="TapCount"/>, single pass), so the cost per pixel is bounded;
/// the radius is in device pixels and scales with density. <see cref="ApplyReference"/> is the
/// same math on the CPU, for tests.
/// </summary>
internal static class GpuGlow
{
    /// <summary>Halo gain: how much of the blurred bright excess is added.</summary>
    public const float Strength = 0.5f;

    /// <summary>Rec.601 luma at which a colour starts to glow.</summary>
    public const float ThresholdLow = 0.5f;

    /// <summary>Rec.601 luma at which a colour glows fully.</summary>
    public const float ThresholdHigh = 0.9f;

    /// <summary>Blur radius at density 1, in device pixels.</summary>
    public const float RadiusAtDensity1 = 2.5f;

    /// <summary>Smallest blur radius, in device pixels.</summary>
    public const float MinRadius = 1f;

    /// <summary>Largest blur radius, in device pixels (bounds the halo at high densities).</summary>
    public const float MaxRadius = 8f;

    /// <summary>Taps per pixel (centre + rings of 8, 8 and 16).</summary>
    public const int TapCount = 33;

    // (x, y, weight) per tap: unit-disc offsets (rings at 0.35, 0.7, 1.0) and Gaussian weights
    // (sigma 0.5 in unit radius) normalised to sum 1. Identical to the table in GlowFrag.hlsl.
    private static readonly float[] TapTable =
    {
         0.000000f,  0.000000f, 0.080454f,
         0.350000f,  0.000000f, 0.062972f,
         0.247487f,  0.247487f, 0.062972f,
         0.000000f,  0.350000f, 0.062972f,
        -0.247487f,  0.247487f, 0.062972f,
        -0.350000f,  0.000000f, 0.062972f,
        -0.247487f, -0.247487f, 0.062972f,
         0.000000f, -0.350000f, 0.062972f,
         0.247487f, -0.247487f, 0.062972f,
         0.646716f,  0.267878f, 0.030195f,
         0.267878f,  0.646716f, 0.030195f,
        -0.267878f,  0.646716f, 0.030195f,
        -0.646716f,  0.267878f, 0.030195f,
        -0.646716f, -0.267878f, 0.030195f,
        -0.267878f, -0.646716f, 0.030195f,
         0.267878f, -0.646716f, 0.030195f,
         0.646716f, -0.267878f, 0.030195f,
         1.000000f,  0.000000f, 0.010888f,
         0.923880f,  0.382683f, 0.010888f,
         0.707107f,  0.707107f, 0.010888f,
         0.382683f,  0.923880f, 0.010888f,
         0.000000f,  1.000000f, 0.010888f,
        -0.382683f,  0.923880f, 0.010888f,
        -0.707107f,  0.707107f, 0.010888f,
        -0.923880f,  0.382683f, 0.010888f,
        -1.000000f,  0.000000f, 0.010888f,
        -0.923880f, -0.382683f, 0.010888f,
        -0.707107f, -0.707107f, 0.010888f,
        -0.382683f, -0.923880f, 0.010888f,
         0.000000f, -1.000000f, 0.010888f,
         0.382683f, -0.923880f, 0.010888f,
         0.707107f, -0.707107f, 0.010888f,
         0.923880f, -0.382683f, 0.010888f,
    };

    /// <summary>Unit-disc offset and weight of tap <paramref name="index"/>.</summary>
    public static (float X, float Y, float Weight) Tap(int index)
    {
        if ((uint)index >= TapCount) throw new ArgumentOutOfRangeException(nameof(index));
        return (TapTable[index * 3], TapTable[index * 3 + 1], TapTable[index * 3 + 2]);
    }

    /// <summary>Blur radius in device pixels at <paramref name="density"/> (non-finite or non-positive → density 1).</summary>
    public static float RadiusPixels(float density)
    {
        if (!float.IsFinite(density) || density <= 0f) density = 1f;
        return Math.Clamp(RadiusAtDensity1 * density, MinRadius, MaxRadius);
    }

    /// <summary>Integer device-pixel offset of tap <paramref name="index"/> at <paramref name="radius"/>: <c>floor(t * radius + 0.5)</c>, as the shader.</summary>
    public static (int Dx, int Dy) TapOffset(int index, float radius)
    {
        var (x, y, _) = Tap(index);
        return ((int)MathF.Floor(x * radius + 0.5f), (int)MathF.Floor(y * radius + 0.5f));
    }

    /// <summary>
    /// Largest |dx| or |dy| of any tap at <paramref name="density"/>: a pixel whose Chebyshev
    /// distance to every bright pixel exceeds this is left exactly unchanged.
    /// </summary>
    public static int ReachPixels(float density)
    {
        float r = RadiusPixels(density);
        int reach = 0;
        for (int i = 0; i < TapCount; i++)
        {
            var (dx, dy) = TapOffset(i, r);
            reach = Math.Max(reach, Math.Max(Math.Abs(dx), Math.Abs(dy)));
        }
        return reach;
    }

    /// <summary>
    /// Uniforms for <c>GlowFrag.hlsl</c>: V0 = viewport (w, h, 1/w, 1/h); V1 = (density, Strength,
    /// radius px, ThresholdLow); V2 = (ThresholdHigh, 0, 0, 0); V3 = 0.
    /// </summary>
    public static GpuPostUniforms BuildUniforms(in GpuPostFrame frame)
    {
        GpuPostUniforms u = GpuPostUniforms.Base(frame);
        u.V1 = new Vector4(frame.Density, Strength, RadiusPixels(frame.Density), ThresholdLow);
        u.V2 = new Vector4(ThresholdHigh, 0f, 0f, 0f);
        return u;
    }

    /// <summary>Bright part of a colour (components 0..1), as the shader's <c>Bright</c>.</summary>
    public static Vector3 Bright(Vector3 c)
    {
        float y = 0.299f * c.X + 0.587f * c.Y + 0.114f * c.Z;
        float t = Math.Clamp((y - ThresholdLow) / (ThresholdHigh - ThresholdLow), 0f, 1f);
        return c * (t * t * (3f - 2f * t));
    }

    /// <summary>
    /// CPU reference of the shader: reads <paramref name="source"/> (tightly packed RGBA8,
    /// <paramref name="width"/> x <paramref name="height"/>), writes the glowing image to
    /// <paramref name="destination"/> (same layout; alpha copied). Edges clamp like the sampler.
    /// </summary>
    public static void ApplyReference(ReadOnlySpan<byte> source, Span<byte> destination, int width, int height, float density)
    {
        int pixels = checked(width * height);
        if (width <= 0 || height <= 0) return;
        if (source.Length < pixels * 4) throw new ArgumentException("Source is too short.", nameof(source));
        if (destination.Length < pixels * 4) throw new ArgumentException("Destination is too short.", nameof(destination));

        float radius = RadiusPixels(density);
        var offsets = new (int Dx, int Dy, float W)[TapCount];
        for (int i = 0; i < TapCount; i++)
        {
            var (dx, dy) = TapOffset(i, radius);
            offsets[i] = (dx, dy, Tap(i).Weight);
        }

        var bright = new Vector3[pixels];
        for (int p = 0; p < pixels; p++)
            bright[p] = Bright(new Vector3(source[p * 4], source[p * 4 + 1], source[p * 4 + 2]) / 255f);

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Vector3 blur = Vector3.Zero;
                foreach (var (dx, dy, w) in offsets)
                {
                    int sx = Math.Clamp(x + dx, 0, width - 1), sy = Math.Clamp(y + dy, 0, height - 1);
                    blur += w * bright[sy * width + sx];
                }
                int p = y * width + x;
                Vector3 halo = Vector3.Max(blur - bright[p], Vector3.Zero);
                Vector3 src = new Vector3(source[p * 4], source[p * 4 + 1], source[p * 4 + 2]) / 255f;
                Vector3 o = Vector3.Clamp(src + Strength * halo, Vector3.Zero, Vector3.One) * 255f;
                destination[p * 4]     = (byte)MathF.Round(o.X);
                destination[p * 4 + 1] = (byte)MathF.Round(o.Y);
                destination[p * 4 + 2] = (byte)MathF.Round(o.Z);
                destination[p * 4 + 3] = source[p * 4 + 3];
            }
    }
}
