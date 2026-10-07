using System;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

/// <summary>
/// The current-editor conventions of <see cref="TEditor"/>'s key map: Shift with a movement key extends the selection,
/// Ctrl+Home / Ctrl+End reach the ends of the document, Ctrl+Backspace / Ctrl+Delete delete a word. Events are built
/// as the drivers build them: one modifier in the key code (Alt, then Ctrl, then Shift), the whole state beside it.
/// </summary>
[Collection("NonParallel")]
public sealed class EditorModernKeyTests : IDisposable
{
    private const uint Shift = Keys.kbLeftShift;
    private const uint Ctrl = Keys.kbLeftCtrl;

    private readonly DriverScope _driver = new();
    private readonly EditorClipboardScope _clipboard = new();

    public void Dispose()
    {
        _clipboard.Dispose();
        _driver.Dispose();
    }

    private static TEditor Editor(string text, uint caret = 0)
    {
        var editor = new TEditor(new TRect(0, 0, 40, 6), null, null, null, 1024);
        editor.InsertText(text);
        editor.SetSelect(caret, caret, true);
        editor.modified = false;
        return editor;
    }

    private static void Press(TEditor editor, ushort keyCode, uint modifiers = 0)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        ev.keyDown.charScan = new CharScanType(keyCode);
        ev.keyDown.controlKeyState = modifiers;
        editor.HandleEvent(ref ev);
    }

    private static void Type(TEditor editor, char c, uint modifiers = 0)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = c;
        ev.keyDown.charScan = new CharScanType(c);
        ev.keyDown.text = c.ToString();
        ev.keyDown.controlKeyState = modifiers;
        editor.HandleEvent(ref ev);
    }

    private static string Text(TEditor editor)
    {
        var builder = new StringBuilder();
        for (uint p = 0; p < editor.bufLen; p++) builder.Append(editor.BufChar(p));
        return builder.ToString();
    }

    private static string Selected(TEditor editor)
    {
        var builder = new StringBuilder();
        for (uint p = editor.selStart; p < editor.selEnd; p++) builder.Append(editor.BufChar(p));
        return builder.ToString();
    }

    // ── Shift selection ──────────────────────────────────────────────────────

    [Fact]
    public void ShiftRightAndLeftExtendFromTheAnchor()
    {
        TEditor editor = Editor("hello world", 2);

        Press(editor, Keys.kbRight, Shift);
        Press(editor, Keys.kbRight, Shift);
        Assert.Equal("ll", Selected(editor));
        Assert.Equal(4u, editor.curPtr);

        Press(editor, Keys.kbLeft, Shift);
        Assert.Equal("l", Selected(editor));
        Assert.Equal(3u, editor.curPtr);
    }

    [Fact]
    public void ShiftMovementReversesThroughTheAnchor()
    {
        TEditor editor = Editor("hello world", 5);

        Press(editor, Keys.kbRight, Shift);
        Press(editor, Keys.kbLeft, Shift);
        Assert.False(editor.HasSelection());
        Assert.Equal(5u, editor.curPtr);

        Press(editor, Keys.kbLeft, Shift);
        Press(editor, Keys.kbLeft, Shift);
        Assert.Equal("lo", Selected(editor));
        Assert.Equal(3u, editor.curPtr);          // the caret is the moving end; the anchor stays at 5
        Assert.Equal(5u, editor.selEnd);
    }

    [Fact]
    public void APlainMovementCollapsesTheSelectionAndTheNextShiftStartsANewAnchor()
    {
        TEditor editor = Editor("hello world", 0);
        Press(editor, Keys.kbRight, Shift);
        Press(editor, Keys.kbRight, Shift);

        Press(editor, Keys.kbRight);
        Assert.False(editor.HasSelection());
        Assert.Equal(3u, editor.curPtr);

        Press(editor, Keys.kbRight, Shift);
        Assert.Equal("l", Selected(editor));
        Assert.Equal(3u, editor.selStart);
    }

    [Fact]
    public void ShiftHomeAndEndSelectToTheLineEnds()
    {
        TEditor editor = Editor("one\ntwo three\nfour", 7);

        Press(editor, Keys.kbEnd, Shift);
        Assert.Equal(" three", Selected(editor));

        Press(editor, Keys.kbHome, Shift);
        Assert.Equal("two", Selected(editor));
        Assert.Equal(4u, editor.curPtr);
    }

    [Fact]
    public void ShiftUpAndDownSelectAcrossLfLines()
    {
        TEditor editor = Editor("abc\ndef\nghi", 1);

        Press(editor, Keys.kbDown, Shift);
        Assert.Equal("bc\nd", Selected(editor));
        Press(editor, Keys.kbDown, Shift);
        Assert.Equal("bc\ndef\ng", Selected(editor));
        Assert.Equal(2, editor.curPos.y);

        Press(editor, Keys.kbUp, Shift);
        Press(editor, Keys.kbUp, Shift);
        Assert.False(editor.HasSelection());      // back at the anchor
        Assert.Equal(0, editor.curPos.y);
    }

    [Fact]
    public void ACarriageReturnIsAnOrdinaryCharacterToShiftEnd()
    {
        // The buffer is LF-only: a CR belongs to the line and is selected with it.
        TEditor editor = Editor("ab\rcd\nef", 0);
        Press(editor, Keys.kbEnd, Shift);
        Assert.Equal("ab\rcd", Selected(editor));
    }

    [Fact]
    public void ShiftPageDownExtendsByAPage()
    {
        TEditor editor = Editor("1\n2\n3\n4\n5\n6\n7\n8\n9", 0);
        Press(editor, Keys.kbPgDn, Shift);
        Assert.Equal(0u, editor.selStart);
        Assert.Equal(5, editor.curPos.y);         // size.y - 1 lines
        Assert.True(editor.HasSelection());
    }

    [Fact]
    public void TypingReplacesAShiftSelection()
    {
        TEditor editor = Editor("hello world", 0);
        for (int i = 0; i < 5; i++) Press(editor, Keys.kbRight, Shift);
        Type(editor, 'J', Shift);                 // a capital letter is typed with Shift held
        Assert.Equal("J world", Text(editor));
        Assert.False(editor.HasSelection());
    }

    [Fact]
    public void ShiftDoesNotTurnAPrefixLetterIntoASelection()
    {
        // Ctrl+Q then a capital D: line end, as a prefix command, without extending anything.
        TEditor editor = Editor("hello world", 2);
        Press(editor, Keys.kbCtrlQ, Ctrl);
        Type(editor, 'D', Shift);
        Assert.Equal(11u, editor.curPtr);
        Assert.False(editor.HasSelection());
    }

    [Fact]
    public void CtrlKBStillExtendsWithoutShift()
    {
        TEditor editor = Editor("hello world", 0);
        Press(editor, Keys.kbCtrlK, Ctrl);
        Type(editor, 'b');
        Press(editor, Keys.kbRight);
        Press(editor, Keys.kbRight);
        Assert.Equal("he", Selected(editor));
    }

    // ── words ────────────────────────────────────────────────────────────────

    [Fact]
    public void CtrlLeftAndRightMoveByWord()
    {
        TEditor editor = Editor("alpha beta_1, gamma", 0);

        Press(editor, Keys.kbCtrlRight, Ctrl);
        Assert.Equal(6u, editor.curPtr);
        Press(editor, Keys.kbCtrlRight, Ctrl);
        Assert.Equal(14u, editor.curPtr);
        Press(editor, Keys.kbCtrlLeft, Ctrl);
        Assert.Equal(6u, editor.curPtr);
        Assert.False(editor.HasSelection());
    }

    [Fact]
    public void CtrlShiftLeftAndRightSelectByWord()
    {
        TEditor editor = Editor("alpha beta gamma", 6);

        Press(editor, Keys.kbCtrlRight, Ctrl | Shift);
        Assert.Equal("beta ", Selected(editor));
        Press(editor, Keys.kbCtrlLeft, Ctrl | Shift);
        Assert.False(editor.HasSelection());
        Press(editor, Keys.kbCtrlLeft, Ctrl | Shift);
        Assert.Equal("alpha ", Selected(editor));
        Assert.Equal(0u, editor.curPtr);
    }

    [Fact]
    public void CtrlRightCrossesALineBreakToTheNextWord()
    {
        TEditor editor = Editor("one\n  two", 0);
        Press(editor, Keys.kbCtrlRight, Ctrl);
        Assert.Equal(6u, editor.curPtr);
        Assert.Equal(1, editor.curPos.y);
    }

    [Fact]
    public void CtrlRightAtTheEndOfAFullBufferStaysThere()
    {
        // NextWord used to classify the character at bufLen: the gap, or past the array when the buffer is full.
        var editor = new TEditor(new TRect(0, 0, 40, 6), null, null, null, 5);
        editor.InsertText("alpha");
        Assert.Equal(0u, editor.gapLen);

        Press(editor, Keys.kbCtrlRight, Ctrl);
        Assert.Equal(5u, editor.curPtr);
        Assert.Equal(5u, editor.NextWord(5));
    }

    // ── document ends ────────────────────────────────────────────────────────

    [Fact]
    public void CtrlHomeAndEndReachTheDocumentEnds()
    {
        TEditor editor = Editor("one\ntwo\nthree", 5);

        Press(editor, Keys.kbCtrlEnd, Ctrl);
        Assert.Equal(editor.bufLen, editor.curPtr);
        Assert.Equal(2, editor.curPos.y);

        Press(editor, Keys.kbCtrlHome, Ctrl);
        Assert.Equal(0u, editor.curPtr);
        Assert.Equal(0, editor.curPos.y);
        Assert.False(editor.HasSelection());
    }

    [Fact]
    public void CtrlShiftHomeAndEndSelectToTheDocumentEnds()
    {
        TEditor editor = Editor("one\ntwo\nthree", 5);

        Press(editor, Keys.kbCtrlEnd, Ctrl | Shift);
        Assert.Equal("wo\nthree", Selected(editor));

        Press(editor, Keys.kbCtrlHome, Ctrl | Shift);
        Assert.Equal("one\nt", Selected(editor));
        Assert.Equal(0u, editor.curPtr);
    }

    [Fact]
    public void CtrlPageUpAndDownStillReachTheDocumentEnds()
    {
        TEditor editor = Editor("one\ntwo", 2);
        Press(editor, Keys.kbCtrlPgDn, Ctrl);
        Assert.Equal(editor.bufLen, editor.curPtr);
        Press(editor, Keys.kbCtrlPgUp, Ctrl);
        Assert.Equal(0u, editor.curPtr);
    }

    // ── word deletion ────────────────────────────────────────────────────────

    [Fact]
    public void CtrlBackspaceDeletesTheWordBeforeTheCaret()
    {
        TEditor editor = Editor("alpha beta gamma", 10);
        Press(editor, Keys.kbCtrlBack, Ctrl);
        Assert.Equal("alpha  gamma", Text(editor));
        Assert.Equal(6u, editor.curPtr);
        Assert.True(editor.modified);
    }

    [Fact]
    public void CtrlBackspaceInsideAWordDeletesBackToItsStart()
    {
        TEditor editor = Editor("alpha beta", 8);
        Press(editor, Keys.kbCtrlBack, Ctrl);
        Assert.Equal("alpha ta", Text(editor));
    }

    [Fact]
    public void CtrlDeleteDeletesThroughTheNextWordStart()
    {
        TEditor editor = Editor("alpha beta gamma", 6);
        Press(editor, Keys.kbCtrlDel, Ctrl);
        Assert.Equal("alpha gamma", Text(editor));
        Assert.Equal(6u, editor.curPtr);
    }

    [Fact]
    public void WordDeletionAtTheDocumentEndsChangesNothing()
    {
        TEditor editor = Editor("alpha", 0);
        Press(editor, Keys.kbCtrlBack, Ctrl);
        Assert.Equal("alpha", Text(editor));
        Assert.False(editor.modified);

        editor.SetSelect(5, 5, true);
        Press(editor, Keys.kbCtrlDel, Ctrl);
        Assert.Equal("alpha", Text(editor));
        Assert.False(editor.modified);
    }

    [Fact]
    public void WordDeletionWithASelectionDeletesTheSelection()
    {
        TEditor editor = Editor("alpha beta gamma", 0);
        editor.SetSelect(6, 8, false);
        Press(editor, Keys.kbCtrlBack, Ctrl);
        Assert.Equal("alpha ta gamma", Text(editor));

        editor.SetSelect(0, 2, false);
        Press(editor, Keys.kbCtrlDel, Ctrl);
        Assert.Equal("pha ta gamma", Text(editor));
    }

    [Theory]
    [InlineData(Keys.kbCtrlBack, 10u)]
    [InlineData(Keys.kbCtrlDel, 6u)]
    public void UndoRestoresADeletedWord(ushort key, uint caret)
    {
        TEditor editor = Editor("alpha beta gamma", caret);
        Press(editor, key, Ctrl);
        Assert.NotEqual("alpha beta gamma", Text(editor));

        editor.Undo();
        Assert.Equal("alpha beta gamma", Text(editor));
    }

    [Fact]
    public void CtrlBackspaceAcrossALineBreakKeepsTheLineCount()
    {
        TEditor editor = Editor("one\ntwo", 4);
        Press(editor, Keys.kbCtrlBack, Ctrl);     // back over the LF and the word before it
        Assert.Equal("two", Text(editor));
        Assert.Equal(0, editor.curPos.y);
        Assert.Equal(1, editor.limit.y);
    }

    // ── the insertion anchor ─────────────────────────────────────────────────

    [Fact]
    public void ReplacingAShiftSelectionThatEndsTheDocumentKeepsTheCaretAndDrawAnchor()
    {
        // The InsertBuffer anchor fix: an insertion ending past the old length must leave the caret after it.
        TEditor editor = Editor("one\ntwo", 4);
        Press(editor, Keys.kbCtrlEnd, Ctrl | Shift);
        editor.InsertText("2\n3\nfour");

        Assert.Equal("one\n2\n3\nfour", Text(editor));
        Assert.Equal(editor.bufLen, editor.curPtr);
        Assert.Equal(3, editor.curPos.y);
        Assert.Equal(4, editor.curPos.x);
        Assert.Equal(editor.LineStart(editor.curPtr), editor.drawPtr);
        Assert.Equal(editor.curPos.y, editor.drawLine);
    }

    // ── CUA clipboard keys ───────────────────────────────────────────────────

    [Fact]
    public void TheCuaClipboardKeysAreTheClipboardCommands()
    {
        var clipboard = new TEditor(new TRect(0, 0, 40, 6), null, null, null, 1024);
        TEditor.clipboard = clipboard;
        IClipboardService saved = ClipboardService.Current;
        ClipboardService.Current = new InMemoryClipboardService();
        try
        {
            TEditor editor = Editor("hello world", 0);
            editor.SetState(Views.sfActive, true);
            for (int i = 0; i < 5; i++) Press(editor, Keys.kbRight, Shift);

            Press(editor, Keys.kbCtrlIns, Ctrl);
            Assert.True(ClipboardService.Current.TryGetText(out string copied));
            Assert.Equal("hello", copied);

            Press(editor, Keys.kbShiftDel, Shift);
            Assert.Equal(" world", Text(editor));

            Press(editor, Keys.kbCtrlEnd, Ctrl);
            Press(editor, Keys.kbShiftIns, Shift);
            Assert.Equal(" worldhello", Text(editor));
        }
        finally
        {
            ClipboardService.Current = saved;
            TEditor.clipboard = null;
        }
    }
}
