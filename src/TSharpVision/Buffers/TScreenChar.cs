using TSharpVision.Text;

namespace TSharpVision;

/// <summary>How a <see cref="TScreenChar"/> takes part in a grapheme cluster.</summary>
public enum TScreenCellKind
{
    /// <summary>A cell holding exactly <see cref="TScreenChar.Character"/>.</summary>
    Single,
    /// <summary>The first cell of a cluster that needs more than one UTF-16 unit or more than one cell.</summary>
    Lead,
    /// <summary>A continuation cell covered by the lead to its left; its <see cref="TScreenChar.Character"/> is '\0'.</summary>
    Trail,
}

/// <summary>
/// One screen cell: a Unicode character plus a <see cref="TColorAttr"/>.
/// Mirrors upstream's packed <c>ushort</c> (low byte char, high byte attr).
/// </summary>
/// <remarks>
/// <para>A cell can also lead a grapheme cluster (an emoji, a combining sequence, a supplementary character) that
/// occupies <see cref="CellWidth"/> cells: the lead keeps the cluster in a payload and a fallback character in
/// <see cref="Character"/> (the base character, or U+FFFD), and each following cell of the cluster is a
/// <see cref="TScreenCellKind.Trail"/> whose <see cref="Character"/> is '\0'. Code that only reads
/// <see cref="Character"/> therefore still sees one sensible character per cell.</para>
/// <para>Writing <see cref="Character"/> directly turns a lead into an ordinary <see cref="TScreenCellKind.Single"/>
/// cell: a lead is a lead only while <see cref="Character"/> still holds its fallback.</para>
/// <para>The struct is 8 bytes and unmanaged (TSharpVision 1.2 and earlier: 4 bytes).</para>
/// </remarks>
public struct TScreenChar
{
    /// <summary>Foreground and background color attributes attached to the cell.</summary>
    public TColorAttr Attr;
    // Kind (bits 0-1), payload-is-cluster-handle (bit 2), cell width of a lead (bits 3-5).
    private byte _flags;
    /// <summary>UTF-16 character stored in the screen cell.</summary>
    public char Character;
    // Lead: a Unicode scalar value, or a ClusterTable handle when bit 2 of _flags is set.
    private int _payload;

    private const byte KindMask = 0x03;
    private const byte KindLead = 0x01;
    private const byte KindTrail = 0x02;
    private const byte HandleFlag = 0x04;
    private const int WidthShift = 3;

    /// <summary>The largest <see cref="CellWidth"/> a lead can carry.</summary>
    public const int MaxCellWidth = 7;

    /// <summary>Creates a screen cell containing the supplied character and color attributes.</summary>
    public TScreenChar(char character, TColorAttr attr)
    {
        Character = character;
        Attr = attr;
    }

    /// <summary>
    /// Backwards-compat constructor used by <see cref="ScreenBuffer.Clear"/>.
    /// The console color enums are translated to a TColorAttr by treating the
    /// foreground/background pair as the low/high nibble of the attr byte.
    /// </summary>
    public TScreenChar(char character, System.ConsoleColor foreground, System.ConsoleColor background)
    {
        Character = character;
        Attr = new TColorAttr((byte)((((int)background) & 0x0F) << 4 | (((int)foreground) & 0x0F)));
    }

    /// <summary>The cell's role in a grapheme cluster.</summary>
    public readonly TScreenCellKind Kind
    {
        get
        {
            switch (_flags & KindMask)
            {
                case KindLead:
                    return Character == Fallback() ? TScreenCellKind.Lead : TScreenCellKind.Single;
                case KindTrail:
                    return Character == '\0' ? TScreenCellKind.Trail : TScreenCellKind.Single;
                default:
                    return TScreenCellKind.Single;
            }
        }
    }

    /// <summary>Cells the cell's cluster occupies from here: the lead's width, 0 for a trail, 1 otherwise.</summary>
    public readonly int CellWidth => Kind switch
    {
        TScreenCellKind.Lead => (_flags >> WidthShift) & 7,
        TScreenCellKind.Trail => 0,
        _ => 1,
    };

    /// <summary>
    /// The text the cell draws: the whole cluster for a lead, nothing for a trail, otherwise
    /// <see cref="Character"/>. <paramref name="scratch"/> (at least two chars) backs the result when needed.
    /// </summary>
    public readonly ReadOnlySpan<char> GetText(Span<char> scratch)
    {
        switch (Kind)
        {
            case TScreenCellKind.Trail:
                return ReadOnlySpan<char>.Empty;
            case TScreenCellKind.Lead:
                if ((_flags & HandleFlag) != 0)
                    return ClusterTable.Shared.Get(_payload) ?? "\uFFFD";
                return scratch.Slice(0, new System.Text.Rune(_payload).EncodeToUtf16(scratch));
            default:
                scratch[0] = Character;
                return scratch.Slice(0, 1);
        }
    }

    /// <summary>The text the cell draws, as a string (allocates; see <see cref="GetText(Span{char})"/>).</summary>
    public readonly string GetText()
    {
        Span<char> scratch = stackalloc char[2];
        return GetText(scratch).ToString();
    }

    /// <summary>
    /// Creates the lead cell of a grapheme cluster that occupies <paramref name="cellWidth"/> cells (1 to
    /// <see cref="MaxCellWidth"/>); the caller writes <c>cellWidth - 1</c> <see cref="CreateTrail"/> cells after it.
    /// A single BMP character one cell wide becomes an ordinary <see cref="TScreenCellKind.Single"/> cell.
    /// </summary>
    public static TScreenChar CreateCluster(ReadOnlySpan<char> cluster, int cellWidth, TColorAttr attr) =>
        CreateCluster(cluster, cellWidth, attr, ClusterTable.Shared);

    internal static TScreenChar CreateCluster(ReadOnlySpan<char> cluster, int cellWidth, TColorAttr attr, ClusterTable table)
    {
        cellWidth = Math.Clamp(cellWidth, 1, MaxCellWidth);
        if (cluster.IsEmpty) return new TScreenChar(' ', attr);
        if (cluster.Length == 1 && !char.IsSurrogate(cluster[0]) && cellWidth == 1)
            return new TScreenChar(cluster[0], attr);

        var cell = new TScreenChar('\0', attr) { _flags = (byte)(KindLead | (cellWidth << WidthShift)) };
        if (System.Text.Rune.DecodeFromUtf16(cluster, out System.Text.Rune rune, out int used) == System.Buffers.OperationStatus.Done
            && used == cluster.Length)
        {
            cell._payload = rune.Value;
        }
        else if (table.TryIntern(cluster, out int handle))
        {
            cell._flags |= HandleFlag;
            cell._payload = handle;
            cell.Character = FallbackOf(table.Get(handle)!);
            return cell;
        }
        else
        {
            cell._payload = 0xFFFD;
        }
        cell.Character = FallbackOf(cell._payload);
        return cell;
    }

    /// <summary>Creates a continuation cell of the cluster led by the cell to its left.</summary>
    public static TScreenChar CreateTrail(TColorAttr attr) =>
        new('\0', attr) { _flags = KindTrail };

    private readonly char Fallback()
    {
        if ((_flags & HandleFlag) == 0) return FallbackOf(_payload);
        string? text = ClusterTable.Shared.Get(_payload);
        return text is null ? '\uFFFD' : FallbackOf(text);
    }

    private static char FallbackOf(int scalar) => scalar is > 0 and <= 0xFFFF and (< 0xD800 or > 0xDFFF) ? (char)scalar : '\uFFFD';

    private static char FallbackOf(string cluster) => char.IsSurrogate(cluster[0]) ? '\uFFFD' : cluster[0];
}
