// Win32 console key/mouse translator table tests.
// Pure translation: no real console I/O, no P/Invoke side effects.
using SharpVision;
using SharpVision.Constants;
using SharpVision.Drivers.Console;
using Xunit;

namespace SharpVision.Tests.Drivers;

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
        Assert.NotEqual(0, kev.keyDown.shiftState & Keys.kbShift);
        Assert.NotEqual(0, kev.keyDown.shiftState & Keys.kbAltShift);
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
    public void Win32Mouse_Move()
    {
        var mev = Win32ConsoleDriver.TranslateMouse(0x0000, 0x0001 /*MOUSE_MOVED*/, 7, 3);
        Assert.Equal(Events.evMouseMove, mev.What);
        Assert.Equal(7, mev.mouse.where.x);
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
