namespace TSharpVision.Diagnostics.SdlGlyphs;

/// <summary>
/// Minimal 32-bit ARGB raster used only by this diagnostic tool.
/// Production glyph data is monochrome alpha (<c>AlphaBitmap</c>); this type exists so that
/// scenes can be shown with their real Turbo Vision foreground/background colours, which is what
/// makes shadow and scrollbar discontinuities obvious to the eye.
/// </summary>
internal sealed class ArgbBitmap
{
    public int    Width  { get; }
    public int    Height { get; }
    public uint[] Pixels { get; }

    public ArgbBitmap(int width, int height, uint fill = 0xFF000000u)
    {
        if (width  <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width  = width;
        Height = height;
        Pixels = new uint[width * height];
        Array.Fill(Pixels, fill);
    }

    public uint this[int x, int y]
    {
        get => (uint)x < (uint)Width && (uint)y < (uint)Height ? Pixels[y * Width + x] : 0u;
        set
        {
            if ((uint)x < (uint)Width && (uint)y < (uint)Height)
                Pixels[y * Width + x] = value;
        }
    }

    public void FillRect(int x0, int y0, int w, int h, uint argb)
    {
        for (int y = Math.Max(0, y0); y < Math.Min(Height, y0 + h); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(Width, x0 + w); x++)
                Pixels[y * Width + x] = argb;
    }

    /// <summary>Alpha-blends <paramref name="argb"/> over the existing pixel. Exact /255 blend.</summary>
    public void Blend(int x, int y, uint argb, byte alpha)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height || alpha == 0) return;

        int idx = y * Width + x;

        if (alpha == 255)
        {
            Pixels[idx] = 0xFF000000u | (argb & 0x00FFFFFFu);
            return;
        }

        uint dst = Pixels[idx];
        int  inv = 255 - alpha;

        byte r = (byte)((((argb >> 16) & 0xFF) * alpha + ((dst >> 16) & 0xFF) * inv) / 255);
        byte g = (byte)((((argb >>  8) & 0xFF) * alpha + ((dst >>  8) & 0xFF) * inv) / 255);
        byte b = (byte)((( argb        & 0xFF) * alpha + ( dst        & 0xFF) * inv) / 255);

        Pixels[idx] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    public void Blit(ArgbBitmap source, int offsetX, int offsetY)
    {
        for (int y = 0; y < source.Height; y++)
        {
            int ty = y + offsetY;
            if ((uint)ty >= (uint)Height) continue;

            for (int x = 0; x < source.Width; x++)
            {
                int tx = x + offsetX;
                if ((uint)tx >= (uint)Width) continue;
                Pixels[ty * Width + tx] = source.Pixels[y * source.Width + x];
            }
        }
    }
}

/// <summary>Writes an <see cref="ArgbBitmap"/> as an uncompressed 32-bit BMP.</summary>
internal static class ArgbBitmapBmpWriter
{
    private const int FileHeaderSize = 14;
    private const int DibHeaderSize  = 40;
    private const int HeadersTotal   = FileHeaderSize + DibHeaderSize;

    public static void WriteToFile(ArgbBitmap bitmap, string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using FileStream stream = File.Create(filePath);
        using var bw = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        int w          = bitmap.Width;
        int h          = bitmap.Height;
        int pixelBytes = w * h * 4;

        bw.Write((byte)'B');
        bw.Write((byte)'M');
        bw.Write(HeadersTotal + pixelBytes);
        bw.Write(0);
        bw.Write(HeadersTotal);

        bw.Write(DibHeaderSize);
        bw.Write(w);
        bw.Write(h);              // positive = bottom-up rows
        bw.Write((short)1);
        bw.Write((short)32);
        bw.Write(0);              // BI_RGB
        bw.Write(pixelBytes);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);

        // 32-bit rows are always 4-byte aligned; no padding needed.
        for (int y = h - 1; y >= 0; y--)
        {
            for (int x = 0; x < w; x++)
            {
                uint p = bitmap.Pixels[y * w + x];
                bw.Write((byte)( p        & 0xFF)); // B
                bw.Write((byte)((p >>  8) & 0xFF)); // G
                bw.Write((byte)((p >> 16) & 0xFF)); // R
                bw.Write((byte)255);                // A / reserved
            }
        }
    }
}
