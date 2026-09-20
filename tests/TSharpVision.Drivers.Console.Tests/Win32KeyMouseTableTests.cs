// Win32 console key/mouse translator table tests.
// Pure translation: no real console I/O, no P/Invoke side effects.
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers.Console;
using Xunit;

namespace TSharpVision.Tests.Drivers;

public sealed class Win32KeyMouseTableTests
{
    private readonly Win32ConsoleDriver _w32 = new();

    // ── Function keys ─────────────────────────────────────────────────────

    [Fact]
    public void Win32_F1_NoModifier()
    {
        bool ok = _w32.TryTranslateKey(true, 0x70, '\0', 0, out var kev);
        Assert.True(ok);
        Assert.Equal(Events.evKeyDown, kev.What);
        Assert.Equal(Keys.kbF1, kev.keyDown.keyCode);
    }

    [Fact]
    public void Win32_ShiftF1()
    {
        bool ok = _w32.TryTranslateKey(true, 0x70, '\0', Win32KeyTranslator.SHIFT_PRESSED, out var kev);
        Assert.True(ok);
        Assert.Equal(Keys.kbShiftF1, kev.keyDown.keyCode);
    }

    [Fact]
    public void Win32_CtrlF12()
    {
        bool ok = _w32.TryTranslateKey(true, 0x7B, '\0', Win32KeyTranslator.LEFT_CTRL_PRESSED, out var kev);
        Assert.True(ok);
        Assert.Equal(Keys.kbCtrlF12, kev.keyDown.keyCode);
    }

    // ── Alt-key ───────────────────────────────────────────────────────────

    [Fact]
    public void Win32_AltX()
    {
        bool ok = _w32.TryTranslateKey(true, (ushort)'X', '\0', Win32KeyTranslator.LEFT_ALT_PRESSED, out var kev);
        Assert.True(ok);
        Assert.Equal(Keys.kbAltX, kev.keyDown.keyCode);
    }

    // ── Common keys ───────────────────────────────────────────────────────

    [Fact]
    public void Win32_Esc()
    {
        bool ok = _w32.TryTranslateKey(true, 0x1B, (char)0x1B, 0, out var kev);
        Assert.True(ok);
        Assert.Equal(Keys.kbEsc, kev.keyDown.keyCode);
    }

    [Fact]
    public void Win32_PlainA()
    {
        bool ok = _w32.TryTranslateKey(true, 0x41, 'a', 0, out var kev);
        Assert.True(ok);
        Assert.Equal(0x61, kev.keyDown.keyCode);
    }

    [Fact]
    public void Win32_CtrlC()
    {
        bool ok = _w32.TryTranslateKey(true, 0x43, '\u0003', Win32KeyTranslator.LEFT_CTRL_PRESSED, out var kev);
        Assert.True(ok);
        Assert.Equal(Keys.kbCtrlC, kev.keyDown.keyCode);
    }

    [Fact]
    public void Win32_Up()
    {
        bool ok = _w32.TryTranslateKey(true, 0x26, '\0', 0, out var kev);
        Assert.True(ok);
        Assert.Equal(Keys.kbUp, kev.keyDown.keyCode);
    }

    [Fact]
    public void Win32_CtrlLeft()
    {
        bool ok = _w32.TryTranslateKey(true, 0x25, '\0', Win32KeyTranslator.LEFT_CTRL_PRESSED, out var kev);
        Assert.True(ok);
        Assert.Equal(Keys.kbCtrlLeft, kev.keyDown.keyCode);
    }

    [Fact]
    public void Win32_Tab()
    {
        bool ok = _w32.TryTranslateKey(true, 0x09, '\t', 0, out var kev);
        Assert.True(ok);
        Assert.Equal(Keys.kbTab, kev.keyDown.keyCode);
    }

    [Fact]
    public void Win32_ShiftTab()
    {
        bool ok = _w32.TryTranslateKey(true, 0x09, '\t', Win32KeyTranslator.SHIFT_PRESSED, out var kev);
        Assert.True(ok);
        Assert.Equal(Keys.kbShiftTab, kev.keyDown.keyCode);
    }

    // ── Filtered records ──────────────────────────────────────────────────

    [Fact]
    public void Win32_KeyUp_Filtered()
    {
        bool ok = _w32.TryTranslateKey(false, 0x41, 'a', 0, out _);
        Assert.False(ok);
    }

    [Fact]
    public void Win32_BareShift_Filtered()
    {
        bool ok = _w32.TryTranslateKey(true, 0x10, '\0', Win32KeyTranslator.SHIFT_PRESSED, out _);
        Assert.False(ok);
    }

    // ── Shift-state propagation ───────────────────────────────────────────

    [Fact]
    public void Win32_ShiftAlt_ShiftStateMerged()
    {
        bool ok = _w32.TryTranslateKey(true, 0x70, '\0',
            Win32KeyTranslator.SHIFT_PRESSED | Win32KeyTranslator.LEFT_ALT_PRESSED, out var kev);
        Assert.True(ok);
        Assert.NotEqual(0u, kev.keyDown.controlKeyState & Keys.kbShift);
        Assert.NotEqual(0u, kev.keyDown.controlKeyState & Keys.kbAltShift);
    }

    // ── Mouse translator ─────────────────────────────────────────────────

    [Fact]
    public void Win32Mouse_LeftClick()
    {
        var mev = Win32ConsoleDriver.TranslateMouse(0x0001, 0x0000, 10, 5);
        Assert.Equal(Events.evMouseDown, mev.What);
        Assert.Equal(0x01, mev.mouse.buttons);
        Assert.Equal(10, mev.mouse.where.x);
        Assert.Equal(5, mev.mouse.where.y);
    }

    [Fact]
    public void Win32Mouse_RightClick()
    {
        var mev = Win32ConsoleDriver.TranslateMouse(0x0002, 0x0000, 0, 0);
        Assert.Equal(Events.evMouseDown, mev.What);
        Assert.Equal(0x02, mev.mouse.buttons);
    }

    [Fact]
    public void Win32Mouse_MiddleClickExtensionIsPreserved()
    {
        var mev = Win32ConsoleDriver.TranslateMouse(0x0004, 0, 0, 0);
        Assert.Equal(Events.evMouseDown, mev.What);
        Assert.Equal(Events.mbMiddleButton, mev.mouse.buttons);
    }

    [Theory]
    [InlineData(0x0008u, 0x08)]
    [InlineData(0x0010u, 0x10)]
    public void Win32Mouse_SideButtonsArePhysical(uint native, int expected)
    {
        var mev = Win32ConsoleDriver.TranslateMouse(native, 0, 0, 0);
        Assert.Equal(Events.evMouseDown, mev.What);
        Assert.Equal(expected, mev.mouse.buttons);
    }

    [Fact]
    public void Win32Mouse_Move()
    {
        var mev = Win32ConsoleDriver.TranslateMouse(0x0000, 0x0001 /*MOUSE_MOVED*/, 7, 3);
        Assert.Equal(Events.evMouseMove, mev.What);
        Assert.Equal(7, mev.mouse.where.x);
        Assert.Equal(Events.meMouseMoved, mev.mouse.eventFlags);
    }

    [Fact]
    public void Win32Mouse_Up()
    {
        var mev = Win32ConsoleDriver.TranslateMouse(0x0000, 0x0000, 4, 4);
        Assert.Equal(Events.evMouseUp, mev.What);
        Assert.Equal(0, mev.mouse.buttons);
    }

    [Fact]
    public void Win32Mouse_DoubleClick()
    {
        var mev = Win32ConsoleDriver.TranslateMouse(0x0001, 0x0002 /*DOUBLE_CLICK*/, 1, 1);
        Assert.Equal(Events.evMouseDown, mev.What);
        Assert.True(mev.mouse.doubleClick);
        Assert.Equal(Events.meDoubleClick, mev.mouse.eventFlags);
    }

    [Theory]
    [InlineData(0x0010u, Keys.kbShift)]
    [InlineData(0x0008u, Keys.kbCtrlShift)]
    [InlineData(0x0002u, Keys.kbAltShift)]
    [InlineData(0x0018u, Keys.kbShift | Keys.kbCtrlShift)]
    [InlineData(0x001Au, Keys.kbShift | Keys.kbCtrlShift | Keys.kbAltShift)]
    public void Win32Mouse_TranslatesNativeControlState(uint native, uint expected)
    {
        var mev = Win32ConsoleDriver.TranslateMouse(0x0001, 0, 1, 2, native);
        Assert.Equal(expected, mev.mouse.controlKeyState);
    }

    [Fact]
    public void Win32Mouse_WheelCarriesTranslatedControlState()
    {
        var mev = Win32ConsoleDriver.TranslateMouse(120u << 16, 0x0004, 1, 2, 0x0018);
        Assert.Equal(Events.evMouseWheel, mev.What);
        Assert.Equal(Events.meWheelUp, mev.mouse.eventFlags);
        Assert.Equal(0, mev.mouse.buttons);
        Assert.Equal(Keys.kbShift | Keys.kbCtrlShift, mev.mouse.controlKeyState);
    }

    [Fact]
    public void Win32Mouse_PreservesReportedLockStates()
    {
        uint native = Win32KeyTranslator.CAPSLOCK_ON
            | Win32KeyTranslator.NUMLOCK_ON | Win32KeyTranslator.SCROLLLOCK_ON;
        var mev = Win32ConsoleDriver.TranslateMouse(0x0001, 0, 1, 2, native);
        Assert.Equal(
            Keys.kbCapsState | Keys.kbNumState | Keys.kbScrollState,
            mev.mouse.controlKeyState);
    }

    // ── Driver lifecycle ──────────────────────────────────────────────────

    [Fact]
    public void Win32_Lifecycle_NoThrow()
    {
        var w32 = new Win32ConsoleDriver();
        w32.Initialize();
        Assert.False(w32.ReadKeyEvent(out _));
        w32.Shutdown();
    }
}
