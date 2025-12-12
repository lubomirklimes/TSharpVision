// ANSI/xterm driver hardening tests.
// Covers headless checks:
//   §1 ANSI SGR mouse wheel (b=64/65) → evMouseWheel + correct button
//   §2 ANSI SGR drag with held-button mask (b=32 left, b=34 right, b=35 none)
//   §3 ANSI CSI param;modifier~ — Shift/Alt/Ctrl F-key decode + no-regression F5
//   §4 AttrToSgr canonical SGR string mappings (0x07, 0x0F, 0x1F, 0xF7)
//   §5 AnsiTerminalDriver capability (SupportsTrueColor=false) + lifecycle safety
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers.Terminal;
using Xunit;

namespace TSharpVision.Tests.Drivers;

public sealed class AnsiDriverHardeningTests
{
    // ── §1 — ANSI SGR mouse wheel ─────────────────────────────────────────

    [Fact]
    public void AnsiMouse_WheelUp_IsEvMouseWheel()
    {
        // ESC [ < 64 ; 5 ; 5 M  — b=64=0x40, column=5, row=5
        var buf = Encoding.ASCII.GetBytes("\x1b[<64;5;5M");
        int n = AnsiMouseDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(buf.Length, n);
        Assert.Equal(Events.evMouseWheel, ev.What);
    }

    [Fact]
    public void AnsiMouse_WheelUp_IsMbButton4()
    {
        var buf = Encoding.ASCII.GetBytes("\x1b[<64;5;5M");
        AnsiMouseDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(0x04, ev.mouse.buttons);
    }

    [Fact]
    public void AnsiMouse_WheelUp_Coords_4_4()
    {
        // 1-based ANSI col=5, row=5 → 0-based (4, 4)
        var buf = Encoding.ASCII.GetBytes("\x1b[<64;5;5M");
        AnsiMouseDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(4, ev.mouse.where.x);
        Assert.Equal(4, ev.mouse.where.y);
    }

    [Fact]
    public void AnsiMouse_WheelDown_IsEvMouseWheel()
    {
        // ESC [ < 65 ; 3 ; 7 M  — b=65=0x41
        var buf = Encoding.ASCII.GetBytes("\x1b[<65;3;7M");
        int n = AnsiMouseDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(buf.Length, n);
        Assert.Equal(Events.evMouseWheel, ev.What);
    }

    [Fact]
    public void AnsiMouse_WheelDown_IsMbButton5()
    {
        var buf = Encoding.ASCII.GetBytes("\x1b[<65;3;7M");
        AnsiMouseDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(0x08, ev.mouse.buttons);
    }

    [Fact]
    public void AnsiMouse_WheelDown_Coords_2_6()
    {
        // col=3, row=7 → (2, 6)
        var buf = Encoding.ASCII.GetBytes("\x1b[<65;3;7M");
        AnsiMouseDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(2, ev.mouse.where.x);
        Assert.Equal(6, ev.mouse.where.y);
    }

    // ── §2 — ANSI SGR drag: held-button mask ─────────────────────────────

    [Fact]
    public void AnsiMouse_LeftDrag_IsEvMouseMove()
    {
        // b = 0x20 | 0 = 32 — motion flag set, buttonIdx = 0 → left
        var buf = Encoding.ASCII.GetBytes("\x1b[<32;5;5M");
        int n = AnsiMouseDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(buf.Length, n);
        Assert.Equal(Events.evMouseMove, ev.What);
    }

    [Fact]
    public void AnsiMouse_LeftDrag_ButtonsMask_Is0x01()
    {
        var buf = Encoding.ASCII.GetBytes("\x1b[<32;5;5M");
        AnsiMouseDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(0x01, ev.mouse.buttons);
    }

    [Fact]
    public void AnsiMouse_RightDrag_IsEvMouseMove()
    {
        // b = 0x20 | 2 = 34 — motion flag, buttonIdx = 2 → right
        var buf = Encoding.ASCII.GetBytes("\x1b[<34;6;6M");
        int n = AnsiMouseDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(buf.Length, n);
        Assert.Equal(Events.evMouseMove, ev.What);
    }

    [Fact]
    public void AnsiMouse_RightDrag_ButtonsMask_Is0x02()
    {
        var buf = Encoding.ASCII.GetBytes("\x1b[<34;6;6M");
        AnsiMouseDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(0x02, ev.mouse.buttons);
    }

    [Fact]
    public void AnsiMouse_PureMove_IsEvMouseMove()
    {
        // b = 0x20 | 3 = 35 — motion flag, buttonIdx = 3 → none held
        var buf = Encoding.ASCII.GetBytes("\x1b[<35;2;2M");
        int n = AnsiMouseDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(buf.Length, n);
        Assert.Equal(Events.evMouseMove, ev.What);
    }

    [Fact]
    public void AnsiMouse_PureMove_Buttons_Is0()
    {
        var buf = Encoding.ASCII.GetBytes("\x1b[<35;2;2M");
        AnsiMouseDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(0, ev.mouse.buttons);
    }

    // ── §3 — ANSI CSI param;modifier~ — Shift/Alt/Ctrl F-key decode ──────

    [Fact]
    public void AnsiKey_ShiftF5_IsKbShiftF5()
    {
        // CSI 15 ; 2 ~ = Shift + F5
        var buf = new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'5', (byte)';', (byte)'2', (byte)'~' };
        int n = AnsiKeyDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(7, n);
        Assert.Equal(Keys.kbShiftF5, ev.keyDown.keyCode);
    }

    [Fact]
    public void AnsiKey_ShiftF5_ShiftState_HasKbShift()
    {
        var buf = new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'5', (byte)';', (byte)'2', (byte)'~' };
        AnsiKeyDecoder.TryDecode(buf, out var ev, out _);
        Assert.NotEqual(0, ev.keyDown.shiftState & Keys.kbShift);
    }

    [Fact]
    public void AnsiKey_AltF5_IsKbAltF5()
    {
        // CSI 15 ; 3 ~ = Alt + F5
        var buf = new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'5', (byte)';', (byte)'3', (byte)'~' };
        int n = AnsiKeyDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(7, n);
        Assert.Equal(Keys.kbAltF5, ev.keyDown.keyCode);
    }

    [Fact]
    public void AnsiKey_AltF5_ShiftState_HasKbAltShift()
    {
        var buf = new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'5', (byte)';', (byte)'3', (byte)'~' };
        AnsiKeyDecoder.TryDecode(buf, out var ev, out _);
        Assert.NotEqual(0, ev.keyDown.shiftState & Keys.kbAltShift);
    }

    [Fact]
    public void AnsiKey_CtrlF5_IsKbCtrlF5()
    {
        // CSI 15 ; 5 ~ = Ctrl + F5
        var buf = new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'5', (byte)';', (byte)'5', (byte)'~' };
        int n = AnsiKeyDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(7, n);
        Assert.Equal(Keys.kbCtrlF5, ev.keyDown.keyCode);
    }

    [Fact]
    public void AnsiKey_CtrlF5_ShiftState_HasKbCtrlShift()
    {
        var buf = new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'5', (byte)';', (byte)'5', (byte)'~' };
        AnsiKeyDecoder.TryDecode(buf, out var ev, out _);
        Assert.NotEqual(0, ev.keyDown.shiftState & Keys.kbCtrlShift);
    }

    [Fact]
    public void AnsiKey_PlainF5_NoRegression()
    {
        // CSI 15 ~ — must still work after modifier-decode was added.
        var buf = new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'5', (byte)'~' };
        int n = AnsiKeyDecoder.TryDecode(buf, out var ev, out _);
        Assert.Equal(5, n);
        Assert.Equal(Keys.kbF5, ev.keyDown.keyCode);
    }

    // ── §4 — AttrToSgr canonical SGR string mappings ─────────────────────

    [Fact]
    public void AttrToSgr_0x07_GrayOnBlack()
    {
        // fg=7 (gray, no bright bit) → 30+7=37; bg=0 (black) → 40+0=40
        Assert.Equal("\x1b[0;37;40m", AnsiTerminalDriver.AttrToSgr(new TColorAttr(0x07)));
    }

    [Fact]
    public void AttrToSgr_0x0F_BrightWhiteOnBlack()
    {
        // fg=15 (bright bit set, val=7) → 90+7=97; bg=0 → 40
        Assert.Equal("\x1b[0;97;40m", AnsiTerminalDriver.AttrToSgr(new TColorAttr(0x0F)));
    }

    [Fact]
    public void AttrToSgr_0x1F_BrightWhiteOnBlue()
    {
        // fg=15 → 97; bg=1 (blue) → 40+1=41
        Assert.Equal("\x1b[0;97;41m", AnsiTerminalDriver.AttrToSgr(new TColorAttr(0x1F)));
    }

    [Fact]
    public void AttrToSgr_0xF7_GrayOnBrightWhite()
    {
        // raw=0xF7 → fg=7 (gray) → 37; bg=15 (bright, val=7) → 100+7=107
        Assert.Equal("\x1b[0;37;107m", AnsiTerminalDriver.AttrToSgr(new TColorAttr(0xF7)));
    }

    // ── §5 — AnsiTerminalDriver capability + lifecycle safety ─────────────

    [Fact]
    public void AnsiTerminalDriver_SupportsTrueColor_IsFalse()
    {
        Assert.False(new AnsiTerminalDriver().SupportsTrueColor);
    }

    [Fact]
    public void AnsiTerminalDriver_ShutdownBeforeInitialize_NoThrow()
    {
        var drv = new AnsiTerminalDriver();
        var ex = Record.Exception(() => { drv.Shutdown(); drv.Shutdown(); });
        Assert.Null(ex);
    }

    [Fact]
    public void AnsiTerminalDriver_InitializeOnNonTty_NoThrow()
    {
        // On Windows or when no real TTY is attached, Initialize() short-circuits.
        var drv = new AnsiTerminalDriver();
        var ex = Record.Exception(() => { drv.Initialize(); drv.Shutdown(); });
        Assert.Null(ex);
    }
}
