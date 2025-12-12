// SDL mouse translator + PixelToCell tests.
// Pure translation: no SDL runtime, no window.
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers.SDL;
using Xunit;

namespace TSharpVision.Tests.Drivers;

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
    }

    [Fact]
    public void MakeEvent_DoubleClick()
    {
        var ev = SdlMouseTranslator.MakeEvent(
            SdlMouseEventKind.Down, SdlMouseTranslator.SDL_BUTTON_LEFT, 0, 0, clicks: 2);
        Assert.Equal(Events.evMouseDown, ev.What);
        Assert.True(ev.mouse.doubleClick);
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
    public void MakeWheelEvent_PositiveDelta_Button4()
    {
        bool ok = SdlMouseTranslator.MakeWheelEvent(3f, 10, 5, out var ev);
        Assert.True(ok);
        Assert.Equal(Events.evMouseWheel, ev.What);
        Assert.NotEqual(0, ev.mouse.buttons & Events.mbButton4);
        Assert.Equal(10, ev.mouse.where.x);
        Assert.Equal(5, ev.mouse.where.y);
    }

    [Fact]
    public void MakeWheelEvent_NegativeDelta_Button5()
    {
        bool ok = SdlMouseTranslator.MakeWheelEvent(-2f, 3, 7, out var ev);
        Assert.True(ok);
        Assert.Equal(Events.evMouseWheel, ev.What);
        Assert.NotEqual(0, ev.mouse.buttons & Events.mbButton5);
    }

    [Fact]
    public void MakeWheelEvent_ZeroDelta_ReturnsFalse()
    {
        bool ok = SdlMouseTranslator.MakeWheelEvent(0f, 0, 0, out _);
        Assert.False(ok);
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
