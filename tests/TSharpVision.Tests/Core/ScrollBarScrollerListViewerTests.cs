using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class ScrollBarScrollerListViewerTests : IDisposable
{
    private readonly DriverScope _driver;
    public ScrollBarScrollerListViewerTests() => _driver = new DriverScope();
    public void Dispose() => _driver.Dispose();

    // ── TScrollBar ───────────────────────────────────────────────────────

    [Fact]
    public void ScrollBar_Vertical_GrowMode()
    {
        var vsb = new TScrollBar(new TRect(0, 0, 1, 10));
        Assert.Equal((ushort)(Views.gfGrowLoX | Views.gfGrowHiX | Views.gfGrowHiY), vsb.growMode);
    }

    [Fact]
    public void ScrollBar_Horizontal_GrowMode()
    {
        var hsb = new TScrollBar(new TRect(0, 0, 30, 1));
        Assert.Equal((ushort)(Views.gfGrowLoY | Views.gfGrowHiX | Views.gfGrowHiY), hsb.growMode);
    }

    [Fact]
    public void ScrollBar_GetSize_Reports_Correct_Dimension()
    {
        var vsb = new TScrollBar(new TRect(0, 0, 1, 10));
        var hsb = new TScrollBar(new TRect(0, 0, 30, 1));
        Assert.Equal(10, vsb.GetSize());
        Assert.Equal(30, hsb.GetSize());
        // Minimum = 3
        Assert.Equal(3, new TScrollBar(new TRect(0, 0, 1, 2)).GetSize());
    }

    [Fact]
    public void ScrollBar_Vertical_Chars()
    {
        var vsb = new TScrollBar(new TRect(0, 0, 1, 10));
        Assert.Equal('▲', vsb.chars[0]);
        Assert.Equal('▼', vsb.chars[1]);
    }

    [Fact]
    public void ScrollBar_Horizontal_Chars()
    {
        var hsb = new TScrollBar(new TRect(0, 0, 30, 1));
        Assert.Equal('◄', hsb.chars[0]);
        Assert.Equal('►', hsb.chars[1]);
    }

    [Fact]
    public void ScrollBar_PaletteSize_Is3()
    {
        var vsb = new TScrollBar(new TRect(0, 0, 1, 10));
        Assert.Equal(3, vsb.GetPalette().Size);
    }

    [Fact]
    public void SetParams_StoresValues()
    {
        var vsb = new TScrollBar(new TRect(0, 0, 1, 10));
        vsb.SetParams(0, 0, 100, 10, 1);
        Assert.Equal(0, vsb.value);
        Assert.Equal(0, vsb.minVal);
        Assert.Equal(100, vsb.maxVal);
        Assert.Equal(10, vsb.pgStep);
        Assert.Equal(1, vsb.arStep);
    }

    [Fact]
    public void SetValue_ClampsAndBroadcasts()
    {
        var vsb = new TScrollBar(new TRect(0, 0, 1, 10));
        var owner = new BroadcastProbeGroup(new TRect(0, 0, 80, 25));
        owner.Insert(vsb);
        vsb.SetParams(0, 0, 100, 10, 1);

        vsb.SetValue(50);
        Assert.Equal(50, vsb.value);
        Assert.Equal(Views.cmScrollBarChanged, owner.LastCmd);

        vsb.SetValue(9999);
        Assert.Equal(100, vsb.value);

        vsb.SetValue(-50);
        Assert.Equal(0, vsb.value);
    }

    [Fact]
    public void SetRange_DoesNotCrash_And_ValueInRange()
    {
        var vsb = new TScrollBar(new TRect(0, 0, 1, 10));
        vsb.SetParams(50, 0, 100, 10, 1);
        vsb.SetRange(0, 20);
        // After SetRange, value must be within [0, 20].
        Assert.True(vsb.value >= 0 && vsb.value <= 20);
    }

    [Fact]
    public void SetStep_Persists()
    {
        var vsb = new TScrollBar(new TRect(0, 0, 1, 10));
        vsb.SetStep(5, 2);
        Assert.Equal(5, vsb.pgStep);
        Assert.Equal(2, vsb.arStep);
    }

    [Fact]
    public void ScrollStep_Arrow_And_Page()
    {
        var vsb = new TScrollBar(new TRect(0, 0, 1, 10));
        vsb.SetParams(50, 0, 100, 10, 1);
        Assert.Equal(-1,  vsb.ScrollStep((int)Views.sbLeftArrow));
        Assert.Equal( 1,  vsb.ScrollStep((int)Views.sbRightArrow));
        Assert.Equal(-10, vsb.ScrollStep((int)Views.sbPageLeft));
        Assert.Equal( 10, vsb.ScrollStep((int)Views.sbPageRight));
    }

    [Fact]
    public void GetPos_Formula()
    {
        var vsb = new TScrollBar(new TRect(0, 0, 1, 10));
        vsb.SetParams(0, 0, 100, 10, 1);
        Assert.Equal(1, vsb.GetPos());
        vsb.SetValue(100);
        Assert.Equal(vsb.GetSize() - 2, vsb.GetPos());
    }

    [Fact]
    public void ScrollDraw_BroadcastsChanged()
    {
        var vsb = new TScrollBar(new TRect(0, 0, 1, 10));
        var owner = new BroadcastProbeGroup(new TRect(0, 0, 80, 25));
        owner.Insert(vsb);
        owner.LastCmd = 0;
        vsb.ScrollDraw();
        Assert.Equal(Views.cmScrollBarChanged, owner.LastCmd);
    }

    // ── TScroller ────────────────────────────────────────────────────────

    [Fact]
    public void Scroller_Constructor_StoresBars()
    {
        var hsb = new TScrollBar(new TRect(0, 9, 39, 10));
        var vsb = new TScrollBar(new TRect(39, 0, 40, 9));
        var scr = new TScroller(new TRect(0, 0, 39, 9), hsb, vsb);
        Assert.Same(hsb, scr.hScrollBar);
        Assert.Same(vsb, scr.vScrollBar);
        Assert.Equal(new TPoint(0, 0), scr.delta);
        Assert.Equal(new TPoint(0, 0), scr.limit);
        Assert.True((scr.options & Views.ofSelectable) != 0);
        Assert.True((scr.eventMask & Events.evBroadcast) != 0);
        Assert.Equal(2, scr.GetPalette().Size);
    }

    [Fact]
    public void Scroller_SetLimit_ProgramsBars()
    {
        var hsb = new TScrollBar(new TRect(0, 9, 39, 10));
        var vsb = new TScrollBar(new TRect(39, 0, 40, 9));
        var scr = new TScroller(new TRect(0, 0, 39, 9), hsb, vsb);
        scr.SetLimit(200, 100);
        Assert.Equal(200, scr.limit.x);
        Assert.Equal(100, scr.limit.y);
        Assert.Equal(200 - scr.size.x, hsb.maxVal);
        Assert.Equal(100 - scr.size.y, vsb.maxVal);
    }

    [Fact]
    public void Scroller_ScrollTo_UpdatesBars()
    {
        var hsb = new TScrollBar(new TRect(0, 19, 40, 20));
        var vsb = new TScrollBar(new TRect(39, 0, 40, 19));
        var scr = new TScroller(new TRect(0, 0, 39, 19), hsb, vsb);
        var host = new BroadcastProbeGroup(new TRect(0, 0, 80, 25));
        host.Insert(hsb); host.Insert(vsb); host.Insert(scr);
        scr.SetLimit(200, 100);
        scr.ScrollTo(20, 10);
        Assert.Equal(20, hsb.value);
        Assert.Equal(10, vsb.value);
    }

    [Fact]
    public void Scroller_ShutDown_ClearsBars()
    {
        var hsb = new TScrollBar(new TRect(0, 9, 39, 10));
        var vsb = new TScrollBar(new TRect(39, 0, 40, 9));
        var scr = new TScroller(new TRect(0, 0, 10, 10), hsb, vsb);
        scr.ShutDown();
        Assert.Null(scr.hScrollBar);
        Assert.Null(scr.vScrollBar);
    }

    // ── TListViewer ──────────────────────────────────────────────────────

    [Fact]
    public void ListViewer_Constructor()
    {
        var lhs = new TScrollBar(new TRect(0, 0, 30, 1));
        var lvs = new TScrollBar(new TRect(0, 0, 1, 10));
        var lv  = new ProbeListViewer(new TRect(0, 0, 30, 10), 1, lhs, lvs);
        Assert.Equal(1, lv.numCols);
        Assert.Same(lhs, lv.hScrollBar);
        Assert.Same(lvs, lv.vScrollBar);
        Assert.True((lv.options & Views.ofFirstClick) != 0);
        Assert.True((lv.options & Views.ofSelectable) != 0);
        Assert.True((lv.eventMask & Events.evBroadcast) != 0);
        Assert.Equal(5, lv.GetPalette().Size);
    }

    [Fact]
    public void ListViewer_SetRange_ProgramsBar()
    {
        var lvs = new TScrollBar(new TRect(0, 0, 1, 10));
        var lv  = new ProbeListViewer(new TRect(0, 0, 30, 10), 1, null, lvs);
        lv.SetRange(50);
        Assert.Equal(50, lv.range);
        Assert.Equal(49, lvs.maxVal);
    }

    [Fact]
    public void ListViewer_FocusItemNum_Clamps()
    {
        var lv = new ProbeListViewer(new TRect(0, 0, 30, 10), 1, null, null);
        lv.SetRange(50);
        lv.FocusItemNum(-5);
        Assert.Equal(0, lv.focused);
        lv.FocusItemNum(999);
        Assert.Equal(49, lv.focused);
    }

    [Fact]
    public void ListViewer_FocusItem_ScrollsTopItem()
    {
        var lv = new ProbeListViewer(new TRect(0, 0, 30, 10), 1, null, null);
        lv.SetRange(50);
        lv.FocusItem(0);
        Assert.Equal(0, lv.topItem);
        lv.FocusItem(20);
        Assert.Equal(20 - lv.size.y + 1, lv.topItem);
    }

    [Fact]
    public void ListViewer_IsSelected_MatchesFocused()
    {
        var lv = new ProbeListViewer(new TRect(0, 0, 30, 10), 1, null, null);
        lv.SetRange(20);
        lv.focused = 7;
        Assert.True(lv.IsSelected(7));
        Assert.False(lv.IsSelected(8));
    }

    [Fact]
    public void ListViewer_GetText_DefaultEmpty()
    {
        var lv = new ProbeListViewer(new TRect(0, 0, 30, 10), 1, null, null);
        Assert.Equal("", lv.GetText(0, 10));
    }

    [Fact]
    public void ListViewer_SelectItem_BroadcastsListItemSelected()
    {
        var lv = new ProbeListViewer(new TRect(0, 0, 30, 10), 1, null, null);
        var host = new BroadcastProbeGroup(new TRect(0, 0, 80, 25));
        host.Insert(lv);
        lv.SetRange(20);
        host.LastCmd = 0;
        lv.SelectItem(7);
        Assert.Equal(Views.cmListItemSelected, host.LastCmd);
    }

    [Fact]
    public void ListViewer_FocusItem_BroadcastsListItemFocused()
    {
        var lv = new ProbeListViewer(new TRect(0, 0, 30, 10), 1, null, null);
        var host = new BroadcastProbeGroup(new TRect(0, 0, 80, 25));
        host.Insert(lv);
        lv.SetRange(20);
        host.LastCmd = 0;
        lv.FocusItem(3);
        Assert.Equal(Views.cmListItemFocused, host.LastCmd);
    }

    [Fact]
    public void ListViewer_KbDown_AdvancesFocused()
    {
        var lv = new ProbeListViewer(new TRect(0, 0, 30, 10), 1, null, null);
        lv.SetRange(20);
        lv.FocusItem(5);
        int before = lv.focused;
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbDown;
        lv.HandleEvent(ref ev);
        Assert.Equal(before + 1, lv.focused);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void ListViewer_KbHome_FocusesTopItem()
    {
        var lv = new ProbeListViewer(new TRect(0, 0, 30, 10), 1, null, null);
        lv.SetRange(50);
        lv.FocusItem(30);
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbHome;
        lv.HandleEvent(ref ev);
        Assert.Equal(lv.topItem, lv.focused);
    }
}
