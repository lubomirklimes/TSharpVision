// ANSI/xterm key decoder tests.
// Pure byte-stream → TEvent translation; no real TTY needed.
using SharpVision;
using SharpVision.Constants;
using SharpVision.Drivers.Terminal;
using Xunit;

namespace SharpVision.Tests.Drivers;

public sealed class AnsiKeyDecoderTests
{
    // ── Single-byte printable / control ───────────────────────────────────

    [Fact]
    public void Decode_PrintableA()
    {
        int n = AnsiKeyDecoder.TryDecode(new byte[] { (byte)'a' }, out var ev, out _);
        Assert.Equal(1, n);
        Assert.Equal((ushort)'a', ev.keyDown.keyCode);
    }

    [Fact]
    public void Decode_Tab()
    {
        int n = AnsiKeyDecoder.TryDecode(new byte[] { 0x09 }, out var ev, out _);
        Assert.Equal(1, n);
        Assert.Equal(Keys.kbTab, ev.keyDown.keyCode);
    }

    [Fact]
    public void Decode_Enter()
    {
        int n = AnsiKeyDecoder.TryDecode(new byte[] { 0x0D }, out var ev, out _);
        Assert.Equal(1, n);
        Assert.Equal(Keys.kbEnter, ev.keyDown.keyCode);
    }

    [Fact]
    public void Decode_Backspace()
    {
        int n = AnsiKeyDecoder.TryDecode(new byte[] { 0x7F }, out var ev, out _);
        Assert.Equal(1, n);
        Assert.Equal(Keys.kbBack, ev.keyDown.keyCode);
    }

    [Fact]
    public void Decode_CtrlC()
    {
        int n = AnsiKeyDecoder.TryDecode(new byte[] { 0x03 }, out var ev, out _);
        Assert.Equal(1, n);
        Assert.Equal(Keys.kbCtrlC, ev.keyDown.keyCode);
    }

    // ── ESC handling ─────────────────────────────────────────────────────

    [Fact]
    public void Decode_LoneEsc_NeedsMoreBytes()
    {
        int n = AnsiKeyDecoder.TryDecode(new byte[] { 0x1B }, out _, out bool complete);
        Assert.Equal(0, n);
        Assert.False(complete);
    }

    [Fact]
    public void Decode_EscEsc_EmitsKbEsc()
    {
        int n = AnsiKeyDecoder.TryDecode(new byte[] { 0x1B, 0x1B }, out var ev, out _);
        Assert.Equal(1, n);
        Assert.Equal(Keys.kbEsc, ev.keyDown.keyCode);
    }

    // ── Alt-letter ────────────────────────────────────────────────────────

    [Fact]
    public void Decode_AltX()
    {
        int n = AnsiKeyDecoder.TryDecode(new byte[] { 0x1B, (byte)'x' }, out var ev, out _);
        Assert.Equal(2, n);
        Assert.Equal(Keys.kbAltX, ev.keyDown.keyCode);
        Assert.NotEqual(0, ev.keyDown.shiftState & Keys.kbAltShift);
    }

    [Fact]
    public void Decode_Alt1()
    {
        int n = AnsiKeyDecoder.TryDecode(new byte[] { 0x1B, (byte)'1' }, out var ev, out _);
        Assert.Equal(2, n);
        Assert.Equal(Keys.kbAlt1, ev.keyDown.keyCode);
    }

    // ── SS3 (ESC O x) ────────────────────────────────────────────────────

    [Fact]
    public void Decode_SS3_F1()
    {
        int n = AnsiKeyDecoder.TryDecode(new byte[] { 0x1B, (byte)'O', (byte)'P' }, out var ev, out _);
        Assert.Equal(3, n);
        Assert.Equal(Keys.kbF1, ev.keyDown.keyCode);
    }

    [Fact]
    public void Decode_SS3_Up()
    {
        int n = AnsiKeyDecoder.TryDecode(new byte[] { 0x1B, (byte)'O', (byte)'A' }, out var ev, out _);
        Assert.Equal(3, n);
        Assert.Equal(Keys.kbUp, ev.keyDown.keyCode);
    }

    // ── CSI arrows ────────────────────────────────────────────────────────

    [Fact]
    public void Decode_CSI_Up()
    {
        int n = AnsiKeyDecoder.TryDecode(new byte[] { 0x1B, (byte)'[', (byte)'A' }, out var ev, out _);
        Assert.Equal(3, n);
        Assert.Equal(Keys.kbUp, ev.keyDown.keyCode);
    }

    [Fact]
    public void Decode_CSI_Left()
    {
        int n = AnsiKeyDecoder.TryDecode(new byte[] { 0x1B, (byte)'[', (byte)'D' }, out var ev, out _);
        Assert.Equal(3, n);
        Assert.Equal(Keys.kbLeft, ev.keyDown.keyCode);
    }

    [Fact]
    public void Decode_CSI_CtrlLeft()
    {
        var bytes = new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'5', (byte)'D' };
        int n = AnsiKeyDecoder.TryDecode(bytes, out var ev, out _);
        Assert.Equal(6, n);
        Assert.Equal(Keys.kbCtrlLeft, ev.keyDown.keyCode);
        Assert.NotEqual(0, ev.keyDown.shiftState & Keys.kbCtrlShift);
    }

    // ── CSI ~-sequences (function / nav) ─────────────────────────────────

    [Fact]
    public void Decode_CSI_F5()
    {
        var bytes = new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'5', (byte)'~' };
        int n = AnsiKeyDecoder.TryDecode(bytes, out var ev, out _);
        Assert.Equal(5, n);
        Assert.Equal(Keys.kbF5, ev.keyDown.keyCode);
    }

    [Fact]
    public void Decode_CSI_F12()
    {
        var bytes = new byte[] { 0x1B, (byte)'[', (byte)'2', (byte)'4', (byte)'~' };
        int n = AnsiKeyDecoder.TryDecode(bytes, out var ev, out _);
        Assert.Equal(5, n);
        Assert.Equal(Keys.kbF12, ev.keyDown.keyCode);
    }

    [Fact]
    public void Decode_CSI_PgUp()
    {
        var bytes = new byte[] { 0x1B, (byte)'[', (byte)'5', (byte)'~' };
        int n = AnsiKeyDecoder.TryDecode(bytes, out var ev, out _);
        Assert.Equal(4, n);
        Assert.Equal(Keys.kbPgUp, ev.keyDown.keyCode);
    }

    [Fact]
    public void Decode_CSI_Del()
    {
        var bytes = new byte[] { 0x1B, (byte)'[', (byte)'3', (byte)'~' };
        int n = AnsiKeyDecoder.TryDecode(bytes, out var ev, out _);
        Assert.Equal(4, n);
        Assert.Equal(Keys.kbDel, ev.keyDown.keyCode);
    }

    // ── Partial / incomplete sequences ────────────────────────────────────

    [Fact]
    public void Decode_PartialCSI_WaitsForMore()
    {
        int n = AnsiKeyDecoder.TryDecode(new byte[] { 0x1B, (byte)'[' }, out _, out bool complete);
        Assert.Equal(0, n);
        Assert.False(complete);
    }
}
