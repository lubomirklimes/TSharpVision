using TSharpVision.Constants;
using TSharpVision.Drivers;
using Xunit;

namespace TSharpVision.Tests.Core;

public sealed class ModifierEventApiTests
{
    [Fact]
    public void ExistingEventValuesAreUnchangedAndModifierChangedUsesAFreeBit()
    {
        Assert.Equal(0x0001, Events.evMouseDown);
        Assert.Equal(0x0002, Events.evMouseUp);
        Assert.Equal(0x0004, Events.evMouseMove);
        Assert.Equal(0x0008, Events.evMouseAuto);
        Assert.Equal(0x0010, Events.evKeyDown);
        Assert.Equal(0x0020, Events.evMouseWheel);
        Assert.Equal(0x0040, Events.evModifierChanged);
        Assert.Equal(0x0080, Events.evKeyUp);
        Assert.Equal(0x0100, Events.evCommand);
        Assert.Equal(0x0200, Events.evBroadcast);
        Assert.Equal(0x0010, Events.evKeyboard);
    }

    [Fact]
    public void ModifierAccessorReusesKeyboardPayloadWithoutAddingStorage()
    {
        TEvent ev = default;
        ev.Modifiers = Keys.kbShift | Keys.kbCtrlShift;

        Assert.Equal(ev.keyDown.controlKeyState, ev.Modifiers);
        Assert.Equal(new[] { "What", "mouse", "keyDown", "message" },
            typeof(TEvent).GetFields().Select(field => field.Name));
        Assert.DoesNotContain(typeof(TEvent).GetFields(), field => field.Name == nameof(TEvent.Modifiers));
        Assert.Equal(
            ["charScan", "keyCode", "controlKeyState", "raw_scanCode"],
            typeof(KeyDownEvent).GetFields().Where(field => field.IsPublic).Select(field => field.Name));
        Assert.Equal(typeof(uint), typeof(KeyDownEvent).GetField("controlKeyState")!.FieldType);
        Assert.Equal(typeof(string), typeof(KeyDownEvent).GetProperty(nameof(KeyDownEvent.text))!.PropertyType);
    }

    [Fact]
    public void ExistingDriverGetsSafeUnsupportedCapabilityDefault()
    {
        IDriver driver = new NullDriver();
        Assert.Equal(KeyboardCapabilities.None, driver.KeyboardCapabilities);
    }

    [Fact]
    public void OldStyleConsumerCanIgnoreModifierChangedAndHandleFollowingKeyDown()
    {
        TEvent[] input =
        [
            new() { What = Events.evModifierChanged, Modifiers = Keys.kbShift },
            new() { What = Events.evKeyDown, Modifiers = Keys.kbShift },
            new() { What = Events.evKeyUp, Modifiers = Keys.kbShift },
            new() { What = Events.evModifierChanged, Modifiers = 0 },
        ];
        input[1].keyDown.keyCode = 'A';
        input[2].keyDown.keyCode = 'A';

        TEvent handled = Assert.Single(input, ev => ev.What == Events.evKeyDown);
        Assert.Equal((ushort)'A', handled.keyDown.keyCode);
        Assert.Equal(Keys.kbShift, handled.Modifiers);
    }

    [Fact]
    public void ExtendedKeyboardSequenceUsesSharedPayloadAndPostTransitionState()
    {
        TEvent[] events =
        [
            new() { What = Events.evModifierChanged, Modifiers = Keys.kbShift },
            new() { What = Events.evKeyDown, Modifiers = Keys.kbShift },
            new() { What = Events.evKeyUp, Modifiers = Keys.kbShift },
            new() { What = Events.evModifierChanged, Modifiers = 0 },
        ];
        events[1].keyDown.keyCode = Keys.kbDown;
        events[2].keyDown.keyCode = Keys.kbDown;

        Assert.Equal(
            [Events.evModifierChanged, Events.evKeyDown, Events.evKeyUp, Events.evModifierChanged],
            events.Select(ev => ev.What));
        Assert.Equal([Keys.kbShift, Keys.kbShift, Keys.kbShift, 0], events.Select(ev => ev.Modifiers));
        Assert.Equal(events[1].keyDown.keyCode, events[2].keyDown.keyCode);
    }
}
