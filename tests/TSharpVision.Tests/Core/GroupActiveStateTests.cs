using TSharpVision;
using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.Tests.Core;

// Original: TGROUP.CPP InsertBefore reapplies sfActive only when it existed in the saved state.
// Corrected: insertion invokes the same state transition, including recursive nested-group effects.
public sealed class GroupActiveStateTests
{
    [Fact]
    public void InsertingOrdinaryInactiveViewIntoActiveGroupPreservesSavedInactiveState()
    {
        var owner = Group(active: true);
        var child = new StateView();

        owner.Insert(child);

        Assert.False(child.GetState(Views.sfActive));
        Assert.DoesNotContain((Views.sfActive, true), child.Transitions);
    }

    [Fact]
    public void InsertingPreviouslyActiveViewReappliesHistoricalActiveState()
    {
        var owner = Group(active: true);
        var child = new StateView();
        child.SetState(Views.sfActive, true);
        child.Transitions.Clear();

        owner.Insert(child);

        Assert.True(child.GetState(Views.sfActive));
        Assert.Contains((Views.sfActive, true), child.Transitions);
    }

    [Fact]
    public void InsertingActiveNestedGroupRecursivelyReappliesActiveState()
    {
        var owner = Group(active: true);
        var nested = Group(active: false);
        var leaf = new StateView();
        nested.Insert(leaf);
        nested.SetState(Views.sfActive, true);
        leaf.state &= unchecked((ushort)~Views.sfActive);
        leaf.Transitions.Clear();

        owner.Insert(nested);

        Assert.True(nested.GetState(Views.sfActive));
        Assert.True(leaf.GetState(Views.sfActive));
        Assert.Contains((Views.sfActive, true), leaf.Transitions);
    }

    [Fact]
    public void InsertingInactiveViewIntoInactiveGroupRemainsInactive()
    {
        var owner = Group(active: false);
        var child = new StateView();

        owner.Insert(child);

        Assert.False(child.GetState(Views.sfActive));
    }

    [Fact]
    public void ActivatingAndDeactivatingOwnerPropagatesRecursively()
    {
        var owner = Group(active: false);
        var nested = Group(active: false);
        var leaf = new StateView();
        nested.Insert(leaf);
        owner.Insert(nested);

        owner.SetState(Views.sfActive, true);
        Assert.True(nested.GetState(Views.sfActive));
        Assert.True(leaf.GetState(Views.sfActive));

        owner.SetState(Views.sfActive, false);
        Assert.False(nested.GetState(Views.sfActive));
        Assert.False(leaf.GetState(Views.sfActive));
    }

    [Fact]
    public void ActivationDoesNotGrantSelectionOrFocus()
    {
        var owner = Group(active: true);
        var child = new StateView();
        child.SetState(Views.sfActive, true);

        owner.Insert(child);

        Assert.False(child.GetState(Views.sfSelected));
        Assert.False(child.GetState(Views.sfFocused));
        Assert.Null(owner.current);
    }

    [Fact]
    public void RemoveAndReinsertReappliesSavedActiveStateWithoutSelecting()
    {
        var first = Group(active: true);
        var second = Group(active: true);
        var child = new StateView();
        child.SetState(Views.sfActive, true);
        first.Insert(child);
        first.Remove(child);
        child.Transitions.Clear();

        second.Insert(child);

        Assert.True(child.GetState(Views.sfActive));
        Assert.Contains((Views.sfActive, true), child.Transitions);
        Assert.False(child.GetState(Views.sfSelected));
        Assert.False(child.GetState(Views.sfFocused));
    }

    [Fact]
    public void DisabledViewKeepsDisabledSemanticsWhenActiveIsReapplied()
    {
        var owner = Group(active: true);
        var child = new StateView();
        child.SetState(Views.sfDisabled, true);
        child.SetState(Views.sfActive, true);

        owner.Insert(child);

        Assert.True(child.GetState(Views.sfDisabled));
        Assert.True(child.GetState(Views.sfActive));
        Assert.False(child.GetState(Views.sfSelected));
        Assert.False(child.GetState(Views.sfFocused));
    }

    private static TGroup Group(bool active)
    {
        var group = new TGroup(new TRect(0, 0, 40, 10));
        if (active)
            group.SetState(Views.sfActive, true);
        return group;
    }

    private sealed class StateView : TView
    {
        internal StateView() : base(new TRect(0, 0, 1, 1)) { }

        internal List<(ushort State, bool Enabled)> Transitions { get; } = [];

        public override void SetState(ushort aState, bool enable)
        {
            Transitions.Add((aState, enable));
            base.SetState(aState, enable);
        }
    }
}
