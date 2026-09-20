using TSharpVision;
using Xunit;

namespace TSharpVision.Tests.Core;

// Original: Borland TGROUP.CPP starts at last, walks prev, and advances one contiguous record.
// Corrected: TGroup preserves that logical order and span geometry in a flattened managed record.
public sealed class GroupDataTransferTests
{
    [Fact]
    public void EmptyGroupProducesCompleteEmptyRecord()
    {
        var group = Group();
        object value = new object();

        group.GetData(ref value);

        var record = Assert.IsType<TDataRecord>(value);
        Assert.Equal(0, group.DataSize());
        Assert.Equal(0, record.Size);
        Assert.Empty(record.Segments);
        group.SetData(record);
    }

    [Fact]
    public void OneChildProducesItsExactLogicalSpan()
    {
        var log = new List<string>();
        var child = new DataView("one", 4, "alpha", log);
        var group = Group(child);

        TDataRecord record = Read(group);

        Assert.Equal((ushort)4, group.DataSize());
        Assert.Equal((ushort)4, record.Size);
        AssertSegment(record, 0, 0, 4, "alpha");
        Assert.Equal(["get:one"], log);
    }

    [Fact]
    public void HeterogeneousChildrenFollowBorlandReverseViewOrderAndExactOffsets()
    {
        var log = new List<string>();
        var first = new DataView("first", 1, "text", log);
        var second = new DataView("second", 2, (ushort)7, log);
        var third = new DataView("third", 5, 42, log);
        var group = Group(first, second, third);

        TDataRecord record = Read(group);

        Assert.Equal(["get:first", "get:second", "get:third"], log);
        AssertSegment(record, 0, 0, 1, "text");
        AssertSegment(record, 1, 1, 2, (ushort)7);
        AssertSegment(record, 2, 3, 5, 42);
        AssertContiguous(record);
    }

    [Fact]
    public void ZeroSizeChildIsRepresentedButConsumesNoSpan()
    {
        var log = new List<string>();
        var first = new DataView("first", 2, "a", log);
        var zero = new DataView("zero", 0, "marker", log);
        var last = new DataView("last", 3, "b", log);
        var group = Group(first, zero, last);

        TDataRecord record = Read(group);

        Assert.Equal(["get:first", "get:zero", "get:last"], log);
        AssertSegment(record, 0, 0, 2, "a");
        AssertSegment(record, 1, 2, 0, "marker");
        AssertSegment(record, 2, 2, 3, "b");
        AssertContiguous(record);
    }

    [Fact]
    public void NestedGroupsFlattenAndRebaseIntoOneLogicalAddressSpace()
    {
        var log = new List<string>();
        var outerFirst = new DataView("outer-first", 2, "outer", log);
        var nestedFirst = new DataView("nested-first", 3, (ushort)11, log);
        var nestedSecond = new DataView("nested-second", 1, true, log);
        var nested = Group(nestedFirst, nestedSecond);
        var outerLast = new DataView("outer-last", 4, 99L, log);
        var outer = Group(outerFirst, nested, outerLast);

        TDataRecord record = Read(outer);

        Assert.Equal(["get:outer-first", "get:nested-first", "get:nested-second", "get:outer-last"], log);
        Assert.Equal((ushort)10, record.Size);
        Assert.Equal(4, record.Segments.Count);
        AssertSegment(record, 0, 0, 2, "outer");
        AssertSegment(record, 1, 2, 3, (ushort)11);
        AssertSegment(record, 2, 5, 1, true);
        AssertSegment(record, 3, 6, 4, 99L);
        Assert.DoesNotContain(record.Segments, segment => segment.Value is TDataRecord);
        AssertContiguous(record);
    }

    [Fact]
    public void SetThenGetRoundTripsValuesAndTraversalOrder()
    {
        var log = new List<string>();
        var first = new DataView("first", 2, "before", log);
        var nestedLeaf = new DataView("nested", 4, 1, log);
        var nested = Group(nestedLeaf);
        var group = Group(first, nested);
        TDataRecord record = Read(group);
        log.Clear();
        record.SetValue(0, "after");
        record.SetValue(1, 27);

        group.SetData(record);

        Assert.Equal(["set:first", "set:nested"], log);
        Assert.Equal("after", first.Value);
        Assert.Equal(27, nestedLeaf.Value);
        log.Clear();
        TDataRecord roundTrip = Read(group);
        Assert.Equal("after", roundTrip.Segments[0].Value);
        Assert.Equal(27, roundTrip.Segments[1].Value);
        Assert.Equal(["get:first", "get:nested"], log);
    }

    [Fact]
    public void MutationAfterSetIsVisibleInNextAggregateRead()
    {
        var child = new DataView("child", 2, 1, []);
        var group = Group(child);
        TDataRecord record = Read(group);
        record.SetValue(0, 2);
        group.SetData(record);
        child.Value = 3;

        TDataRecord reread = Read(group);

        Assert.Equal(3, reread.Segments[0].Value);
    }

    [Fact]
    public void SetDataRejectsNonAggregateAndIncompatibleRecord()
    {
        var one = Group(new DataView("one", 1, "x", []));
        var two = Group(new DataView("two", 2, "y", []));
        TDataRecord wrongSize = Read(two);

        Assert.Throws<ArgumentException>(() => one.SetData("not a record"));
        Assert.Throws<ArgumentException>(() => one.SetData(wrongSize));
    }

    [Fact]
    public void SetDataRejectsDifferentSegmentGeometryEvenWhenTotalSizeMatches()
    {
        var split = Group(
            new DataView("first", 1, "a", []),
            new DataView("second", 2, "b", []));
        var single = Group(new DataView("single", 3, "c", []));
        TDataRecord sameSizeDifferentLayout = Read(split);

        Assert.Equal(single.DataSize(), sameSizeDifferentLayout.Size);
        Assert.Throws<ArgumentException>(() => single.SetData(sameSizeDifferentLayout));
    }

    [Fact]
    public void AggregateSizeArithmeticIsChecked()
    {
        var group = Group(
            new DataView("first", 40000, null, []),
            new DataView("second", 40000, null, []));

        Assert.Throws<OverflowException>(() => group.DataSize());
        object value = new object();
        Assert.Throws<OverflowException>(() => group.GetData(ref value));
    }

    [Fact]
    public void RealInputLineAndCheckBoxesTransferThroughGroupRecord()
    {
        var input = new TInputLine(new TRect(0, 0, 20, 1), 8);
        var checks = new TCheckBoxes(
            new TRect(0, 1, 20, 3),
            new TSItem("One", new TSItem("Two", null)));
        input.SetData("before");
        checks.SetData((ushort)1);
        var group = Group(input, checks);

        TDataRecord record = Read(group);

        AssertSegment(record, 0, 0, 8, "before");
        AssertSegment(record, 1, 8, 2, (ushort)1);
        record.SetValue(0, "after");
        record.SetValue(1, (ushort)2);
        group.SetData(record);
        Assert.Equal("after", input.Data);
        Assert.Equal((uint)2, checks.value);
        AssertContiguous(record);
    }

    private static TGroup Group(params TView[] children)
    {
        var group = new TGroup(new TRect(0, 0, 80, 25));
        foreach (TView child in children)
            group.Insert(child);
        return group;
    }

    private static TDataRecord Read(TGroup group)
    {
        object value = new object();
        group.GetData(ref value);
        return Assert.IsType<TDataRecord>(value);
    }

    private static void AssertSegment(TDataRecord record, int index, ushort offset, ushort size, object? value)
    {
        TDataRecord.Segment segment = record.Segments[index];
        Assert.Equal(offset, segment.Offset);
        Assert.Equal(size, segment.Size);
        Assert.Equal(value, segment.Value);
    }

    private static void AssertContiguous(TDataRecord record)
    {
        int offset = 0;
        foreach (TDataRecord.Segment segment in record.Segments)
        {
            Assert.Equal(offset, segment.Offset);
            offset = checked(offset + segment.Size);
        }
        Assert.Equal(record.Size, offset);
    }

    private sealed class DataView : TView
    {
        private readonly string _name;
        private readonly ushort _size;
        private readonly List<string> _log;

        internal DataView(string name, ushort size, object? value, List<string> log)
            : base(new TRect(0, 0, 1, 1))
        {
            _name = name;
            _size = size;
            _log = log;
            Value = value;
        }

        internal object? Value { get; set; }

        public override ushort DataSize() => _size;

        public override void GetData(ref object rec)
        {
            _log.Add($"get:{_name}");
            rec = Value!;
        }

        public override void SetData(object rec)
        {
            _log.Add($"set:{_name}");
            Value = rec;
        }
    }
}
