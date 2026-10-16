// Win32 driver hardening tests.
// Covers headless checks:
//   §1 NullDriver Suspend/Resume lifecycle
//   §2 TEventQueue wheel/click interleave
//   §3 NullDriver.SimulateResize → cmScreenResized + TScreen dimensions
//   §4 Win32KeyTranslator static TryTranslate key-up suppression
//   §5 TranslateMouse WHEELED|MOVED combination — wheel takes precedence
//   §6 Win32KeyTranslator key-coverage gaps (CtrlEnter, AltBack, F12, CtrlF1, AltF1)
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers.Console;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Drivers;

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
        wheel.mouse.eventFlags = Events.meWheelUp;
        wheel.mouse.where   = new TPoint(10, 5);
        TEventQueue.Enqueue(wheel);

        TEvent got = default;
        TEventQueue.GetNextEvent(ref got);
        Assert.Equal(Events.evMouseWheel, got.What);
    }

    [Fact]
    public void Interleave_WheelUpFlag_Preserved()
    {
        TEvent wheel = default;
        wheel.What          = Events.evMouseWheel;
        wheel.mouse.eventFlags = Events.meWheelUp;
        wheel.mouse.where   = new TPoint(10, 5);
        TEventQueue.Enqueue(wheel);

        TEvent got = default;
        TEventQueue.GetNextEvent(ref got);
        Assert.True((got.mouse.eventFlags & Events.meWheelUp) != 0);
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
        wheel.mouse.eventFlags = Events.meWheelUp;
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

    // ── §3b — Win32ConsoleDriver: a buffer-size record that changes nothing is not a resize ──

    private static int QueuedResizes()
    {
        int count = 0;
        for (int i = 0; i < 20; i++)
        {
            TEvent ev = default;
            TEventQueue.GetNextEvent(ref ev);
            if (ev.What == Events.evNothing) break;
            if (ev.What == Events.evCommand && ev.message.command == Views.cmScreenResized) count++;
        }

        return count;
    }

    [Fact]
    public void Win32Driver_UnchangedWindowSize_IsNotReportedAsAResize()
    {
        // The record of a change the terminal made before Initialize() read the size is still queued
        // at the first pump: it reads the size already in use. Reporting it replaced the screen buffer
        // with an empty one and had the application clear a screen that was already complete.
        var driver = new Win32ConsoleDriver();
        driver.SetWindowSizeForTests(160, 45);
        ScreenBuffer? buffer = TScreen.ScreenBuffer;
        ushort width = TScreen.ScreenWidth;
        ushort height = TScreen.ScreenHeight;

        Assert.False(driver.ApplyWindowSize(160, 45));

        Assert.Equal(0, QueuedResizes());
        Assert.Same(buffer, TScreen.ScreenBuffer);
        Assert.Equal(width, TScreen.ScreenWidth);
        Assert.Equal(height, TScreen.ScreenHeight);
    }

    [Fact]
    public void Win32Driver_ChangedWindowSize_IsReportedOnce()
    {
        var driver = new Win32ConsoleDriver();
        driver.SetWindowSizeForTests(160, 45);
        ScreenBuffer? buffer = TScreen.ScreenBuffer;

        Assert.True(driver.ApplyWindowSize(120, 30));

        Assert.Equal(1, QueuedResizes());
        Assert.NotSame(buffer, TScreen.ScreenBuffer);
        Assert.Equal(160, TScreen.ScreenWidth);
        Assert.Equal(45, TScreen.ScreenHeight);
        Assert.Equal(160 * 45, TScreen.ScreenBuffer!.Data.Length);
    }

    [Theory]
    [InlineData(160, 30)]
    [InlineData(120, 45)]
    public void Win32Driver_AChangeInOneDimension_IsAResize(ushort previousCols, ushort previousRows)
    {
        var driver = new Win32ConsoleDriver();
        driver.SetWindowSizeForTests(160, 45);

        Assert.True(driver.ApplyWindowSize(previousCols, previousRows));
        Assert.Equal(1, QueuedResizes());
    }

    // ── §3c — driver discovery: a driver package is a candidate without being loaded first ──

    [Fact]
    public void DriverDiscovery_TheConsoleDriverPackageIsACandidate()
    {
        // Discovery decides from metadata which assemblies beside the application are worth loading.
        // The package that declares [ScreenDriver] classes must be one of them.
        var method = typeof(TSharpVision.Drivers.ScreenDriverFactory).GetMethod(
            "MayDefineDriver", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

        Assert.True((bool)method.Invoke(null, [typeof(Win32ConsoleDriver).Assembly.Location])!);
    }

    [Fact]
    public void DriverDiscovery_FindsTheConsoleDriver()
    {
        var field = typeof(TSharpVision.Drivers.ScreenDriverFactory).GetField(
            "DriverTypes", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

        Assert.Contains(typeof(Win32ConsoleDriver), (Type[])field.GetValue(null)!);
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
    public void TranslateMouse_WheeledAndMoved_PositiveDelta_HasWheelUpFlag()
    {
        uint posDelta = (uint)((short)120 << 16);
        const uint MOUSE_WHEELED = 0x0004;
        const uint MOUSE_MOVED   = 0x0001;
        var ev = Win32ConsoleDriver.TranslateMouse(posDelta, MOUSE_WHEELED | MOUSE_MOVED, 5, 3);
        Assert.True((ev.mouse.eventFlags & Events.meWheelUp) != 0);
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
    public void TranslateMouse_WheeledAndMoved_NegativeDelta_HasWheelDownFlag()
    {
        uint negDelta = unchecked((uint)((short)(-120) << 16));
        const uint MOUSE_WHEELED = 0x0004;
        const uint MOUSE_MOVED   = 0x0001;
        var ev = Win32ConsoleDriver.TranslateMouse(negDelta, MOUSE_WHEELED | MOUSE_MOVED, 5, 3);
        Assert.True((ev.mouse.eventFlags & Events.meWheelDown) != 0);
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
