using System.Reflection;
using System.Runtime.InteropServices;
using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.Tests.Core;

public sealed class MouseEventCompatibilityTests
{
    [Fact]
    public void HistoricalMousePayload_HasExactLogicalFieldShapeAndOrder()
    {
        FieldInfo[] fields = typeof(MouseEventType).GetFields(BindingFlags.Instance | BindingFlags.Public);
        Assert.Equal(["where", "eventFlags", "controlKeyState", "buttons"],
            fields.OrderBy(f => f.MetadataToken).Select(f => f.Name));
        Assert.Equal(typeof(TPoint), fields[0].FieldType);
        Assert.Equal(typeof(uint), fields[1].FieldType);
        Assert.Equal(typeof(uint), fields[2].FieldType);
        Assert.Equal(typeof(byte), fields[3].FieldType);
        Assert.Equal(0, Marshal.OffsetOf<MouseEventType>("where").ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<MouseEventType>("eventFlags").ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<MouseEventType>("controlKeyState").ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<MouseEventType>("buttons").ToInt32());
    }

    [Fact]
    public void HistoricalMouseConstants_AreExact_AndWheelRemainsAdditive()
    {
        Assert.Equal(0x01u, Events.meMouseMoved);
        Assert.Equal(0x02u, Events.meDoubleClick);
        Assert.Equal(0x04u, Events.meWheelUp);
        Assert.Equal(0x08u, Events.meWheelDown);
        Assert.Equal(0x10u, Events.meWheelLeft);
        Assert.Equal(0x20u, Events.meWheelRight);
        uint[] flags = [Events.meMouseMoved, Events.meDoubleClick, Events.meWheelUp,
            Events.meWheelDown, Events.meWheelLeft, Events.meWheelRight];
        Assert.Equal(flags.Length, flags.Distinct().Count());
        Assert.Equal(0x01, Events.mbLeftButton);
        Assert.Equal(0x02, Events.mbRightButton);
        Assert.Equal(0x04, Events.mbMiddleButton);
        Assert.Equal(0x08, Events.mbButton4);
        Assert.Equal(0x10, Events.mbButton5);
        Assert.InRange(Events.mbButton5, 0, byte.MaxValue);
        Assert.Equal(0x000F, Events.evMouse);
        Assert.Equal(0x0020, Events.evMouseWheel);
    }

    [Fact]
    public void DoubleClick_IsDerivedFromEventFlagsWithoutIndependentStorage()
    {
        Assert.Null(typeof(MouseEventType).GetField("doubleClick"));
        Assert.NotNull(typeof(MouseEventType).GetProperty("doubleClick"));
        MouseEventType mouse = default;
        mouse.eventFlags = Events.meDoubleClick;
        Assert.True(mouse.doubleClick);
        mouse.doubleClick = false;
        Assert.Equal(0u, mouse.eventFlags & Events.meDoubleClick);
        mouse.doubleClick = true;
        Assert.NotEqual(0u, mouse.eventFlags & Events.meDoubleClick);

        mouse.eventFlags = Events.meWheelUp;
        Assert.False(mouse.doubleClick);
        mouse.eventFlags |= Events.meDoubleClick;
        Assert.True(mouse.doubleClick);
    }

    [Fact]
    public void Queue_ClassifiesMovementWithHistoricalFlag()
    {
        ResetQueue();
        TEvent down = Mouse(Events.evMouseDown, 1, 3, 4);
        TEventQueue.Enqueue(down);
        TEvent got = default;
        TEventQueue.GetNextEvent(ref got);

        TEvent moved = Mouse(Events.evMouseDown, 1, 4, 4);
        TEventQueue.Enqueue(moved);
        TEventQueue.GetNextEvent(ref got);
        Assert.Equal(Events.evMouseMove, got.What);
        Assert.Equal(Events.meMouseMoved, got.mouse.eventFlags & Events.meMouseMoved);
    }

    [Fact]
    public void Queue_DoesNotClassifyThirdClickAsAnotherDoubleClick()
    {
        ResetQueue();
        TEvent got = default;
        for (int click = 1; click <= 3; click++)
        {
            TEventQueue.Enqueue(Mouse(Events.evMouseDown, 1, 7, 8));
            TEventQueue.GetNextEvent(ref got);
            Assert.Equal(click == 2, got.mouse.doubleClick);
            TEventQueue.Enqueue(Mouse(Events.evMouseUp, 0, 7, 8));
            TEventQueue.GetNextEvent(ref got);
        }
    }

    [Fact]
    public void InputTrace_FormatsCompletePublicMousePayload()
    {
        TEvent ev = Mouse(Events.evMouseWheel, (byte)Events.mbButton4, 11, 6);
        ev.mouse.eventFlags = Events.meWheelLeft;
        ev.mouse.controlKeyState = Keys.kbShift | Keys.kbCtrlShift;
        string trace = InputTrace.FormatEvent(ev);
        Assert.Contains("evMouseWheel", trace);
        Assert.Contains("btn=0x08", trace);
        Assert.Contains("at=(11,6)", trace);
        Assert.Contains("flags=0x00000010", trace);
        Assert.Contains("wheel=Left", trace);
        Assert.Contains($"control=0x{(Keys.kbShift | Keys.kbCtrlShift):X8}", trace);
    }

    [Theory]
    [InlineData(0x04u, "Up")]
    [InlineData(0x08u, "Down")]
    [InlineData(0x10u, "Left")]
    [InlineData(0x20u, "Right")]
    public void InputTrace_NamesEveryWheelDirection(uint flag, string name)
    {
        TEvent ev = Mouse(Events.evMouseWheel, 0, 1, 2);
        ev.mouse.eventFlags = flag;
        Assert.Contains($"wheel={name}", InputTrace.FormatEvent(ev));
    }

    private static TEvent Mouse(ushort what, byte buttons, int x, int y)
    {
        TEvent ev = default;
        ev.What = what;
        ev.mouse.buttons = buttons;
        ev.mouse.where = new TPoint(x, y);
        return ev;
    }

    private static void ResetQueue()
    {
        TEventQueue.Resume();
        TEvent discarded = default;
        do TEventQueue.GetNextEvent(ref discarded);
        while (discarded.What != Events.evNothing);
        TEventQueue.Resume();
    }
}
