using TSharpVision.Drivers;

namespace TSharpVision.Graphics;

/// <summary>
/// Generated <see cref="PixelImage"/>s for demos and tests: no files, no decoder. Colours are 0xRRGGBBAA with
/// straight alpha, as everywhere in this library. Each call creates a new image.
/// </summary>
public static class TestPatterns
{
    /// <summary>The eight bars of <see cref="ColorBars"/>, left to right: white, yellow, cyan, green, magenta, red,
    /// blue, black.</summary>
    public static ReadOnlySpan<uint> BarColors =>
    [
        0xFFFFFFFF, 0xFFFF00FF, 0x00FFFFFF, 0x00FF00FF, 0xFF00FFFF, 0xFF0000FF, 0x0000FFFF, 0x000000FF,
    ];

    /// <summary>
    /// A checkerboard of <paramref name="cellSize"/>-pixel squares; the top-left square is <paramref name="first"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A size is outside the <see cref="PixelImage"/> limits, or
    /// <paramref name="cellSize"/> is below 1.</exception>
    public static PixelImage Checkerboard(int width, int height, int cellSize, uint first, uint second)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cellSize, 1);
        return PixelImage.Create(width, height, rgba =>
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    Put(rgba, (y * width + x) * 4, ((x / cellSize + y / cellSize) & 1) == 0 ? first : second);
        });
    }

    /// <summary>
    /// A linear gradient from <paramref name="from"/> (left, or top when <paramref name="vertical"/>) to
    /// <paramref name="to"/> (right or bottom), all four channels interpolated and rounded: pixel <c>k</c> of
    /// <c>n</c> gets <c>(from·(n − 1 − k) + to·k + (n − 1) / 2) / (n − 1)</c> per channel.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A size is outside the <see cref="PixelImage"/> limits.</exception>
    public static PixelImage Gradient(int width, int height, uint from, uint to, bool vertical = false)
    {
        return PixelImage.Create(width, height, rgba =>
        {
            int n = vertical ? height : width;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    Put(rgba, (y * width + x) * 4, Lerp(from, to, vertical ? y : x, n));
        });
    }

    /// <summary>
    /// Eight vertical colour bars (<see cref="BarColors"/>; bar of column x is <c>x * 8 / width</c>). The top half is
    /// opaque; over the bottom half the alpha falls row by row from 255 to 0 in the last row, so the image shows
    /// what is drawn underneath it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A size is outside the <see cref="PixelImage"/> limits.</exception>
    public static PixelImage ColorBars(int width, int height)
    {
        return PixelImage.Create(width, height, rgba =>
        {
            ReadOnlySpan<uint> bars = BarColors;
            int top = height / 2, fade = height - top;
            for (int y = 0; y < height; y++)
            {
                uint alpha = y < top || fade <= 1 ? 255u : 255u - (uint)(((y - top) * 255 + (fade - 1) / 2) / (fade - 1));
                for (int x = 0; x < width; x++)
                    Put(rgba, (y * width + x) * 4, (bars[(int)((long)x * 8 / width)] & 0xFFFFFF00u) | alpha);
            }
        });
    }

    private static uint Lerp(uint from, uint to, int k, int n)
    {
        if (n <= 1) return from;
        uint result = 0;
        for (int shift = 24; shift >= 0; shift -= 8)
        {
            long a = (from >> shift) & 0xFF, b = (to >> shift) & 0xFF;
            result |= (uint)((a * (n - 1 - k) + b * k + (n - 1) / 2) / (n - 1)) << shift;
        }
        return result;
    }

    private static void Put(Span<byte> rgba, int offset, uint color)
    {
        rgba[offset] = (byte)(color >> 24);
        rgba[offset + 1] = (byte)(color >> 16);
        rgba[offset + 2] = (byte)(color >> 8);
        rgba[offset + 3] = (byte)color;
    }
}
