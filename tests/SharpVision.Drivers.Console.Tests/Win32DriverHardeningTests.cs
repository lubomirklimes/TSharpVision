// Win32 driver hardening tests.
// Covers headless checks:
//   §1 NullDriver Suspend/Resume lifecycle
//   §2 TEventQueue wheel/click interleave
//   §3 NullDriver.SimulateResize → cmScreenResized + TScreen dimensions
//   §4 Win32KeyTranslator static TryTranslate key-up suppression
//   §5 TranslateMouse WHEELED|MOVED combination — wheel takes precedence
//   §6 Win32KeyTranslator key-coverage gaps (CtrlEnter, AltBack, F12, CtrlF1, AltF1)
using SharpVision;
using SharpVision.Constants;
using SharpVision.Drivers.Console;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.Drivers;

[Collection("NonParallel")]
public sealed class Win32DriverHardeningTests : IDisposable
{
    private readonly DriverScope _ds;

    public Win32DriverHardeningTests()
    {
        _ds = new DriverScope();
        TEventQueue.Resume();
    }

    public void Dispose() => _ds.Dispose();

    // ── §1 — NullDriver Suspend/Resume lifecycle ──────────────────────────

    [Fact]
    public void NullDriver_SuspendResume_NoThrow()
    {
        var ex = Record.Exception(() => { _ds.Driver.Suspend(); _ds.Driver.Resume(); });
        Assert.Null(ex);
    }

    [Fact]
    public void NullDriver_GetCols_UnchangedAfterSuspendResume()
    {
        ushort before = _ds.Driver.GetCols();
        _ds.Driver.Suspend();
        _ds.Driver.Resume();
        Assert.Equal(before, _ds.Driver.GetCols());
    }

    [Fact]
    public void NullDriver_GetRows_UnchangedAfterSuspendResume()
    {
        ushort before = _ds.Driver.GetRows();
        _ds.Driver.Suspend();
        _ds.Driver.Resume();
        Assert.Equal(before, _ds.Driver.GetRows());
    }

    [Fact]
    public void NullDriver_ReadKeyEvent_UsableAfterResume()
    {
        _ds.Driver.Suspend();
        _ds.Driver.Resume();
        // No key enqueued — ReadKeyEvent returns false (not crashes).
        Assert.False(_ds.Driver.ReadKeyEvent(out _));
    }

    // ── §2 — TEventQueue wheel/click interleave ───────────────────────────

    // Verifies the wheel event does not corrupt _lastMouse so that the
    // subsequent click is still recognised as evMouseDown (not evNothing).

    [Fact]
    public void Interleave_Click1_IsEvMouseDown()
    {
        TEvent down1 = default;
        down1.What          = Events.evMouseDown;
        down1.mouse.buttons = (byte)Events.mbLeftButton;
        down1.mouse.where   = new TPoint(10, 5);
        TEventQueue.Enqueue(down1);

        TEvent g = default;
        TEventQueue.GetNextEvent(ref g);
        Assert.Equal(Events.evMouseDown, g.What);
    }

    [Fact]
    public void Interleave_Release_IsEvMouseUp()
    {
        // Press first so _lastMouse.buttons is non-zero.
        TEvent down = default;
        down.What          = Events.evMouseDown;
        down.mouse.buttons = (byte)Events.mbLeftButton;
        down.mouse.where   = new TPoint(10, 5);
        TEventQueue.Enqueue(down);
        TEvent g = default;
        TEventQueue.GetNextEvent(ref g); // consume press

        // Enqueue a release (evMouseDown with buttons==0 triggers UP detection).
        TEvent release = default;
        release.What          = Events.evMouseDown;
        release.mouse.buttons = 0;
        release.mouse.where   = new TPoint(10, 5);
        TEventQueue.Enqueue(release);

        TEvent up = default;
        TEventQueue.GetNextEvent(ref up);
        Assert.Equal(Events.evMouseUp, up.What);
    }

    [Fact]
    public void Interleave_WheelBetweenClicks_IsEvMouseWheel()
    {
        // Prime _lastMouse to buttons=0 (clean state after release).
        TEvent release = default;
        release.What          = Events.evMouseDown;
        release.mouse.buttons = 0;
        release.mouse.where   = new TPoint(10, 5);
        TEventQueue.Enqueue(release);
        TEvent tmp = default; TEventQueue.GetNextEvent(ref tmp);

        TEvent wheel = default;
        wheel.What          = Events.evMouseWheel;
        wheel.mouse.buttons = (byte)Events.mbButton4;
        wheel.mouse.where   = new TPoint(10, 5);
        TEventQueue.Enqueue(wheel);

        TEvent got = default;
        TEventQueue.GetNextEvent(ref got);
        Assert.Equal(Events.evMouseWheel, got.What);
    }

    [Fact]
    public void Interleave_WheelMbButton4_Preserved()
    {
        TEvent wheel = default;
        wheel.What          = Events.evMouseWheel;
        wheel.mouse.buttons = (byte)Events.mbButton4;
        wheel.mouse.where   = new TPoint(10, 5);
        TEventQueue.Enqueue(wheel);

        TEvent got = default;
        TEventQueue.GetNextEvent(ref got);
        Assert.True((got.mouse.buttons & Events.mbButton4) != 0);
    }

    [Fact]
    public void Interleave_Click2AfterWheel_IsEvMouseDown_NotDoubleClick()
    {
        // After a wheel event, a click at a DIFFERENT position must not be
        // flagged as a double-click (position mismatch prevents it).
        TEvent press1 = default;
        press1.What          = Events.evMouseDown;
        press1.mouse.buttons = (byte)Events.mbLeftButton;
        press1.mouse.where   = new TPoint(10, 5);
        TEventQueue.Enqueue(press1);
        TEvent g1 = default; TEventQueue.GetNextEvent(ref g1);

        TEvent rel = default;
        rel.What          = Events.evMouseDown;
        rel.mouse.buttons = 0;
        rel.mouse.where   = new TPoint(10, 5);
        TEventQueue.Enqueue(rel);
        TEvent gUp = default; TEventQueue.GetNextEvent(ref gUp);

        TEvent wheel = default;
        wheel.What          = Events.evMouseWheel;
        wheel.mouse.buttons = (byte)Events.mbButton4;
        wheel.mouse.where   = new TPoint(10, 5);
        TEventQueue.Enqueue(wheel);
        TEvent gW = default; TEventQueue.GetNextEvent(ref gW);

        // Click at a DIFFERENT position so doubleClick cannot fire.
        TEvent press2 = default;
        press2.What          = Events.evMouseDown;
        press2.mouse.buttons = (byte)Events.mbLeftButton;
        press2.mouse.where   = new TPoint(20, 8);
        TEventQueue.Enqueue(press2);
        TEvent g2 = default;
        TEventQueue.GetNextEvent(ref g2);

        Assert.Equal(Events.evMouseDown, g2.What);
        Assert.False(g2.mouse.doubleClick);
    }

    // ── §3 — NullDriver.SimulateResize → cmScreenResized ─────────────────

    [Fact]
    public void SimulateResize_DeliversCmScreenResized()
    {
        // DriverScope already installed _ds.Driver as TDisplay.driver.
        _ds.Driver.SimulateResize(100, 30);

        int count = 0;
        for (int i = 0; i < 20; i++)
        {
            TEvent ev = default;
            TEventQueue.GetNextEvent(ref ev);
            if (ev.What == Events.evNothing) break;
            if (ev.What == Events.evCommand
                && ev.message.command == Views.cmScreenResized)
                count++;
        }

        Assert.Equal(1, count);
    }

    [Fact]
    public void SimulateResize_UpdatesTScreenWidth()
    {
        _ds.Driver.SimulateResize(100, 30);
        // Drain queue.
        for (int i = 0; i < 10; i++)
        {
            TEvent ev = default;
            TEventQueue.GetNextEvent(ref ev);
            if (ev.What == Events.evNothing) break;
        }
        Assert.Equal(100, TScreen.ScreenWidth);
    }

    [Fact]
    public void SimulateResize_UpdatesTScreenHeight()
    {
        _ds.Driver.SimulateResize(100, 30);
        for (int i = 0; i < 10; i++)
        {
            TEvent ev = default;
            TEventQueue.GetNextEvent(ref ev);
            if (ev.What == Events.evNothing) break;
        }
        Assert.Equal(30, TScreen.ScreenHeight);
    }

    // ── §4 — Win32KeyTranslator static TryTranslate key-up suppression ───

    [Fact]
    public void Win32KeyTranslator_KeyUp_F1_ReturnsFalse()
    {
        bool ok = Win32KeyTranslator.TryTranslate(false, 0x70, '\0', 0, out _);
        Assert.False(ok);
    }

    [Fact]
    public void Win32KeyTranslator_KeyUp_Enter_ReturnsFalse()
    {
        bool ok = Win32KeyTranslator.TryTranslate(false, 0x0D, '\r', 0, out _);
        Assert.False(ok);
    }

    [Fact]
    public void Win32KeyTranslator_KeyUp_A_ReturnsFalse()
    {
        bool ok = Win32KeyTranslator.TryTranslate(false, 0x41, 'a', 0, out _);
        Assert.False(ok);
    }

    // ── §5 — TranslateMouse WHEELED|MOVED: wheel takes precedence ─────────

    [Fact]
    public void TranslateMouse_WheeledAndMoved_PositiveDelta_IsEvMouseWheel()
    {
        uint posDelta = (uint)((short)120 << 16);
        const uint MOUSE_WHEELED = 0x0004;
        const uint MOUSE_MOVED   = 0x0001;
        var ev = Win32ConsoleDriver.TranslateMouse(posDelta, MOUSE_WHEELED | MOUSE_MOVED, 5, 3);
        Assert.Equal(Events.evMouseWheel, ev.What);
    }

    [Fact]
    public void TranslateMouse_WheeledAndMoved_PositiveDelta_IsMbButton4()
    {
        uint posDelta = (uint)((short)120 << 16);
        const uint MOUSE_WHEELED = 0x0004;
        const uint MOUSE_MOVED   = 0x0001;
        var ev = Win32ConsoleDriver.TranslateMouse(posDelta, MOUSE_WHEELED | MOUSE_MOVED, 5, 3);
        Assert.True((ev.mouse.buttons & Events.mbButton4) != 0);
    }

    [Fact]
    public void TranslateMouse_WheeledAndMoved_PositiveDelta_NotEvMouseMove()
    {
        uint posDelta = (uint)((short)120 << 16);
        const uint MOUSE_WHEELED = 0x0004;
        const uint MOUSE_MOVED   = 0x0001;
        var ev = Win32ConsoleDriver.TranslateMouse(posDelta, MOUSE_WHEELED | MOUSE_MOVED, 5, 3);
        Assert.NotEqual(Events.evMouseMove, ev.What);
    }

    [Fact]
    public void TranslateMouse_WheeledAndMoved_NegativeDelta_IsEvMouseWheel()
    {
        uint negDelta = unchecked((uint)((short)(-120) << 16));
        const uint MOUSE_WHEELED = 0x0004;
        const uint MOUSE_MOVED   = 0x0001;
        var ev = Win32ConsoleDriver.TranslateMouse(negDelta, MOUSE_WHEELED | MOUSE_MOVED, 5, 3);
        Assert.Equal(Events.evMouseWheel, ev.What);
    }

    [Fact]
    public void TranslateMouse_WheeledAndMoved_NegativeDelta_IsMbButton5()
    {
        uint negDelta = unchecked((uint)((short)(-120) << 16));
        const uint MOUSE_WHEELED = 0x0004;
        const uint MOUSE_MOVED   = 0x0001;
        var ev = Win32ConsoleDriver.TranslateMouse(negDelta, MOUSE_WHEELED | MOUSE_MOVED, 5, 3);
        Assert.True((ev.mouse.buttons & Events.mbButton5) != 0);
    }

    // ── §6 — Win32KeyTranslator key-coverage gaps ─────────────────────────

    [Fact]
    public void Win32KeyTranslator_CtrlEnter_IsKbCtrlEnter()
    {
        bool ok = Win32KeyTranslator.TryTranslate(
            true, 0x0D, '\n', Win32KeyTranslator.LEFT_CTRL_PRESSED, out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbCtrlEnter, ev.keyDown.keyCode);
    }

    [Fact]
    public void Win32KeyTranslator_AltBack_IsKbAltBack()
    {
        bool ok = Win32KeyTranslator.TryTranslate(
            true, 0x08, '\b', Win32KeyTranslator.LEFT_ALT_PRESSED, out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbAltBack, ev.keyDown.keyCode);
    }

    [Fact]
    public void Win32KeyTranslator_F12_IsKbF12()
    {
        bool ok = Win32KeyTranslator.TryTranslate(true, 0x7B, '\0', 0, out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbF12, ev.keyDown.keyCode);
    }

    [Fact]
    public void Win32KeyTranslator_CtrlF1_IsKbCtrlF1()
    {
        bool ok = Win32KeyTranslator.TryTranslate(
            true, 0x70, '\0', Win32KeyTranslator.LEFT_CTRL_PRESSED, out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbCtrlF1, ev.keyDown.keyCode);
    }

    [Fact]
    public void Win32KeyTranslator_AltF1_IsKbAltF1()
    {
        bool ok = Win32KeyTranslator.TryTranslate(
            true, 0x70, '\0', Win32KeyTranslator.LEFT_ALT_PRESSED, out var ev);
        Assert.True(ok);
        Assert.Equal(Keys.kbAltF1, ev.keyDown.keyCode);
    }
}
