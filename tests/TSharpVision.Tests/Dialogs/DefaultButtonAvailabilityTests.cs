// TV-1.1-H2. Enter in a dialog broadcasts cmDefault, and only a default button that is shown and enabled, with every
// owner shown and enabled, answers it. These tests feed keys to the driver and run the dialog modally, so Enter takes
// the real route: TDialog posts the broadcast, the modal loop reads it back and TGroup delivers it to every child.
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Dialogs;

[Collection("NonParallel")]
public sealed class DefaultButtonAvailabilityTests : IDisposable
{
    private const ushort TimedOut = 9999;
    private const ushort cmProbe = 1000;

    private readonly DriverScope _driver = new();
    private readonly TestApp _app;
    private readonly ScriptedDialog _dialog = new();

    public DefaultButtonAvailabilityTests()
    {
        TEventQueue.Resume();
        TEventQueue.ClearPosted();
        _app = new TestApp();
        // TProgram.Pending is process-wide and outlives the program that posted it: a test elsewhere that sends Esc
        // straight to a dialog leaves its cmCancel post behind, and this modal loop would read it first.
        TestApp.DropPendingEvent();
    }

    public void Dispose()
    {
        _dialog.ShutDown();
        _app.ShutDown();
        TEventQueue.ClearPosted();
        _driver.Dispose();
    }

    private sealed class TestApp : TProgram
    {
        public static void DropPendingEvent() => Pending = default;
    }

    /// <summary>
    /// A modal dialog that runs the action bound to a function key when that key arrives, so a test can change the
    /// dialog while it stays open. It gives up instead of spinning when the scripted keys do not close it.
    /// </summary>
    private sealed class ScriptedDialog : TDialog
    {
        private int _idle;
        public readonly Dictionary<ushort, Action> Actions = new();

        public ScriptedDialog() : base(new TRect(2, 2, 78, 22), "Default") { }

        public override void GetEvent(ref TEvent ev)
        {
            base.GetEvent(ref ev);
            if (ev.What != Events.evNothing) _idle = 0;
            else if (++_idle > 200) EndModal(TimedOut);
        }

        public override void HandleEvent(ref TEvent ev)
        {
            if (ev.What == Events.evKeyDown && Actions.TryGetValue(ev.keyDown.keyCode, out Action? action))
            {
                action();
                ClearEvent(ref ev);
                return;
            }
            base.HandleEvent(ref ev);
        }
    }

    /// <summary>A button that counts presses and posts nothing, so pressing it never ends the dialog.</summary>
    private sealed class ProbeButton(TRect bounds, string title, ushort flags) : TButton(bounds, title, cmProbe, flags)
    {
        public int Presses;
        public override void Press() => Presses++;
    }

    private static TEvent Key(ushort keyCode)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        ev.keyDown.charScan = new CharScanType(keyCode);
        return ev;
    }

    private static TButton Ok(int x, int y) => new(new TRect(x, y, x + 10, y + 2), "O~K~", Views.cmOK, ButtonConstants.bfDefault);
    private static ProbeButton Default(int x, int y) => new(new TRect(x, y, x + 12, y + 2), "~D~efault", ButtonConstants.bfDefault);
    private static ProbeButton Normal(int x, int y) => new(new TRect(x, y, x + 12, y + 2), "~N~ormal", ButtonConstants.bfNormal);
    private static TInputLine Line(int x, int y) => new(new TRect(x, y, x + 16, y + 1), 30);

    /// <summary>Feeds <paramref name="keys"/> followed by Esc, runs the dialog modally and returns its result.</summary>
    private ushort Run(params ushort[] keys)
    {
        foreach (ushort key in keys) _driver.Driver.EnqueueKey(Key(key));
        _driver.Driver.EnqueueKey(Key(Keys.kbEsc));
        ushort result = _app.DeskTop!.ExecView(_dialog);
        while (_driver.Driver.ReadKeyEvent(out _))
        {
        }
        return result;
    }

    // ── A, H: flat dialog ───────────────────────────────────────────────────

    /// <summary>A. The ordinary contract: Enter in an input line presses the visible, enabled default button.</summary>
    [Fact]
    public void AVisibleEnabledDefaultButtonAnswersEnter()
    {
        var line = Line(2, 2);
        _dialog.Insert(Ok(2, 6));
        _dialog.Insert(line);
        line.Select();

        Assert.Equal(Views.cmOK, Run(Keys.kbEnter));
    }

    /// <summary>B, C, H. A hidden or disabled default button in a flat dialog ignores Enter; Esc still cancels.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AHiddenOrDisabledDefaultButtonInAFlatDialogIgnoresEnter(bool hidden)
    {
        var line = Line(2, 2);
        var ok = Ok(2, 6);
        _dialog.Insert(ok);
        _dialog.Insert(line);
        if (hidden) ok.Hide(); else ok.SetState(Views.sfDisabled, true);
        line.Select();

        Assert.Equal(Views.cmCancel, Run(Keys.kbEnter));
    }

    /// <summary>
    /// I. With the default button unavailable, Enter presses no other button in its place: neither a normal button
    /// nor another default button that is itself unavailable.
    /// </summary>
    [Fact]
    public void AnUnavailableDefaultButtonHasNoReplacement()
    {
        var line = Line(2, 2);
        var hiddenDefault = Default(2, 6);
        var normal = Normal(16, 6);
        _dialog.Insert(hiddenDefault);
        _dialog.Insert(normal);
        _dialog.Insert(line);
        hiddenDefault.Hide();
        line.Select();

        Assert.Equal(Views.cmCancel, Run(Keys.kbEnter));
        Assert.Equal(0, hiddenDefault.Presses);
        Assert.Equal(0, normal.Presses);
    }

    /// <summary>I. A focused normal button takes Enter itself; the default button is not pressed as well.</summary>
    [Fact]
    public void EnterOnAFocusedNormalButtonPressesOnlyThatButton()
    {
        var def = Default(2, 6);
        var normal = Normal(16, 6);
        _dialog.Insert(def);
        _dialog.Insert(normal);
        normal.Select();

        Assert.Equal(Views.cmCancel, Run(Keys.kbEnter));
        Assert.Equal(1, normal.Presses);
        Assert.Equal(0, def.Presses);
    }

    /// <summary>Explicit activation is unchanged: the Alt mnemonic of a visible default button presses it once.</summary>
    [Fact]
    public void TheMnemonicOfAVisibleDefaultButtonStillPressesIt()
    {
        var line = Line(2, 2);
        var def = Default(2, 6);
        _dialog.Insert(def);
        _dialog.Insert(line);
        line.Select();

        Assert.Equal(Views.cmCancel, Run(Keys.kbAltD));
        Assert.Equal(1, def.Presses);
    }

    // ── D, E, F, G: group boxes ─────────────────────────────────────────────

    /// <summary>D, E. A default button inside a hidden or disabled group box ignores Enter, although its own state is clear.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADefaultButtonInAHiddenOrDisabledGroupBoxIgnoresEnter(bool hidden)
    {
        var line = Line(2, 2);
        var box = new TGroupBox(new TRect(2, 5, 40, 10), "Actions");
        var def = Default(2, 1);
        box.Insert(def);
        _dialog.Insert(box);
        _dialog.Insert(line);
        if (hidden) box.Hide(); else box.SetState(Views.sfDisabled, true);
        line.Select();

        Assert.True(def.GetState(Views.sfVisible));
        Assert.False(def.GetState(Views.sfDisabled));
        Assert.Equal(Views.cmCancel, Run(Keys.kbEnter));
        Assert.Equal(0, def.Presses);
    }

    /// <summary>D. Nested: a default button two group boxes down, below a hidden outer box.</summary>
    [Fact]
    public void ADefaultButtonBelowAHiddenOuterGroupBoxIgnoresEnter()
    {
        var line = Line(2, 2);
        var outer = new TGroupBox(new TRect(2, 5, 50, 14), "Outer");
        var inner = new TGroupBox(new TRect(1, 1, 40, 7), "Inner");
        var def = Default(2, 1);
        inner.Insert(def);
        outer.Insert(inner);
        _dialog.Insert(outer);
        _dialog.Insert(line);
        outer.Hide();
        line.Select();

        Assert.Equal(Views.cmCancel, Run(Keys.kbEnter));
        Assert.Equal(0, def.Presses);
    }

    /// <summary>
    /// F, G. The default button is dialog-wide: Enter in a field presses it whether the field is in the button's own,
    /// focused group or in a sibling group, and no other button is pressed.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AVisibleDefaultButtonAnswersEnterFromItsOwnOrASiblingGroup(bool sameGroup)
    {
        var a = new TGroupBox(new TRect(1, 1, 37, 10), "A");
        var b = new TGroupBox(new TRect(38, 1, 74, 10), "B");
        var line = Line(2, 2);
        var def = Default(2, 5);
        var normalA = Normal(16, 5);
        a.Insert(line);
        a.Insert(normalA);
        if (sameGroup) a.Insert(def); else b.Insert(def);
        b.Insert(Line(2, 2));
        _dialog.Insert(a);
        _dialog.Insert(b);
        line.Select();
        a.Select();

        Assert.Equal(Views.cmCancel, Run(Keys.kbEnter));
        Assert.Same(a, _dialog.current);
        Assert.Equal(1, def.Presses);
        Assert.Equal(0, normalA.Presses);
    }

    // ── J, K, L: changes while the dialog is open ───────────────────────────

    /// <summary>
    /// J, K, L. The dialog stays open while the default button is hidden, shown, disabled and enabled again. It answers
    /// Enter exactly when it is available.
    /// </summary>
    [Fact]
    public void ADefaultButtonChangedWhileTheDialogIsOpenAnswersEnterOnlyWhenAvailable()
    {
        var line = Line(2, 2);
        var def = Default(2, 6);
        _dialog.Insert(def);
        _dialog.Insert(line);
        line.Select();
        var seen = new List<int>();
        _dialog.Actions[Keys.kbF2] = () => { seen.Add(def.Presses); def.Hide(); };
        _dialog.Actions[Keys.kbF3] = () => { seen.Add(def.Presses); def.Show(); line.Focus(); };
        _dialog.Actions[Keys.kbF4] = () => { seen.Add(def.Presses); def.SetState(Views.sfDisabled, true); };
        // Not F5: the default status line turns it into cmZoom.
        _dialog.Actions[Keys.kbF7] = () => { seen.Add(def.Presses); def.SetState(Views.sfDisabled, false); };

        ushort result = Run(Keys.kbEnter, Keys.kbF2, Keys.kbEnter, Keys.kbF3, Keys.kbEnter,
                            Keys.kbF4, Keys.kbEnter, Keys.kbF7, Keys.kbEnter);

        Assert.Equal(Views.cmCancel, result);
        // Visible: pressed. Hidden: not. Shown: pressed. Disabled: not. Enabled: pressed.
        Assert.Equal(new[] { 1, 1, 2, 2 }, seen);
        Assert.Equal(3, def.Presses);
    }

    /// <summary>J, L. The same within a group box that is hidden and shown while the dialog is open.</summary>
    [Fact]
    public void ADefaultButtonInAGroupBoxHiddenWhileTheDialogIsOpenAnswersEnterOnlyWhenShown()
    {
        var line = Line(2, 2);
        var box = new TGroupBox(new TRect(2, 5, 40, 10), "Actions");
        var def = Default(2, 1);
        box.Insert(def);
        _dialog.Insert(box);
        _dialog.Insert(line);
        line.Select();
        var seen = new List<int>();
        _dialog.Actions[Keys.kbF2] = () => { seen.Add(def.Presses); box.Hide(); };
        _dialog.Actions[Keys.kbF3] = () => { seen.Add(def.Presses); box.Show(); line.Focus(); };

        Assert.Equal(Views.cmCancel, Run(Keys.kbEnter, Keys.kbF2, Keys.kbEnter, Keys.kbF3, Keys.kbEnter));
        Assert.Equal(new[] { 1, 1 }, seen);
        Assert.Equal(2, def.Presses);
    }
}
