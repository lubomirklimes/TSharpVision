// All tests touch global ClipboardService.Current → NonParallel collection.
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Dialogs;

[Collection("NonParallel")]
public sealed class InputLineClipboardTests : IDisposable
{
    private const string Czech = "Příliš žluťoučký kůň";
    private const string Emoji = "\U0001F600";          // one code point, two UTF-16 units
    private const string Combining = "e\u0301a\u030A";  // e + acute, a + ring: not precomposed

    private readonly DriverScope _driver;
    private readonly ClipboardServiceScope _scope;
    private readonly InMemoryClipboardService _clipboard = new();

    public InputLineClipboardTests()
    {
        _driver = new DriverScope();
        _scope = new ClipboardServiceScope();
        ClipboardService.Current = _clipboard;
    }

    public void Dispose()
    {
        _scope.Dispose();
        _driver.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // A selected input line in a host group, holding text with the cursor at its end and nothing selected.
    private static TInputLine Line(string text, int maxLen = 64, int width = 30)
    {
        var il = new TInputLine(new TRect(0, 0, width, 1), maxLen + 1);
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.Insert(il);
        il.SetState(Views.sfSelected, true);
        il.SetData(text);
        Select(il, 0, 0);
        return il;
    }

    private static void Select(TInputLine il, int start, int end)
    {
        il.SelStart = start;
        il.SelEnd = end;
        if (start < end) il.CurPos = end;
    }

    // Sends one key and returns whether the line consumed it.
    private static bool Key(TInputLine il, ushort keyCode, string? text = null)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        if (text != null) ev.keyDown.text = text;
        il.HandleEvent(ref ev);
        return ev.What == Events.evNothing;
    }

    private static void AssertCursorVisible(TInputLine il)
    {
        Assert.InRange(il.CurPos, 0, il.Data.Length);
        Assert.InRange(il.FirstPos, 0, il.CurPos);
        // The column Draw hands to SetCursor; the host here is never exposed, so nothing is drawn.
        Assert.InRange(il.CurPos - il.FirstPos + 1, 1, il.size.x - 1);
    }

    private sealed class RejectingClipboard : IClipboardService
    {
        public bool IsAvailable => true;
        public string? GetText() => null;
        public bool TryGetText(out string text) { text = string.Empty; return false; }
        public bool SetText(string text) => false;
    }

    private sealed class ThrowingClipboard : IClipboardService
    {
        public bool IsAvailable => true;
        public string? GetText() => throw new InvalidOperationException("clipboard");
        public bool TryGetText(out string text) => throw new InvalidOperationException("clipboard");
        public bool SetText(string text) => throw new InvalidOperationException("clipboard");
    }

    // ── Copy ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Keys.kbCtrlC)]
    [InlineData(Keys.kbCtrlIns)]
    public void Copy_PutsSelectedUnicodeTextOnClipboard_AndChangesNothing(ushort key)
    {
        string text = "<" + Czech + Emoji + Combining + ">";
        var il = Line(text);
        Select(il, 1, text.Length - 1);
        int cursor = il.CurPos, first = il.FirstPos;

        Assert.True(Key(il, key));

        Assert.Equal(Czech + Emoji + Combining, _clipboard.GetText());
        Assert.Equal(text, il.Data);
        Assert.Equal(1, il.SelStart);
        Assert.Equal(text.Length - 1, il.SelEnd);
        Assert.Equal(cursor, il.CurPos);
        Assert.Equal(first, il.FirstPos);
    }

    [Fact]
    public void Copy_WithoutSelection_ChangesNothing()
    {
        _clipboard.SetText("before");
        var il = Line("hello");
        il.CurPos = 2;

        Assert.True(Key(il, Keys.kbCtrlC));

        Assert.Equal("before", _clipboard.GetText());
        Assert.Equal("hello", il.Data);
        Assert.Equal(2, il.CurPos);
    }

    [Fact]
    public void Copy_FromMaskedLine_CopiesNothing()
    {
        var il = Line("secret");
        il.PasswordChar = '*';
        Select(il, 0, 6);

        Key(il, Keys.kbCtrlC);
        Key(il, Keys.kbCtrlX);

        Assert.Null(_clipboard.GetText());
        Assert.Equal("secret", il.Data);
    }

    // ── Cut ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Keys.kbCtrlX)]
    [InlineData(Keys.kbShiftDel)]
    public void Cut_MovesSelectionToClipboard(ushort key)
    {
        var il = Line("one " + Czech + " two");
        Select(il, 4, 4 + Czech.Length);

        Assert.True(Key(il, key));

        Assert.Equal(Czech, _clipboard.GetText());
        Assert.Equal("one  two", il.Data);
        Assert.Equal(4, il.CurPos);
        Assert.Equal(il.SelStart, il.SelEnd);
        AssertCursorVisible(il);
    }

    [Fact]
    public void Cut_WithoutSelection_ChangesNothing()
    {
        var il = Line("hello");
        il.CurPos = 2;

        Assert.True(Key(il, Keys.kbCtrlX));

        Assert.Null(_clipboard.GetText());
        Assert.Equal("hello", il.Data);
        Assert.Equal(2, il.CurPos);
    }

    [Fact]
    public void Cut_WhenClipboardRejectsTheText_LeavesTextAndSelection()
    {
        ClipboardService.Current = new RejectingClipboard();
        var il = Line("hello world");
        Select(il, 0, 5);

        Assert.True(Key(il, Keys.kbCtrlX));

        Assert.Equal("hello world", il.Data);
        Assert.Equal(0, il.SelStart);
        Assert.Equal(5, il.SelEnd);
    }

    // ── Paste ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Keys.kbCtrlV)]
    [InlineData(Keys.kbShiftIns)]
    public void Paste_InsertsAtCursor(ushort key)
    {
        _clipboard.SetText("XY");
        var il = Line("abcd");
        il.CurPos = 2;

        Assert.True(Key(il, key));

        Assert.Equal("abXYcd", il.Data);
        Assert.Equal(4, il.CurPos);
        Assert.Equal(il.SelStart, il.SelEnd);
        Assert.Equal("XY", _clipboard.GetText());
    }

    [Fact]
    public void Paste_ReplacesSelection()
    {
        _clipboard.SetText("new");
        var il = Line("an old name");
        Select(il, 3, 6);

        Key(il, Keys.kbCtrlV);

        Assert.Equal("an new name", il.Data);
        Assert.Equal(6, il.CurPos);
        Assert.Equal(il.SelStart, il.SelEnd);
    }

    [Fact]
    public void Paste_InOverwriteMode_StillInserts()
    {
        _clipboard.SetText("XY");
        var il = Line("abcd");
        il.CurPos = 1;
        il.SetState(Views.sfCursorIns, true);

        Key(il, Keys.kbCtrlV);

        Assert.Equal("aXYbcd", il.Data);
        Assert.True((il.state & Views.sfCursorIns) != 0);
    }

    [Fact]
    public void Paste_EmptyOrMissingText_ChangesNothing()
    {
        var il = Line("hello world");
        Select(il, 0, 5);

        Assert.True(Key(il, Keys.kbCtrlV));         // nothing on the clipboard
        _clipboard.SetText(string.Empty);
        Assert.True(Key(il, Keys.kbCtrlV));
        _clipboard.SetText("\r\n\u0001");            // nothing left after sanitizing
        Assert.True(Key(il, Keys.kbCtrlV));

        Assert.Equal("hello world", il.Data);
        Assert.Equal(0, il.SelStart);
        Assert.Equal(5, il.SelEnd);
    }

    [Fact]
    public void UnavailableClipboard_MakesEveryOperationANoOp()
    {
        ClipboardService.Current = new NullClipboardService();
        var il = Line("hello world");
        Select(il, 0, 5);

        Assert.True(Key(il, Keys.kbCtrlC));
        Assert.True(Key(il, Keys.kbCtrlX));
        Assert.True(Key(il, Keys.kbCtrlV));

        Assert.Equal("hello world", il.Data);
        Assert.Equal(0, il.SelStart);
        Assert.Equal(5, il.SelEnd);
    }

    [Fact]
    public void ThrowingClipboard_DoesNotEscapeHandleEvent()
    {
        ClipboardService.Current = new ThrowingClipboard();
        var il = Line("hello world");
        Select(il, 0, 5);

        foreach (ushort key in new[] { Keys.kbCtrlC, Keys.kbCtrlIns, Keys.kbCtrlX, Keys.kbShiftDel, Keys.kbCtrlV, Keys.kbShiftIns })
            Assert.True(Key(il, key));

        Assert.Equal("hello world", il.Data);
        Assert.Equal(0, il.SelStart);
        Assert.Equal(5, il.SelEnd);
    }

    [Theory]
    [InlineData("one\r\ntwo", "one two")]
    [InlineData("one\ntwo\rthree", "one two three")]
    [InlineData("one\r\n\r\n\ntwo", "one two")]
    [InlineData("\r\none\r\n", "one")]
    [InlineData("one\u2028two\u2029three\u0085four", "one two three four")]
    [InlineData("a\tb", "a b")]
    [InlineData("a\u0000b\u0007c\u001Bd\u007Fe", "abcde")]
    [InlineData(" kept spaces ", " kept spaces ")]
    public void Paste_MultilineText_BecomesOneLine(string clipboard, string expected)
    {
        _clipboard.SetText(clipboard);
        var il = Line(string.Empty);

        Key(il, Keys.kbCtrlV);

        Assert.Equal(expected, il.Data);
        Assert.DoesNotContain('\r', il.Data);
        Assert.DoesNotContain('\n', il.Data);
    }

    [Theory]
    [InlineData(Czech)]
    [InlineData(Emoji)]
    [InlineData(Combining)]
    [InlineData("a" + Emoji + "b" + Combining + Czech)]
    public void Paste_ThenCopy_PreservesUnicodeExactly(string text)
    {
        _clipboard.SetText(text);
        var il = Line(string.Empty);

        Key(il, Keys.kbCtrlV);
        Assert.Equal(text, il.Data);
        Assert.Equal(text.Length, il.CurPos);

        _clipboard.Clear();
        il.SelectAll(true);
        Key(il, Keys.kbCtrlC);
        Assert.Equal(text, _clipboard.GetText());
    }

    [Fact]
    public void Paste_DropsUnpairedSurrogates()
    {
        _clipboard.SetText("a\uD83Db\uDE00c");
        var il = Line(string.Empty);

        Key(il, Keys.kbCtrlV);

        Assert.Equal("abc", il.Data);
    }

    // ── Capacity ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("abc", "12", 8, "abc12")]           // fits
    [InlineData("abc", "12345", 8, "abc12345")]     // exactly fills
    [InlineData("abc", "1234567", 8, "abc12345")]   // exceeds: cut at capacity, as typed text is
    [InlineData("abcdefgh", "1", 8, "abcdefgh")]    // full: nothing goes in
    public void Paste_RespectsMaxLen(string initial, string clipboard, int maxLen, string expected)
    {
        _clipboard.SetText(clipboard);
        var il = Line(initial, maxLen);

        Key(il, Keys.kbCtrlV);

        Assert.Equal(expected, il.Data);
        Assert.Equal(expected.Length, il.CurPos);
    }

    [Fact]
    public void Paste_MatchesTypedInputAtCapacity()
    {
        var typed = Line("abc", 8);
        foreach (char ch in "1234567") Key(typed, 0, ch.ToString());

        _clipboard.SetText("1234567");
        var pasted = Line("abc", 8);
        Key(pasted, Keys.kbCtrlV);

        Assert.Equal(typed.Data, pasted.Data);
        Assert.Equal(typed.CurPos, pasted.CurPos);
    }

    [Fact]
    public void Paste_OverSelection_UsesTheCapacityTheSelectionFrees()
    {
        _clipboard.SetText("12345");
        var il = Line("abcdefgh", 8);
        Select(il, 2, 6);

        Key(il, Keys.kbCtrlV);

        Assert.Equal("ab1234gh", il.Data);
        Assert.Equal(6, il.CurPos);
    }

    [Fact]
    public void Paste_VeryLargeText_IsCutToCapacity()
    {
        _clipboard.SetText(new string('x', 2_000_000));
        var il = Line("ab", 40);
        il.CurPos = 1;

        Key(il, Keys.kbCtrlV);

        Assert.Equal("a" + new string('x', 38) + "b", il.Data);
        Assert.Equal(39, il.CurPos);
    }

    [Theory]
    [InlineData("abcd", Emoji, 5, "abcd")]                  // one unit of room: the pair stays out whole
    [InlineData("abc", "x" + Emoji + "y", 5, "abcx")]       // the cut falls inside the pair
    [InlineData("abc", Emoji + "y", 5, "abc" + Emoji)]      // the pair fits exactly
    public void Paste_AtCapacity_NeverSplitsSurrogatePair(string initial, string clipboard, int maxLen, string expected)
    {
        _clipboard.SetText(clipboard);
        var il = Line(initial, maxLen);

        Key(il, Keys.kbCtrlV);

        Assert.Equal(expected, il.Data);
        Assert.False(char.IsHighSurrogate(il.Data[^1]));
    }

    // ── Scrolling and state ───────────────────────────────────────────────

    [Fact]
    public void Paste_LongerThanTheView_ScrollsToKeepCursorVisible()
    {
        _clipboard.SetText("0123456789ABCDEFGHIJKLMNOPQRST");
        var il = Line("<>", maxLen: 64, width: 12);
        il.CurPos = 1;

        Key(il, Keys.kbCtrlV);

        Assert.Equal("<0123456789ABCDEFGHIJKLMNOPQRST>", il.Data);
        Assert.Equal(31, il.CurPos);
        Assert.Equal(21, il.FirstPos);
        AssertCursorVisible(il);
        Assert.True(il.CanScroll(-1));
    }

    [Fact]
    public void Cut_OfScrolledText_KeepsCursorVisible()
    {
        var il = Line("0123456789ABCDEFGHIJKLMNOPQRST", maxLen: 64, width: 12);
        il.SelectAll(true);
        Assert.True(il.FirstPos > 0);

        Key(il, Keys.kbCtrlX);

        Assert.Equal(string.Empty, il.Data);
        Assert.Equal(0, il.CurPos);
        Assert.Equal(0, il.FirstPos);
        AssertCursorVisible(il);
    }

    [Fact]
    public void RepeatedCopyCutPaste_LeavesConsistentState()
    {
        var il = Line("alpha beta", maxLen: 20, width: 12);

        Select(il, 0, 5);
        Key(il, Keys.kbCtrlC);                      // "alpha"
        Key(il, Keys.kbEnd);
        Key(il, Keys.kbCtrlV);                      // alpha betaalpha
        Assert.Equal("alpha betaalpha", il.Data);
        AssertCursorVisible(il);

        Select(il, 5, 10);
        Key(il, Keys.kbCtrlX);                      // " beta" → alphaalpha
        Assert.Equal("alphaalpha", il.Data);
        Assert.Equal(" beta", _clipboard.GetText());
        Assert.Equal(5, il.CurPos);
        AssertCursorVisible(il);

        Key(il, Keys.kbCtrlV);
        Key(il, Keys.kbCtrlV);                      // alpha beta betaalpha — exactly 20
        Assert.Equal("alpha beta betaalpha", il.Data);
        Key(il, Keys.kbCtrlV);                      // full
        Assert.Equal("alpha beta betaalpha", il.Data);
        Assert.Equal(15, il.CurPos);
        Assert.Equal(il.SelStart, il.SelEnd);
        AssertCursorVisible(il);

        il.SelectAll(true);
        Key(il, Keys.kbCtrlX);
        Key(il, Keys.kbCtrlV);
        Assert.Equal("alpha beta betaalpha", il.Data);
        Assert.Equal(20, il.CurPos);
        AssertCursorVisible(il);
    }

    // ── Neighbouring behaviour ────────────────────────────────────────────

    [Fact]
    public void OtherKeys_AreUntouched()
    {
        _clipboard.SetText("XY");
        var il = Line("abcd");
        il.CurPos = 2;

        Assert.True(Key(il, Keys.kbIns));            // plain Insert still toggles the mode
        Assert.True((il.state & Views.sfCursorIns) != 0);
        Assert.True(Key(il, Keys.kbIns));
        Assert.True(Key(il, Keys.kbDel));            // plain Delete still deletes one character
        Assert.Equal("abd", il.Data);
        Assert.False(Key(il, Keys.kbCtrlDel));       // not a clipboard key: passes on
        Assert.False(Key(il, Keys.kbAltC));
        Assert.True(Key(il, 0, "v"));                // letters are text
        Assert.Equal("abvd", il.Data);
    }

    [Fact]
    public void UnselectedLine_IgnoresClipboardKeys()
    {
        _clipboard.SetText("XY");
        var il = Line("abcd");
        il.SetState(Views.sfSelected, false);

        Assert.False(Key(il, Keys.kbCtrlV));
        Assert.Equal("abcd", il.Data);
    }

    // Found while auditing the insertion path: a typed character of two UTF-16 units deleted the selected range
    // once per unit, which threw or removed text beyond the selection.
    [Fact]
    public void TypedSurrogatePair_ReplacesSelectionOnce()
    {
        var il = Line("hello world");
        Select(il, 0, 5);

        Assert.True(Key(il, 0, Emoji));

        Assert.Equal(Emoji + " world", il.Data);
        Assert.Equal(2, il.CurPos);
    }

    // ── Real modal dialog ─────────────────────────────────────────────────

    // Runs a modal loop from the scripted driver keys and fails, instead of hanging, if the loop never ends.
    private sealed class ModalHost : TGroup
    {
        private int _idle;

        public ModalHost(TRect bounds) : base(bounds)
            => options = (ushort)(options & ~Views.ofSelectable);

        public override void GetEvent(ref TEvent e)
        {
            TScreen.GetEvent(ref e);
            if (e.What == Events.evNothing && ++_idle > 1000)
                throw new InvalidOperationException("The modal loop did not end.");
        }

        public override void PutEvent(ref TEvent e) => TEventQueue.Enqueue(e);
    }

    [Fact]
    public void ModalDialog_InputLineReceivesPaste_WithoutApplicationHandling()
    {
        _clipboard.SetText("C:\\Temp\\" + Czech + "\r\n");
        var host = new ModalHost(new TRect(0, 0, 80, 25));
        var dialog = new TDialog(new TRect(10, 5, 60, 14), "Rename");
        var input = new TInputLine(new TRect(10, 2, 44, 3), 80);
        dialog.Insert(input);
        dialog.Insert(new TLabel(new TRect(2, 2, 10, 3), "~N~ame:", input));
        dialog.Insert(new TButton(new TRect(9, 5, 21, 7), "~O~K", Views.cmOK, ButtonConstants.bfDefault));
        input.SetData("old name");
        input.Select();

        foreach (ushort key in new[] { Keys.kbCtrlV, Keys.kbEnter })
        {
            var ev = new TEvent { What = Events.evKeyDown };
            ev.keyDown.keyCode = key;
            _driver.Driver.EnqueueKey(ev);
        }

        ushort result = host.ExecView(dialog);

        Assert.Equal(Views.cmOK, result);
        Assert.Equal("C:\\Temp\\" + Czech, input.Data);
    }
}
