// Keypad Enter is a distinct driver identity (kbKeypadEnter) and an ordinary Enter to everything above the driver.
// The identity is replaced once, in TProgram.GetEvent, so these tests feed the driver and read through the program:
// handing a view the event directly would bypass the layer under test.
using System;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class KeypadEnterTests : IDisposable
{
    private const uint Shift = Keys.kbLeftShift;
    private const uint Ctrl = Keys.kbLeftCtrl;
    private const uint Alt = Keys.kbLeftAlt;
    private const ushort TimedOut = 9999;
    private const ushort cmPicked = 1234;

    private readonly DriverScope _driver = new();

    public KeypadEnterTests()
    {
        TEventQueue.Resume();
        TEventQueue.ClearPosted();
    }

    public void Dispose()
    {
        TEventQueue.ClearPosted();
        _driver.Dispose();
    }

    /// <summary>A key as the Console, SDL and Kitty translators build it: the code, its scan pair, the whole state.</summary>
    private static TEvent Key(ushort keyCode, uint modifiers = 0, ushort what = Events.evKeyDown)
    {
        var ev = new TEvent { What = what };
        ev.keyDown.keyCode = keyCode;
        ev.keyDown.charScan = new CharScanType(keyCode);
        ev.keyDown.controlKeyState = modifiers;
        return ev;
    }

    private static TEvent Typed(char c)
    {
        TEvent ev = Key(c);
        ev.keyDown.text = c.ToString();
        return ev;
    }

    /// <summary>The Enter key of the main block or of the keypad, as a driver reports it with <paramref name="modifiers"/>.</summary>
    private static TEvent Enter(bool keypad, uint modifiers = 0) =>
        keypad ? Key(Keys.kbKeypadEnter, modifiers)
               : Key((modifiers & Keys.kbCtrlShift) != 0 ? Keys.kbCtrlEnter : Keys.kbEnter, modifiers);

    private sealed class TestApp : TProgram
    {
        public bool Raw { get; set; }

        public override TMenuBar? InitMenuBar(TRect r)
        {
            r.b.y = r.a.y + 1;
            var file = new TSubMenu("~F~ile", Keys.kbAltF)
                + new TMenuItem("~P~ick", cmPicked, Keys.kbNoKey)
                + new TMenuItem("~O~ther", 1235, Keys.kbNoKey);
            return new TMenuBar(r, file);
        }

        protected override void NormalizeKeyEvent(ref TEvent ev)
        {
            if (!Raw) base.NormalizeKeyEvent(ref ev);
        }

        public TEvent Read()
        {
            TEvent ev = default;
            GetEvent(ref ev);
            return ev;
        }

        public void Pump()
        {
            for (int i = 0; i < 1000; i++)
            {
                TEvent ev = Read();
                if (ev.What == Events.evNothing) return;
                HandleEvent(ref ev);
            }

            Assert.Fail("The event queue did not settle.");
        }
    }

    /// <summary>A modal dialog that gives up instead of spinning when the scripted keys do not close it.</summary>
    private sealed class GuardedDialog : TDialog
    {
        private int _idle;

        public GuardedDialog() : base(new TRect(5, 3, 55, 15), "Keypad") { }

        public override void GetEvent(ref TEvent ev)
        {
            base.GetEvent(ref ev);
            if (ev.What != Events.evNothing) _idle = 0;
            else if (++_idle > 200) EndModal(TimedOut);
        }
    }

    // ── the normalization itself ────────────────────────────────────────────

    [Theory]
    [InlineData(0u, Keys.kbEnter)]
    [InlineData(Shift, Keys.kbEnter)]
    [InlineData(Alt, Keys.kbEnter)]
    [InlineData(Ctrl, Keys.kbCtrlEnter)]
    [InlineData(Ctrl | Shift, Keys.kbCtrlEnter)]
    [InlineData(Ctrl | Alt, Keys.kbCtrlEnter)]
    [InlineData(Keys.kbNumState, Keys.kbEnter)]
    public void KeypadEnterBecomesTheCodeTheMainEnterKeyHas(uint modifiers, ushort expected)
    {
        foreach (ushort what in new[] { Events.evKeyDown, Events.evKeyUp })
        {
            TEvent ev = Key(Keys.kbKeypadEnter, modifiers, what);

            Assert.True(KeypadKeys.Normalize(ref ev));

            Assert.Equal(what, ev.What);
            Assert.Equal(expected, ev.keyDown.keyCode);
            Assert.Equal(expected, ev.keyDown.charScan.ToUShort());
            Assert.Equal(Keys.kbKeypadEnter, ev.keyDown.keypadKey);
            Assert.Equal(modifiers, ev.keyDown.controlKeyState);
            Assert.Equal(string.Empty, ev.keyDown.text);
        }
    }

    [Fact]
    public void TheMainEnterKeyAndEveryOtherEventAreLeftAlone()
    {
        foreach (TEvent original in new[]
                 {
                     Key(Keys.kbEnter), Key(Keys.kbCtrlEnter, Ctrl), Key(Keys.kbLeft), Key(Keys.kbNumLock),
                     Key(Keys.kbKeypadEnter, 0, Events.evModifierChanged), new TEvent { What = Events.evNothing },
                 })
        {
            TEvent ev = original;
            Assert.False(KeypadKeys.Normalize(ref ev));
            Assert.Equal(original.What, ev.What);
            Assert.Equal(original.keyDown.keyCode, ev.keyDown.keyCode);
            Assert.Equal(0, ev.keyDown.keypadKey);
        }
    }

    // ── where it happens ────────────────────────────────────────────────────

    [Fact]
    public void TheProgramNormalizesWhatTheDriverReportsAndKeepsTheIdentity()
    {
        var app = new TestApp();
        _driver.Driver.EnqueueKey(Key(Keys.kbKeypadEnter));
        _driver.Driver.EnqueueKey(Key(Keys.kbKeypadEnter, Ctrl));
        _driver.Driver.EnqueueKey(Key(Keys.kbKeypadEnter, 0, Events.evKeyUp));
        _driver.Driver.EnqueueKey(Key(Keys.kbEnter));

        TEvent press = app.Read(), ctrl = app.Read(), release = app.Read(), main = app.Read();

        Assert.Equal(Keys.kbEnter, press.keyDown.keyCode);
        Assert.Equal(Keys.kbKeypadEnter, press.keyDown.keypadKey);
        Assert.Equal(Keys.kbCtrlEnter, ctrl.keyDown.keyCode);
        Assert.Equal(Keys.kbKeypadEnter, ctrl.keyDown.keypadKey);
        Assert.Equal(Events.evKeyUp, release.What);
        Assert.Equal(Keys.kbEnter, release.keyDown.keyCode);
        Assert.Equal(Keys.kbEnter, main.keyDown.keyCode);
        Assert.Equal(0, main.keyDown.keypadKey);
        app.ShutDown();
    }

    [Fact]
    public void AnEventPutBackIsNormalizedToo()
    {
        var app = new TestApp();
        TEvent put = Key(Keys.kbKeypadEnter);
        app.PutEvent(ref put);

        Assert.Equal(Keys.kbEnter, app.Read().keyDown.keyCode);
        app.ShutDown();
    }

    [Fact]
    public void AProgramCanKeepTheDriverIdentity()
    {
        var app = new TestApp { Raw = true };
        _driver.Driver.EnqueueKey(Key(Keys.kbKeypadEnter));

        TEvent ev = app.Read();

        Assert.Equal(Keys.kbKeypadEnter, ev.keyDown.keyCode);
        Assert.Equal(0, ev.keyDown.keypadKey);
        app.ShutDown();
    }

    // ── controls ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnterInAnInputLineActivatesTheDialogsDefaultButton(bool keypad)
    {
        var app = new TestApp();
        var dialog = new GuardedDialog();
        var line = new TInputLine(new TRect(2, 2, 30, 3), 40);
        dialog.Insert(new TButton(new TRect(2, 8, 12, 10), "O~K~", Views.cmOK, ButtonConstants.bfDefault));
        dialog.Insert(new TButton(new TRect(14, 8, 26, 10), "Cancel", Views.cmCancel, ButtonConstants.bfNormal));
        dialog.Insert(line);
        line.Select();

        _driver.Driver.EnqueueKey(Typed('a'));
        _driver.Driver.EnqueueKey(Typed('b'));
        _driver.Driver.EnqueueKey(Enter(keypad));
        ushort result = app.DeskTop!.ExecView(dialog);

        Assert.Equal(Views.cmOK, result);
        Assert.Equal("ab", line.Data);
        dialog.ShutDown();
        app.ShutDown();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnterOnAFocusedButtonPressesThatButtonNotTheDefault(bool keypad)
    {
        var app = new TestApp();
        var dialog = new GuardedDialog();
        var yes = new TButton(new TRect(14, 8, 26, 10), "~Y~es", Views.cmYes, ButtonConstants.bfNormal);
        dialog.Insert(new TButton(new TRect(2, 8, 12, 10), "O~K~", Views.cmOK, ButtonConstants.bfDefault));
        dialog.Insert(yes);
        yes.Select();

        _driver.Driver.EnqueueKey(Enter(keypad));
        ushort result = app.DeskTop!.ExecView(dialog);

        Assert.Equal(Views.cmYes, result);
        dialog.ShutDown();
        app.ShutDown();
    }

    /// <summary>Ctrl+Enter is not Enter to a dialog, from either key: it must not press the default button.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CtrlEnterDoesNotActivateTheDefaultButton(bool keypad)
    {
        var app = new TestApp();
        var dialog = new GuardedDialog();
        var line = new TInputLine(new TRect(2, 2, 30, 3), 40);
        dialog.Insert(new TButton(new TRect(2, 8, 12, 10), "O~K~", Views.cmOK, ButtonConstants.bfDefault));
        dialog.Insert(line);
        line.Select();

        _driver.Driver.EnqueueKey(Enter(keypad, Ctrl));
        _driver.Driver.EnqueueKey(Key(Keys.kbEsc));
        ushort result = app.DeskTop!.ExecView(dialog);

        Assert.Equal(Views.cmCancel, result);
        dialog.ShutDown();
        app.ShutDown();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnterInAnEditorStartsANewLine(bool keypad)
    {
        var app = new TestApp();
        var editor = new TEditor(new TRect(0, 0, 40, 6), null, null, null, 1024);
        app.DeskTop!.Insert(editor);
        editor.Select();

        _driver.Driver.EnqueueKey(Typed('a'));
        _driver.Driver.EnqueueKey(Enter(keypad));
        _driver.Driver.EnqueueKey(Typed('b'));
        app.Pump();

        var text = new StringBuilder();
        for (uint p = 0; p < editor.bufLen; p++) text.Append(editor.BufChar(p));
        Assert.Equal("a\nb", text.ToString());
        app.ShutDown();
    }

    /// <summary>
    /// A menu reads its keys in its own loop. Enter on the bar opens the menu, Enter in the box chooses the item; the
    /// trailing Esc keys only end the loop if Enter did nothing.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnterOpensAMenuAndChoosesItsItem(bool keypad)
    {
        var app = new TestApp();
        _driver.Driver.EnqueueKey(Enter(keypad));
        _driver.Driver.EnqueueKey(Enter(keypad));
        for (int i = 0; i < 4; i++) _driver.Driver.EnqueueKey(Key(Keys.kbEsc));

        ushort command = app.MenuBar!.Execute();

        Assert.Equal(cmPicked, command);
        while (_driver.Driver.ReadKeyEvent(out _))
        {
        }

        app.ShutDown();
    }
}
