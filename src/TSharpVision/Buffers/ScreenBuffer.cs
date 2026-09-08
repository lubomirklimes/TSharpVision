namespace TSharpVision;

/// <summary>A row-major array of managed screen cells. Dimensions are cell counts.</summary>
public class ScreenBuffer
{
    private readonly TScreenChar[] _buffer;
    public uint Width { get; }
    public uint Height { get; }
    public Memory<TScreenChar> BufferMemory => _buffer;
    public Span<TScreenChar> Data => _buffer;
    public int Size => _buffer.Length;

    /// <summary>Creates a linear single-row buffer; zero cells gives a 0x0 buffer.</summary>
    public ScreenBuffer(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        Width = (uint)size;
        Height = size == 0 ? 0u : 1u;
        _buffer = new TScreenChar[size];
        Clear();
    }

    public ScreenBuffer(uint width, uint height)
    {
        Width = width;
        Height = height;
        _buffer = new TScreenChar[checked((int)((ulong)width * height))];
        Clear();
    }

    /// <summary>Allocation units per managed cell (one), not byte size or buffer length.</summary>
    public static int GetSize() => 1;

    /// <summary>Returns a mutable draw-buffer view over all cells, without copying.</summary>
    public TDrawBuffer GetSpan() => new(_buffer);

    private int Index(uint x, uint y)
    {
        if (x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
        if (y >= Height) throw new ArgumentOutOfRangeException(nameof(y));
        return checked((int)((ulong)y * Width + x));
    }

    public void SetChar(uint x, uint y, TScreenChar c) => _buffer[Index(x, y)] = c;
    public TScreenChar GetChar(uint x, uint y) => _buffer[Index(x, y)];

    public void Clear() => Array.Fill(_buffer, new TScreenChar(' ', ConsoleColor.White, ConsoleColor.Black));
}
