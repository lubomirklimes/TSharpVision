using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
// Locks interactions among the four independent source-backed Phase C corrections.
public sealed class PhaseCInteractionTests
{
    [Fact]
    public void DataControlsRoundTripBeforeTheirWindowClosesCleanly()
    {
        var owner = new TGroup(new TRect(0, 0, 80, 25));
        var window = new TWindow(new TRect(1, 1, 40, 10), "Data", 0);
        var input = new TInputLine(new TRect(1, 1, 20, 2), 8);
        input.SetData("before");
        window.Insert(input);
        owner.Insert(window);
        TDataRecord record = Read(window);
        int inputIndex = record.Segments.ToList().FindIndex(segment => segment.Size == input.DataSize());
        Assert.True(inputIndex >= 0);
        record.SetValue(inputIndex, "after");

        window.SetData(record);
        window.Close();

        Assert.Equal("after", input.Data);
        Assert.Null(window.owner);
        Assert.Null(input.owner);
        Assert.Equal((ushort)0, window.DataSize());
        TDataRecord closedRecord = Read(window);
        Assert.Equal((ushort)0, closedRecord.Size);
        Assert.Empty(closedRecord.Segments);
    }

    [Fact]
    public void ScreenModeChangeDoesNotAlterWindowCloseLifecycle()
    {
        using var driver = new DriverScope(80, 25);
        TDisplay.SM savedMode = TScreen.ScreenMode;
        TPoint savedShadow = TView.shadowSize;
        bool savedMarkers = TView.showMarkers;
        var program = new TProgram();
        try
        {
            var owner = new TGroup(new TRect(0, 0, 80, 25));
            var window = new TWindow(new TRect(1, 1, 30, 8), "Mode", 0);
            owner.Insert(window);
            TScreen.ScreenMode = TDisplay.SM.Mono;

            program.InitScreen();
            window.Close();

            Assert.Equal(new TPoint(0, 0), TView.shadowSize);
            Assert.Null(window.owner);
            Assert.Null(window.frame);
        }
        finally
        {
            program.ShutDown();
            TScreen.ScreenMode = savedMode;
            TView.shadowSize = savedShadow;
            TView.showMarkers = savedMarkers;
        }
    }

    [Fact]
    public void ActiveNestedGroupsStillFlattenDataIntoOneRecord()
    {
        var owner = new TGroup(new TRect(0, 0, 40, 10));
        owner.SetState(Views.sfActive, true);
        var nested = new TGroup(new TRect(0, 0, 20, 5));
        var first = new ScalarView(2, "first");
        var second = new ScalarView(3, 7);
        nested.Insert(first);
        nested.Insert(second);
        nested.SetState(Views.sfActive, true);
        owner.Insert(nested);

        TDataRecord record = Read(owner);

        Assert.True(nested.GetState(Views.sfActive));
        Assert.True(first.GetState(Views.sfActive));
        Assert.True(second.GetState(Views.sfActive));
        Assert.Equal((ushort)5, record.Size);
        Assert.Collection(
            record.Segments,
            segment =>
            {
                Assert.Equal((ushort)0, segment.Offset);
                Assert.Equal((ushort)2, segment.Size);
                Assert.Equal("first", segment.Value);
            },
            segment =>
            {
                Assert.Equal((ushort)2, segment.Offset);
                Assert.Equal((ushort)3, segment.Size);
                Assert.Equal(7, segment.Value);
            });
    }

    private static TDataRecord Read(TGroup group)
    {
        object value = new object();
        group.GetData(ref value);
        return Assert.IsType<TDataRecord>(value);
    }

    private sealed class ScalarView : TView
    {
        private readonly ushort _size;
        private object _value;

        internal ScalarView(ushort size, object value) : base(new TRect(0, 0, 1, 1))
        {
            _size = size;
            _value = value;
        }

        public override ushort DataSize() => _size;

        public override void GetData(ref object rec) => rec = _value;

        public override void SetData(object rec) => _value = rec;
    }
}
