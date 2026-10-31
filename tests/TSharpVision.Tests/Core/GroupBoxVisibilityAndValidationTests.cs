using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

/// <summary>
/// TV-1.1-H1. A hidden or disabled control never acts on a mnemonic, wherever it sits, and a validating field nested in a
/// group box cannot be left for another group without its <see cref="Views.ofValidate"/> check, by any route: Tab,
/// Shift+Tab, the mouse, a label or cluster mnemonic, or <see cref="TView.Focus"/>. The flat-dialog cases pin the same
/// behaviour without group boxes.
/// </summary>
[Collection("NonParallel")]
public sealed class GroupBoxVisibilityAndValidationTests : IDisposable
{
    private readonly DriverScope _scope = new();
    private readonly TApplication _app = new();
    private readonly TDialog _dialog = new(new TRect(0, 0, 76, 22), "Hardening");

    public void Dispose()
    {
        _dialog.ShutDown();
        _app.Dispose();
        _scope.Dispose();
    }

    private sealed class ProbeButton(TRect bounds, string title) : TButton(bounds, title, 1000, ButtonConstants.bfNormal)
    {
        public int Presses;
        public override void Press() => Presses++;
    }

    /// <summary>An input line marked <see cref="Views.ofValidate"/> whose release check the test controls and counts.</summary>
    private sealed class ValidatingInput : TInputLine
    {
        public bool Accept = true;
        public int ReleaseChecks;

        public ValidatingInput(TRect bounds) : base(bounds, 30) => options |= Views.ofValidate;

        public override bool Valid(ushort command)
        {
            if (command != Views.cmReleasedFocus) return base.Valid(command);
            ReleaseChecks++;
            return Accept;
        }
    }

    /// <summary>A selectable view that takes a click without tracking the mouse and counts clicks and focus gains.</summary>
    private sealed class ClickProbe : TView
    {
        public int Clicks, Focusings;

        public ClickProbe(TRect bounds) : base(bounds) => options |= Views.ofSelectable | Views.ofFirstClick;

        public override void SetState(ushort aState, bool enable)
        {
            if ((aState & Views.sfFocused) != 0 && enable && (state & Views.sfFocused) == 0) Focusings++;
            base.SetState(aState, enable);
        }

        public override void HandleEvent(ref TEvent ev)
        {
            base.HandleEvent(ref ev);
            if (ev.What == Events.evMouseDown) { Clicks++; ClearEvent(ref ev); }
        }
    }

    private static TInputLine Input(int y) => new(new TRect(12, y, 28, y + 1), 30);
    private static ValidatingInput Validating(int y) => new(new TRect(12, y, 28, y + 1));
    private static ProbeButton Button(int y, string title) => new(new TRect(30, y, 42, y + 2), title);

    private void Open() => _app.DeskTop!.Insert(_dialog);

    private static TEvent Key(ushort keyCode)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        ev.keyDown.charScan = new CharScanType(keyCode);
        return ev;
    }

    private static TEvent Typed(char c)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = c;
        ev.keyDown.charScan.charCode = (byte)c;
        ev.keyDown.text = c.ToString();
        return ev;
    }

    private TEvent Send(TEvent ev)
    {
        _dialog.HandleEvent(ref ev);
        return ev;
    }

    private TEvent Click(TView target)
    {
        var click = new TEvent { What = Events.evMouseDown };
        click.mouse.where = target.MakeGlobal(new TPoint(1, 0));
        click.mouse.buttons = (byte)Events.mbLeftButton;
        return Send(click);
    }

    private static IEnumerable<TView> Descendants(TGroup group)
    {
        var result = new List<TView>();
        group.ForEachView(v =>
        {
            result.Add(v);
            if (v is TGroup g) result.AddRange(Descendants(g));
        });
        return result;
    }

    private IReadOnlyList<TView> FocusedLeaves() =>
        Descendants(_dialog).Where(v => v.GetState(Views.sfFocused)
            && (v is not TGroup g || g.current == null || !g.current.GetState(Views.sfFocused))).ToList();

    /// <summary>Every group on the path to <paramref name="leaf"/> has the next view down as its current, and is focused.</summary>
    private void AssertFocusChain(TView leaf)
    {
        Assert.Same(leaf, Assert.Single(FocusedLeaves()));
        for (TView v = leaf; !ReferenceEquals(v, _dialog); v = v.owner!)
        {
            Assert.Same(v, v.owner!.current);
            Assert.True(v.GetState(Views.sfSelected | Views.sfFocused));
        }
    }

    // ── A–D: hidden and disabled buttons ────────────────────────────────────

    /// <summary>A, D. The defect TV-1.1-FINAL left: a hidden button in the focused group still pressed on its mnemonic.</summary>
    [Fact]
    public void AHiddenOrDisabledButtonInTheFocusedGroupIgnoresAltAndPlainMnemonics()
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 10), "A");
        var ok = Button(1, "~O~k"); var hidden = Button(3, "~H~old"); var disabled = Button(5, "~D~rop");
        a.Insert(ok); a.Insert(hidden); a.Insert(disabled);
        _dialog.Insert(a);
        Open();
        ok.Focus();
        hidden.Hide();
        disabled.SetState(Views.sfDisabled, true);

        Assert.Equal(Events.evKeyDown, Send(Key(Keys.kbAltH)).What);
        Assert.Equal(Events.evKeyDown, Send(Typed('h')).What);
        Assert.Equal(Events.evKeyDown, Send(Key(Keys.kbAltD)).What);
        Assert.Equal(Events.evKeyDown, Send(Typed('d')).What);
        Assert.Equal((0, 0), (hidden.Presses, disabled.Presses));
        AssertFocusChain(ok);

        // Shown and enabled again, the same keys press them.
        hidden.Show();
        disabled.SetState(Views.sfDisabled, false);
        Assert.Equal(Events.evNothing, Send(Key(Keys.kbAltH)).What);
        Assert.Equal(Events.evNothing, Send(Typed('d')).What);
        Assert.Equal((1, 1), (hidden.Presses, disabled.Presses));
    }

    /// <summary>A, D in a flat dialog: the rule is the framework's, not the group box's.</summary>
    [Fact]
    public void AHiddenOrDisabledButtonInAFlatDialogIgnoresItsMnemonics()
    {
        var ok = Button(1, "~O~k"); var hidden = Button(3, "~H~old"); var disabled = Button(5, "~D~rop");
        _dialog.Insert(ok); _dialog.Insert(hidden); _dialog.Insert(disabled);
        Open();
        // Hiding a selectable view in a plain group resets its current view (TGroup.ResetCurrent), so focus comes last.
        hidden.Hide();
        disabled.SetState(Views.sfDisabled, true);
        ok.Focus();

        Assert.Equal(Events.evKeyDown, Send(Key(Keys.kbAltH)).What);
        Assert.Equal(Events.evKeyDown, Send(Typed('h')).What);
        Assert.Equal(Events.evKeyDown, Send(Key(Keys.kbAltD)).What);
        Assert.Equal((0, 0), (hidden.Presses, disabled.Presses));
        Assert.Same(ok, Assert.Single(FocusedLeaves()));
    }

    /// <summary>B, C. Hidden buttons in a sibling group, in a nested group (focused and not), and inside a hidden nested group.</summary>
    [Fact]
    public void HiddenButtonsInSiblingAndNestedGroupsIgnoreTheirMnemonics()
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 8), "A");
        var ok = Button(1, "~O~k");
        a.Insert(ok);
        var b = new TGroupBox(new TRect(1, 9, 74, 21), "B");
        var sibling = Button(1, "~S~ibling");
        var nested = new TGroupBox(new TRect(44, 1, 70, 6), "Nested");
        var inNested = new ProbeButton(new TRect(2, 1, 14, 3), "~N~ested");
        var nestedOk = new ProbeButton(new TRect(14, 1, 24, 3), "~Y~es");
        nested.Insert(inNested); nested.Insert(nestedOk);
        var hiddenBox = new TGroupBox(new TRect(44, 6, 70, 11), "Hidden");
        var inHiddenBox = new ProbeButton(new TRect(2, 1, 14, 3), "~G~o");
        hiddenBox.Insert(inHiddenBox);
        b.Insert(Input(1)); b.Insert(sibling); b.Insert(nested); b.Insert(hiddenBox);
        _dialog.Insert(a); _dialog.Insert(b);
        Open();
        sibling.Hide(); inNested.Hide(); hiddenBox.Hide();

        ok.Focus();
        foreach (var ev in new[] { Key(Keys.kbAltS), Typed('s'), Key(Keys.kbAltN), Typed('n'), Key(Keys.kbAltG), Typed('g') })
            Assert.Equal(Events.evKeyDown, Send(ev).What);

        // Focus inside the nested group: its hidden button is now in the focused group.
        nestedOk.Focus();
        AssertFocusChain(nestedOk);
        Assert.Equal(Events.evKeyDown, Send(Key(Keys.kbAltN)).What);
        Assert.Equal(Events.evKeyDown, Send(Typed('n')).What);
        Assert.Equal(Events.evKeyDown, Send(Key(Keys.kbAltG)).What);

        Assert.Equal((0, 0, 0), (sibling.Presses, inNested.Presses, inHiddenBox.Presses));
        AssertFocusChain(nestedOk);
    }

    // ── E: labels ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("label hidden", false)]
    [InlineData("label disabled", false)]
    [InlineData("link hidden", false)]
    [InlineData("link disabled", false)]
    [InlineData("label hidden", true)]
    [InlineData("label disabled", true)]
    [InlineData("link hidden", true)]
    [InlineData("link disabled", true)]
    public void AHiddenOrDisabledLabelOrLinkDoesNotMoveFocus(string variant, bool otherGroup)
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 8), "A");
        var port = Input(1);
        a.Insert(port);
        var b = new TGroupBox(new TRect(1, 9, 45, 16), "B");
        var data = Input(3);
        var label = new TLabel(new TRect(2, 3, 11, 4), "~D~ata", data);
        TGroupBox home = otherGroup ? b : a;
        if (otherGroup) b.Insert(Input(1));
        home.Insert(data); home.Insert(label);
        _dialog.Insert(a); _dialog.Insert(b);
        Open();
        port.Focus();

        switch (variant)
        {
            case "label hidden": label.Hide(); break;
            case "label disabled": label.state |= Views.sfDisabled; break; // the label alone; SetState would disable the link too
            case "link hidden": data.Hide(); break;
            case "link disabled": data.SetState(Views.sfDisabled, true); break;
        }

        TEvent left = Send(Key(Keys.kbAltD));

        AssertFocusChain(port);
        // A label that is not shown or not enabled leaves the key for someone else; a shown label keeps its mnemonic.
        Assert.Equal(variant.StartsWith("label") ? Events.evKeyDown : Events.evNothing, left.What);
    }

    // ── F: clusters ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("hidden", false)]
    [InlineData("disabled", false)]
    [InlineData("in hidden group", false)]
    [InlineData("hidden", true)]
    [InlineData("disabled", true)]
    [InlineData("flat hidden", false)]
    public void AHiddenOrDisabledClusterIgnoresItsItemMnemonics(string variant, bool focusedGroup)
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 8), "A");
        var ok = Button(1, "~O~k");
        a.Insert(ok);
        var b = new TGroupBox(new TRect(1, 9, 45, 21), "B");
        var inner = new TGroupBox(new TRect(2, 4, 40, 10), "Inner");
        var echo = new TCheckBoxes(new TRect(2, 1, 20, 2), new TSItem("~E~cho", null));
        if (variant == "flat hidden") _dialog.Insert(echo);
        else if (variant == "in hidden group") { inner.Insert(echo); b.Insert(inner); }
        else (focusedGroup ? a : b).Insert(echo);
        b.Insert(Input(1));
        _dialog.Insert(a); _dialog.Insert(b);
        Open();

        if (variant == "disabled") echo.SetState(Views.sfDisabled, true);
        else if (variant == "in hidden group") inner.Hide();
        else echo.Hide();
        // After hiding: a plain group resets its current view when a selectable child is hidden.
        ok.Focus();

        Assert.Equal(Events.evKeyDown, Send(Key(Keys.kbAltE)).What);
        Assert.Equal(Events.evKeyDown, Send(Typed('e')).What);
        Assert.False(echo.Mark(0));
        AssertFocusChain(ok);
    }

    // ── G–N: validation when focus leaves a nested group ────────────────────

    public enum Route { Tab, ShiftTab, Mouse, LabelMnemonic, ClusterMnemonic, Focus, FocusGroup }

    /// <summary>
    /// Window ▸ A ▸ [Nested ▸] validating field, and Window ▸ B ▸ destinations. Tab leaves A forwards to B, Shift+Tab
    /// leaves it backwards and wraps to B, since B is A's only sibling.
    /// </summary>
    private sealed record Layout(TGroupBox A, TGroupBox B, ValidatingInput Field, ClickProbe Target,
        TInputLine LabelLink, TCheckBoxes Cluster);

    private Layout Build(bool deep)
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 10), "A");
        var field = Validating(1);
        if (deep)
        {
            var nested = new TGroupBox(new TRect(1, 1, 40, 6), "Nested");
            nested.Insert(field);
            a.Insert(nested);
        }
        else a.Insert(field);
        var b = new TGroupBox(new TRect(1, 11, 74, 21), "B");
        var target = new ClickProbe(new TRect(2, 1, 20, 2));
        var linked = Input(3);
        var cluster = new TCheckBoxes(new TRect(2, 5, 20, 6), new TSItem("~E~cho", null));
        b.Insert(target); b.Insert(linked); b.Insert(cluster);
        b.Insert(new TLabel(new TRect(2, 3, 11, 4), "~D~ata", linked));
        _dialog.Insert(a); _dialog.Insert(b);
        Open();
        field.Focus();
        return new Layout(a, b, field, target, linked, cluster);
    }

    private TView Attempt(Layout l, Route route)
    {
        switch (route)
        {
            case Route.Tab: Send(Key(Keys.kbTab)); return l.Target;
            case Route.ShiftTab: Send(Key(Keys.kbShiftTab)); return l.Cluster;
            case Route.Mouse: Click(l.Target); return l.Target;
            case Route.LabelMnemonic: Send(Key(Keys.kbAltD)); return l.LabelLink;
            case Route.ClusterMnemonic: Send(Key(Keys.kbAltE)); return l.Cluster;
            case Route.Focus: Assert.Equal(l.Field.Accept, l.LabelLink.Focus()); return l.LabelLink;
            case Route.FocusGroup: Assert.Equal(l.Field.Accept, l.B.Focus()); return l.Target;
            default: throw new ArgumentOutOfRangeException(nameof(route));
        }
    }

    /// <summary>H, I, J, K, L, M, N. Refusal keeps the field, its groups and the destination exactly as they were.</summary>
    [Theory]
    [InlineData(Route.Tab, false)] [InlineData(Route.ShiftTab, false)] [InlineData(Route.Mouse, false)]
    [InlineData(Route.LabelMnemonic, false)] [InlineData(Route.ClusterMnemonic, false)]
    [InlineData(Route.Focus, false)] [InlineData(Route.FocusGroup, false)]
    [InlineData(Route.Tab, true)] [InlineData(Route.ShiftTab, true)] [InlineData(Route.Mouse, true)]
    [InlineData(Route.LabelMnemonic, true)] [InlineData(Route.ClusterMnemonic, true)]
    [InlineData(Route.Focus, true)] [InlineData(Route.FocusGroup, true)]
    public void AnInvalidNestedFieldRefusesToLeaveForAnotherGroup(Route route, bool deep)
    {
        var l = Build(deep);
        TView? bCurrent = l.B.current;
        int targetFocusings = l.Target.Focusings;
        l.Field.Accept = false;

        Attempt(l, route);

        Assert.Equal(1, l.Field.ReleaseChecks);
        AssertFocusChain(l.Field);
        Assert.Same(l.A, _dialog.current);
        Assert.False(l.B.GetState(Views.sfSelected));
        Assert.False(l.B.GetState(Views.sfFocused));
        Assert.Same(bCurrent, l.B.current);
        Assert.False(l.Cluster.Mark(0));
        Assert.Equal((0, targetFocusings), (l.Target.Clicks, l.Target.Focusings));

        // Once the value is valid the same route moves focus, checking the field once more.
        l.Field.Accept = true;
        TView destination = Attempt(l, route);
        Assert.Equal(2, l.Field.ReleaseChecks);
        AssertFocusChain(destination);
        Assert.Equal(route == Route.ClusterMnemonic, l.Cluster.Mark(0));
        Assert.Equal(route == Route.Mouse ? 1 : 0, l.Target.Clicks);
    }

    /// <summary>G, J. A valid field lets every route through and is checked exactly once per transition.</summary>
    [Theory]
    [InlineData(Route.Tab)] [InlineData(Route.ShiftTab)] [InlineData(Route.Mouse)]
    [InlineData(Route.LabelMnemonic)] [InlineData(Route.ClusterMnemonic)] [InlineData(Route.Focus)]
    public void AValidNestedFieldLetsFocusMoveAndIsCheckedOnce(Route route)
    {
        var l = Build(deep: true);

        TView destination = Attempt(l, route);

        AssertFocusChain(destination);
        Assert.Equal(1, l.Field.ReleaseChecks);
        Assert.False(l.A.GetState(Views.sfFocused));
    }

    /// <summary>J. Moving focus inside the field's own group, and inside the destination group, checks only what is left.</summary>
    [Fact]
    public void FocusMovesWithinAGroupCheckOnlyTheViewBeingLeft()
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 10), "A");
        var field = Validating(1); var next = Input(3);
        a.Insert(field); a.Insert(next);
        var b = new TGroupBox(new TRect(1, 11, 45, 21), "B");
        var first = Input(1); var second = Input(3);
        b.Insert(first); b.Insert(second);
        _dialog.Insert(a); _dialog.Insert(b);
        Open();
        field.Focus();
        int before = field.ReleaseChecks;

        field.Accept = false;
        Send(Key(Keys.kbTab));
        AssertFocusChain(field);
        Assert.Equal(before + 1, field.ReleaseChecks);

        field.Accept = true;
        Send(Key(Keys.kbTab));
        AssertFocusChain(next);
        Send(Key(Keys.kbTab));
        Send(Key(Keys.kbTab));
        AssertFocusChain(second);
        Assert.Equal(before + 2, field.ReleaseChecks);
    }

    /// <summary>N. <see cref="TView.Select"/> stays the unchecked primitive: it moves focus only within an already focused owner.</summary>
    [Fact]
    public void SelectOnAViewInAnUnfocusedGroupDoesNotMoveFocus()
    {
        var l = Build(deep: false);
        l.Field.Accept = false;

        l.LabelLink.Select();

        AssertFocusChain(l.Field);
        Assert.Same(l.LabelLink, l.B.current);
        Assert.Equal(0, l.Field.ReleaseChecks);

        // Focus() then refuses, and once valid enters B at the view Select made current.
        Assert.False(l.B.Focus());
        l.Field.Accept = true;
        Assert.True(l.B.Focus());
        AssertFocusChain(l.LabelLink);
    }

    /// <summary>A refused Tab leaves no Tab entry behind in the destination: a later Focus() re-enters at its current child.</summary>
    [Fact]
    public void ARefusedTabLeavesNoStaleEntryInTheDestination()
    {
        var l = Build(deep: false);
        l.LabelLink.Select();
        l.Field.Accept = false;

        Send(Key(Keys.kbTab));
        l.Field.Accept = true;
        Assert.True(l.B.Focus());

        AssertFocusChain(l.LabelLink);
    }

    /// <summary>The flat-dialog contract the group boxes follow: every route consults the field being left.</summary>
    [Theory]
    [InlineData(Route.Tab)] [InlineData(Route.ShiftTab)] [InlineData(Route.Mouse)]
    [InlineData(Route.LabelMnemonic)] [InlineData(Route.ClusterMnemonic)] [InlineData(Route.Focus)]
    public void AnInvalidFieldInAFlatDialogRefusesEveryRoute(Route route)
    {
        var field = Validating(1);
        var target = new ClickProbe(new TRect(2, 3, 10, 4));
        var linked = Input(5);
        var cluster = new TCheckBoxes(new TRect(2, 7, 20, 8), new TSItem("~E~cho", null));
        _dialog.Insert(field); _dialog.Insert(target); _dialog.Insert(linked); _dialog.Insert(cluster);
        _dialog.Insert(new TLabel(new TRect(2, 5, 11, 6), "~D~ata", linked));
        Open();
        field.Focus();
        field.Accept = false;

        switch (route)
        {
            case Route.Tab: Send(Key(Keys.kbTab)); break;
            case Route.ShiftTab: Send(Key(Keys.kbShiftTab)); break;
            case Route.Mouse: Click(target); break;
            case Route.LabelMnemonic: Send(Key(Keys.kbAltD)); break;
            case Route.ClusterMnemonic: Send(Key(Keys.kbAltE)); break;
            case Route.Focus: Assert.False(linked.Focus()); break;
        }

        Assert.Equal(1, field.ReleaseChecks);
        Assert.Same(field, Assert.Single(FocusedLeaves()));
        Assert.False(cluster.Mark(0));
        Assert.Equal(0, target.Clicks);
    }
}
