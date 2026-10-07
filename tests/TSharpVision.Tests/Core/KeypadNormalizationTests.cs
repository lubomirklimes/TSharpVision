// The numeric keypad above the drivers. A driver reports which key it was (kbKeypad0 … kbKeypadEnter, kbGrayMinus,
// kbGrayPlus), its text if it typed any, and the lock state. TProgram.GetEvent → KeypadKeys.Normalize turns that
// into the key a view handles and keeps the physical key in keyDown.keypadKey. The control tests queue what the
// drivers report on the driver and read through a real TProgram, as the keypad Enter tests do.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class KeypadNormalizationTests : IDisposable
{
    private const uint Shift = Keys.kbLeftShift;
    private const uint Ctrl = Keys.kbLeftCtrl;
    private const uint Alt = Keys.kbLeftAlt;
    private const uint Num = Keys.kbNumState;
    private const ushort cmSecond = 1235;

    private readonly DriverScope _driver = new();

    public KeypadNormalizationTests()
    {
        TEventQueue.Resume();
        TEventQueue.ClearPosted();
    }

    public void Dispose()
    {
        TEventQueue.ClearPosted();
        _driver.Dispose();
    }

    private static readonly ushort[] Digits =
    {
        Keys.kbKeypad0, Keys.kbKeypad1, Keys.kbKeypad2, Keys.kbKeypad3, Keys.kbKeypad4,
        Keys.kbKeypad5, Keys.kbKeypad6, Keys.kbKeypad7, Keys.kbKeypad8, Keys.kbKeypad9,
    };

    /// <summary>A keypad key as the Console, SDL and Kitty drivers build it: identity, its scan pair, state, text.</summary>
    private static TEvent Keypad(ushort identity, uint state = 0, string text = "", ushort what = Events.evKeyDown)
    {
        var ev = new TEvent { What = what };
        ev.keyDown.keyCode = identity;
        ev.keyDown.charScan = new CharScanType(identity);
        ev.keyDown.controlKeyState = state;
        ev.keyDown.text = text;
        return ev;
    }

    private static TEvent Key(ushort keyCode, uint state = 0) => Keypad(keyCode, state);

    /// <summary>Text as a driver reports a typed character: the ASCII code is the key code, the text beside it.</summary>
    private static TEvent Typed(string text, uint state = 0)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        if (text.Length == 1 && text[0] <= 0x7F)
        {
            ev.keyDown.keyCode = text[0];
            ev.keyDown.charScan.charCode = (byte)text[0];
        }

        ev.keyDown.text = text;
        ev.keyDown.controlKeyState = state;
        return ev;
    }

    private static TEvent Normalized(TEvent ev)
    {
        KeypadKeys.Normalize(ref ev);
        return ev;
    }

    // ── identities ──────────────────────────────────────────────────────────

    public static TheoryData<ushort> Identities
    {
        get
        {
            var data = new TheoryData<ushort>();
            foreach (ushort digit in Digits) data.Add(digit);
            foreach (ushort other in new[] { Keys.kbKeypadDecimal, Keys.kbKeypadDivide, Keys.kbKeypadMultiply,
                         Keys.kbKeypadEnter, Keys.kbGrayMinus, Keys.kbGrayPlus })
                data.Add(other);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Identities))]
    public void EveryKeypadEventKeepsItsPhysicalIdentityWhateverItBecomes(ushort identity)
    {
        Assert.True(KeypadKeys.IsKeypadIdentity(identity));
        foreach (uint state in new uint[] { 0, Num, Shift, Num | Ctrl })
            foreach (string text in new[] { "", "x" })
                foreach (ushort what in new[] { Events.evKeyDown, Events.evKeyUp })
                {
                    TEvent ev = Keypad(identity, state, what == Events.evKeyUp ? "" : text, what);

                    Assert.True(KeypadKeys.Normalize(ref ev));

                    Assert.Equal(identity, ev.keyDown.keypadKey);
                    Assert.Equal(state, ev.keyDown.controlKeyState);
                    Assert.Equal(what, ev.What);
                }
    }

    [Fact]
    public void MainKeysTheLockKeyAndOtherEventsAreNotKeypadEvents()
    {
        foreach (ushort code in new ushort[] { Keys.kbNumLock, Keys.kbHome, Keys.kbLeft, Keys.kbIns, Keys.kbDel,
                     Keys.kbEnter, '1', '+', '-', '*', '/', 0x9001, 0x8F00, 0 })
        {
            Assert.False(KeypadKeys.IsKeypadIdentity(code));
            TEvent ev = Key(code, Num);
            Assert.False(KeypadKeys.Normalize(ref ev));
            Assert.Equal(code, ev.keyDown.keyCode);
            Assert.Equal(0, ev.keyDown.keypadKey);
        }

        TEvent modifier = Keypad(Keys.kbKeypad4, 0, "", Events.evModifierChanged);
        Assert.False(KeypadKeys.Normalize(ref modifier));
        Assert.Equal(Keys.kbKeypad4, modifier.keyDown.keyCode);
    }

    [Fact]
    public void NormalizingTwiceChangesNothingMore()
    {
        TEvent once = Normalized(Keypad(Keys.kbKeypad8, Shift));
        TEvent twice = once;
        Assert.False(KeypadKeys.Normalize(ref twice));
        Assert.Equal(Keys.kbUp, twice.keyDown.keyCode);
        Assert.Equal(Keys.kbKeypad8, twice.keyDown.keypadKey);
    }

    // ── text ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Keys.kbKeypad0, "0")]
    [InlineData(Keys.kbKeypad1, "1")]
    [InlineData(Keys.kbKeypad5, "5")]
    [InlineData(Keys.kbKeypad9, "9")]
    [InlineData(Keys.kbKeypadDecimal, ".")]
    [InlineData(Keys.kbKeypadDecimal, ",")]
    [InlineData(Keys.kbKeypadDivide, "/")]
    [InlineData(Keys.kbKeypadMultiply, "*")]
    public void AKeyThatTypedTextIsThatTextWithTheCharacterAsItsCode(ushort identity, string text)
    {
        // Text decides, not the lock state the event happens to carry.
        foreach (uint state in new uint[] { Num, 0, Num | Shift })
        {
            TEvent ev = Normalized(Keypad(identity, state, text));

            Assert.Equal(text[0], ev.keyDown.keyCode);
            Assert.Equal((byte)text[0], ev.keyDown.charScan.charCode);
            Assert.Equal(0, ev.keyDown.charScan.scanCode);
            Assert.Equal(text, ev.keyDown.text);
            Assert.Equal(text, KeyText.PrintableText(ev.keyDown));
            Assert.Equal(identity, ev.keyDown.keypadKey);
        }
    }

    /// <summary>The text is the driver's. A character outside ASCII has no key code, like any typed character.</summary>
    [Theory]
    [InlineData("٤")]
    [InlineData("٫")]
    [InlineData("12")]
    public void TextThatIsNotOneAsciiCharacterHasNoKeyCode(string text)
    {
        TEvent ev = Normalized(Keypad(Keys.kbKeypad4, Num, text));

        Assert.Equal(0, ev.keyDown.keyCode);
        Assert.Equal(0, ev.keyDown.charScan.ToUShort());
        Assert.Equal(text, ev.keyDown.text);
        Assert.Equal(Keys.kbKeypad4, ev.keyDown.keypadKey);
    }

    /// <summary>
    /// NumLock on and no text: the text, if there is any, is an event of its own (SDL commits it separately; Alt+digits
    /// compose a character). Nothing is made up for it and it is not navigation.
    /// </summary>
    [Fact]
    public void WithNumLockOnAKeyWithoutTextIsNeitherTextNorNavigation()
    {
        foreach (ushort identity in Digits.Append(Keys.kbKeypadDecimal))
            foreach (uint state in new uint[] { Num, Num | Shift, Num | Ctrl, Num | Alt })
            {
                TEvent ev = Normalized(Keypad(identity, state));

                Assert.Equal(identity, ev.keyDown.keyCode);
                Assert.Equal(identity, ev.keyDown.keypadKey);
                Assert.Equal(string.Empty, ev.keyDown.text);
                Assert.Equal(string.Empty, KeyText.PrintableText(ev.keyDown));
            }
    }

    [Theory]
    [InlineData(Keys.kbKeypadDivide)]
    [InlineData(Keys.kbKeypadMultiply)]
    public void AnOperatorWithoutTextStaysItsIdentityAndTypesNothing(ushort identity)
    {
        foreach (uint state in new uint[] { 0, Num, Ctrl })
        {
            TEvent ev = Normalized(Keypad(identity, state));
            Assert.Equal(identity, ev.keyDown.keyCode);
            Assert.Equal(string.Empty, KeyText.PrintableText(ev.keyDown));
        }
    }

    /// <summary>
    /// Gray minus and plus keep their historical codes, which applications bind. They type the text of their event;
    /// without text the character of the legacy pair is not typed either (SDL sends the text as its own event).
    /// </summary>
    [Theory]
    [InlineData(Keys.kbGrayMinus, "-")]
    [InlineData(Keys.kbGrayPlus, "+")]
    public void GrayMinusAndPlusKeepTheirCodesAndTypeOnlyTheirOwnText(ushort identity, string text)
    {
        TEvent with = Normalized(Keypad(identity, Num, text));
        Assert.Equal(identity, with.keyDown.keyCode);
        Assert.Equal(text, KeyText.PrintableText(with.keyDown));
        Assert.Equal(identity, with.keyDown.keypadKey);

        TEvent without = Normalized(Keypad(identity, Num));
        Assert.Equal(identity, without.keyDown.keyCode);
        Assert.Equal(string.Empty, KeyText.PrintableText(without.keyDown));
        Assert.Equal(identity, without.keyDown.keypadKey);
    }

    // ── navigation ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Keys.kbKeypad7, Keys.kbHome)]
    [InlineData(Keys.kbKeypad8, Keys.kbUp)]
    [InlineData(Keys.kbKeypad9, Keys.kbPgUp)]
    [InlineData(Keys.kbKeypad4, Keys.kbLeft)]
    [InlineData(Keys.kbKeypad6, Keys.kbRight)]
    [InlineData(Keys.kbKeypad1, Keys.kbEnd)]
    [InlineData(Keys.kbKeypad2, Keys.kbDown)]
    [InlineData(Keys.kbKeypad3, Keys.kbPgDn)]
    [InlineData(Keys.kbKeypad0, Keys.kbIns)]
    [InlineData(Keys.kbKeypadDecimal, Keys.kbDel)]
    public void WithNumLockOffAKeyWithoutTextIsTheCursorKeyPrintedOnIt(ushort identity, ushort expected)
    {
        foreach (ushort what in new[] { Events.evKeyDown, Events.evKeyUp })
        {
            TEvent ev = Normalized(Keypad(identity, 0, "", what));

            Assert.Equal(expected, ev.keyDown.keyCode);
            Assert.Equal(expected, ev.keyDown.charScan.ToUShort());
            Assert.Equal(identity, ev.keyDown.keypadKey);
            Assert.Equal(string.Empty, ev.keyDown.text);
        }
    }

    /// <summary>
    /// Keypad 5 has no cursor key: the console's VK_CLEAR, Kitty's KP_BEGIN and CSI E have never been a Turbo Vision
    /// key. It stays its identity, which no view handles.
    /// </summary>
    [Fact]
    public void KeypadFiveWithoutTextIsNoKey()
    {
        foreach (uint state in new uint[] { 0, Shift, Ctrl, Num })
        {
            TEvent ev = Normalized(Keypad(Keys.kbKeypad5, state));
            Assert.Equal(Keys.kbKeypad5, ev.keyDown.keyCode);
            Assert.Equal(Keys.kbKeypad5, ev.keyDown.keypadKey);
            Assert.Equal(0, KeypadKeys.NavigationKey(Keys.kbKeypad5, state));
        }
    }

    // ── modifiers ───────────────────────────────────────────────────────────

    /// <summary>
    /// The codes the main cursor keys have on every driver: Ctrl has its own for Home, End, PgUp, PgDn, Left and Right;
    /// Ins and Del have Shift, Ctrl and Ctrl+Shift codes; Up and Down have none; Alt and a lone Shift on a cursor key
    /// are in the state only.
    /// </summary>
    [Theory]
    [InlineData(Keys.kbKeypad8, Shift, Keys.kbUp)]
    [InlineData(Keys.kbKeypad8, Ctrl, Keys.kbUp)]
    [InlineData(Keys.kbKeypad2, Ctrl | Shift, Keys.kbDown)]
    [InlineData(Keys.kbKeypad4, Shift, Keys.kbLeft)]
    [InlineData(Keys.kbKeypad4, Ctrl, Keys.kbCtrlLeft)]
    [InlineData(Keys.kbKeypad4, Ctrl | Shift, Keys.kbCtrlLeft)]
    [InlineData(Keys.kbKeypad4, Alt, Keys.kbLeft)]
    [InlineData(Keys.kbKeypad6, Ctrl, Keys.kbCtrlRight)]
    [InlineData(Keys.kbKeypad7, Shift, Keys.kbHome)]
    [InlineData(Keys.kbKeypad7, Ctrl, Keys.kbCtrlHome)]
    [InlineData(Keys.kbKeypad7, Ctrl | Alt, Keys.kbCtrlHome)]
    [InlineData(Keys.kbKeypad1, Ctrl, Keys.kbCtrlEnd)]
    [InlineData(Keys.kbKeypad9, Ctrl, Keys.kbCtrlPgUp)]
    [InlineData(Keys.kbKeypad3, Ctrl, Keys.kbCtrlPgDn)]
    [InlineData(Keys.kbKeypad0, Shift, Keys.kbShiftIns)]
    [InlineData(Keys.kbKeypad0, Ctrl, Keys.kbCtrlIns)]
    [InlineData(Keys.kbKeypad0, Ctrl | Shift, Keys.kbCtrlShiftIns)]
    [InlineData(Keys.kbKeypad0, Alt, Keys.kbIns)]
    [InlineData(Keys.kbKeypadDecimal, Shift, Keys.kbShiftDel)]
    [InlineData(Keys.kbKeypadDecimal, Ctrl, Keys.kbCtrlDel)]
    [InlineData(Keys.kbKeypadDecimal, Ctrl | Shift, Keys.kbCtrlShiftDel)]
    public void NavigationTakesTheModifierVariantOfTheMainKeyAndKeepsTheWholeState(ushort identity, uint state, ushort expected)
    {
        TEvent ev = Normalized(Keypad(identity, state));

        Assert.Equal(expected, ev.keyDown.keyCode);
        Assert.Equal(state, ev.keyDown.controlKeyState);
        Assert.Equal(identity, ev.keyDown.keypadKey);
        Assert.Equal(expected, KeypadKeys.NavigationKey(identity, state));
    }

    // ── through a program ───────────────────────────────────────────────────

    private sealed class TestApp : TProgram
    {
        public bool Raw { get; set; }

        public override TMenuBar? InitMenuBar(TRect r)
        {
            r.b.y = r.a.y + 1;
            var file = new TSubMenu("~F~ile", Keys.kbAltF)
                + new TMenuItem("~F~irst", 1234, Keys.kbNoKey)
                + new TMenuItem("~S~econd", cmSecond, Keys.kbNoKey);
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

    private void Queue(params TEvent[] events)
    {
        foreach (TEvent ev in events) _driver.Driver.EnqueueKey(ev);
    }

    private static string Text(TEditor editor)
    {
        var builder = new StringBuilder();
        for (uint p = 0; p < editor.bufLen; p++) builder.Append(editor.BufChar(p));
        return builder.ToString();
    }

    [Fact]
    public void TheProgramDispatchesTheSemanticKeyAndTheIdentityBesideIt()
    {
        var app = new TestApp();
        Queue(Keypad(Keys.kbKeypad8), Keypad(Keys.kbKeypad1, Num, "1"), Keypad(Keys.kbKeypad1, Num), Key(Keys.kbUp));

        TEvent up = app.Read(), one = app.Read(), silent = app.Read(), main = app.Read();

        Assert.Equal((Keys.kbUp, Keys.kbKeypad8), (up.keyDown.keyCode, up.keyDown.keypadKey));
        Assert.Equal(((ushort)'1', Keys.kbKeypad1, "1"), (one.keyDown.keyCode, one.keyDown.keypadKey, one.keyDown.text));
        Assert.Equal((Keys.kbKeypad1, Keys.kbKeypad1), (silent.keyDown.keyCode, silent.keyDown.keypadKey));
        Assert.Equal((Keys.kbUp, (ushort)0), (main.keyDown.keyCode, main.keyDown.keypadKey));
        app.ShutDown();
    }

    /// <summary>A keyboard diagnostic overrides the normalization and sees exactly what the driver reported.</summary>
    [Fact]
    public void AProgramThatOptsOutSeesTheDriverEventsUnchanged()
    {
        var app = new TestApp { Raw = true };
        TEvent[] reported =
        {
            Keypad(Keys.kbKeypad8), Keypad(Keys.kbKeypad1, Num, "1"), Keypad(Keys.kbGrayPlus, Num),
            Keypad(Keys.kbKeypadDecimal, Shift), Keypad(Keys.kbKeypadEnter, Ctrl),
        };
        Queue(reported);

        foreach (TEvent expected in reported)
        {
            TEvent ev = app.Read();
            Assert.Equal(expected.keyDown.keyCode, ev.keyDown.keyCode);
            Assert.Equal(expected.keyDown.charScan.ToUShort(), ev.keyDown.charScan.ToUShort());
            Assert.Equal(expected.keyDown.text, ev.keyDown.text);
            Assert.Equal(0, ev.keyDown.keypadKey);
        }

        app.ShutDown();
    }

    [Fact]
    public void AnInputLineIsEditedWithTheKeypad()
    {
        var app = new TestApp();
        var line = new TInputLine(new TRect(2, 2, 40, 3), 40);
        app.DeskTop!.Insert(line);
        line.Select();

        Queue(Typed("a"), Typed("b"), Typed("c"),
            Keypad(Keys.kbKeypad4),                       // Left
            Keypad(Keys.kbKeypad1, Num, "1"),             // types 1 before c
            Keypad(Keys.kbKeypad7),                       // Home
            Keypad(Keys.kbKeypadDecimal),                 // Del: removes a
            Keypad(Keys.kbKeypad1),                       // End
            Keypad(Keys.kbKeypadDecimal, Num, ","),       // types the layout's decimal separator
            Keypad(Keys.kbKeypadDivide, Num, "/"),
            Keypad(Keys.kbKeypadMultiply, Num, "*"),
            Keypad(Keys.kbGrayMinus, Num, "-"),
            Keypad(Keys.kbGrayPlus, Num, "+"),
            Keypad(Keys.kbKeypad5),                       // nothing
            Keypad(Keys.kbKeypad6));                      // Right at the end: nothing
        app.Pump();

        Assert.Equal("b1c,/*-+", line.Data);
        Assert.Equal(line.Data.Length, line.CurPos);
        app.ShutDown();
    }

    /// <summary>
    /// SDL reports a keypad key and commits its text as a second event. The first is the identity, with NumLock on and
    /// no text; the second types. Each character arrives once — also for + and −, whose identity has a character in
    /// its legacy scan pair.
    /// </summary>
    [Fact]
    public void AnIdentityEventFollowedByItsTextCommitTypesOnce()
    {
        var app = new TestApp();
        var line = new TInputLine(new TRect(2, 2, 40, 3), 40);
        app.DeskTop!.Insert(line);
        line.Select();

        (ushort Identity, string Text)[] keys =
        {
            (Keys.kbKeypad1, "1"), (Keys.kbKeypad5, "5"), (Keys.kbKeypad0, "0"), (Keys.kbKeypadDecimal, "."),
            (Keys.kbKeypadDivide, "/"), (Keys.kbKeypadMultiply, "*"), (Keys.kbGrayMinus, "-"), (Keys.kbGrayPlus, "+"),
        };
        foreach ((ushort identity, string text) in keys)
            Queue(Keypad(identity, Num), Typed(text, Num), Keypad(identity, Num, "", Events.evKeyUp));
        app.Pump();

        Assert.Equal("150./*-+", line.Data);
        app.ShutDown();
    }

    [Fact]
    public void AnEditorIsNavigatedAndSelectedInWithTheKeypad()
    {
        var app = new TestApp();
        var editor = new TEditor(new TRect(0, 0, 40, 6), null, null, null, 1024);
        app.DeskTop!.Insert(editor);
        editor.Select();
        editor.InsertText("one\ntwo\nthree");
        editor.SetSelect(0, 0, true);

        Queue(Keypad(Keys.kbKeypad2), Keypad(Keys.kbKeypad6));                 // Down, Right: after 't' of two
        app.Pump();
        Assert.Equal(5u, editor.curPtr);

        Queue(Keypad(Keys.kbKeypad6, Shift), Keypad(Keys.kbKeypad6, Shift));   // Shift+Right twice: selects "wo"
        app.Pump();
        Assert.Equal((5u, 7u), (editor.selStart, editor.selEnd));

        Queue(Keypad(Keys.kbKeypad4));                                         // Left collapses the selection
        app.Pump();
        Assert.False(editor.HasSelection());

        Queue(Keypad(Keys.kbKeypad1, Ctrl));                                   // Ctrl+End: end of the document
        app.Pump();
        Assert.Equal(editor.bufLen, editor.curPtr);

        Queue(Keypad(Keys.kbKeypad7, Ctrl), Keypad(Keys.kbKeypad3, Num, "3")); // Ctrl+Home, then a digit
        app.Pump();
        Assert.Equal("3one\ntwo\nthree", Text(editor));

        Queue(Keypad(Keys.kbKeypad1), Keypad(Keys.kbKeypad7, Shift), Keypad(Keys.kbKeypadDecimal));  // End, Shift+Home, Del
        app.Pump();
        Assert.Equal("\ntwo\nthree", Text(editor));
        app.ShutDown();
    }

    [Fact]
    public void AListBoxInADialogFollowsTheKeypadCursorKeys()
    {
        var app = new TestApp();
        var dialog = new TDialog(new TRect(5, 3, 45, 15), "List");
        var list = new TListBox(new TRect(2, 2, 30, 6), 1, null);
        var items = new TStringCollection();
        for (int i = 0; i < 12; i++) items.Insert($"item {i:00}");
        list.NewList(items);
        dialog.Insert(list);
        app.DeskTop!.Insert(dialog);
        list.Select();

        Queue(Keypad(Keys.kbKeypad2), Keypad(Keys.kbKeypad2), Keypad(Keys.kbKeypad8));    // Down, Down, Up
        app.Pump();
        Assert.Equal(1, list.focused);

        Queue(Keypad(Keys.kbKeypad3));                                                    // PgDn: one page of four
        app.Pump();
        Assert.Equal(5, list.focused);

        Queue(Keypad(Keys.kbKeypad3, Ctrl));                                              // Ctrl+PgDn: the last item
        app.Pump();
        Assert.Equal(11, list.focused);

        Queue(Keypad(Keys.kbKeypad9, Ctrl), Keypad(Keys.kbKeypad2, Num), Keypad(Keys.kbKeypad5)); // Ctrl+PgUp: the first; then two silent keys
        app.Pump();
        Assert.Equal(0, list.focused);
        dialog.ShutDown();
        app.ShutDown();
    }

    /// <summary>The menu reads its keys in its own loop: keypad Enter opens it, keypad Down moves, keypad Enter chooses.</summary>
    [Fact]
    public void AMenuIsWalkedWithTheKeypad()
    {
        var app = new TestApp();
        Queue(Keypad(Keys.kbKeypadEnter), Keypad(Keys.kbKeypad2), Keypad(Keys.kbKeypadEnter));
        for (int i = 0; i < 4; i++) Queue(Key(Keys.kbEsc));

        ushort command = app.MenuBar!.Execute();

        Assert.Equal(cmSecond, command);
        while (_driver.Driver.ReadKeyEvent(out _))
        {
        }

        app.ShutDown();
    }
}
