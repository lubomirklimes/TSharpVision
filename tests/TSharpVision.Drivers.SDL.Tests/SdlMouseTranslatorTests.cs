// SDL mouse translator + PixelToCell tests.
// Pure translation: no SDL runtime, no window.
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers.SDL;
using Xunit;

namespace TSharpVision.Drivers.SDL.Tests;

public sealed class SdlMouseTranslatorTests
{
    // ── MakeEvent ─────────────────────────────────────────────────────────

    [Fact]
    public void MakeEvent_LeftDown()
    {
        var ev = SdlMouseTranslator.MakeEvent(
            SdlMouseEventKind.Down, SdlMouseTranslator.SDL_BUTTON_LEFT, 5, 7);
        Assert.Equal(Events.evMouseDown, ev.What);
        Assert.Equal(0x01, ev.mouse.buttons);
        Assert.Equal(5, ev.mouse.where.x);
        Assert.Equal(7, ev.mouse.where.y);
        Assert.False(ev.mouse.doubleClick);
    }

    [Fact]
    public void MakeEvent_RightDown()
    {
        var ev = SdlMouseTranslator.MakeEvent(
            SdlMouseEventKind.Down, SdlMouseTranslator.SDL_BUTTON_RIGHT, 1, 1);
        Assert.Equal(Events.evMouseDown, ev.What);
        Assert.Equal(0x02, ev.mouse.buttons);
    }

    [Fact]
    public void MakeEvent_MiddleDownExtensionIsPreserved()
    {
        var ev = SdlMouseTranslator.MakeEvent(
            SdlMouseEventKind.Down, SdlMouseTranslator.SDL_BUTTON_MIDDLE, 1, 1);
        Assert.Equal(Events.evMouseDown, ev.What);
        Assert.Equal(Events.mbMiddleButton, ev.mouse.buttons);
    }

    [Theory]
    [InlineData(SdlMouseTranslator.SDL_BUTTON_X1, 0x08)]
    [InlineData(SdlMouseTranslator.SDL_BUTTON_X2, 0x10)]
    public void MakeEvent_SideButtonsArePhysical(byte button, int expected)
    {
        var ev = SdlMouseTranslator.MakeEvent(SdlMouseEventKind.Down, button, 1, 1);
        Assert.Equal(expected, ev.mouse.buttons);
    }

    [Fact]
    public void MakeEvent_LeftUp()
    {
        var ev = SdlMouseTranslator.MakeEvent(
            SdlMouseEventKind.Up, SdlMouseTranslator.SDL_BUTTON_LEFT, 5, 7);
        Assert.Equal(Events.evMouseUp, ev.What);
        Assert.Equal(0, ev.mouse.buttons);
    }

    [Fact]
    public void MakeEvent_Move_NoButtons()
    {
        var ev = SdlMouseTranslator.MakeEvent(SdlMouseEventKind.Move, 0, 9, 9);
        Assert.Equal(Events.evMouseMove, ev.What);
        Assert.Equal(0, ev.mouse.buttons);
        Assert.Equal(Events.meMouseMoved, ev.mouse.eventFlags);
    }

    [Fact]
    public void MakeEvent_DoubleClick()
    {
        var ev = SdlMouseTranslator.MakeEvent(
            SdlMouseEventKind.Down, SdlMouseTranslator.SDL_BUTTON_LEFT, 0, 0, clicks: 2);
        Assert.Equal(Events.evMouseDown, ev.What);
        Assert.True(ev.mouse.doubleClick);
        Assert.Equal(Events.meDoubleClick, ev.mouse.eventFlags);
    }

    // ── Move with held button ─────────────────────────────────────────────

    [Fact]
    public void MakeEvent_Move_WithHeldLeftButton()
    {
        var ev = SdlMouseTranslator.MakeEvent(
            SdlMouseEventKind.Move, 0, 8, 3, heldButtons: 0x01);
        Assert.Equal(Events.evMouseMove, ev.What);
        Assert.Equal(0x01, ev.mouse.buttons);
        Assert.Equal(8, ev.mouse.where.x);
        Assert.Equal(3, ev.mouse.where.y);
    }

    [Fact]
    public void MakeEvent_Move_WithHeldRightButton()
    {
        var ev = SdlMouseTranslator.MakeEvent(
            SdlMouseEventKind.Move, 0, 2, 9, heldButtons: 0x02);
        Assert.Equal(Events.evMouseMove, ev.What);
        Assert.Equal(0x02, ev.mouse.buttons);
    }

    // ── MakeWheelEvent ────────────────────────────────────────────────────

    [Fact]
    public void MakeWheelEvent_PositiveDelta_WheelUp()
    {
        bool ok = SdlMouseTranslator.MakeWheelEvent(3f, 10, 5, out var ev);
        Assert.True(ok);
        Assert.Equal(Events.evMouseWheel, ev.What);
        Assert.Equal(Events.meWheelUp, ev.mouse.eventFlags);
        Assert.Equal(0, ev.mouse.buttons);
        Assert.Equal(10, ev.mouse.where.x);
        Assert.Equal(5, ev.mouse.where.y);
    }

    [Fact]
    public void MakeWheelEvent_NegativeDelta_WheelDown()
    {
        bool ok = SdlMouseTranslator.MakeWheelEvent(-2f, 3, 7, out var ev);
        Assert.True(ok);
        Assert.Equal(Events.evMouseWheel, ev.What);
        Assert.Equal(Events.meWheelDown, ev.mouse.eventFlags);
        Assert.Equal(0, ev.mouse.buttons);
    }

    [Fact]
    public void MakeWheelEvent_ZeroDelta_ReturnsFalse()
    {
        bool ok = SdlMouseTranslator.MakeWheelEvent(0f, 0, 0, out _);
        Assert.False(ok);
    }

    [Fact]
    public void MakeEvent_ClickCarriesLogicalModifierSnapshot()
    {
        uint modifiers = Keys.kbShift | Keys.kbCtrlShift | Keys.kbAltShift;
        var ev = SdlMouseTranslator.MakeEvent(
            SdlMouseEventKind.Down, SdlMouseTranslator.SDL_BUTTON_LEFT, 1, 2,
            controlKeyState: modifiers);
        Assert.Equal(modifiers, ev.mouse.controlKeyState);
    }

    [Fact]
    public void MakeWheelEvent_CarriesLogicalModifierSnapshot()
    {
        Assert.True(SdlMouseTranslator.MakeWheelEvent(
            1, 1, 2, out var ev, Keys.kbCtrlShift));
        Assert.Equal(Keys.kbCtrlShift, ev.mouse.controlKeyState);
    }

    [Theory]
    [InlineData(1f, 0f, false, 0x20u)]
    [InlineData(-1f, 0f, false, 0x10u)]
    [InlineData(0f, 1f, false, 0x04u)]
    [InlineData(0f, -1f, false, 0x08u)]
    [InlineData(1f, 0f, true, 0x10u)]
    [InlineData(0f, 1f, true, 0x08u)]
    public void MakeWheelEvents_MapsAxesAndFlippedDirection(
        float x, float y, bool flipped, uint expected)
    {
        TEvent ev = Assert.Single(SdlMouseTranslator.MakeWheelEvents(
            x, y, flipped, 4, 5));
        Assert.Equal(expected, ev.mouse.eventFlags);
        Assert.Equal(0, ev.mouse.buttons);
    }

    [Fact]
    public void MakeWheelEvents_DualAxisIsVerticalThenHorizontal()
    {
        TEvent[] events = SdlMouseTranslator.MakeWheelEvents(
            2, -3, flipped: false, 4, 5,
            heldButtons: (byte)(Events.mbLeftButton | Events.mbButton4),
            controlKeyState: Keys.kbShift);
        Assert.Equal(2, events.Length);
        Assert.Equal(Events.meWheelDown, events[0].mouse.eventFlags);
        Assert.Equal(Events.meWheelRight, events[1].mouse.eventFlags);
        Assert.All(events, ev =>
        {
            Assert.Equal((byte)(Events.mbLeftButton | Events.mbButton4), ev.mouse.buttons);
            Assert.Equal(Keys.kbShift, ev.mouse.controlKeyState);
        });
    }

    // ── PixelToCell ────────────────────────────────────────────────────────

    [Fact]
    public void PixelToCell_Normal()
    {
        var pt = SdlMouseTranslator.PixelToCell(36, 78, 12, 26);
        Assert.Equal(3, pt.x);
        Assert.Equal(3, pt.y);
    }

    [Fact]
    public void PixelToCell_Origin()
    {
        var pt = SdlMouseTranslator.PixelToCell(0, 0, 12, 26);
        Assert.Equal(0, pt.x);
        Assert.Equal(0, pt.y);
    }

    [Fact]
    public void PixelToCell_ExactCellBoundary()
    {
        var pt = SdlMouseTranslator.PixelToCell(12, 26, 12, 26);
        Assert.Equal(1, pt.x);
        Assert.Equal(1, pt.y);
    }

    [Fact]
    public void PixelToCell_NegativeCoords_ClampedToZero()
    {
        var pt = SdlMouseTranslator.PixelToCell(-5, -10, 12, 26);
        Assert.Equal(0, pt.x);
        Assert.Equal(0, pt.y);
    }
}
