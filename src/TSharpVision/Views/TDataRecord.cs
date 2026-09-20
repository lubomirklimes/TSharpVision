namespace TSharpVision;

/// <summary>
/// Represents the logical contiguous data record transferred by a <see cref="TGroup"/>.
/// Offsets and sizes use <see cref="TView.DataSize"/> units; they are not .NET object
/// addresses, serialized-byte offsets, or structure-marshalling offsets.
/// </summary>
public sealed class TDataRecord
{
    private readonly List<Segment> _segments = new();
    private readonly IReadOnlyList<Segment> _readOnlySegments;

    internal TDataRecord(ushort size)
    {
        Size = size;
        _readOnlySegments = _segments.AsReadOnly();
    }

    /// <summary>Gets the total logical record size, equal to the originating group's aggregate <see cref="TView.DataSize"/>.</summary>
    public ushort Size { get; }

    /// <summary>Gets the flattened leaf segments in Turbo Vision transfer order.</summary>
    public IReadOnlyList<Segment> Segments => _readOnlySegments;

    /// <summary>Replaces the managed value of one segment without changing its logical offset or size.</summary>
    /// <param name="segmentIndex">Zero-based index in <see cref="Segments"/>.</param>
    /// <param name="value">Value that will be passed to the corresponding leaf view's <see cref="TView.SetData"/> method.</param>
    public void SetValue(int segmentIndex, object? value)
    {
        Segment segment = _segments[segmentIndex];
        _segments[segmentIndex] = new Segment(segment.Offset, segment.Size, value);
    }

    internal void Append(int offset, ushort size, object? value)
    {
        int expectedOffset = 0;
        if (_segments.Count != 0)
        {
            Segment previous = _segments[^1];
            expectedOffset = checked(previous.Offset + previous.Size);
        }

        if (offset != expectedOffset)
            throw new InvalidOperationException("Aggregate data segments must be contiguous and ordered.");

        int end = checked(offset + size);
        if (end > Size)
            throw new InvalidOperationException("Aggregate data segment exceeds the record size.");

        _segments.Add(new Segment(checked((ushort)offset), size, value));
    }

    internal object? Read(int segmentIndex, int offset, ushort size)
    {
        if ((uint)segmentIndex >= (uint)_segments.Count)
            throw new ArgumentException("The aggregate data record has too few segments.");

        Segment segment = _segments[segmentIndex];
        if (segment.Offset != offset || segment.Size != size)
            throw new ArgumentException("The aggregate data record layout does not match the target group.");

        return segment.Value;
    }

    internal void EnsureComplete(int segmentIndex, int offset)
    {
        if (segmentIndex != _segments.Count || offset != Size)
            throw new ArgumentException("The aggregate data record does not exactly cover the target group's data size.");
    }

    /// <summary>Describes one flattened leaf value and its exact span in the logical aggregate record.</summary>
    public readonly struct Segment
    {
        internal Segment(ushort offset, ushort size, object? value)
        {
            Offset = offset;
            Size = size;
            Value = value;
        }

        /// <summary>Gets the leaf's logical Turbo Vision data-record offset.</summary>
        public ushort Offset { get; }

        /// <summary>Gets the leaf's logical span size, equal to that leaf's <see cref="TView.DataSize"/>.</summary>
        public ushort Size { get; }

        /// <summary>Gets the managed scalar value produced by the leaf view.</summary>
        public object? Value { get; }
    }
}

internal sealed class TDataRecordCursor
{
    internal static object NoValue { get; } = new();

    internal TDataRecordCursor(TDataRecord record)
    {
        Record = record;
    }

    internal TDataRecord Record { get; }
    internal int Offset { get; private set; }
    internal int SegmentIndex { get; private set; }

    internal void Append(ushort size, object? value)
    {
        Record.Append(Offset, size, value);
        Offset = checked(Offset + size);
        SegmentIndex++;
    }

    internal object? Read(ushort size)
    {
        object? value = Record.Read(SegmentIndex, Offset, size);
        Offset = checked(Offset + size);
        SegmentIndex++;
        return value;
    }

    internal void EnsureComplete() => Record.EnsureComplete(SegmentIndex, Offset);
}
