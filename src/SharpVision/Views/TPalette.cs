// In upstream Turbo Vision a palette is stored as a single byte vector where
// data[0] holds the entry count and data[1..n] hold the per-index color
// mappings. TView::mapColor walks this layout chained through ancestor groups
// to translate a logical color number into a physical TColorAttr.
namespace SharpVision;

public class TPalette
{
    /// <summary>
    /// Raw palette buffer. Index 0 holds <see cref="Size"/>, indices 1..Size
    /// hold the actual mapping bytes. Kept public for parity with upstream
    /// where TView::mapColor accesses palette.data directly.
    /// </summary>
    public byte[] Data { get; private set; }

    /// <summary>Number of palette entries (mirrors upstream <c>data[0]</c>).</summary>
    public int Size => Data[0];

    /// <summary>
    /// Constructs a palette from <paramref name="data"/> entries. The string
    /// is treated as raw bytes; non-ASCII chars are masked to a byte to
    /// preserve the 1:1 mapping the C++ code uses with <c>const char*</c>.
    /// </summary>
    public TPalette(string data, int size)
    {
        Data = new byte[size + 1];
        Data[0] = (byte)size;
        int copy = System.Math.Min(size, data?.Length ?? 0);
        for (int i = 0; i < copy; i++)
            Data[i + 1] = (byte)data![i];
    }

    public TPalette(byte[] rawWithLeadingSize)
    {
        Data = (byte[])rawWithLeadingSize.Clone();
    }

    public TPalette(TPalette other)
    {
        Data = (byte[])other.Data.Clone();
    }

    public TPalette Clone() => new TPalette(this);

    /// <summary>
    /// Indexer mirroring upstream <c>uchar&amp; operator[](int)</c>. Index 0
    /// is the size byte; valid mapping indices are 1..Size.
    /// </summary>
    public byte this[int index]
    {
        get => Data[index];
        set => Data[index] = value;
    }
}
