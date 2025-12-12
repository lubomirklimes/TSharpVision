// No DriverScope needed: all operations are pure geometry with no screen I/O.
using TSharpVision;
using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.Tests.Core;

public sealed class ViewGeometryTests
{
    // ── GetBounds / GetExtent / size / origin ─────────────────────────────────

    [Fact]
    public void GetBounds_MatchesCtor()
    {
        var v = new TView(new TRect(5, 4, 25, 14));
        Assert.Equal(new TRect(5, 4, 25, 14), v.GetBounds());
    }

    [Fact]
    public void GetExtent_IsZeroOriginBounds()
    {
        var v = new TView(new TRect(5, 4, 25, 14));
        Assert.Equal(new TRect(0, 0, 20, 10), v.GetExtent());
    }

    [Fact]
    public void Size_DerivedFromBounds()
    {
        var v = new TView(new TRect(5, 4, 25, 14));
        Assert.Equal(new TPoint(20, 10), v.size);
    }

    [Fact]
    public void Origin_DerivedFromBounds()
    {
        var v = new TView(new TRect(5, 4, 25, 14));
        Assert.Equal(new TPoint(5, 4), v.origin);
    }

    // ── MoveTo ────────────────────────────────────────────────────────────────

    [Fact]
    public void MoveTo_UpdatesOrigin()
    {
        var v = new TView(new TRect(5, 4, 25, 14));
        v.MoveTo(10, 8);
        Assert.Equal(new TPoint(10, 8), v.origin);
    }

    [Fact]
    public void MoveTo_PreservesSize()
    {
        var v = new TView(new TRect(5, 4, 25, 14));
        v.MoveTo(10, 8);
        Assert.Equal(new TPoint(20, 10), v.size);
    }

    // ── GrowTo ────────────────────────────────────────────────────────────────

    [Fact]
    public void GrowTo_UpdatesSize()
    {
        var v = new TView(new TRect(5, 4, 25, 14));
        v.GrowTo(30, 12);
        Assert.Equal(new TPoint(30, 12), v.size);
    }

    // ── SetState / GetState ───────────────────────────────────────────────────

    [Fact]
    public void SetState_ClearsSfVisible()
    {
        var v = new TView(new TRect(0, 0, 10, 5));
        v.SetState(Views.sfVisible, false);
        Assert.Equal(0, v.state & Views.sfVisible);
    }

    [Fact]
    public void SetState_SetsSfVisible()
    {
        var v = new TView(new TRect(0, 0, 10, 5));
        v.SetState(Views.sfVisible, false);
        v.SetState(Views.sfVisible, true);
        Assert.NotEqual(0, v.state & Views.sfVisible);
    }

    [Fact]
    public void GetState_MatchesStateBit()
    {
        var v = new TView(new TRect(0, 0, 10, 5));
        v.SetState(Views.sfVisible, true);
        Assert.True(v.GetState(Views.sfVisible));
    }

    // ── MakeGlobal / MakeLocal ────────────────────────────────────────────────

    [Fact]
    public void MakeGlobal_AddsOrigin()
    {
        var v = new TView(new TRect(5, 4, 25, 14));
        v.MoveTo(10, 8);
        var g = v.MakeGlobal(new TPoint(3, 2));
        Assert.Equal(v.origin + new TPoint(3, 2), g);
    }

    [Fact]
    public void MakeLocal_InvertsMakeGlobal()
    {
        var v = new TView(new TRect(5, 4, 25, 14));
        v.MoveTo(10, 8);
        var g = v.MakeGlobal(new TPoint(3, 2));
        Assert.Equal(new TPoint(3, 2), v.MakeLocal(g));
    }

    // ── SetCursor ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetCursor_StoresX()
    {
        var v = new TView(new TRect(0, 0, 20, 10));
        v.SetCursor(7, 3);
        Assert.Equal(7, v.cursor.x);
    }

    [Fact]
    public void SetCursor_StoresY()
    {
        var v = new TView(new TRect(0, 0, 20, 10));
        v.SetCursor(7, 3);
        Assert.Equal(3, v.cursor.y);
    }

    // ── DisableCommand / EnableCommand ────────────────────────────────────────

    [Fact]
    public void DisableCommand_RemovesFromCommandSet()
    {
        TCommandSet saved = TView.curCommandSet;
        try
        {
            TView.DisableCommand(Views.cmZoom);
            Assert.False(TView.CommandEnabled(Views.cmZoom));
        }
        finally { TView.curCommandSet = saved; }
    }

    [Fact]
    public void EnableCommand_RestoresInCommandSet()
    {
        TCommandSet saved = TView.curCommandSet;
        try
        {
            TView.DisableCommand(Views.cmZoom);
            TView.EnableCommand(Views.cmZoom);
            Assert.True(TView.CommandEnabled(Views.cmZoom));
        }
        finally { TView.curCommandSet = saved; }
    }

    // ── ContainsMouse ─────────────────────────────────────────────────────────

    [Fact]
    public void ContainsMouse_TrueForInsidePoint()
    {
        var v = new TView(new TRect(10, 8, 30, 18));
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = v.MakeGlobal(new TPoint(2, 2));
        Assert.True(v.ContainsMouse(ev));
    }

    [Fact]
    public void ContainsMouse_FalseForOutsidePoint()
    {
        var v = new TView(new TRect(10, 8, 30, 18));
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(999, 999);
        Assert.False(v.ContainsMouse(ev));
    }

    // ── ClearEvent ────────────────────────────────────────────────────────────

    [Fact]
    public void ClearEvent_ZerosWhat()
    {
        var v = new TView(new TRect(0, 0, 10, 5));
        var ev = new TEvent { What = Events.evCommand };
        v.ClearEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void ClearEvent_StampsInfoPtr()
    {
        var v = new TView(new TRect(0, 0, 10, 5));
        var ev = new TEvent { What = Events.evCommand };
        v.ClearEvent(ref ev);
        Assert.Same(v, ev.message.infoPtr);
    }

    // ── GetHelpCtx ────────────────────────────────────────────────────────────

    [Fact]
    public void GetHelpCtx_DefaultIsHcNoContext()
    {
        var v = new TView(new TRect(0, 0, 10, 5));
        Assert.Equal(Views.hcNoContext, v.GetHelpCtx());
    }

    [Fact]
    public void GetHelpCtx_ReturnsDraggingWhileDragging()
    {
        var v = new TView(new TRect(0, 0, 10, 5));
        v.SetState(Views.sfDragging, true);
        Assert.Equal(Views.hcDragging, v.GetHelpCtx());
    }

    [Fact]
    public void GetHelpCtx_RestoresHelpCtxAfterDrag()
    {
        var v = new TView(new TRect(0, 0, 10, 5)) { helpCtx = 42 };
        v.SetState(Views.sfDragging, true);
        v.SetState(Views.sfDragging, false);
        Assert.Equal(42, v.GetHelpCtx());
    }

    // ── Valid / DataSize ──────────────────────────────────────────────────────

    [Fact]
    public void Valid_DefaultReturnsTrue()
    {
        var v = new TView(new TRect(0, 0, 10, 5));
        Assert.True(v.Valid(Views.cmOK));
    }

    [Fact]
    public void DataSize_DefaultZero()
    {
        var v = new TView(new TRect(0, 0, 10, 5));
        Assert.Equal(0, (int)v.DataSize());
    }

    // ── TGroup.GetHelpCtx delegation ─────────────────────────────────────────

    [Fact]
    public void Group_GetHelpCtx_FallsBackToBaseWhenNoChildren()
    {
        var g = new TGroup(new TRect(0, 0, 80, 25)) { helpCtx = 7 };
        Assert.Equal(7, g.GetHelpCtx());
    }

    [Fact]
    public void Group_GetHelpCtx_UsesCurrentChildHelpCtx()
    {
        var g = new TGroup(new TRect(0, 0, 80, 25)) { helpCtx = 7 };
        var child = new TView(new TRect(0, 0, 4, 4)) { helpCtx = 99 };
        child.options |= Views.ofSelectable;
        g.Insert(child);
        Assert.Equal(99, g.GetHelpCtx());
    }

    [Fact]
    public void Group_GetHelpCtx_FallsBackWhenChildIsHcNoContext()
    {
        var g = new TGroup(new TRect(0, 0, 80, 25)) { helpCtx = 7 };
        var child = new TView(new TRect(0, 0, 4, 4)) { helpCtx = Views.hcNoContext };
        child.options |= Views.ofSelectable;
        g.Insert(child);
        Assert.Equal(7, g.GetHelpCtx());
    }
}
