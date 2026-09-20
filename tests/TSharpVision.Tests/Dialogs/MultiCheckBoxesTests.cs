using System.Reflection;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Dialogs;

[Collection("NonParallel")]
public sealed class MultiCheckBoxesTests : IDisposable
{
    private readonly DriverScope _driver = new();

    public void Dispose() => _driver.Dispose();

    private static TSItem Items() => new("~O~ne", new TSItem("~T~wo", new TSItem("T~h~ree", null)));

    [Fact]
    public void ApiMetadataAndConstants_MatchBorland()
    {
        Assert.Equal(typeof(TCluster), typeof(TMultiCheckBoxes).BaseType);
        Assert.Equal((ushort)0x0101, TMultiCheckBoxes.cfOneBit);
        Assert.Equal((ushort)0x0203, TMultiCheckBoxes.cfTwoBits);
        Assert.Equal((ushort)0x040F, TMultiCheckBoxes.cfFourBits);
        Assert.Equal((ushort)0x08FF, TMultiCheckBoxes.cfEightBits);
        Assert.True(typeof(TMultiCheckBoxes).GetMethod(nameof(TMultiCheckBoxes.MultiMark))!.IsVirtual);
        Assert.True(typeof(TMultiCheckBoxes).GetMethod(nameof(TMultiCheckBoxes.Press))!.IsVirtual);
    }

    [Fact]
    public void Construction_PreservesClusterItemsAndStartsAtZero()
    {
        var boxes = Box();
        Assert.Equal(3, boxes.Strings.Count);
        Assert.Equal((uint)0, boxes.value);
        Assert.Equal((byte)0, boxes.MultiMark(0));
    }

    [Fact]
    public void Press_CyclesDownwardAndWrapsThroughSelectionRange()
    {
        var boxes = Box();
        boxes.Press(0); Assert.Equal((byte)2, boxes.MultiMark(0));
        boxes.Press(0); Assert.Equal((byte)1, boxes.MultiMark(0));
        boxes.Press(0); Assert.Equal((byte)0, boxes.MultiMark(0));
    }

    [Fact]
    public void PackedStates_AreIndependentPerItem()
    {
        var boxes = Box();
        boxes.Press(1);
        boxes.Press(2);
        boxes.Press(2);
        Assert.Equal((uint)(2 << 2 | 1 << 4), boxes.value);
        Assert.Equal((byte)0, boxes.MultiMark(0));
        Assert.Equal((byte)2, boxes.MultiMark(1));
        Assert.Equal((byte)1, boxes.MultiMark(2));
    }

    [Theory]
    [InlineData(TMultiCheckBoxes.cfOneBit, 1)]
    [InlineData(TMultiCheckBoxes.cfTwoBits, 3)]
    [InlineData(TMultiCheckBoxes.cfFourBits, 15)]
    [InlineData(TMultiCheckBoxes.cfEightBits, 255)]
    public void MultiMark_UsesLowByteMaskAndHighByteFieldWidth(ushort flags, byte expected)
    {
        var boxes = new TMultiCheckBoxes(new TRect(0, 0, 20, 1), new TSItem("x", null),
            byte.MaxValue, flags, new string('x', 256));
        boxes.SetData(uint.MaxValue);
        Assert.Equal(expected, boxes.MultiMark(0));
    }

    [Fact]
    public void OutOfRangePackedItem_DoesNotMutateManagedValue()
    {
        var boxes = Box();
        boxes.SetData(123u);
        boxes.Press(16);
        Assert.Equal((uint)123, boxes.value);
        Assert.Equal((byte)0, boxes.MultiMark(16));
    }

    [Fact]
    public void DataTransfer_IsFourByteScalarAndRoundTripsUInt32()
    {
        var boxes = Box();
        boxes.SetData(0xDEADBEEFu);
        object data = new object();
        boxes.GetData(ref data);
        Assert.Equal((ushort)4, boxes.DataSize());
        Assert.Equal(0xDEADBEEFu, Assert.IsType<uint>(data));
    }

    [Fact]
    public void DataTransfer_AcceptsSignedLongIntBitPatternAndRejectsOtherTypes()
    {
        var boxes = Box();
        boxes.SetData(-1);
        Assert.Equal(uint.MaxValue, boxes.value);
        Assert.Throws<ArgumentException>(() => boxes.SetData((ushort)1));
    }

    [Fact]
    public void GroupDataTransfer_UsesOneLogicalFourUnitLeafSpan()
    {
        var group = new TGroup(new TRect(0, 0, 30, 5));
        var boxes = Box();
        boxes.SetData(9u);
        group.Insert(boxes);
        object data = new object();
        group.GetData(ref data);
        var record = Assert.IsType<TDataRecord>(data);
        var segment = Assert.Single(record.Segments);
        Assert.Equal((ushort)4, record.Size);
        Assert.Equal((ushort)0, segment.Offset);
        Assert.Equal((ushort)4, segment.Size);
        Assert.Equal((uint)9, segment.Value);
    }

    [Fact]
    public void GroupDataTransfer_RoundTripsThePackedScalarThroughTDataRecord()
    {
        var group = new TGroup(new TRect(0, 0, 30, 5));
        var boxes = Box();
        group.Insert(boxes);
        object data = new object();
        group.GetData(ref data);
        var record = Assert.IsType<TDataRecord>(data);
        record.SetValue(0, 0x12345678u);
        group.SetData(record);
        Assert.Equal(0x12345678u, boxes.value);
    }

    [Fact]
    public void SpaceHotkeyAndMouse_UseInheritedClusterPressPath()
    {
        var boxes = new MouseProbe();
        boxes.state |= Views.sfFocused;
        TEvent space = Key(0, ' '); boxes.HandleEvent(ref space);
        Assert.Equal((byte)2, boxes.MultiMark(0));
        TEvent hotkey = Key(0, 't'); boxes.HandleEvent(ref hotkey);
        Assert.Equal((byte)2, boxes.MultiMark(1));
        TEvent click = Mouse(0, 2); boxes.HandleEvent(ref click);
        Assert.Equal((byte)2, boxes.MultiMark(2));
    }

    [Fact]
    public void DisabledItem_UsesDisabledPaletteAndCanDisableClusterSelection()
    {
        var boxes = new DrawingProbe();
        boxes.SetButtonState(1u << 1, false);
        boxes.Draw();
        Assert.False(boxes.ButtonState(1));
        Assert.Equal((TColorAttr)0x1F, boxes.Attributes[1][0]);
        boxes.SetButtonState(uint.MaxValue, false);
        Assert.Equal(0, boxes.options & Views.ofSelectable);
    }

    [Fact]
    public void DisabledItem_IsSkippedByKeyboardAndIgnoredByMouseAndHotkey()
    {
        var boxes = new DrawingProbe();
        boxes.state |= Views.sfFocused;
        boxes.SetButtonState(1u << 1, false);
        TEvent down = Key(Keys.kbDown); boxes.HandleEvent(ref down);
        Assert.Equal(2, boxes.sel);
        uint before = boxes.value;
        TEvent hotkey = Key(0, 't'); boxes.HandleEvent(ref hotkey);
        TEvent click = Mouse(0, 1); boxes.HandleEvent(ref click);
        Assert.Equal(before, boxes.value);
        Assert.Equal(2, boxes.sel);
    }

    [Fact]
    public void Draw_UsesStateStringAtPackedMarkerIndex()
    {
        var boxes = new DrawingProbe();
        boxes.Press(0);
        boxes.Draw();
        Assert.Equal('2', boxes.Rows[0][2]);
        Assert.Equal('O', boxes.Rows[0][5]);
    }

    [Fact]
    public void Palette_RemainsHistoricalFiveEntryClusterPalette()
    {
        Assert.Equal(5, Box().GetPalette().Size);
    }

    [Fact]
    public void Streaming_RoundTripsFullValueConfigurationAndEnabledMask()
    {
        var source = Box();
        source.SetData(0xFEDCBA98u);
        source.SetButtonState(1u << 1, false);
        using var stream = new MemoryStream();
        source.Write(new Opstream(stream));
        stream.Position = 0;
        var restored = (TMultiCheckBoxes)TMultiCheckBoxes.Build();
        restored.Read(new Ipstream(stream));
        Assert.Equal(0xFEDCBA98u, restored.value);
        Assert.False(restored.ButtonState(1));
        restored.SetData(0u);
        restored.Press(0);
        Assert.Equal((byte)2, restored.MultiMark(0));
    }

    [Fact]
    public void StreamRegistrationAndName_AreConcrete()
    {
        Pstream.DeInitTypes();
        StreamableRegistration.RegisterAll();
        Assert.Same(TMultiCheckBoxes.StreamableClassTMultiCheckBoxes,
            Pstream.types.Lookup(TMultiCheckBoxes.Name));
        Assert.Equal(TMultiCheckBoxes.Name, Box().StreamableName());
    }

    [Fact]
    public void PublicNullability_RequiresItemsAndStateMarkerString()
    {
        ConstructorInfo constructor = typeof(TMultiCheckBoxes).GetConstructors().Single();
        var context = new NullabilityInfoContext();
        Assert.Equal(NullabilityState.NotNull, context.Create(constructor.GetParameters()[1]).ReadState);
        Assert.Equal(NullabilityState.NotNull, context.Create(constructor.GetParameters()[4]).ReadState);
    }

    private static TMultiCheckBoxes Box() =>
        new(new TRect(0, 0, 30, 3), Items(), 3, TMultiCheckBoxes.cfTwoBits, " 12");

    private static TEvent Key(ushort code, char character = '\0')
    {
        TEvent ev = default;
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = code;
        ev.keyDown.charScan.charCode = (byte)character;
        return ev;
    }

    private static TEvent Mouse(int x, int y)
    {
        TEvent ev = default;
        ev.What = Events.evMouseDown;
        ev.mouse.where = new TPoint(x, y);
        return ev;
    }

    private class MouseProbe : TMultiCheckBoxes
    {
        public MouseProbe() : base(new TRect(0, 0, 30, 3), Items(), 3, cfTwoBits, " 12") { }
        public override bool MouseEvent(ref TEvent ev, ushort mask) => false;
    }

    private sealed class DrawingProbe : MouseProbe
    {
        public Dictionary<int, string> Rows { get; } = new();
        public Dictionary<int, TColorAttr[]> Attributes { get; } = new();
        public override void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> cells)
        {
            string row = new(cells[..w].ToArray().Select(cell => cell.Character).ToArray());
            TColorAttr[] attrs = cells[..w].ToArray().Select(cell => cell.Attr).ToArray();
            Rows[y] = row;
            Attributes[y] = attrs;
        }
    }
}
