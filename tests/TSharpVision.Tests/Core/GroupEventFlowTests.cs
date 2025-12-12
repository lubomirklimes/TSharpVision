using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class GroupEventFlowTests
{
    // ── linked-list shape ────────────────────────────────────────────────────

    [Fact]
    public void Insert_SetsGroupLast()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        var v1 = new ProbeView(new TRect(0, 0, 10, 10));
        grp.Insert(v1);
        Assert.NotNull(grp.last);
    }

    [Fact]
    public void Insert_FirstInsertedBecomesCurrentAfterMultiple()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        var v1 = new ProbeView(new TRect(0, 0, 10, 10));
        var v2 = new ProbeView(new TRect(10, 0, 20, 10));
        var v3 = new ProbeView(new TRect(20, 0, 30, 10));
        grp.Insert(v3);
        grp.Insert(v2);
        grp.Insert(v1);
        // first inserted (v3) remains current per upstream tgroup.cc:246
        Assert.Same(v3, grp.current);
    }

    [Fact]
    public void IndexOf_ReturnsCorrectIndex()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        var v1 = new ProbeView(new TRect(0, 0, 10, 10));
        var v2 = new ProbeView(new TRect(10, 0, 20, 10));
        var v3 = new ProbeView(new TRect(20, 0, 30, 10));
        grp.Insert(v3);
        grp.Insert(v2);
        grp.Insert(v1);
        Assert.Equal(1, grp.IndexOf(v1));
        Assert.Equal(3, grp.IndexOf(v3));
    }

    [Fact]
    public void At_ReturnsLast()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        var v3 = new ProbeView(new TRect(20, 0, 30, 10));
        grp.Insert(v3);
        Assert.Same(v3, grp.At(0));
    }

    [Fact]
    public void Matches_ReturnsTrueForOwnedView()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        var v1 = new ProbeView(new TRect(0, 0, 10, 10));
        grp.Insert(v1);
        Assert.True(grp.Matches(v1));
    }

    [Fact]
    public void Select_PromotesToCurrent()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        var v1 = new ProbeView(new TRect(0, 0, 10, 10));
        var v3 = new ProbeView(new TRect(20, 0, 30, 10));
        grp.Insert(v3);
        grp.Insert(v1);
        v1.Select();
        Assert.Same(v1, grp.current);
    }

    // ── FirstThat ────────────────────────────────────────────────────────────

    [Fact]
    public void FirstThat_FindsSelectableView()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        var v1 = new ProbeView(new TRect(0, 0, 10, 10));
        grp.Insert(v1);
        var found = grp.FirstThat<TView>((vv, _) => (vv.options & Views.ofSelectable) != 0, null!);
        Assert.NotNull(found);
    }

    // ── Remove / InsertBefore ─────────────────────────────────────────────────

    [Fact]
    public void Remove_ClearsOwner()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        var v2 = new ProbeView(new TRect(10, 0, 20, 10));
        grp.Insert(v2);
        grp.Remove(v2);
        Assert.Null(v2.owner);
    }

    [Fact]
    public void Remove_PreservesOtherViews()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        var v1 = new ProbeView(new TRect(0, 0, 10, 10));
        var v2 = new ProbeView(new TRect(10, 0, 20, 10));
        var v3 = new ProbeView(new TRect(20, 0, 30, 10));
        grp.Insert(v3); grp.Insert(v2); grp.Insert(v1);
        grp.Remove(v2);
        Assert.Equal(1, grp.IndexOf(v1));
        Assert.Equal(2, grp.IndexOf(v3));
    }

    [Fact]
    public void InsertBefore_RelinksView()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        var v1 = new ProbeView(new TRect(0, 0, 10, 10));
        var v2 = new ProbeView(new TRect(10, 0, 20, 10));
        var v3 = new ProbeView(new TRect(20, 0, 30, 10));
        grp.Insert(v3); grp.Insert(v1);
        grp.InsertBefore(v2, v3);
        Assert.Equal(2, grp.IndexOf(v2));
    }

    // ── Focused dispatch ──────────────────────────────────────────────────────

    [Fact]
    public void HandleEvent_FocusedKey_DeliveredOnlyToCurrent()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        grp.SetState(Views.sfActive, true);
        var v1 = new ProbeView(new TRect(0, 0, 10, 10));
        var v2 = new ProbeView(new TRect(10, 0, 20, 10));
        var v3 = new ProbeView(new TRect(20, 0, 30, 10));
        grp.Insert(v3); grp.Insert(v2); grp.Insert(v1);
        v1.Select();
        v1.Hits = v2.Hits = v3.Hits = 0;
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbEsc;
        grp.HandleEvent(ref ev);
        Assert.Equal(1, v1.Hits);
        Assert.Equal(0, v2.Hits);
        Assert.Equal(0, v3.Hits);
    }

    // ── Positional dispatch ───────────────────────────────────────────────────

    [Fact]
    public void HandleEvent_MouseDown_HitsViewUnderMouse()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        var v1 = new ProbeView(new TRect(0, 0, 10, 10));
        var v2 = new ProbeView(new TRect(10, 0, 20, 10));
        var v3 = new ProbeView(new TRect(20, 0, 30, 10));
        grp.Insert(v3); grp.Insert(v2); grp.Insert(v1);
        v1.Hits = v2.Hits = v3.Hits = 0;
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(15, 5);   // inside v2
        grp.HandleEvent(ref ev);
        Assert.Equal(1, v2.Hits);
        Assert.Equal(0, v1.Hits);
        Assert.Equal(0, v3.Hits);
    }

    // ── Broadcast dispatch ────────────────────────────────────────────────────

    [Fact]
    public void HandleEvent_Broadcast_HitsAllSubviews()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        var v1 = new ProbeView(new TRect(0, 0, 10, 10));
        var v2 = new ProbeView(new TRect(10, 0, 20, 10));
        var v3 = new ProbeView(new TRect(20, 0, 30, 10));
        grp.Insert(v3); grp.Insert(v2); grp.Insert(v1);
        v1.Hits = v2.Hits = v3.Hits = 0;
        var ev = new TEvent { What = Events.evBroadcast };
        ev.message.command = Views.cmReceivedFocus;
        grp.HandleEvent(ref ev);
        Assert.Equal(1, v1.Hits);
        Assert.Equal(1, v2.Hits);
        Assert.Equal(1, v3.Hits);
    }

    // ── ExecView ─────────────────────────────────────────────────────────────

    [Fact]
    public void ExecView_ReturnsCmOK()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        var modal = new ModalView(new TRect(2, 2, 20, 8), Views.cmOK);
        ushort rv = grp.ExecView(modal);
        Assert.Equal(Views.cmOK, rv);
    }

    [Fact]
    public void ExecView_RemovesUnparentedView()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        var modal = new ModalView(new TRect(2, 2, 20, 8), Views.cmOK);
        grp.ExecView(modal);
        Assert.Null(modal.owner);
    }

    [Fact]
    public void ExecView_ReturnsCmCancel()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        var cancel = new ModalView(new TRect(2, 2, 20, 8), Views.cmCancel);
        ushort rv = grp.ExecView(cancel);
        Assert.Equal(Views.cmCancel, rv);
    }

    // ── Valid / GetHelpCtx ────────────────────────────────────────────────────

    [Fact]
    public void Valid_AllSubviewsValid()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        grp.Insert(new ProbeView(new TRect(0, 0, 10, 10)));
        Assert.True(grp.Valid(Views.cmOK));
    }

    [Fact]
    public void GetHelpCtx_FallsBackWhenNoSelectableCurrent()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25)) { helpCtx = Views.hcNoContext };
        Assert.Equal(Views.hcNoContext, grp.GetHelpCtx());
    }

    // ── SelectNext ───────────────────────────────────────────────────────────

    [Fact]
    public void SelectNext_AdvancesCurrent()
    {
        using var driver = new DriverScope();
        var grp = new TestGroup(new TRect(0, 0, 80, 25));
        var v1 = new ProbeView(new TRect(0, 0, 10, 10));
        var v2 = new ProbeView(new TRect(10, 0, 20, 10));
        grp.Insert(v2); grp.Insert(v1);
        v1.Select();
        var before = grp.current;
        grp.SelectNext(true);
        Assert.NotSame(before, grp.current);
    }
}
