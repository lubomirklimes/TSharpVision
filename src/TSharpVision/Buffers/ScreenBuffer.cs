namespace TSharpVision;

/// <summary>A row-major array of managed screen cells. Dimensions are cell counts.</summary>
public class ScreenBuffer
{
    private readonly TScreenChar[] _buffer;
    /// <summary>Number of character cells in each row.</summary>
    public uint Width { get; }
    /// <summary>Number of rows in the buffer.</summary>
    public uint Height { get; }
    /// <summary>Mutable memory over all cells in row-major order, without a copy.</summary>
    public Memory<TScreenChar> BufferMemory => _buffer;
    /// <summary>Mutable span over all cells in row-major order, without a copy.</summary>
    public Span<TScreenChar> Data => _buffer;
    /// <summary>Total number of cells, equal to width times height.</summary>
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

    /// <summary>Allocates a rectangular cell buffer and fills it with white-on-black spaces.</summary>
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

    /// <summary>Replaces the cell at a zero-based column and row; both coordinates must be within the buffer.</summary>
    public void SetChar(uint x, uint y, TScreenChar c) => _buffer[Index(x, y)] = c;
    /// <summary>Returns the cell at a zero-based column and row; both coordinates must be within the buffer.</summary>
    public TScreenChar GetChar(uint x, uint y) => _buffer[Index(x, y)];

    /// <summary>Fills every cell with a white-on-black space.</summary>
    public void Clear() => Array.Fill(_buffer, new TScreenChar(' ', ConsoleColor.White, ConsoleColor.Black));
}
