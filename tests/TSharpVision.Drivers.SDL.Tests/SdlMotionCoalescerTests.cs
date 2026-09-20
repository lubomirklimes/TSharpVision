// Unit tests for SdlMotionCoalescer — pure C#, no SDL runtime.
using TSharpVision.Drivers.SDL;
using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.Drivers.SDL.Tests;

public sealed class SdlMotionCoalescerTests
{
    // ── Initial state ─────────────────────────────────────────────────────

    [Fact]
    public void HasPending_StartsFlase()
    {
        var c = new SdlMotionCoalescer();
        Assert.False(c.HasPending);
    }

    [Fact]
    public void TryFlush_WhenEmpty_ReturnsFalse()
    {
        var c = new SdlMotionCoalescer();
        Assert.False(c.TryFlush(out _, out _, out _));
    }

    // ── Single accumulation ────────────────────────────────────────────────

    [Fact]
    public void Accumulate_Single_SetsPending()
    {
        var c = new SdlMotionCoalescer();
        c.Accumulate(10, 20, 0);
        Assert.True(c.HasPending);
    }

    [Fact]
    public void Accumulate_Single_ReturnsFalse_NoCoalesce()
    {
        var c = new SdlMotionCoalescer();
        bool coalesced = c.Accumulate(10, 20, 0);
        Assert.False(coalesced);
    }

    [Fact]
    public void TryFlush_AfterSingle_ReturnsCorrectPosition()
    {
        var c = new SdlMotionCoalescer();
        c.Accumulate(42, 17, 1);
        bool ok = c.TryFlush(out int x, out int y, out byte held);
        Assert.True(ok);
        Assert.Equal(42, x);
        Assert.Equal(17, y);
        Assert.Equal((byte)1, held);
    }

    [Fact]
    public void TryFlush_AfterSingle_ClearsPending()
    {
        var c = new SdlMotionCoalescer();
        c.Accumulate(5, 5, 0);
        c.TryFlush(out _, out _, out _);
        Assert.False(c.HasPending);
    }

    [Fact]
    public void TryFlush_AfterSingle_SecondFlush_ReturnsFalse()
    {
        var c = new SdlMotionCoalescer();
        c.Accumulate(5, 5, 0);
        c.TryFlush(out _, out _, out _);
        Assert.False(c.TryFlush(out _, out _, out _));
    }

    // ── Multiple accumulations — coalescing ────────────────────────────────

    [Fact]
    public void Accumulate_Second_ReturnsTrue_Coalesced()
    {
        var c = new SdlMotionCoalescer();
        c.Accumulate(1, 1, 0);
        bool coalesced = c.Accumulate(2, 2, 0);
        Assert.True(coalesced);
    }

    [Fact]
    public void TryFlush_AfterMultiple_ReturnsLastPosition()
    {
        var c = new SdlMotionCoalescer();
        c.Accumulate(10, 10, 0);
        c.Accumulate(20, 20, 0);
        c.Accumulate(30, 30, 0);
        bool ok = c.TryFlush(out int x, out int y, out _);
        Assert.True(ok);
        Assert.Equal(30, x);
        Assert.Equal(30, y);
    }

    [Fact]
    public void TryFlush_AfterMultiple_EmitsExactlyOne()
    {
        var c = new SdlMotionCoalescer();
        for (int i = 0; i < 5; i++)
            c.Accumulate(i * 10, i * 10, 0);

        int flushes = 0;
        while (c.TryFlush(out _, out _, out _))
            flushes++;

        Assert.Equal(1, flushes);
    }

    [Fact]
    public void TryFlush_AfterMultiple_ClearsPending()
    {
        var c = new SdlMotionCoalescer();
        c.Accumulate(1, 2, 0);
        c.Accumulate(3, 4, 0);
        c.TryFlush(out _, out _, out _);
        Assert.False(c.HasPending);
    }

    // ── Held-buttons tracking ──────────────────────────────────────────────

    [Fact]
    public void TryFlush_ReturnsHeldButtonsFromLastAccumulate()
    {
        var c = new SdlMotionCoalescer();
        c.Accumulate(0, 0, heldButtons: 1);
        c.Accumulate(5, 5, heldButtons: 3);
        c.TryFlush(out _, out _, out byte held);
        Assert.Equal((byte)3, held);
    }

    [Fact]
    public void TryFlush_ReturnsModifierSnapshotFromLatestMotion()
    {
        var c = new SdlMotionCoalescer();
        c.Accumulate(1, 1, 0, Keys.kbShift);
        c.Accumulate(2, 2, 1, Keys.kbShift | Keys.kbCtrlShift);
        Assert.True(c.TryFlush(out _, out _, out _, out uint controlKeyState));
        Assert.Equal(Keys.kbShift | Keys.kbCtrlShift, controlKeyState);
    }

    [Fact]
    public void TryFlush_NoAccumulate_HeldButtonsIsZero()
    {
        var c = new SdlMotionCoalescer();
        c.TryFlush(out _, out _, out byte held);
        Assert.Equal((byte)0, held);
    }

    // ── Reuse after flush ──────────────────────────────────────────────────

    [Fact]
    public void AccumulateAfterFlush_Works()
    {
        var c = new SdlMotionCoalescer();
        c.Accumulate(1, 1, 0);
        c.TryFlush(out _, out _, out _);

        // Reuse for a second drain cycle.
        bool coalesced = c.Accumulate(9, 9, 0);
        Assert.False(coalesced);
        Assert.True(c.HasPending);

        bool ok = c.TryFlush(out int x, out int y, out _);
        Assert.True(ok);
        Assert.Equal(9, x);
        Assert.Equal(9, y);
    }

    // ── Edge cases ─────────────────────────────────────────────────────────

    [Fact]
    public void Accumulate_ZeroPosition_Works()
    {
        var c = new SdlMotionCoalescer();
        c.Accumulate(0, 0, 0);
        bool ok = c.TryFlush(out int x, out int y, out _);
        Assert.True(ok);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void Accumulate_NegativePosition_Works()
    {
        var c = new SdlMotionCoalescer();
        c.Accumulate(-5, -10, 0);
        c.TryFlush(out int x, out int y, out _);
        Assert.Equal(-5, x);
        Assert.Equal(-10, y);
    }

    // ── Ordering invariant ─────────────────────────────────────────────────

    [Fact]
    public void FlushBeforeButtonEvent_ProducesMotionFirst()
    {
        // SDLDriver flushes pending motion before a button event, so the button can never
        // overtake the motion that preceded it.
        var motionPositions = new List<(int x, int y)>();
        var buttonsSeen = new List<string>();

        var c = new SdlMotionCoalescer();
        c.Accumulate(10, 10, 0);
        c.Accumulate(20, 20, 0);

        if (c.TryFlush(out int mx, out int my, out _))
            motionPositions.Add((mx, my));
        buttonsSeen.Add("down");

        Assert.Single(motionPositions);
        Assert.Equal((20, 20), motionPositions[0]);
        Assert.Single(buttonsSeen);
    }
}
