// ANSI mouse decoder + AttrToSgr tests.
// SGR 1006 (ESC [ < btn ; col ; row M/m) encoding.
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers.Terminal;
using Xunit;

namespace TSharpVision.Tests.Drivers;

public sealed class AnsiMouseDecoderTests
{
    // ── Press / release / motion ──────────────────────────────────────────

    [Fact]
    public void Decode_LeftPress_At10_5()
    {
        var buf = Encoding.ASCII.GetBytes("\x1b[<0;11;6M");
        int n = AnsiMouseDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(buf.Length, n);
        Assert.Equal(Events.evMouseDown, ev.What);
        Assert.Equal(0x01, ev.mouse.buttons);
        Assert.Equal(10, ev.mouse.where.x);
        Assert.Equal(5, ev.mouse.where.y);
    }

    [Fact]
    public void Decode_LeftRelease()
    {
        var buf = Encoding.ASCII.GetBytes("\x1b[<0;11;6m");
        int n = AnsiMouseDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(buf.Length, n);
        Assert.Equal(Events.evMouseUp, ev.What);
        Assert.Equal(0, ev.mouse.buttons);
    }

    [Fact]
    public void Decode_Motion()
    {
        // ESC [ < 32 ; 4 ; 2 M → move to (3,1) (1-based → 0-based)
        var buf = Encoding.ASCII.GetBytes("\x1b[<32;4;2M");
        int n = AnsiMouseDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(buf.Length, n);
        Assert.Equal(Events.evMouseMove, ev.What);
        Assert.Equal(3, ev.mouse.where.x);
        Assert.Equal(1, ev.mouse.where.y);
    }

    [Fact]
    public void Decode_RightPress()
    {
        var buf = Encoding.ASCII.GetBytes("\x1b[<2;1;1M");
        int n = AnsiMouseDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(buf.Length, n);
        Assert.Equal(Events.evMouseDown, ev.What);
        Assert.Equal(0x02, ev.mouse.buttons);
    }

    // ── Non-mouse CSI / partial ───────────────────────────────────────────

    [Fact]
    public void Decode_NonMouseCSI_ReturnsZero()
    {
        // CSI 'A' is a cursor-up key, not a mouse sequence.
        int n = AnsiMouseDecoder.TryDecode(new byte[] { 0x1B, (byte)'[', (byte)'A' }, out _, out _);
        Assert.Equal(0, n);
    }

    [Fact]
    public void Decode_PrintableKey_IsNotPartialMouse()
    {
        int n = AnsiMouseDecoder.TryDecode(new byte[] { (byte)'a' }, out _, out bool complete);
        Assert.Equal(0, n);
        Assert.True(complete);
    }

    [Fact]
    public void Decode_ShortNonMouseEscape_IsNotPartialMouse()
    {
        int n = AnsiMouseDecoder.TryDecode(new byte[] { 0x1B, (byte)'O' }, out _, out bool complete);
        Assert.Equal(0, n);
        Assert.True(complete);
    }

    [Fact]
    public void Decode_PartialMouse_WaitsForTerminator()
    {
        // Incomplete: ESC [ < 0  — missing col/row/terminator.
        int n = AnsiMouseDecoder.TryDecode(
            new byte[] { 0x1B, (byte)'[', (byte)'<', (byte)'0' }, out _, out bool complete);
        Assert.Equal(0, n);
        Assert.False(complete);
    }

    // ── AttrToSgr ─────────────────────────────────────────────────────────

    [Fact]
    public void AttrToSgr_ProducesValidEscapeSequence()
    {
        string sgr = AnsiTerminalDriver.AttrToSgr(new TColorAttr(0x07));
        Assert.StartsWith("\x1b[0;", sgr);
        Assert.EndsWith("m", sgr);
    }
}
