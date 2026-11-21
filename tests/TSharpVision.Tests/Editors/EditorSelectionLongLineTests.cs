using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

/// <summary>Select All, double-click word selection and long lines without the 256-column ceiling (TV13c).</summary>
[Collection("NonParallel")]
public sealed class EditorSelectionLongLineTests : IDisposable
{
    private const int Width = 40;
    private const int Height = 5;

    private readonly DriverScope _driver = new(80, 25);
    private readonly EditorClipboardScope _clipboard = new();
    private readonly uint _tabSize = TEditor.tabSize;
    private readonly TestGroup _host;

    public EditorSelectionLongLineTests()
    {
        TEventQueue.Resume();
        DrainQueue();
        _host = new TestGroup(new TRect(0, 0, Width, Height)) { buffer = new ScreenBuffer(Width * Height) };
        _host.state |= (ushort)(Views.sfVisible | Views.sfExposed);
    }

    // Mouse tests queue their move and release events; none may leak into another test.
    private static void DrainQueue()
    {
        var ev = new TEvent();
        do { TEventQueue.GetNextEvent(ref ev); } while (ev.What != Events.evNothing);
    }

    public void Dispose()
    {
        DrainQueue();
        TEditor.tabSize = _tabSize;
        _clipboard.Dispose();
        _driver.Dispose();
    }

    private TEditor Load(string text, uint caret = 0)
    {
        var editor = new TEditor(new TRect(0, 0, Width, Height), null, null, null, (uint)Math.Max(4096, text.Length + 4096));
        _host.Insert(editor);
        editor.Select();                                               // a click on an unfocused view only focuses it
        editor.InsertText(text);
        editor.SetCurPtr(caret, 0);
        editor.ClearUndoHistory();
        editor.MarkUnmodified();
        return editor;
    }

    private static string Text(TEditor editor)
    {
        var builder = new StringBuilder();
        for (uint p = 0; p < editor.bufLen; p++) builder.Append(editor.BufChar(p));
        return builder.ToString();
    }

    private static void Command(TEditor editor, ushort command)
    {
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = command;
        editor.HandleEvent(ref ev);
    }

    private static void Key(TEditor editor, ushort keyCode)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        editor.HandleEvent(ref ev);
    }

    private static void Type(TEditor editor, string text)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.text = text;
        editor.HandleEvent(ref ev);
    }

    private string Row(int y)
    {
        var line = new StringBuilder(Width);
        for (int x = 0; x < Width; x++)
        {
            char c = _host.buffer!.Data[y * Width + x].Character;
            line.Append(c == '\0' ? ' ' : c);
        }
        return line.ToString();
    }

    // Presses at a column of row 0 (optionally as a double click), then moves to further columns and releases.
    private static void Mouse(TEditor editor, int x, bool doubleClick, params int[] dragTo)
    {
        foreach (int to in dragTo)
        {
            var move = new TEvent { What = Events.evMouseMove };
            move.mouse.where = new TPoint(to, 0);
            move.mouse.buttons = 1;
            TEventQueue.Enqueue(move);
        }
        var up = new TEvent { What = Events.evMouseUp };
        up.mouse.where = new TPoint(dragTo.Length > 0 ? dragTo[^1] : x, 0);
        TEventQueue.Enqueue(up);
        var down = new TEvent { What = Events.evMouseDown };
        down.mouse.where = new TPoint(x, 0);
        down.mouse.buttons = 1;
        down.mouse.doubleClick = doubleClick;
        editor.HandleEvent(ref down);
    }

    // ── Select All ───────────────────────────────────────────────────────────

    [Fact]
    public void SelectAllSelectsTheWholeTextWithTheCaretAtTheEnd()
    {
        TEditor editor = Load("ab\ncd", caret: 1);
        editor.SelectAll();
        Assert.Equal((0u, 5u, 5u), (editor.selStart, editor.selEnd, editor.curPtr));
        Assert.False(editor.modified);
        Assert.False(editor.HasUndo);                                  // selecting is not an edit
    }

    [Fact]
    public void CtrlAAndCmSelectAllSelectEverything()
    {
        TEditor editor = Load("hello");
        Key(editor, Keys.kbCtrlA);
        Assert.Equal((0u, 5u), (editor.selStart, editor.selEnd));
        editor.SetCurPtr(0, 0);
        Command(editor, Views.cmSelectAll);
        Assert.Equal((0u, 5u), (editor.selStart, editor.selEnd));
    }

    [Fact]
    public void SelectAllOnAnEmptyDocumentSelectsNothing()
    {
        TEditor editor = Load("");
        editor.SelectAll();
        Assert.False(editor.HasSelection());
    }

    [Fact]
    public void TypingDeletingCuttingAndCopyingAfterSelectAll()
    {
        var clip = new TEditor(new TRect(0, 0, 10, 2), null, null, null, 1024);
        TEditor.clipboard = clip;
        TEditor editor = Load("one\ntwo");

        editor.SelectAll();
        Command(editor, Views.cmCopy);
        Assert.Equal("one\ntwo", Text(clip));
        Assert.Equal("one\ntwo", Text(editor));

        editor.SelectAll();
        Type(editor, "x");
        Assert.Equal("x", Text(editor));
        editor.Undo();
        Assert.Equal("one\ntwo", Text(editor));

        editor.SelectAll();
        Command(editor, Views.cmClear);
        Assert.Equal("", Text(editor));
        editor.Undo();

        editor.SelectAll();
        Command(editor, Views.cmCut);
        Assert.Equal("", Text(editor));
        Assert.Equal(1, editor.limit.y);
        Command(editor, Views.cmPaste);
        Assert.Equal("one\ntwo", Text(editor));
    }

    [Fact]
    public void SelectAllCommandIsEnabledForTheActiveEditorAndInTheEditMenu()
    {
        var commands = new TCommandSet();
        TView.GetCommands(commands);
        try
        {
            TView.DisableCommand(Views.cmSelectAll);
            TEditor editor = Load("x");
            editor.SetState(Views.sfActive, true);
            Assert.True(TView.CommandEnabled(Views.cmSelectAll));
            editor.SetState(Views.sfActive, false);
            Assert.False(TView.CommandEnabled(Views.cmSelectAll));
        }
        finally { TView.SetCommands(commands); }

        var app = new TEditorApp();
        try
        {
            Assert.False(TView.CommandEnabled(Views.cmSelectAll));
            bool found = false;
            for (var sub = Assert.IsType<TMenu>(app.MenuBar!.Menu).Items; sub != null; sub = sub.Next)
                for (var item = sub.SubMenu?.Items; item != null; item = item.Next)
                    if (item.Command == Views.cmSelectAll) { found = true; Assert.Equal("Ctrl+A", item.Param); }
            Assert.True(found);
        }
        finally
        {
            app.ShutDown();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    // ── double click ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(5, 4u, 9u)]       // inside "brown"
    [InlineData(4, 4u, 9u)]       // its first letter
    [InlineData(9, 9u, 10u)]      // the space after it: the one character
    [InlineData(19, 15u, 20u)]    // the last letter of the text
    [InlineData(13, 13u, 14u)]    // a comma: the one character
    public void DoubleClickSelectsTheWord(int column, uint start, uint end)
    {
        TEditor editor = Load("the brown fox, jumps");
        Mouse(editor, column, doubleClick: true);
        Assert.Equal((start, end), (editor.selStart, editor.selEnd));
    }

    [Fact]
    public void DoubleClickPastTheLineEnd()
    {
        TEditor editor = Load("a  \nb");
        Mouse(editor, 30, doubleClick: true);                          // past the end of "a  ": nothing
        Assert.False(editor.HasSelection());
        TEditor word = Load("end\nb");
        Mouse(word, 30, doubleClick: true);                            // past the end of "end": that word
        Assert.Equal((0u, 3u), (word.selStart, word.selEnd));
    }

    [Fact]
    public void DraggingAfterADoubleClickExtendsByWholeWords()
    {
        TEditor editor = Load("alpha beta gamma delta");
        Mouse(editor, 7, doubleClick: true, 12);                       // from "beta" into "gamma"
        Assert.Equal((6u, 16u), (editor.selStart, editor.selEnd));
        Mouse(editor, 12, doubleClick: true, 1);                       // from "gamma" back into "alpha"
        Assert.Equal((0u, 16u), (editor.selStart, editor.selEnd));
        Assert.Equal(0u, editor.curPtr);
    }

    [Fact]
    public void SingleClickAndDragStillSelectByCharacter()
    {
        TEditor editor = Load("alpha beta");
        Mouse(editor, 2, doubleClick: false, 7);
        Assert.Equal((2u, 7u), (editor.selStart, editor.selEnd));
        Mouse(editor, 4, doubleClick: false);
        Assert.False(editor.HasSelection());
        Assert.Equal(4u, editor.curPtr);
    }

    // ── long lines ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(300)]
    [InlineData(100_000)]
    public void TheCaretAtTheEndOfALongLineScrollsIntoView(int length)
    {
        string line = new string('x', length - 1) + "Z";
        TEditor editor = Load(line + "\nshort");
        Assert.Equal(length + 1, editor.limit.x);
        Command(editor, Views.cmLineEnd);
        Assert.Equal(length, editor.curPos.x);
        Assert.True(editor.CursorVisible());
        Assert.Equal(length - Width + 1, editor.delta.x);
        editor.Draw();
        Assert.Equal(new string('x', Width - 2) + "Z ", Row(0));
        Assert.Equal(new string(' ', Width), Row(1));                // "short" is scrolled out of view
    }

    [Fact]
    public void ShortTextKeepsTheHistoricExtent()
    {
        TEditor editor = Load("abc");
        Assert.Equal(Views.maxLineLength, editor.limit.x);
    }

    [Fact]
    public void TheExtentGrowsWithTypingAndPasteAndIsRecomputedOnLoad()
    {
        TEditor editor = Load("");
        editor.InsertText(new string('a', 500));
        Assert.Equal(501, editor.limit.x);
        editor.InsertText("\n" + new string('b', 900));
        Assert.Equal(901, editor.limit.x);
        editor.SetSelect(0, editor.bufLen, false);
        editor.DeleteSelect();
        Assert.Equal(901, editor.limit.x);                             // shrinking is deferred to the next load
        editor.SetBufLen(0);
        Assert.Equal(Views.maxLineLength, editor.limit.x);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(8u)]
    public void TabsCountTowardsTheExtentAndDrawAtTheirStops(uint tabSize)
    {
        TEditor.tabSize = tabSize;
        string line = string.Concat(Enumerable.Repeat("\tab", 200));
        TEditor editor = Load(line);
        int columns = editor.CharPos(0, (uint)line.Length);
        Assert.Equal(Math.Max(Views.maxLineLength, columns + 1), editor.limit.x);
        editor.ScrollTo(columns - 10, 0);
        editor.Draw();
        string row = Row(0);
        for (uint p = 0; p < line.Length; p++)
        {
            int column = editor.CharPos(0, p) - editor.delta.x;
            if (line[(int)p] != '\t' && column >= 0 && column < Width) Assert.Equal(line[(int)p], row[column]);
        }
    }

    [Fact]
    public void SelectionIsDrawnInsideAFarScrolledWindow()
    {
        TEditor editor = Load(new string('q', 1000));
        editor.SetSelect(500, 510, false);
        editor.ScrollTo(495, 0);
        editor.Draw();
        byte normal = (byte)_host.buffer!.Data[0].Attr;
        byte selected = (byte)_host.buffer!.Data[5].Attr;
        Assert.NotEqual(normal, selected);
        Assert.Equal(selected, (byte)_host.buffer!.Data[14].Attr);
        Assert.Equal(normal, (byte)_host.buffer!.Data[15].Attr);
    }

    [Fact]
    public void AOneMebicharacterLineDrawsFarRightWithoutProportionalAllocation()
    {
        const int length = 1 << 20;
        TEditor editor = Load(new string('m', length - 1) + "E");
        editor.SetCurPtr((uint)length, 0);
        editor.TrackCursor(false);
        editor.Draw();                                                 // warm up
        long before = GC.GetAllocatedBytesForCurrentThread();
        editor.Draw();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 64 * 1024, $"Draw allocated {allocated} bytes");
        Assert.Equal('E', Row(0)[Width - 2]);
    }

    [Fact]
    public void MouseAutoScrollPassesColumn256()
    {
        TEditor editor = Load(new string('w', 400));
        editor.ScrollTo(300, 0);
        Assert.Equal(300, editor.delta.x);
        Mouse(editor, 10, doubleClick: false);
        Assert.Equal(310u, editor.curPtr);
    }

    [Fact]
    public void UndoOfALongLineEditRestoresTheText()
    {
        TEditor editor = Load(new string('k', 5000), caret: 4000);
        editor.TrackCursor(false);
        Type(editor, "NEW");
        editor.Undo();
        Assert.Equal(new string('k', 5000), Text(editor));
        Assert.True(editor.CursorVisible() || editor.selEnd == 4000);
    }
}
