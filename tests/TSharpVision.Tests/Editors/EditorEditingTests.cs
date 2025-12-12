using System;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

public sealed class EditorEditingTests
{
    // ── helpers ──────────────────────────────────────────────────────────────
    static TEditor MakeEditor(string text)
    {
        var ed = new TEditor(new TRect(0, 0, 40, 10), null, null, null, 1024);
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        ed.bufLen = (uint)bytes.Length;
        ed.gapLen = ed.bufSize - ed.bufLen;
        Array.Copy(bytes, 0, ed.buffer, (int)ed.gapLen, bytes.Length);
        ed.curPtr   = 0;
        ed.curPos   = default;
        ed.delta    = default;
        ed.drawLine = 0;
        ed.drawPtr  = 0;
        ed.limit.x  = Views.maxLineLength;
        int lines = 1;
        foreach (byte b in bytes) if (b == 0x0A) lines++;
        ed.limit.y = lines;
        return ed;
    }

    static string ReadAll(TEditor ed)
    {
        var sb = new StringBuilder();
        for (uint p = 0; p < ed.bufLen; p++) sb.Append((char)ed.BufChar(p));
        return sb.ToString();
    }

    // ── InsertText ───────────────────────────────────────────────────────────

    [Fact]
    public void InsertText_PrependsSingleChar()
    {
        var ed = MakeEditor("hello");
        ed.InsertText(Encoding.ASCII.GetBytes("X"), 1, false);
        Assert.Equal("Xhello", ReadAll(ed));
    }

    [Fact]
    public void InsertText_AdvancesCurPtr()
    {
        var ed = MakeEditor("hello");
        ed.InsertText(Encoding.ASCII.GetBytes("X"), 1, false);
        Assert.Equal(1u, ed.curPtr);
    }

    [Fact]
    public void InsertText_UpdatesBufLen()
    {
        var ed = MakeEditor("hello");
        ed.InsertText(Encoding.ASCII.GetBytes("X"), 1, false);
        Assert.Equal(6u, ed.bufLen);
    }

    [Fact]
    public void InsertText_SetsModified()
    {
        var ed = MakeEditor("");
        ed.InsertText(Encoding.ASCII.GetBytes("X"), 1, false);
        Assert.True(ed.modified);
    }

    [Fact]
    public void InsertText_TracksInsCount()
    {
        var ed = MakeEditor("hello");
        ed.InsertText(Encoding.ASCII.GetBytes("X"), 1, false);
        Assert.Equal(1, (int)ed.insCount);
    }

    [Fact]
    public void InsertText_ReplacesSelection()
    {
        var ed = MakeEditor("hello world");
        ed.SetSelect(6, 11, false);   // select "world"
        ed.InsertText(Encoding.ASCII.GetBytes("there"), 5, false);
        Assert.Equal("hello there", ReadAll(ed));
    }

    [Fact]
    public void InsertText_Replace_CurPtrAfterReplacement()
    {
        var ed = MakeEditor("hello world");
        ed.SetSelect(6, 11, false);
        ed.InsertText(Encoding.ASCII.GetBytes("there"), 5, false);
        Assert.Equal(11u, ed.curPtr);
    }

    // ── NewLine ──────────────────────────────────────────────────────────────

    [Fact]
    public void NewLine_SplitsText()
    {
        var ed = MakeEditor("hello");
        ed.SetCurPtr(2, 0);
        ed.NewLine();
        Assert.Equal("he\nllo", ReadAll(ed));
    }

    [Fact]
    public void NewLine_SetsCurPtr()
    {
        var ed = MakeEditor("hello");
        ed.SetCurPtr(2, 0);
        ed.NewLine();
        Assert.Equal(3u, ed.curPtr);
    }

    [Fact]
    public void NewLine_IncrementsLimitY()
    {
        var ed = MakeEditor("hello");
        ed.SetCurPtr(2, 0);
        ed.NewLine();
        Assert.Equal(2, ed.limit.y);
    }

    [Fact]
    public void NewLine_AutoIndent_CopiesLeadingSpaces()
    {
        var ed = MakeEditor("    hello");
        ed.autoIndent = true;
        ed.SetCurPtr(9, 0);
        ed.NewLine();
        Assert.Equal("    hello\n    ", ReadAll(ed));
    }

    [Fact]
    public void NewLine_AutoIndent_CurPtrAtEndOfIndent()
    {
        var ed = MakeEditor("    hello");
        ed.autoIndent = true;
        ed.SetCurPtr(9, 0);
        ed.NewLine();
        Assert.Equal(14u, ed.curPtr);
    }

    // ── DeleteSelect ─────────────────────────────────────────────────────────

    [Fact]
    public void DeleteSelect_RemovesSelection()
    {
        var ed = MakeEditor("hello world");
        ed.SetSelect(5, 11, false);   // " world"
        ed.DeleteSelect();
        Assert.Equal("hello", ReadAll(ed));
    }

    [Fact]
    public void DeleteSelect_UpdatesBufLen()
    {
        var ed = MakeEditor("hello world");
        ed.SetSelect(5, 11, false);
        ed.DeleteSelect();
        Assert.Equal(5u, ed.bufLen);
    }

    [Fact]
    public void DeleteSelect_ClearsSelection()
    {
        var ed = MakeEditor("hello world");
        ed.SetSelect(5, 11, false);
        ed.DeleteSelect();
        Assert.False(ed.HasSelection());
    }

    // ── HandleEvent deletion commands ────────────────────────────────────────

    [Fact]
    public void HandleEvent_CmDelChar_RemovesFirstChar()
    {
        var ed = MakeEditor("hello");
        ed.SetCurPtr(0, 0);
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmDelChar;
        ed.HandleEvent(ref ev);
        Assert.Equal("ello", ReadAll(ed));
    }

    [Fact]
    public void HandleEvent_CmBackSpace_RemovesPrevChar()
    {
        var ed = MakeEditor("hello");
        ed.SetCurPtr(3, 0);
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmBackSpace;
        ed.HandleEvent(ref ev);
        Assert.Equal("helo", ReadAll(ed));
    }

    [Fact]
    public void HandleEvent_CmBackSpace_DecrementsCurPtr()
    {
        var ed = MakeEditor("hello");
        ed.SetCurPtr(3, 0);
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmBackSpace;
        ed.HandleEvent(ref ev);
        Assert.Equal(2u, ed.curPtr);
    }

    [Fact]
    public void HandleEvent_CmDelWord_RemovesWord()
    {
        var ed = MakeEditor("foo bar baz");
        ed.SetCurPtr(4, 0);
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmDelWord;
        ed.HandleEvent(ref ev);
        Assert.Equal("foo baz", ReadAll(ed));
    }

    [Fact]
    public void HandleEvent_CmDelLine_RemovesLine()
    {
        var ed = MakeEditor("alpha\nbeta\ngamma");
        ed.SetCurPtr(8, 0);
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmDelLine;
        ed.HandleEvent(ref ev);
        Assert.Equal("alpha\ngamma", ReadAll(ed));
    }

    // ── Printable key inserts char ───────────────────────────────────────────

    [Fact]
    public void HandleEvent_PrintableKey_InsertsChar()
    {
        var ed = MakeEditor("hi");
        ed.SetCurPtr(2, 0);
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.charScan.charCode = (byte)'!';
        ev.keyDown.keyCode = 0;
        ed.HandleEvent(ref ev);
        Assert.Equal("hi!", ReadAll(ed));
    }

    // ── Undo ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Undo_RestoresDeletedChars()
    {
        var ed = MakeEditor("hello");
        ed.SetSelect(2, 4, false);
        ed.DeleteSelect();
        Assert.Equal("heo", ReadAll(ed));
        ed.Undo();
        Assert.Equal("hello", ReadAll(ed));
    }

    [Fact]
    public void HandleEvent_CmUndo_RevertsInsert()
    {
        var ed = MakeEditor("xyz");
        ed.InsertText(Encoding.ASCII.GetBytes("Q"), 1, false);
        Assert.Equal("Qxyz", ReadAll(ed));
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmUndo;
        ed.HandleEvent(ref ev);
        Assert.Equal("xyz", ReadAll(ed));
    }

    // ── Clipboard ────────────────────────────────────────────────────────────

    [Fact]
    public void ClipCopy_PopulatesClipboard()
    {
        using var scope = new EditorClipboardScope();
        var clip = new TEditor(new TRect(0, 0, 10, 5), null, null, null, 256);
        TEditor.clipboard = clip;

        var ed = MakeEditor("hello world");
        ed.SetSelect(0, 5, false);
        Assert.True(ed.ClipCopy());
        Assert.Equal("hello", ReadAll(clip));
    }

    [Fact]
    public void ClipPaste_AppendsCopiedText()
    {
        using var scope = new EditorClipboardScope();
        var clip = new TEditor(new TRect(0, 0, 10, 5), null, null, null, 256);
        TEditor.clipboard = clip;

        var ed = MakeEditor("hello world");
        ed.SetSelect(0, 5, false);
        ed.ClipCopy();
        ed.SetCurPtr(11, 0);
        ed.ClipPaste();
        Assert.Equal("hello worldhello", ReadAll(ed));
    }

    [Fact]
    public void ClipCut_RemovesSelectionFromSource()
    {
        using var scope = new EditorClipboardScope();
        var clip = new TEditor(new TRect(0, 0, 10, 5), null, null, null, 256);
        TEditor.clipboard = clip;

        var ed = MakeEditor("abcdef");
        ed.SetSelect(2, 5, false);
        ed.ClipCut();
        Assert.Equal("abf", ReadAll(ed));
    }

    [Fact]
    public void ClipCut_PopulatesClipboard()
    {
        using var scope = new EditorClipboardScope();
        var clip = new TEditor(new TRect(0, 0, 10, 5), null, null, null, 256);
        TEditor.clipboard = clip;

        var ed = MakeEditor("abcdef");
        ed.SetSelect(2, 5, false);
        ed.ClipCut();
        Assert.Equal("cde", ReadAll(clip));
    }

    [Fact]
    public void IsClipboard_TrueOnClipboardEditor()
    {
        using var scope = new EditorClipboardScope();
        var clip = new TEditor(new TRect(0, 0, 10, 5), null, null, null, 64);
        TEditor.clipboard = clip;
        Assert.True(clip.IsClipboard());
    }

    [Fact]
    public void IsClipboard_FalseOnOtherEditor()
    {
        using var scope = new EditorClipboardScope();
        var clip = new TEditor(new TRect(0, 0, 10, 5), null, null, null, 64);
        TEditor.clipboard = clip;
        var other = MakeEditor("x");
        Assert.False(other.IsClipboard());
    }

    // ── cmInsertText ─────────────────────────────────────────────────────────

    [Fact]
    public void HandleEvent_CmInsertText_InsertsPayload()
    {
        var ed = MakeEditor("ab");
        ed.SetCurPtr(1, 0);
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmInsertText;
        ev.message.infoPtr = new TextInfo(Encoding.ASCII.GetBytes("XY"));
        ed.HandleEvent(ref ev);
        Assert.Equal("aXYb", ReadAll(ed));
    }

    // ── UpdateCommands ───────────────────────────────────────────────────────

    [Fact]
    public void UpdateCommands_DisablesCmCutWithoutSelection()
    {
        var ed = MakeEditor("hello");
        ed.state |= Views.sfActive;
        TCommandSet savedCmds = TView.curCommandSet;
        try
        {
            ed.UpdateCommands();
            Assert.False(TView.curCommandSet.Has(Views.cmCut));
        }
        finally { TView.curCommandSet = savedCmds; }
    }

    [Fact]
    public void UpdateCommands_EnablesCmCutWithSelection()
    {
        var ed = MakeEditor("hello");
        ed.state |= Views.sfActive;
        TCommandSet savedCmds = TView.curCommandSet;
        try
        {
            ed.SetSelect(0, 3, false);
            ed.UpdateCommands();
            Assert.True(TView.curCommandSet.Has(Views.cmCut));
        }
        finally { TView.curCommandSet = savedCmds; }
    }

    [Fact]
    public void UpdateCommands_EnablesCmCopyWithSelection()
    {
        var ed = MakeEditor("hello");
        ed.state |= Views.sfActive;
        TCommandSet savedCmds = TView.curCommandSet;
        try
        {
            ed.SetSelect(0, 3, false);
            ed.UpdateCommands();
            Assert.True(TView.curCommandSet.Has(Views.cmCopy));
        }
        finally { TView.curCommandSet = savedCmds; }
    }
}
