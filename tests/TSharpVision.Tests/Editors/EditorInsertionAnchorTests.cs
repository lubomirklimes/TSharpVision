using System;
using System.Linq;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

/// <summary>
/// After every successful insertion, <see cref="TEditor"/>'s caret and drawing state must describe the new buffer:
/// <c>curPos</c> is the caret's (column, line), <c>drawPtr</c> is the start of line <c>drawLine</c>, the hardware cursor
/// sits at <c>curPos - delta</c>, and <c>Draw</c> paints line <c>delta.y + row</c> on each row.
/// </summary>
/// <remarks>
/// Every document is built through the public API (<c>InsertText</c>, then <c>SetCurPtr</c>, which recomputes the caret
/// against the committed buffer), and the baseline is checked before the operation under test, so a failure is the
/// operation's own. The expected values come from the text itself, never from TEditor's helpers.
/// </remarks>
[Collection("NonParallel")]
public sealed class EditorInsertionAnchorTests : IDisposable
{
    private const int Width = 20;
    private const int Height = 4;

    private readonly DriverScope _driver;
    private readonly TestGroup _host;

    public EditorInsertionAnchorTests()
    {
        _driver = new DriverScope();
        TEventQueue.Resume();
        _host = new TestGroup(new TRect(0, 0, 80, 25));
        _host.buffer = new ScreenBuffer(80 * 25 * ScreenBuffer.GetSize());
        _host.state |= (ushort)(Views.sfVisible | Views.sfExposed);
    }

    public void Dispose() => _driver.Dispose();

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>A 20×4 editor holding <paramref name="text"/>, caret at <paramref name="caret"/> (default: the end).</summary>
    private TEditor Load(string text, int? caret = null)
    {
        var editor = new TEditor(new TRect(0, 0, Width, Height), null, null, null, 4096);
        _host.Insert(editor);
        editor.InsertText(text);
        editor.SetCurPtr(0, 0);
        editor.SetCurPtr((uint)(caret ?? text.Length), 0);
        editor.TrackCursor(false);
        editor.delCount = 0;                                         // loading is not an edit to undo
        editor.insCount = 0;
        AssertConsistent(editor, text, caret ?? text.Length, "baseline");
        return editor;
    }

    private static string Text(TEditor editor)
    {
        var builder = new StringBuilder();
        for (uint p = 0; p < editor.bufLen; p++) builder.Append(editor.BufChar(p));
        return builder.ToString();
    }

    private static int LineOf(string text, int offset) => text.Take(offset).Count(c => c == '\n');

    private static int ColumnOf(string text, int offset) => offset - (text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1);

    private static bool IsLineStart(string text, int offset) => offset == 0 || text[offset - 1] == '\n';

    private string Row(int y)
    {
        var chars = new char[Width];
        for (int x = 0; x < Width; x++) chars[x] = _host.buffer!.Data[(y * 80) + x].Character;
        return new string(chars).TrimEnd();
    }

    /// <summary>The editor state after an operation, checked field by field against the text, then as drawn.</summary>
    private void AssertConsistent(TEditor editor, string text, int caret, string when, bool selected = false)
    {
        Assert.Equal(text, Text(editor));
        Assert.True((uint)text.Length == editor.bufLen, $"{when}: bufLen {editor.bufLen}, expected {text.Length}");
        Assert.True(editor.bufSize - editor.bufLen == editor.gapLen, $"{when}: gapLen {editor.gapLen}");
        Assert.True((uint)caret == editor.curPtr, $"{when}: curPtr {editor.curPtr}, expected {caret}");

        var expected = new TPoint(ColumnOf(text, caret), LineOf(text, caret));
        Assert.True(expected == editor.curPos,
            $"{when}: curPos ({editor.curPos.x},{editor.curPos.y}), expected ({expected.x},{expected.y})");

        // drawPtr is a (line, offset) anchor: it must be the start of line drawLine.
        int drawPtr = (int)editor.drawPtr;
        Assert.True(drawPtr <= text.Length && IsLineStart(text, drawPtr) && LineOf(text, drawPtr) == editor.drawLine,
            $"{when}: drawPtr {drawPtr} is not the start of drawLine {editor.drawLine}");

        if (!selected)
            Assert.True(editor.selStart == editor.curPtr && editor.selEnd == editor.curPtr, $"{when}: selection {editor.selStart}..{editor.selEnd}");

        // The caret is inside the viewport and the hardware cursor is on it.
        Assert.True(editor.CursorVisible(), $"{when}: caret ({editor.curPos.x},{editor.curPos.y}) outside the viewport at ({editor.delta.x},{editor.delta.y})");
        Assert.True(new TPoint(editor.curPos.x - editor.delta.x, editor.curPos.y - editor.delta.y) == editor.cursor,
            $"{when}: cursor ({editor.cursor.x},{editor.cursor.y})");

        // What Draw paints starts from drawPtr: every row must be line delta.y + row.
        editor.Draw();
        string[] lines = text.Split('\n');
        for (int y = 0; y < Height; y++)
        {
            int line = editor.delta.y + y;
            string expectedRow = line < lines.Length ? lines[line] : string.Empty;
            expectedRow = expectedRow.Length > editor.delta.x ? expectedRow[editor.delta.x..] : string.Empty;
            if (expectedRow.Length > Width) expectedRow = expectedRow[..Width];
            Assert.True(expectedRow == Row(y), $"{when}: row {y} is '{Row(y)}', expected '{expectedRow}'");
        }
    }

    private static void Type(TEditor editor, char c)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.charScan.charCode = (byte)c;
        ev.keyDown.text = c.ToString();
        editor.HandleEvent(ref ev);
    }

    private static void Press(TEditor editor, ushort keyCode)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        editor.HandleEvent(ref ev);
    }

    // ── A–F: insertion ───────────────────────────────────────────────────────

    [Fact]
    public void A_OneCharacterAtTheEndOfANonEmptyDocument()
    {
        TEditor editor = Load("abc");
        editor.InsertText("X");
        AssertConsistent(editor, "abcX", 4, "X at EOF");
    }

    [Fact]
    public void B_ANewLineAtTheEndOfTheDocument()
    {
        TEditor editor = Load("abc");
        editor.NewLine();
        AssertConsistent(editor, "abc\n", 4, "newline at EOF");      // line 1, column 0
    }

    [Fact]
    public void C_TextAfterANewLineAtTheEndOfTheDocument()
    {
        TEditor editor = Load("abc\n");
        editor.InsertText("X");
        AssertConsistent(editor, "abc\nX", 5, "X after the final newline");
    }

    [Fact]
    public void D_TheFirstCharacterOfAnEmptyDocument()
    {
        TEditor editor = Load("");
        editor.InsertText("X");
        AssertConsistent(editor, "X", 1, "first character");
    }

    [Fact]
    public void E_SeveralCharactersAndLinesAtOnceAtTheEnd()
    {
        TEditor editor = Load("abc");
        editor.InsertText("XY\nZW");
        AssertConsistent(editor, "abcXY\nZW", 8, "multi-line at EOF");
    }

    [Fact]
    public void F_InsertionInTheMiddleIsUnchanged()
    {
        TEditor editor = Load("hello\nworld", caret: 2);
        editor.InsertText("XY");
        AssertConsistent(editor, "heXYllo\nworld", 4, "middle, line 0");
    }

    /// <summary>
    /// Not only EOF: any insertion whose new caret passes the <em>old</em> document length. Two characters before the
    /// end, four inserted.
    /// </summary>
    [Fact]
    public void F2_InsertionNearTheEndLongerThanTheRestOfTheDocument()
    {
        TEditor editor = Load("abc", caret: 1);
        editor.InsertText("WXYZ");
        AssertConsistent(editor, "aWXYZbc", 5, "near the end");
    }

    // ── through the key path ─────────────────────────────────────────────────

    [Fact]
    public void TypingEnterAndTypingAgainAtTheEndThroughHandleEvent()
    {
        TEditor editor = Load("abc");
        Type(editor, 'x');
        AssertConsistent(editor, "abcx", 4, "typed x");
        Press(editor, Keys.kbEnter);
        AssertConsistent(editor, "abcx\n", 5, "Enter");
        Type(editor, 'y');
        AssertConsistent(editor, "abcx\ny", 6, "typed y");

        Press(editor, Keys.kbUp);                                    // movement starts from the new state
        AssertConsistent(editor, "abcx\ny", 1, "Up");
    }

    // ── selection replacement ────────────────────────────────────────────────

    [Fact]
    public void ReplacingASelectionThatEndsTheDocument()
    {
        TEditor editor = Load("hello world");
        editor.SetSelect(6, 11, curStart: false);
        editor.InsertText("X");
        AssertConsistent(editor, "hello X", 7, "replaced 'world'");

        editor.Undo();
        AssertConsistent(editor, "hello world", 11, "undo", selected: true);
        Assert.Equal((6u, 11u), (editor.selStart, editor.selEnd));  // the restored text, selected
        Assert.Equal((0u, 0u), (editor.delCount, editor.insCount));   // single-level: nothing left to undo
    }

    [Fact]
    public void ReplacingAMultiLineSelectionWithLongerText()
    {
        TEditor editor = Load("ab\ncd\nef");
        editor.SetSelect(1, 4, curStart: false);                       // "b\nc"
        editor.InsertText("1\n2\n3\n4");
        AssertConsistent(editor, "a1\n2\n3\n4d\nef", 8, "replaced across lines");
    }

    // ── undo ─────────────────────────────────────────────────────────────────

    [Fact]
    public void UndoOfAnInsertionAtTheEnd()
    {
        TEditor editor = Load("abc");
        editor.InsertText("XYZ");
        editor.Undo();
        AssertConsistent(editor, "abc", 3, "undo");
        editor.Undo();                                                 // single level: a second undo changes nothing
        AssertConsistent(editor, "abc", 3, "second undo");
    }

    /// <summary>Undo re-inserts deleted text through the same insertion path: at the end, it is an EOF insertion.</summary>
    [Fact]
    public void UndoOfADeletionAtTheEndReinsertsWithAConsistentCaret()
    {
        TEditor editor = Load("abc\nde");
        Press(editor, Keys.kbBack);
        Press(editor, Keys.kbBack);
        Press(editor, Keys.kbBack);                                    // the "de" and the line break
        AssertConsistent(editor, "abc", 3, "deleted");
        editor.Undo();
        AssertConsistent(editor, "abc\nde", 6, "undo", selected: true);
    }

    // ── deletion (audit: already consistent) ─────────────────────────────────

    [Fact]
    public void DeletionAtTheEndAndOfASelectionStayConsistent()
    {
        TEditor editor = Load("abc\nde");
        Press(editor, Keys.kbBack);
        AssertConsistent(editor, "abc\nd", 5, "backspace at EOF");
        editor.SetSelect(2, 5, curStart: true);
        editor.DeleteSelect();
        AssertConsistent(editor, "ab", 2, "selection deleted");
        Press(editor, Keys.kbHome);
        Press(editor, Keys.kbDel);
        AssertConsistent(editor, "b", 0, "delete at start");
    }

    // ── the viewport ─────────────────────────────────────────────────────────

    private static string Lines(int count) => string.Join('\n', Enumerable.Range(0, count).Select(i => $"line {i}"));

    [Fact]
    public void TypingOnTheBottomRowOfTheViewportDoesNotScroll()
    {
        string text = Lines(10);
        TEditor editor = Load(text, caret: text.IndexOf('\n', text.IndexOf("line 3", StringComparison.Ordinal)));
        Assert.Equal(0, editor.delta.y);
        Type(editor, 'X');
        AssertConsistent(editor, text.Replace("line 3", "line 3X"), text.IndexOf("line 3", StringComparison.Ordinal) + 7, "bottom row");
        Assert.Equal(0, editor.delta.y);
    }

    [Fact]
    public void ANewLineOnTheBottomRowScrollsOneLine()
    {
        string text = Lines(10);
        int caret = text.IndexOf('\n', text.IndexOf("line 3", StringComparison.Ordinal));
        TEditor editor = Load(text, caret);
        Press(editor, Keys.kbEnter);
        Assert.Equal(1, editor.delta.y);
        AssertConsistent(editor, text.Insert(caret, "\n"), caret + 1, "scrolled by the new line");
    }

    [Fact]
    public void NewLinesAndTypingAtTheEndOfAScrolledDocument()
    {
        string text = Lines(10);
        TEditor editor = Load(text);
        Assert.Equal(6, editor.delta.y);                               // lines 6–9 visible, caret at the end of line 9

        Press(editor, Keys.kbEnter);
        AssertConsistent(editor, text + "\n", text.Length + 1, "new last line");
        Assert.Equal(7, editor.delta.y);

        Type(editor, 'y');
        AssertConsistent(editor, text + "\ny", text.Length + 2, "typed on the new last line");

        Press(editor, Keys.kbEnter);
        Press(editor, Keys.kbEnter);
        AssertConsistent(editor, text + "\ny\n\n", text.Length + 4, "two more lines");
        Assert.Equal(9, editor.delta.y);
    }
}
