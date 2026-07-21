// Dirty-rendering scheduling tests for SDLDriver. Pure: no SDL runtime, no window.
// SetCursorType and SetCaretPosition set _dirty unconditionally, so they are reachable headless.
// WriteBuf is gated on _attached and therefore cannot be covered here.
using TSharpVision.Drivers.SDL;
using Xunit;

namespace TSharpVision.Drivers.SDL.Tests;

public sealed class SdlDirtyRenderingTests
{
    // ── Initial state ─────────────────────────────────────────────────────

    [Fact]
    public void DirtyFlag_StartsTrue()
    {
        // The driver must render at least once on startup.
        var d = new SDLDriver();
        Assert.True(d.IsDirty);
    }

    [Fact]
    public void DirtyRenderCount_StartsZero()
    {
        var d = new SDLDriver();
        Assert.Equal(0, d.DirtyRenderCount);
    }

    // ── ClearDirtyForTest helper ──────────────────────────────────────────

    [Fact]
    public void ClearDirtyForTest_ClearsFlag()
    {
        var d = new SDLDriver();
        Assert.True(d.IsDirty);

        d.ClearDirtyForTest();
        Assert.False(d.IsDirty);
    }

    // ── SetCursorType sets dirty ──────────────────────────────────────────

    [Fact]
    public void SetCursorType_SetsDirty()
    {
        var d = new SDLDriver();
        d.ClearDirtyForTest();
        Assert.False(d.IsDirty);

        d.SetCursorType(100);
        Assert.True(d.IsDirty);
    }

    [Fact]
    public void SetCursorType_Zero_SetsDirty()
    {
        var d = new SDLDriver();
        d.ClearDirtyForTest();

        d.SetCursorType(0); // hide cursor
        Assert.True(d.IsDirty);
    }

    [Fact]
    public void SetCursorType_Underline_SetsDirty()
    {
        var d = new SDLDriver();
        d.ClearDirtyForTest();

        d.SetCursorType(0x0C0D); // underline cursor value from TView.ResetCursor
        Assert.True(d.IsDirty);
    }

    [Fact]
    public void SetCursorType_SetsAndClearsSequentially()
    {
        var d = new SDLDriver();

        d.ClearDirtyForTest();
        d.SetCursorType(100);
        Assert.True(d.IsDirty);

        d.ClearDirtyForTest();
        d.SetCursorType(0);
        Assert.True(d.IsDirty);
    }

    // ── SetCaretPosition sets dirty ───────────────────────────────────────

    [Fact]
    public void SetCaretPosition_SetsDirty()
    {
        var d = new SDLDriver();
        d.ClearDirtyForTest();
        Assert.False(d.IsDirty);

        d.SetCaretPosition(5, 3);
        Assert.True(d.IsDirty);
    }

    [Fact]
    public void SetCaretPosition_Origin_SetsDirty()
    {
        var d = new SDLDriver();
        d.ClearDirtyForTest();

        d.SetCaretPosition(0, 0);
        Assert.True(d.IsDirty);
    }

    [Fact]
    public void SetCaretPosition_RepeatedCalls_EachSetsDirty()
    {
        var d = new SDLDriver();

        for (int i = 0; i < 5; i++)
        {
            d.ClearDirtyForTest();
            d.SetCaretPosition(i, i);
            Assert.True(d.IsDirty);
        }
    }

    // ── Headless PumpMessages is a no-op ─────────────────────────────────

    [Fact]
    public void PumpMessages_Headless_DoesNotRender()
    {
        var d = new SDLDriver();
        Assert.Equal(0, d.DirtyRenderCount);

        d.PumpMessages();
        Assert.Equal(0, d.DirtyRenderCount);
    }

    [Fact]
    public void PumpMessages_Headless_DirtyFlagUnchanged()
    {
        // Nothing was rendered, so the flag must survive the pump.
        var d = new SDLDriver();
        Assert.True(d.IsDirty);

        d.PumpMessages();
        Assert.True(d.IsDirty);
    }

    // ── Dirty reason tracking ────────────────────────────────────────────

    [Fact]
    public void SetCursorType_SetsCursorChangeReason()
    {
        var d = new SDLDriver();
        d.ClearDirtyForTest();
        d.SetCursorType(100);
        Assert.True((d.PendingReasons & SDLDriver.SdlDirtyReason.CursorChange) != 0);
    }

    [Fact]
    public void SetCaretPosition_SetsCursorChangeReason()
    {
        var d = new SDLDriver();
        d.ClearDirtyForTest();
        d.SetCaretPosition(3, 5);
        Assert.True((d.PendingReasons & SDLDriver.SdlDirtyReason.CursorChange) != 0);
    }

    [Fact]
    public void InitialPendingReasons_IncludesInitial()
    {
        var d = new SDLDriver();
        Assert.True((d.PendingReasons & SDLDriver.SdlDirtyReason.Initial) != 0);
    }

    [Fact]
    public void ClearDirtyForTest_ClearsPendingReasons()
    {
        var d = new SDLDriver();
        Assert.True(d.PendingReasons != SDLDriver.SdlDirtyReason.None);
        d.ClearDirtyForTest();
        Assert.Equal(SDLDriver.SdlDirtyReason.None, d.PendingReasons);
    }

    [Fact]
    public void MultipleSetCalls_AccumulateReasons()
    {
        var d = new SDLDriver();
        d.ClearDirtyForTest();
        d.SetCursorType(100);
        d.SetCaretPosition(1, 1);
        // Both calls raise the same bit, so the mask is unchanged but dirty must still be set.
        Assert.True(d.IsDirty);
        Assert.True((d.PendingReasons & SDLDriver.SdlDirtyReason.CursorChange) != 0);
    }
}
