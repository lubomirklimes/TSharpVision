using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

/// <summary>Multi-level Undo/Redo: grouping, redo invalidation, saved position, limits and command wiring (TV13b).</summary>
[Collection("NonParallel")]
public sealed class EditorUndoRedoTests : IDisposable
{
    private readonly EditorClipboardScope _clipboard = new();

    public void Dispose() => _clipboard.Dispose();

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>An editor holding <paramref name="text"/> as loaded, unmodified text with an empty history.</summary>
    private static TEditor Load(string text, uint? caret = null, uint bufSize = 4096)
    {
        var editor = new TEditor(new TRect(0, 0, 40, 10), null, null, null, bufSize);
        editor.InsertText(text);
        editor.SetCurPtr(caret ?? (uint)text.Length, 0);
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

    private static void Type(TEditor editor, string text)
    {
        foreach (char c in text)
        {
            var ev = new TEvent { What = Events.evKeyDown };
            ev.keyDown.text = c.ToString();
            editor.HandleEvent(ref ev);
        }
    }

    private static void Command(TEditor editor, ushort command)
    {
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = command;
        editor.HandleEvent(ref ev);
    }

    private static void Key(TEditor editor, ushort keyCode, uint controlKeyState = 0)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        ev.keyDown.controlKeyState = (ushort)controlKeyState;
        editor.HandleEvent(ref ev);
    }

    private static int UndoAll(TEditor editor)
    {
        int count = 0;
        while (editor.HasUndo) { editor.Undo(); count++; }
        return count;
    }

    // ── grouping ─────────────────────────────────────────────────────────────

    [Fact]
    public void ContiguousTypingIsOneGroupAndRedoRestoresIt()
    {
        TEditor editor = Load("");
        Type(editor, "abc");
        Assert.Equal((0u, 3u), (editor.delCount, editor.insCount));
        editor.Undo();
        Assert.Equal("", Text(editor));
        Assert.False(editor.HasUndo);
        Assert.True(editor.HasRedo);
        editor.Redo();
        Assert.Equal("abc", Text(editor));
        Assert.Equal(3u, editor.curPtr);
        Assert.False(editor.HasRedo);
    }

    [Fact]
    public void CaretMovementClosesTheGroupButKeepsTheHistory()
    {
        TEditor editor = Load("");
        Type(editor, "ab");
        Command(editor, Views.cmCharLeft);
        Assert.Equal((0u, 0u), (editor.delCount, editor.insCount));
        Type(editor, "x");
        Assert.Equal("axb", Text(editor));
        editor.Undo();
        Assert.Equal("ab", Text(editor));
        editor.Undo();
        Assert.Equal("", Text(editor));
        Assert.Equal(2, CountRedo(editor));
    }

    private static int CountRedo(TEditor editor)
    {
        int count = 0;
        while (editor.HasRedo) { editor.Redo(); count++; }
        return count;
    }

    [Fact]
    public void BackspaceRunIsOneGroupAndUndoSelectsTheRestoredText()
    {
        TEditor editor = Load("hello");
        Command(editor, Views.cmBackSpace);
        Command(editor, Views.cmBackSpace);
        Command(editor, Views.cmBackSpace);
        Assert.Equal("he", Text(editor));
        Assert.Equal((3u, 0u), (editor.delCount, editor.insCount));
        editor.Undo();
        Assert.Equal("hello", Text(editor));
        Assert.Equal((2u, 5u), (editor.selStart, editor.selEnd));
        Assert.False(editor.HasUndo);
    }

    [Fact]
    public void DeleteRunIsOneGroup()
    {
        TEditor editor = Load("hello", caret: 0);
        Command(editor, Views.cmDelChar);
        Command(editor, Views.cmDelChar);
        Assert.Equal("llo", Text(editor));
        editor.Undo();
        Assert.Equal("hello", Text(editor));
        Assert.False(editor.HasUndo);
    }

    [Fact]
    public void BackspaceAfterTypingStartsANewGroup()
    {
        TEditor editor = Load("");
        Type(editor, "abc");
        Command(editor, Views.cmBackSpace);
        editor.Undo();
        Assert.Equal("abc", Text(editor));
        editor.Undo();
        Assert.Equal("", Text(editor));
    }

    [Fact]
    public void NewLineIsADistinctGroupIncludingAutoIndent()
    {
        TEditor editor = Load("  ab");
        editor.autoIndent = true;
        Command(editor, Views.cmNewLine);
        Type(editor, "c");
        Assert.Equal("  ab\n  c", Text(editor));
        editor.Undo();
        Assert.Equal("  ab\n  ", Text(editor));
        editor.Undo();
        Assert.Equal("  ab", Text(editor));
        Assert.False(editor.HasUndo);
    }

    [Fact]
    public void PasteAndCutAreDistinctGroups()
    {
        var clip = new TEditor(new TRect(0, 0, 10, 2), null, null, null, 1024);
        TEditor.clipboard = clip;
        TEditor editor = Load("hello");
        Type(editor, "!");
        editor.SetSelect(0, 2, false);
        Command(editor, Views.cmCut);
        Assert.Equal("llo!", Text(editor));
        editor.SetCurPtr(4, 0);
        Command(editor, Views.cmPaste);
        Assert.Equal("llo!he", Text(editor));
        Assert.False(clip.HasUndo);                                   // the clipboard editor keeps no history

        editor.Undo();
        Assert.Equal("llo!", Text(editor));
        editor.Undo();
        Assert.Equal("hello!", Text(editor));
        editor.Undo();
        Assert.Equal("hello", Text(editor));
    }

    [Fact]
    public void ProgrammaticInsertTextIsItsOwnGroup()
    {
        TEditor editor = Load("");
        editor.InsertText("a");
        editor.InsertText("b");
        editor.Undo();
        Assert.Equal("a", Text(editor));
    }

    [Fact]
    public void ReplaceAllIsOneGroup()
    {
        TEditor editor = Load("cat cat cat", caret: 0);
        "cat".CopyTo(0, TEditor.findStr, 0, 3);
        "dog".CopyTo(0, TEditor.replaceStr, 0, 3);
        TEditor.editorFlags = (ushort)(Views.efDoReplace | Views.efReplaceAll | Views.efCaseSensitive);
        Assert.Equal(0, TEditor.editorFlags & Views.efPromptOnReplace);
        editor.DoSearchReplace();
        Assert.Equal("dog dog dog", Text(editor));
        editor.Undo();
        Assert.Equal("cat cat cat", Text(editor));
        Assert.False(editor.HasUndo);
        editor.Redo();
        Assert.Equal("dog dog dog", Text(editor));
    }

    [Fact]
    public void TypingOverASelectionUndoesToTheSelectedText()
    {
        TEditor editor = Load("hello world");
        editor.SetSelect(6, 11, false);
        Type(editor, "you");
        Assert.Equal("hello you", Text(editor));
        editor.Undo();
        Assert.Equal("hello world", Text(editor));
        Assert.Equal((6u, 11u), (editor.selStart, editor.selEnd));
    }

    [Fact]
    public void OverwriteTypingIsOneGroup()
    {
        TEditor editor = Load("abcd", caret: 0);
        editor.overwrite = true;
        Type(editor, "xy");
        Assert.Equal("xycd", Text(editor));
        editor.Undo();
        Assert.Equal("abcd", Text(editor));
        Assert.False(editor.HasUndo);
    }

    [Fact]
    public void AFreshEditDiscardsRedo()
    {
        TEditor editor = Load("");
        Type(editor, "ab");
        editor.Undo();
        Assert.True(editor.HasRedo);
        Type(editor, "z");
        Assert.False(editor.HasRedo);
        editor.Redo();
        Assert.Equal("z", Text(editor));
    }

    // ── every caret and gap position, including the end ──────────────────────

    [Fact]
    public void UndoAndRedoRoundTripAtEveryPosition()
    {
        const string text = "ab\ncd\n";
        for (uint p = 0; p <= text.Length; p++)
        {
            TEditor editor = Load(text, caret: p);
            Type(editor, "XY");
            Command(editor, Views.cmCharRight);                        // closes the group, moves the gap
            Command(editor, Views.cmBackSpace);
            string edited = Text(editor);
            editor.Undo();
            editor.Undo();
            Assert.Equal(text, Text(editor));
            Assert.Equal(p, editor.curPtr);
            Assert.Equal(text.Count(c => c == '\n') + 1, editor.limit.y);
            editor.Redo();
            editor.Redo();
            Assert.Equal(edited, Text(editor));
        }
    }

    // ── saved position and modified ──────────────────────────────────────────

    [Fact]
    public void UndoAndRedoAcrossTheSavedPositionTrackModified()
    {
        TEditor editor = Load("");
        Type(editor, "a");
        Command(editor, Views.cmCharLeft);
        editor.MarkUnmodified();                                       // as a successful save does
        Assert.False(editor.modified);
        Type(editor, "b");
        Assert.True(editor.modified);
        editor.Undo();
        Assert.False(editor.modified);
        editor.Undo();
        Assert.True(editor.modified);
        editor.Redo();
        Assert.False(editor.modified);
        editor.Redo();
        Assert.True(editor.modified);
    }

    [Fact]
    public void LoadedTextIsTheSavedPosition()
    {
        TEditor editor = Load("x");
        Type(editor, "y");
        editor.Undo();
        Assert.False(editor.modified);
    }

    [Fact]
    public void SavedPositionDiscardedByANewBranchStaysModified()
    {
        TEditor editor = Load("");
        Type(editor, "a");
        editor.MarkUnmodified();
        editor.Undo();
        Type(editor, "b");                                             // the saved text "a" is no longer reachable
        editor.Undo();
        Assert.True(editor.modified);
    }

    [Fact]
    public void ExplicitlyMarkedModifiedSurvivesUndo()
    {
        TEditor editor = Load("x");
        editor.modified = true;                                        // e.g. a change outside the text
        Type(editor, "y");
        editor.Undo();
        Assert.True(editor.modified);
        editor.MarkUnmodified();
        Type(editor, "z");
        editor.Undo();
        Assert.False(editor.modified);
    }

    [Fact]
    public void ClearUndoHistoryKeepsModified()
    {
        TEditor editor = Load("");
        Type(editor, "a");
        editor.ClearUndoHistory();
        Assert.False(editor.HasUndo);
        Assert.True(editor.modified);
    }

    // ── limits ───────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultLimits()
    {
        TEditor editor = Load("");
        Assert.Equal(1000, editor.UndoGroupLimit);
        Assert.Equal(4 * 1024 * 1024, editor.UndoTextLimit);
        Assert.Equal(TEditor.DefaultUndoGroupLimit, editor.UndoGroupLimit);
        Assert.Equal(TEditor.DefaultUndoTextLimit, editor.UndoTextLimit);
    }

    [Fact]
    public void GroupLimitDropsTheOldestGroups()
    {
        TEditor editor = Load("");
        editor.UndoGroupLimit = 3;
        foreach (string s in new[] { "1", "2", "3", "4", "5" }) editor.InsertText(s);
        Assert.Equal(3, UndoAll(editor));
        Assert.Equal("12", Text(editor));
        Assert.True(editor.modified);                                  // the loaded text can no longer be reached
    }

    [Fact]
    public void LoweringTheGroupLimitTrimsExistingHistory()
    {
        TEditor editor = Load("");
        foreach (string s in new[] { "1", "2", "3", "4" }) editor.InsertText(s);
        editor.UndoGroupLimit = 2;
        Assert.Equal(2, UndoAll(editor));
        Assert.Equal("12", Text(editor));
    }

    [Fact]
    public void TextLimitDropsGroupsAndAGroupLargerThanTheLimitIsNotKept()
    {
        TEditor editor = Load("");
        editor.UndoTextLimit = 10;
        editor.InsertText("abcdef");                                   // 6
        editor.InsertText("ghij");                                     // 10 in all
        editor.InsertText("k");                                        // 11: the oldest group goes
        Assert.Equal(2, UndoAll(editor));
        Assert.Equal("abcdef", Text(editor));

        TEditor big = Load("");
        big.UndoTextLimit = 10;
        big.InsertText(new string('x', 11));
        Assert.False(big.HasUndo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ZeroLimitDisablesTheHistory(bool groups)
    {
        TEditor editor = Load("");
        Type(editor, "a");
        if (groups) editor.UndoGroupLimit = 0; else editor.UndoTextLimit = 0;
        Assert.False(editor.HasUndo);
        Type(editor, "b");
        Assert.False(editor.HasUndo);
        editor.Undo();
        Assert.Equal("ab", Text(editor));
    }

    [Fact]
    public void CanUndoFalseRecordsNothingAndClearsTheHistory()
    {
        TEditor editor = Load("");
        Type(editor, "a");
        editor.canUndo = false;
        Type(editor, "b");
        Assert.False(editor.HasUndo);
        editor.Undo();
        Assert.Equal("ab", Text(editor));
        editor.canUndo = true;
        Type(editor, "c");
        editor.Undo();
        Assert.Equal("ab", Text(editor));
    }

    [Fact]
    public void UnrecordedInsertBufferClearsTheHistory()
    {
        TEditor editor = Load("");
        Type(editor, "a");
        char[] x = { 'x' };
        editor.InsertBuffer(x, 0, 1, false, false);
        Assert.False(editor.HasUndo);
        Assert.Equal("ax", Text(editor));
    }

    // ── fixed-size buffers ───────────────────────────────────────────────────

    [Fact]
    public void DeletedTextNoLongerReservesFixedBufferCapacity()
    {
        TEditor editor = Load("", bufSize: 8);
        Assert.True(editor.InsertText("abcdefgh"));
        editor.SetSelect(0, 8, false);
        editor.DeleteSelect();
        Assert.True(editor.InsertText("12345678"));                    // the deleted text is kept in the history
        editor.Undo();
        editor.Undo();
        Assert.Equal("abcdefgh", Text(editor));
    }

    [Fact]
    public void MemoUndoAndData()
    {
        var memo = new TMemo(new TRect(0, 0, 20, 5), null, null, null, 16);
        var data = new TMemoData(16);
        "memo".CopyTo(0, data.buffer, 0, 4);
        data.length = 4;
        memo.SetData(data);
        Assert.False(memo.HasUndo);
        memo.SetCurPtr(4, 0);
        Type(memo, "!!");
        memo.Undo();
        var back = new TMemoData(16);
        memo.GetData(back);
        Assert.Equal("memo", new string(back.buffer, 0, (int)back.length));
        Assert.False(memo.modified);
    }

    // ── streams: history is runtime state only ───────────────────────────────

    [Fact]
    public void StreamRoundTripCarriesNoHistory()
    {
        var memo = new TMemo(new TRect(0, 0, 20, 5), null, null, null, 64);
        Type(memo, "abc");
        Assert.True(memo.HasUndo);
        using var stream = new MemoryStream();
        memo.Write(new Opstream(stream));
        stream.Position = 0;
        var restored = (TMemo)TMemo.Build();
        restored.Read(new Ipstream(stream));
        Assert.Equal("abc", Text(restored));
        Assert.False(restored.HasUndo);
        Assert.False(restored.HasRedo);
        Assert.False(restored.modified);
        Assert.True(restored.canUndo);
    }

    // ── keys, commands and menu ──────────────────────────────────────────────

    [Fact]
    public void CtrlZUndoesAndCtrlYAndCtrlShiftZRedo()
    {
        TEditor editor = Load("");
        Type(editor, "ab");
        Key(editor, Keys.kbCtrlZ);
        Assert.Equal("", Text(editor));
        Key(editor, Keys.kbCtrlY);
        Assert.Equal("ab", Text(editor));
        Key(editor, Keys.kbCtrlZ);
        Key(editor, Keys.kbCtrlZ, Keys.kbShift);
        Assert.Equal("ab", Text(editor));
    }

    [Fact]
    public void CmRedoCommandRedoes()
    {
        TEditor editor = Load("");
        Type(editor, "q");
        Command(editor, Views.cmUndo);
        Command(editor, Views.cmRedo);
        Assert.Equal("q", Text(editor));
    }

    [Fact]
    public void UndoAndRedoCommandsFollowTheHistoryWhenActive()
    {
        using var driver = new DriverScope();
        var commands = new TCommandSet();
        TView.GetCommands(commands);
        try
        {
            TEditor editor = Load("");
            editor.SetState(Views.sfActive, true);
            Assert.False(TView.CommandEnabled(Views.cmUndo));
            Assert.False(TView.CommandEnabled(Views.cmRedo));
            Type(editor, "a");
            Assert.True(TView.CommandEnabled(Views.cmUndo));
            editor.Undo();
            Assert.False(TView.CommandEnabled(Views.cmUndo));
            Assert.True(TView.CommandEnabled(Views.cmRedo));
        }
        finally { TView.SetCommands(commands); }
    }

    [Fact]
    public void EditMenuOffersRedoAfterUndo()
    {
        using var driver = new DriverScope();
        var app = new TEditorApp();
        try
        {
            Assert.False(TView.CommandEnabled(Views.cmRedo));
            var items = new List<(ushort Command, string? Param)>();
            Collect(Assert.IsType<TMenu>(app.MenuBar!.Menu), items);
            int undo = items.FindIndex(i => i.Command == Views.cmUndo);
            Assert.True(undo >= 0);
            Assert.Equal((Views.cmRedo, "Ctrl+Y"), items[undo + 1]);
            Assert.Equal("Ctrl+Z", items[undo].Param);
        }
        finally
        {
            app.ShutDown();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private static void Collect(TMenu? menu, List<(ushort Command, string? Param)> items)
    {
        for (var item = menu?.Items; item != null; item = item.Next)
        {
            if (item.Command != 0) items.Add((item.Command, item.Param));
            if (item.SubMenu != null) Collect(item.SubMenu, items);
        }
    }
}
