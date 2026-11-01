using System.Threading;

namespace TSharpVision.Drivers;

/// <summary>Fills a new image's pixels: tightly packed RGBA8888 rows, <c>width * 4</c> bytes each.</summary>
public delegate void PixelImageFill(Span<byte> rgba);

/// <summary>
/// An immutable RGBA8888 image for <see cref="IPixelGraphicsDriver"/> layers: 4 bytes per pixel in R, G, B, A order,
/// straight (non-premultiplied) alpha, rows top to bottom, tightly packed (<see cref="Stride"/> = <c>Width * 4</c>).
/// </summary>
/// <remarks>
/// <para><b>Ownership.</b> The constructor copies the caller's pixels, so the caller keeps and may reuse its buffer.
/// The image never changes afterwards and may be shared freely between layers, views and threads. A driver keeps a
/// reference (and a texture) for as long as a layer refers to the image, and releases its resources once no layer
/// does. To show new content, create a new image; unchanged images are never uploaded again.</para>
/// <para><b>Limits.</b> Both sides are 1..<see cref="MaxDimension"/> and the pixels at most <see cref="MaxBytes"/>.</para>
/// <para>Decoding files (PNG, JPEG, ...), orientation and colour management are outside this type: pass decoded
/// pixels.</para>
/// </remarks>
public sealed class PixelImage
{
    /// <summary>Largest width or height.</summary>
    public const int MaxDimension = 16384;

    /// <summary>Largest pixel buffer, in bytes (64 Mi pixels).</summary>
    public const long MaxBytes = 256L * 1024 * 1024;

    private static long s_nextId;
    private readonly byte[] _pixels;

    /// <summary>Copies <paramref name="height"/> rows of <paramref name="width"/> RGBA8888 pixels.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="rgba">Source pixels; row <c>y</c> starts at <c>y * stride</c>.</param>
    /// <param name="stride">Bytes between source rows; 0 means <c>width * 4</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A size is outside the limits, or the stride is smaller than a row.</exception>
    /// <exception cref="ArgumentException"><paramref name="rgba"/> is too short.</exception>
    public PixelImage(int width, int height, ReadOnlySpan<byte> rgba, int stride = 0)
    {
        int row = CheckSize(width, height);
        if (stride == 0) stride = row;
        if (stride < row) throw new ArgumentOutOfRangeException(nameof(stride), stride, "The stride is smaller than one row.");
        long needed = (long)stride * (height - 1) + row;
        if (rgba.Length < needed)
            throw new ArgumentException($"{needed} bytes are needed for {width} x {height} with stride {stride}.", nameof(rgba));

        _pixels = GC.AllocateUninitializedArray<byte>(row * height);
        if (stride == row) rgba[.._pixels.Length].CopyTo(_pixels);
        else
            for (int y = 0; y < height; y++)
                rgba.Slice(y * stride, row).CopyTo(_pixels.AsSpan(y * row, row));
        (Width, Height, Id) = (width, height, Interlocked.Increment(ref s_nextId));
    }

    private PixelImage(int width, int height, byte[] pixels)
    {
        _pixels = pixels;
        (Width, Height, Id) = (width, height, Interlocked.Increment(ref s_nextId));
    }

    /// <summary>Creates an image whose pixels <paramref name="fill"/> writes (starting zeroed: transparent black).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="fill"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size is outside the limits.</exception>
    public static PixelImage Create(int width, int height, PixelImageFill fill)
    {
        ArgumentNullException.ThrowIfNull(fill);
        int row = CheckSize(width, height);
        var pixels = new byte[row * height];
        fill(pixels);
        return new PixelImage(width, height, pixels);
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Bytes per row: <c>Width * 4</c>.</summary>
    public int Stride => Width * 4;

    /// <summary>Process-unique identity, increasing with creation; drivers key their texture caches on it.</summary>
    public long Id { get; }

    /// <summary>The pixels, <see cref="Stride"/> bytes per row.</summary>
    public ReadOnlySpan<byte> Pixels => _pixels;

    private static int CheckSize(int width, int height)
    {
        if (width < 1 || width > MaxDimension) throw new ArgumentOutOfRangeException(nameof(width), width, $"1..{MaxDimension}.");
        if (height < 1 || height > MaxDimension) throw new ArgumentOutOfRangeException(nameof(height), height, $"1..{MaxDimension}.");
        long bytes = (long)width * height * 4;
        if (bytes > MaxBytes) throw new ArgumentOutOfRangeException(nameof(height), height, $"At most {MaxBytes} bytes.");
        return width * 4;
    }
}
