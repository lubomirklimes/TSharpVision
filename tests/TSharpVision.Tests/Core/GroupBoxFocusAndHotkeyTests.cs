using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

/// <summary>
/// TV-1.1-FIX-2. Mnemonics across sibling and nested group boxes, and the focus chain a selection through a group box
/// leaves: the focused leaf and every group above it agree. Found in a Commander window with three group boxes, where
/// an Alt mnemonic in an unfocused group did nothing and a label activated its field without focusing the group.
/// </summary>
[Collection("NonParallel")]
public sealed class GroupBoxFocusAndHotkeyTests : IDisposable
{
    private readonly DriverScope _scope = new();
    private readonly TApplication _app = new();
    private readonly TDialog _dialog = new(new TRect(0, 0, 76, 22), "Serial");

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

    /// <summary>An input line that counts how often it gains focus, to show no child is focused on the way.</summary>
    private sealed class ProbeInput(TRect bounds) : TInputLine(bounds, 30)
    {
        public int Focusings;
        public override void SetState(ushort aState, bool enable)
        {
            if ((aState & Views.sfFocused) != 0 && enable && (state & Views.sfFocused) == 0) Focusings++;
            base.SetState(aState, enable);
        }
    }

    private static ProbeInput Input(int y) => new(new TRect(12, y, 28, y + 1));
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

    /// <summary>Sends the event to the dialog as the event loop would and returns what is left of it.</summary>
    private TEvent Send(TEvent ev)
    {
        _dialog.HandleEvent(ref ev);
        return ev;
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

    /// <summary>The views with focus that have no focused child: there is exactly one focused branch.</summary>
    private IReadOnlyList<TView> FocusedLeaves() =>
        Descendants(_dialog).Where(v => v.GetState(Views.sfFocused)
            && (v is not TGroup g || g.current == null || !g.current.GetState(Views.sfFocused))).ToList();

    // ── hotkeys ─────────────────────────────────────────────────────────────

    [Fact]
    public void AnAltMnemonicReachesAButtonInASiblingAndInANestedGroup()
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 8), "Connection");
        var port = Input(1); var open = Button(3, "~O~pen");
        a.Insert(port); a.Insert(open);
        var b = new TGroupBox(new TRect(1, 9, 74, 21), "Send");
        var data = Input(1); var send = Button(1, "~S~end");
        var c = new TGroupBox(new TRect(44, 3, 70, 10), "Nested");
        var transmit = new ProbeButton(new TRect(2, 2, 14, 4), "~X~mit");
        c.Insert(transmit);
        b.Insert(data); b.Insert(send); b.Insert(c);
        _dialog.Insert(a); _dialog.Insert(b);
        Open();
        port.Focus();

        Assert.Equal(Events.evNothing, Send(Key(Keys.kbAltS)).What);
        Assert.Equal(Events.evNothing, Send(Key(Keys.kbAltX)).What);
        Assert.Equal(Events.evNothing, Send(Key(Keys.kbAltO)).What);

        Assert.Equal((1, 1, 1), (send.Presses, transmit.Presses, open.Presses));
        // Pressing a button does not move focus, as in a flat dialog.
        Assert.Same(port, Assert.Single(FocusedLeaves()));
    }

    [Fact]
    public void TheFocusedGroupWinsADuplicateMnemonic()
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 8), "A");
        var inA = Input(1); var dupA = Button(3, "~D~up");
        a.Insert(inA); a.Insert(dupA);
        var b = new TGroupBox(new TRect(1, 9, 45, 16), "B");
        var inB = Input(1); var dupB = Button(3, "~D~up");
        b.Insert(inB); b.Insert(dupB);
        _dialog.Insert(a); _dialog.Insert(b);
        Open();

        inB.Focus();
        Send(Key(Keys.kbAltD));
        inA.Focus();
        Send(Key(Keys.kbAltD));

        Assert.Equal((1, 1), (dupA.Presses, dupB.Presses));
    }

    [Fact]
    public void DisabledHiddenAndHiddenGroupControlsAreNotActivated()
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 8), "A");
        var port = Input(1);
        a.Insert(port);
        var b = new TGroupBox(new TRect(1, 9, 45, 16), "B");
        var disabled = Button(1, "~S~end"); var hidden = Button(3, "~H~old");
        b.Insert(Input(1)); b.Insert(disabled); b.Insert(hidden);
        var c = new TGroupBox(new TRect(46, 1, 74, 8), "C");
        var inHiddenGroup = Button(1, "~G~o");
        c.Insert(Input(3)); c.Insert(inHiddenGroup);
        _dialog.Insert(a); _dialog.Insert(b); _dialog.Insert(c);
        Open();
        disabled.SetState(Views.sfDisabled, true);
        hidden.Hide();
        c.Hide();
        port.Focus();

        Assert.Equal(Events.evKeyDown, Send(Key(Keys.kbAltS)).What);
        Assert.Equal(Events.evKeyDown, Send(Key(Keys.kbAltH)).What);
        Assert.Equal(Events.evKeyDown, Send(Key(Keys.kbAltG)).What);
        Assert.Equal((0, 0, 0), (disabled.Presses, hidden.Presses, inHiddenGroup.Presses));
    }

    /// <summary>Text typed into a focused editor is the editor's; it is never a sibling group's plain-letter mnemonic.</summary>
    [Fact]
    public void PlainTextTypedIntoAnEditorIsNotAMnemonicOfAnotherGroup()
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 8), "A");
        var port = Input(1);
        a.Insert(port);
        var b = new TGroupBox(new TRect(1, 9, 45, 16), "B");
        var send = Button(1, "~S~end");
        var echo = new TCheckBoxes(new TRect(2, 4, 20, 5), new TSItem("~E~cho", null));
        b.Insert(Input(1)); b.Insert(send); b.Insert(echo);
        _dialog.Insert(a); _dialog.Insert(b);
        Open();
        port.Focus();

        foreach (char ch in "sees") Assert.Equal(Events.evNothing, Send(Typed(ch)).What);

        Assert.Equal("sees", port.Data);
        Assert.Equal(0, send.Presses);
        Assert.False(echo.Mark(0));
        Assert.Same(port, Assert.Single(FocusedLeaves()));
    }

    [Fact]
    public void TabEnterAndEscapeAreNotOfferedAsMnemonics()
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 8), "A");
        var port = Input(1); var next = Input(3);
        a.Insert(port); a.Insert(next);
        var b = new TGroupBox(new TRect(1, 9, 45, 16), "B");
        var send = Button(1, "~S~end");
        b.Insert(send);
        _dialog.Insert(a); _dialog.Insert(b);
        Open();
        port.Focus();

        Send(Key(Keys.kbTab));
        Assert.Same(next, Assert.Single(FocusedLeaves()));
        Send(Key(Keys.kbEnter));
        Send(Key(Keys.kbEsc));
        Assert.Equal(0, send.Presses);
    }

    // ── focus chain ─────────────────────────────────────────────────────────

    [Fact]
    public void ALabelMnemonicFocusesItsFieldInASiblingGroupAndTheGroupWithIt()
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 8), "A");
        var port = Input(1);
        a.Insert(port);
        var b = new TGroupBox(new TRect(1, 9, 45, 16), "B");
        var first = Input(1); var data = Input(3);
        b.Insert(first); b.Insert(data);
        b.Insert(new TLabel(new TRect(2, 3, 11, 4), "~D~ata", data));
        _dialog.Insert(a); _dialog.Insert(b);
        Open();
        port.Focus();
        int firstFocusings = first.Focusings;

        Assert.Equal(Events.evNothing, Send(Key(Keys.kbAltD)).What);

        Assert.Same(b, _dialog.current);
        Assert.Same(data, b.current);
        Assert.Same(data, Assert.Single(FocusedLeaves()));
        Assert.False(a.GetState(Views.sfFocused));
        Assert.Equal(1, data.Focusings);
        // The group entered its target directly; its first child was never focused on the way.
        Assert.Equal(firstFocusings, first.Focusings);

        // Tab and Shift+Tab continue from the field the label focused.
        Send(Key(Keys.kbShiftTab));
        Assert.Same(first, Assert.Single(FocusedLeaves()));
        Send(Key(Keys.kbShiftTab));
        Assert.Same(port, Assert.Single(FocusedLeaves()));
        Send(Key(Keys.kbTab));
        Assert.Same(first, Assert.Single(FocusedLeaves()));
    }

    [Fact]
    public void ALabelOutsideTheGroupsFocusesAFieldInANestedGroup()
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 8), "A");
        var port = Input(1);
        a.Insert(port);
        var b = new TGroupBox(new TRect(1, 9, 50, 21), "B");
        var c = new TGroupBox(new TRect(2, 2, 40, 10), "C");
        var other = Input(1); var deep = Input(3);
        c.Insert(other); c.Insert(deep);
        b.Insert(Input(1)); b.Insert(c);
        _dialog.Insert(a); _dialog.Insert(b);
        _dialog.Insert(new TLabel(new TRect(52, 2, 64, 3), "~R~ate", deep));
        Open();
        port.Focus();

        Send(Key(Keys.kbAltR));

        Assert.Same(b, _dialog.current);
        Assert.Same(c, b.current);
        Assert.Same(deep, c.current);
        Assert.Same(deep, Assert.Single(FocusedLeaves()));
        Assert.Equal(0, other.Focusings);
    }

    [Fact]
    public void AClusterMnemonicInAnotherGroupTogglesAndFocusesTheCluster()
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 8), "A");
        var port = Input(1);
        a.Insert(port);
        var b = new TGroupBox(new TRect(1, 9, 45, 16), "B");
        var echo = new TCheckBoxes(new TRect(2, 4, 20, 5), new TSItem("~E~cho", null));
        b.Insert(Input(1)); b.Insert(echo);
        _dialog.Insert(a); _dialog.Insert(b);
        Open();
        port.Focus();

        Assert.Equal(Events.evNothing, Send(Key(Keys.kbAltE)).What);

        Assert.True(echo.Mark(0));
        Assert.Same(echo, Assert.Single(FocusedLeaves()));
        Assert.Same(b, _dialog.current);
    }

    [Fact]
    public void ReenteringAGroupRestoresItsCurrentChildWhileTabEntersAtTheEnds()
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 8), "A");
        var port = Input(1);
        a.Insert(port);
        var b = new TGroupBox(new TRect(1, 9, 45, 16), "B");
        var first = Input(1); var middle = Input(3); var last = Input(5);
        b.Insert(first); b.Insert(middle); b.Insert(last);
        _dialog.Insert(a); _dialog.Insert(b);
        Open();

        middle.Focus();
        port.Focus();
        b.Select();
        Assert.Same(middle, Assert.Single(FocusedLeaves()));

        port.Focus();
        Send(Key(Keys.kbTab));
        Assert.Same(first, Assert.Single(FocusedLeaves()));
        port.Focus();
        Send(Key(Keys.kbShiftTab));
        Assert.Same(last, Assert.Single(FocusedLeaves()));
    }

    [Fact]
    public void AClickInAnUnfocusedGroupFocusesOnlyTheChildUnderTheMouse()
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 8), "A");
        var port = Input(1);
        a.Insert(port);
        var b = new TGroupBox(new TRect(1, 9, 45, 16), "B");
        var first = new ClickProbe(new TRect(12, 1, 28, 2)); var second = new ClickProbe(new TRect(12, 3, 28, 4));
        b.Insert(first); b.Insert(second);
        _dialog.Insert(a); _dialog.Insert(b);
        Open();
        port.Focus();
        int firstFocusings = first.Focusings;

        var click = new TEvent { What = Events.evMouseDown };
        click.mouse.where = second.MakeGlobal(new TPoint(1, 0));
        click.mouse.buttons = (byte)Events.mbLeftButton;
        _dialog.HandleEvent(ref click);

        Assert.Same(second, Assert.Single(FocusedLeaves()));
        Assert.Equal(1, second.Clicks);
        Assert.Equal(firstFocusings, first.Focusings);
    }

    /// <summary>A selectable view that takes a click without tracking the mouse, and counts how often it gains focus.</summary>
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

    // ── show, hide and the current child ────────────────────────────────────

    [Fact]
    public void InsertingControlsLeavesTheFirstInsertedCurrent()
    {
        var box = new TGroupBox(new TRect(1, 1, 45, 12), "B");
        var first = Input(1);
        box.Insert(first); box.Insert(Input(3)); box.Insert(Button(5, "~O~K"));
        Assert.Same(first, box.current);
    }

    [Fact]
    public void ShowingOrHidingAnotherControlKeepsTheCurrentChildAndHidingItChoosesAnEligibleOne()
    {
        var a = new TGroupBox(new TRect(1, 1, 45, 8), "A");
        var port = Input(1);
        a.Insert(port);
        var b = new TGroupBox(new TRect(1, 9, 45, 21), "B");
        var first = Input(1); var data = Input(3); var optional = Button(5, "~X~");
        b.Insert(first); b.Insert(data); b.Insert(optional);
        _dialog.Insert(a); _dialog.Insert(b);
        Open();
        data.Focus();

        optional.Hide();
        Assert.Same(data, Assert.Single(FocusedLeaves()));
        optional.Show();
        Assert.Same(data, Assert.Single(FocusedLeaves()));
        b.ChangeBounds(new TRect(1, 9, 50, 21));
        Assert.Same(data, Assert.Single(FocusedLeaves()));

        // An unfocused group showing a control does not take focus, and keeps its current child.
        port.Focus();
        optional.Hide(); optional.Show();
        Assert.Same(port, Assert.Single(FocusedLeaves()));
        Assert.Same(data, b.current);

        // Hiding the current child moves to an eligible one; entering the group then restores that one.
        data.Hide();
        Assert.Same(first, b.current);
        b.Select();
        Assert.Same(first, Assert.Single(FocusedLeaves()));
        first.SetState(Views.sfDisabled, true);
        first.Hide();
        Assert.Same(optional, b.current);
        Assert.Same(optional, Assert.Single(FocusedLeaves()));
    }
}
