// Mouse wheel events: Win32 constants, TEventQueue pass-through,
// TScroller/TListViewer/TEditor wheel scroll, THelpViewer inheritance.
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers.Console;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class MouseWheelTests : IDisposable
{
    private readonly DriverScope _ds;
    public MouseWheelTests()
    {
        _ds = new DriverScope();
        TEventQueue.Resume();
    }
    public void Dispose() => _ds.Dispose();

    // ── 1. Constants ──────────────────────────────────────────────────────

    [Fact] public void evMouseWheel_Value()  => Assert.Equal(0x0020, Events.evMouseWheel);
    [Fact] public void mbButton4_Value()     => Assert.Equal(0x04,   Events.mbButton4);
    [Fact] public void mbButton5_Value()     => Assert.Equal(0x08,   Events.mbButton5);
    [Fact] public void evMouse_IncludesWheel()
        => Assert.True((Events.evMouse & Events.evMouseWheel) != 0);
    [Fact] public void mbButton4_NotButton5()
        => Assert.NotEqual(Events.mbButton4, Events.mbButton5);
    [Fact] public void WheelButtons_DontOverlapRealButtons()
    {
        Assert.Equal(0, Events.mbButton4 & (Events.mbLeftButton | Events.mbRightButton));
        Assert.Equal(0, Events.mbButton5 & (Events.mbLeftButton | Events.mbRightButton));
    }
    [Fact] public void evMouseWheel_DistinctFromOtherCodes()
    {
        Assert.NotEqual(Events.evMouseWheel, Events.evMouseDown);
        Assert.NotEqual(Events.evMouseWheel, Events.evMouseUp);
        Assert.NotEqual(Events.evMouseWheel, Events.evMouseMove);
        Assert.NotEqual(Events.evMouseWheel, Events.evMouseAuto);
        Assert.NotEqual(Events.evMouseWheel, Events.evKeyDown);
        Assert.NotEqual(Events.evMouseWheel, Events.evCommand);
        Assert.NotEqual(Events.evMouseWheel, Events.evBroadcast);
        Assert.NotEqual(Events.evMouseWheel, Events.evNothing);
    }

    // ── 2. Win32 driver: TranslateMouse maps wheel delta ─────────────────

    [Fact]
    public void Win32_PosWheelDelta_ProducesEvMouseWheel_Button4()
    {
        var ev = Win32ConsoleDriver.TranslateMouse(
            (uint)((short)120 << 16), 0x0004, 10, 5);
        Assert.Equal(Events.evMouseWheel, ev.What);
        Assert.True((ev.mouse.buttons & Events.mbButton4) != 0);
        Assert.NotEqual(Events.evMouseDown, ev.What);
        Assert.Equal(10, ev.mouse.where.x);
        Assert.Equal(5, ev.mouse.where.y);
        Assert.False(ev.mouse.doubleClick);
    }

    [Fact]
    public void Win32_NegWheelDelta_ProducesEvMouseWheel_Button5()
    {
        var ev = Win32ConsoleDriver.TranslateMouse(
            unchecked((uint)((short)(-120) << 16)), 0x0004, 3, 7);
        Assert.Equal(Events.evMouseWheel, ev.What);
        Assert.True((ev.mouse.buttons & Events.mbButton5) != 0);
        Assert.Equal(0, ev.mouse.buttons & Events.mbButton4);
    }

    [Fact]
    public void Win32_RegularClick_StillEvMouseDown()
    {
        var ev = Win32ConsoleDriver.TranslateMouse(0x0001, 0x0000, 4, 4);
        Assert.Equal(Events.evMouseDown, ev.What);
    }

    // ── 3. TEventQueue: wheel passes through, doesn't corrupt _lastMouse ─

    [Fact]
    public void Queue_WheelEvent_PassesThrough_Unchanged()
    {
        TEvent wheelEv = default;
        wheelEv.What          = Events.evMouseWheel;
        wheelEv.mouse.buttons = (byte)Events.mbButton4;
        wheelEv.mouse.where   = new TPoint(5, 5);
        TEventQueue.Enqueue(wheelEv);

        TEvent got = default;
        TEventQueue.GetNextEvent(ref got);
        Assert.Equal(Events.evMouseWheel, got.What);
        Assert.True((got.mouse.buttons & Events.mbButton4) != 0);
        Assert.False(got.mouse.doubleClick);
    }

    [Fact]
    public void Queue_ClickAfterWheel_NotCorrupted()
    {
        TEvent wheelEv = default;
        wheelEv.What          = Events.evMouseWheel;
        wheelEv.mouse.buttons = (byte)Events.mbButton4;
        wheelEv.mouse.where   = new TPoint(5, 5);
        TEventQueue.Enqueue(wheelEv);
        TEvent w = default; TEventQueue.GetNextEvent(ref w);

        TEvent clickEv = default;
        clickEv.What          = Events.evMouseDown;
        clickEv.mouse.buttons = (byte)Events.mbLeftButton;
        clickEv.mouse.where   = new TPoint(5, 5);
        TEventQueue.Enqueue(clickEv);

        TEvent clickGot = default;
        TEventQueue.GetNextEvent(ref clickGot);
        Assert.Equal(Events.evMouseDown, clickGot.What);
        Assert.False(clickGot.mouse.doubleClick);
    }

    // ── 4. TScroller: delta.y updates and clamped ────────────────────────

    [Fact]
    public void Scroller_WheelDown_IncreasesDeltaY()
    {
        var scroller = new TScroller(new TRect(0, 0, 40, 10), null, null);
        scroller.SetLimit(40, 50);
        TEvent ev = default;
        ev.What = Events.evMouseWheel;
        ev.mouse.buttons = (byte)Events.mbButton5;
        ev.mouse.where   = new TPoint(10, 5);
        scroller.HandleEvent(ref ev);
        Assert.Equal(3, scroller.delta.y);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void Scroller_WheelUp_DecreasesDeltaY()
    {
        var scroller = new TScroller(new TRect(0, 0, 40, 10), null, null);
        scroller.SetLimit(40, 50);
        TEvent down = default; down.What = Events.evMouseWheel;
        down.mouse.buttons = (byte)Events.mbButton5; down.mouse.where = new TPoint(10, 5);
        scroller.HandleEvent(ref down);
        Assert.Equal(3, scroller.delta.y);

        TEvent up = default; up.What = Events.evMouseWheel;
        up.mouse.buttons = (byte)Events.mbButton4; up.mouse.where = new TPoint(10, 5);
        scroller.HandleEvent(ref up);
        Assert.Equal(0, scroller.delta.y);
    }

    [Fact]
    public void Scroller_WheelUp_AtTop_StaysZero()
    {
        var scroller = new TScroller(new TRect(0, 0, 40, 10), null, null);
        scroller.SetLimit(40, 50);
        TEvent up = default; up.What = Events.evMouseWheel;
        up.mouse.buttons = (byte)Events.mbButton4; up.mouse.where = new TPoint(10, 5);
        scroller.HandleEvent(ref up);
        Assert.Equal(0, scroller.delta.y);
    }

    [Fact]
    public void Scroller_WheelDown_NearLimit_Clamped()
    {
        var scroller = new TScroller(new TRect(0, 0, 40, 10), null, null);
        scroller.SetLimit(40, 50);   // max delta.y = 40
        scroller.delta.y = 38;
        TEvent down = default; down.What = Events.evMouseWheel;
        down.mouse.buttons = (byte)Events.mbButton5; down.mouse.where = new TPoint(10, 5);
        scroller.HandleEvent(ref down);
        Assert.Equal(40, scroller.delta.y);

        // Further down must not exceed max
        TEvent down2 = default; down2.What = Events.evMouseWheel;
        down2.mouse.buttons = (byte)Events.mbButton5; down2.mouse.where = new TPoint(10, 5);
        scroller.HandleEvent(ref down2);
        Assert.Equal(40, scroller.delta.y);
    }

    // ── 5. TListViewer: focused moves and clamped ─────────────────────────

    [Fact]
    public void ListViewer_WheelDown_FocusedMovesBy3()
    {
        var lv = new ProbeListViewer(new TRect(0, 0, 40, 10), 1, null, null);
        lv.range = 20;
        lv.SetState(Views.sfSelected, true);
        TEvent ev = default; ev.What = Events.evMouseWheel;
        ev.mouse.buttons = (byte)Events.mbButton5; ev.mouse.where = new TPoint(10, 5);
        lv.HandleEvent(ref ev);
        Assert.Equal(3, lv.focused);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void ListViewer_WheelUp_FocusedDecreases()
    {
        var lv = new ProbeListViewer(new TRect(0, 0, 40, 10), 1, null, null);
        lv.range = 20;
        lv.SetState(Views.sfSelected, true);
        TEvent down = default; down.What = Events.evMouseWheel;
        down.mouse.buttons = (byte)Events.mbButton5; down.mouse.where = new TPoint(10, 5);
        lv.HandleEvent(ref down);

        TEvent up = default; up.What = Events.evMouseWheel;
        up.mouse.buttons = (byte)Events.mbButton4; up.mouse.where = new TPoint(10, 5);
        lv.HandleEvent(ref up);
        Assert.Equal(0, lv.focused);
    }

    [Fact]
    public void ListViewer_WheelUp_AtTop_StaysZero()
    {
        var lv = new ProbeListViewer(new TRect(0, 0, 40, 10), 1, null, null);
        lv.range = 20;
        lv.SetState(Views.sfSelected, true);
        TEvent up = default; up.What = Events.evMouseWheel;
        up.mouse.buttons = (byte)Events.mbButton4; up.mouse.where = new TPoint(10, 5);
        lv.HandleEvent(ref up);
        Assert.Equal(0, lv.focused);
    }

    [Fact]
    public void ListViewer_WheelDown_AtEnd_ClampsToRangeMinusOne()
    {
        var lv = new ProbeListViewer(new TRect(0, 0, 40, 10), 1, null, null);
        lv.range = 20;
        lv.SetState(Views.sfSelected, true);
        lv.focused = 18;
        TEvent down = default; down.What = Events.evMouseWheel;
        down.mouse.buttons = (byte)Events.mbButton5; down.mouse.where = new TPoint(10, 5);
        lv.HandleEvent(ref down);   // 18 + 3 = 21, clamped to 19
        Assert.Equal(19, lv.focused);
    }

    // ── 6. TEditor: viewport scrolls ─────────────────────────────────────

    private static TEditor MakeEditor(string text)
    {
        var ed = new TEditor(new TRect(0, 0, 40, 10), null, null, null, 4096);
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(text);
        ed.bufLen = (uint)bytes.Length;
        ed.gapLen = ed.bufSize - ed.bufLen;
        Array.Copy(bytes, 0, ed.buffer, (int)ed.gapLen, bytes.Length);
        ed.curPtr = 0; ed.curPos = default; ed.delta = default;
        ed.drawLine = 0; ed.drawPtr = 0;
        ed.limit.x = Views.maxLineLength;
        int lines = 1;
        foreach (byte b in bytes) if (b == 0x0A) lines++;
        ed.limit.y = lines;
        return ed;
    }

    [Fact]
    public void Editor_WheelDown_IncreaseDeltaY_NotModified()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 25; i++) sb.AppendLine($"Line {i + 1}");
        var ed = MakeEditor(sb.ToString());
        Assert.Equal(0, ed.delta.y);
        TEvent ev = default; ev.What = Events.evMouseWheel;
        ev.mouse.buttons = (byte)Events.mbButton5; ev.mouse.where = new TPoint(5, 5);
        ed.HandleEvent(ref ev);
        Assert.True(ed.delta.y > 0);
        Assert.False(ed.modified);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void Editor_WheelUp_DecreaseDeltaY()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 25; i++) sb.AppendLine($"Line {i + 1}");
        var ed = MakeEditor(sb.ToString());
        TEvent down = default; down.What = Events.evMouseWheel;
        down.mouse.buttons = (byte)Events.mbButton5; down.mouse.where = new TPoint(5, 5);
        ed.HandleEvent(ref down);
        int savedDelta = ed.delta.y;
        TEvent up = default; up.What = Events.evMouseWheel;
        up.mouse.buttons = (byte)Events.mbButton4; up.mouse.where = new TPoint(5, 5);
        ed.HandleEvent(ref up);
        Assert.True(ed.delta.y < savedDelta);
    }

    [Fact]
    public void Editor_WheelUp_ToTop_ClampsAtZero()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 25; i++) sb.AppendLine($"Line {i + 1}");
        var ed = MakeEditor(sb.ToString());
        for (int i = 0; i < 12; i++)
        {
            var up = new TEvent(); up.What = Events.evMouseWheel;
            up.mouse.buttons = (byte)Events.mbButton4; up.mouse.where = new TPoint(5, 5);
            ed.HandleEvent(ref up);
        }
        Assert.Equal(0, ed.delta.y);
    }

    [Fact]
    public void Editor_WheelDown_ToBottom_ClampsAtLimit()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 25; i++) sb.AppendLine($"Line {i + 1}");
        var ed = MakeEditor(sb.ToString());
        for (int i = 0; i < 12; i++)
        {
            var down = new TEvent(); down.What = Events.evMouseWheel;
            down.mouse.buttons = (byte)Events.mbButton5; down.mouse.where = new TPoint(5, 5);
            ed.HandleEvent(ref down);
        }
        Assert.Equal(Math.Max(0, ed.limit.y - ed.size.y), ed.delta.y);
    }

    // ── 7. THelpViewer: inherits TScroller wheel handling ────────────────

    [Fact]
    public void HelpViewer_WheelDown_IncreasesDeltaY()
    {
        using var tmp = new TempDirectory();
        string path = System.IO.Path.Combine(tmp.Path, "wheel.hlp");
        {
            var fp = new Fpstream(path);
            var hf = new THelpFile(fp);
            var t  = new THelpTopic();
            var lines = new System.Text.StringBuilder();
            for (int i = 0; i < 30; i++) lines.Append($"Line {i + 1:D2}\n");
            byte[] bytes = System.Text.Encoding.Latin1.GetBytes(lines.ToString());
            t.AddParagraph(new TParagraph { text = bytes, size = (ushort)bytes.Length, wrap = false });
            hf.RecordPositionInIndex(1);
            hf.PutTopic(t);
            hf.Flush();
            fp.Close();
        }

        var fp2    = new Fpstream(path);
        var hf2    = new THelpFile(fp2);
        var vsb    = new TScrollBar(new TRect(39, 1, 40, 9));
        var viewer = new THelpViewer(new TRect(0, 0, 40, 10), null, vsb, hf2, 1);
        viewer.SetLimit(40, 30);
        Assert.Equal(0, viewer.delta.y);

        TEvent down = default; down.What = Events.evMouseWheel;
        down.mouse.buttons = (byte)Events.mbButton5; down.mouse.where = new TPoint(10, 5);
        viewer.HandleEvent(ref down);
        Assert.True(viewer.delta.y > 0);
        Assert.Equal(Events.evNothing, down.What);

        TEvent up = default; up.What = Events.evMouseWheel;
        up.mouse.buttons = (byte)Events.mbButton4; up.mouse.where = new TPoint(10, 5);
        viewer.HandleEvent(ref up);
        Assert.Equal(0, viewer.delta.y);

        fp2.Close();
    }
}
