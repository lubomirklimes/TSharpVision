using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class WindowFrameTests
{
    // ── TBackground ──────────────────────────────────────────────────────────

    [Fact]
    public void Background_StoresPattern()
    {
        using var driver = new DriverScope();
        var bg = new TBackground(new TRect(0, 0, 80, 25), '▒');
        Assert.Equal('▒', bg.pattern);
    }

    [Fact]
    public void Background_GrowMode_HiXHiY()
    {
        using var driver = new DriverScope();
        var bg = new TBackground(new TRect(0, 0, 80, 25), '▒');
        Assert.Equal((byte)(Views.gfGrowHiX | Views.gfGrowHiY), bg.growMode);
    }

    [Fact]
    public void Background_Palette_Size1()
    {
        using var driver = new DriverScope();
        var bg = new TBackground(new TRect(0, 0, 80, 25), '▒');
        var pal = bg.GetPalette();
        Assert.Equal(1, pal.Size);
    }

    [Fact]
    public void Background_Palette_Entry1_Is_0x01()
    {
        using var driver = new DriverScope();
        var bg = new TBackground(new TRect(0, 0, 80, 25), '▒');
        Assert.Equal(0x01, bg.GetPalette()[1]);
    }

    // ── TFrame ───────────────────────────────────────────────────────────────

    [Fact]
    public void Frame_GrowMode_HiXHiY()
    {
        using var driver = new DriverScope();
        var frm = new TFrame(new TRect(0, 0, 30, 10));
        Assert.Equal((byte)(Views.gfGrowHiX | Views.gfGrowHiY), frm.growMode);
    }

    [Fact]
    public void Frame_EventMask_IncludesEvMouseUp()
    {
        using var driver = new DriverScope();
        var frm = new TFrame(new TRect(0, 0, 30, 10));
        Assert.True((frm.eventMask & Events.evMouseUp) != 0);
    }

    [Fact]
    public void Frame_EventMask_IncludesEvBroadcast()
    {
        using var driver = new DriverScope();
        var frm = new TFrame(new TRect(0, 0, 30, 10));
        Assert.True((frm.eventMask & Events.evBroadcast) != 0);
    }

    [Fact]
    public void Frame_Palette_Has5Entries()
    {
        using var driver = new DriverScope();
        var frm = new TFrame(new TRect(0, 0, 30, 10));
        Assert.Equal(5, frm.GetPalette().Size);
    }

    // ── TWindow construction ──────────────────────────────────────────────────

    [Fact]
    public void Window_GetTitle()
    {
        using var driver = new DriverScope();
        var win = new TWindow(new TRect(5, 5, 45, 20), "Hello", 1);
        Assert.Equal("Hello", win.GetTitle(20));
    }

    [Fact]
    public void Window_Number()
    {
        using var driver = new DriverScope();
        var win = new TWindow(new TRect(5, 5, 45, 20), "Hello", 1);
        Assert.Equal(1, win.number);
    }

    [Fact]
    public void Window_DefaultFlags()
    {
        using var driver = new DriverScope();
        var win = new TWindow(new TRect(5, 5, 45, 20), "Hello", 1);
        Assert.Equal((ushort)(Views.wfMove | Views.wfGrow | Views.wfClose | Views.wfZoom), win.flags);
    }

    [Fact]
    public void Window_DefaultPalette_Blue()
    {
        using var driver = new DriverScope();
        var win = new TWindow(new TRect(5, 5, 45, 20), "Hello", 1);
        Assert.Equal((short)Views.wpBlueWindow, win.palette);
    }

    [Fact]
    public void Window_HasSfShadow()
    {
        using var driver = new DriverScope();
        var win = new TWindow(new TRect(5, 5, 45, 20), "Hello", 1);
        Assert.True((win.state & Views.sfShadow) != 0);
    }

    [Fact]
    public void Window_HasOfSelectable()
    {
        using var driver = new DriverScope();
        var win = new TWindow(new TRect(5, 5, 45, 20), "Hello", 1);
        Assert.True((win.options & Views.ofSelectable) != 0);
    }

    [Fact]
    public void Window_HasOfTopSelect()
    {
        using var driver = new DriverScope();
        var win = new TWindow(new TRect(5, 5, 45, 20), "Hello", 1);
        Assert.True((win.options & Views.ofTopSelect) != 0);
    }

    [Fact]
    public void Window_GrowMode_GrowAllRel()
    {
        using var driver = new DriverScope();
        var win = new TWindow(new TRect(5, 5, 45, 20), "Hello", 1);
        Assert.Equal((byte)(Views.gfGrowAll | Views.gfGrowRel), win.growMode);
    }

    [Fact]
    public void Window_EventMask_IncludesEvMouseUp()
    {
        using var driver = new DriverScope();
        var win = new TWindow(new TRect(5, 5, 45, 20), "Hello", 1);
        Assert.True((win.eventMask & Events.evMouseUp) != 0);
    }

    [Fact]
    public void Window_FrameInserted()
    {
        using var driver = new DriverScope();
        var win = new TWindow(new TRect(5, 5, 45, 20), "Hello", 1);
        Assert.NotNull(win.frame);
        Assert.Same(win, win.frame.owner);
    }

    [Fact]
    public void Window_ZoomRect_EqualsInitialBounds()
    {
        using var driver = new DriverScope();
        var win = new TWindow(new TRect(5, 5, 45, 20), "Hello", 1);
        Assert.Equal(new TRect(5, 5, 45, 20), win.zoomRect);
    }

    // ── SizeLimits ───────────────────────────────────────────────────────────

    [Fact]
    public void Window_SizeLimits_MinEqualsMinWinSize()
    {
        using var driver = new DriverScope();
        var win = new TWindow(new TRect(5, 5, 45, 20), "Hello", 1);
        TPoint smin = default, smax = default;
        win.SizeLimits(ref smin, ref smax);
        Assert.Equal(TWindow.MinWinSize, smin);
    }

    // ── GetPalette by palette index ───────────────────────────────────────────

    [Fact]
    public void Window_Palette_Blue_Size8()
    {
        using var driver = new DriverScope();
        var win = new TWindow(new TRect(5, 5, 45, 20), "Hello", 1);
        Assert.Equal(8, win.GetPalette().Size);
    }

    [Fact]
    public void Window_Palette_Blue_Entry1_0x08()
    {
        using var driver = new DriverScope();
        var win = new TWindow(new TRect(5, 5, 45, 20), "Hello", 1);
        Assert.Equal(0x08, win.GetPalette()[1]);
    }

    [Fact]
    public void Window_Palette_Cyan_Entry1_0x10()
    {
        using var driver = new DriverScope();
        var win = new TWindow(new TRect(5, 5, 45, 20), "Hello", 1);
        win.palette = (short)Views.wpCyanWindow;
        Assert.Equal(0x10, win.GetPalette()[1]);
    }

    [Fact]
    public void Window_Palette_Gray_Entry1_0x18()
    {
        using var driver = new DriverScope();
        var win = new TWindow(new TRect(5, 5, 45, 20), "Hello", 1);
        win.palette = (short)Views.wpGrayWindow;
        Assert.Equal(0x18, win.GetPalette()[1]);
    }

    // ── Zoom ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Zoom_FirstCall_EnlargesWindow()
    {
        using var driver = new DriverScope();
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.SetState(Views.sfActive, true);
        var win = new TWindow(new TRect(5, 5, 45, 20), "W", 1);
        host.Insert(win);
        var orig = win.GetBounds();
        win.Zoom();
        Assert.NotEqual(orig, win.GetBounds());
    }

    [Fact]
    public void Zoom_FirstCall_RemembersZoomRect()
    {
        using var driver = new DriverScope();
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.SetState(Views.sfActive, true);
        var win = new TWindow(new TRect(5, 5, 45, 20), "W", 1);
        host.Insert(win);
        var orig = win.GetBounds();
        win.Zoom();
        Assert.Equal(orig, win.zoomRect);
    }

    [Fact]
    public void Zoom_SecondCall_RestoresBounds()
    {
        using var driver = new DriverScope();
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.SetState(Views.sfActive, true);
        var win = new TWindow(new TRect(5, 5, 45, 20), "W", 1);
        host.Insert(win);
        var orig = win.GetBounds();
        win.Zoom();
        win.Zoom();
        Assert.Equal(orig, win.GetBounds());
    }

    // ── kbTab clears event ────────────────────────────────────────────────────

    [Fact]
    public void KbTab_InWindow_ClearsEvent()
    {
        using var driver = new DriverScope();
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.SetState(Views.sfActive, true);
        host.SetState(Views.sfExposed, true);
        host.SetState(Views.sfFocused, true);
        var win = new TWindow(new TRect(0, 0, 40, 10), "W2", 2);
        host.Insert(win);
        win.Select();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbTab;
        win.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
    }

    // ── cmZoom command ────────────────────────────────────────────────────────

    [Fact]
    public void CmZoom_ClearsEvent()
    {
        using var driver = new DriverScope();
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.SetState(Views.sfActive, true);
        var win = new TWindow(new TRect(0, 0, 40, 10), "W3", 3);
        host.Insert(win);
        win.Select();
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmZoom;
        win.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void CmZoom_ChangesWindowBounds()
    {
        using var driver = new DriverScope();
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.SetState(Views.sfActive, true);
        var win = new TWindow(new TRect(0, 0, 40, 10), "W3", 3);
        host.Insert(win);
        win.Select();
        var origBounds = win.GetBounds();
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmZoom;
        win.HandleEvent(ref ev);
        Assert.NotEqual(origBounds, win.GetBounds());
    }

    // ── cmSelectWindowNum ─────────────────────────────────────────────────────

    [Fact]
    public void CmSelectWindowNum_ClearsEvent()
    {
        using var driver = new DriverScope();
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var win = new TWindow(new TRect(0, 0, 40, 10), "W7", 7);
        host.Insert(win);
        var ev = new TEvent { What = Events.evBroadcast };
        ev.message.command = Views.cmSelectWindowNum;
        ev.message.infoInt = 7;
        win.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
    }

    // ── SetState(sfSelected) ──────────────────────────────────────────────────

    [Fact]
    public void SetState_Selected_ActivatesFrame()
    {
        using var driver = new DriverScope();
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var win = new TWindow(new TRect(0, 0, 40, 10), "W4", 4);
        host.Insert(win);
        win.SetState(Views.sfSelected, true);
        Assert.NotEqual(0, win.frame.state & Views.sfActive);
    }

    [Fact]
    public void SetState_Deselected_DeactivatesFrame()
    {
        using var driver = new DriverScope();
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var win = new TWindow(new TRect(0, 0, 40, 10), "W4", 4);
        host.Insert(win);
        win.SetState(Views.sfSelected, true);
        win.SetState(Views.sfSelected, false);
        Assert.Equal(0, win.frame.state & Views.sfActive);
    }

    [Fact]
    public void SetState_Selected_EnablesCmZoom()
    {
        using var driver = new DriverScope();
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var win = new TWindow(new TRect(0, 0, 40, 10), "W4", 4);
        host.Insert(win);
        TView.DisableCommand(Views.cmZoom);
        TCommandSet saved = TView.curCommandSet;
        try
        {
            win.SetState(Views.sfSelected, true);
            Assert.True(TView.CommandEnabled(Views.cmZoom));
        }
        finally
        {
            win.SetState(Views.sfSelected, false);
            TView.curCommandSet = saved;
            TView.EnableCommand(Views.cmZoom);
        }
    }
}
