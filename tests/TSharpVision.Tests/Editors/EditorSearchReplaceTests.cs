using System;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

public sealed class EditorSearchReplaceTests
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

    /// <summary>null-terminated C-string from ASCII text.</summary>
    static byte[] AsCStr(string s)
    {
        byte[] raw = Encoding.ASCII.GetBytes(s);
        byte[] buf = new byte[raw.Length + 1];
        Array.Copy(raw, buf, raw.Length);
        return buf;
    }

    // ── search ───────────────────────────────────────────────────────────────

    [Fact]
    public void Search_CaseSensitive_HitsFirstMatch()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("alpha beta beta gamma");
        Assert.True(ed.Search(AsCStr("beta"), Views.efCaseSensitive));
        Assert.Equal(6u, ed.selStart);
        Assert.Equal(10u, ed.selEnd);
    }

    [Fact]
    public void Search_ContinuesFromCurPtr()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("alpha beta beta gamma");
        ed.SetCurPtr(11, 0);
        Assert.True(ed.Search(AsCStr("beta"), Views.efCaseSensitive));
        Assert.Equal(11u, ed.selStart);
    }

    [Fact]
    public void Search_Miss_ReturnsFalse()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("hello");
        Assert.False(ed.Search(AsCStr("zzz"), Views.efCaseSensitive));
    }

    [Fact]
    public void Search_EmptyNeedle_ReturnsFalse()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("hello");
        Assert.False(ed.Search(AsCStr(""), Views.efCaseSensitive));
    }

    [Fact]
    public void Search_CaseInsensitive_FindsUppercaseText()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("Alpha BETA gamma");
        Assert.True(ed.Search(AsCStr("beta"), 0 /* no efCaseSensitive */));
        Assert.Equal(6u, ed.selStart);
    }

    [Fact]
    public void Search_CaseSensitive_MissesWrongCase()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("Alpha BETA gamma");
        Assert.False(ed.Search(AsCStr("beta"), Views.efCaseSensitive));
    }

    [Fact]
    public void Search_WholeWords_SkipsSubstring()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("alphabet alpha beta");
        Assert.True(ed.Search(AsCStr("alpha"),
            (ushort)(Views.efCaseSensitive | Views.efWholeWordsOnly)));
        Assert.Equal(9u, ed.selStart);
    }

    [Fact]
    public void Search_PastEnd_ReturnsFalse()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("xyz");
        ed.SetCurPtr(3, 0);
        Assert.False(ed.Search(AsCStr("xyz"), Views.efCaseSensitive));
    }

    // ── DoSearchReplace ───────────────────────────────────────────────────────

    [Fact]
    public void DoSearchReplace_Single_ReplacesFirstOccurrence()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("foo bar foo baz");
        Array.Copy(AsCStr("foo"), TEditor.findStr, 4);
        Array.Copy(AsCStr("FOO"), TEditor.replaceStr, 4);
        TEditor.editorFlags = (ushort)(Views.efDoReplace | Views.efCaseSensitive);
        ed.DoSearchReplace();
        Assert.Equal("FOO bar foo baz", ReadAll(ed));
    }

    [Fact]
    public void DoSearchReplace_ReplaceAll_ReplacesAllOccurrences()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("foo bar foo baz foo");
        Array.Copy(AsCStr("foo"), TEditor.findStr, 4);
        Array.Copy(AsCStr("XX"), TEditor.replaceStr, 3);
        TEditor.editorFlags = (ushort)(
            Views.efDoReplace | Views.efReplaceAll | Views.efCaseSensitive);
        ed.DoSearchReplace();
        Assert.Equal("XX bar XX baz XX", ReadAll(ed));
    }

    [Fact]
    public void DoSearchReplace_Prompt_CmYes_ReplacesAll()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("aa bb aa cc");
        Array.Copy(AsCStr("aa"), TEditor.findStr, 3);
        Array.Copy(AsCStr("ZZ"), TEditor.replaceStr, 3);
        TEditor.editorFlags = (ushort)(
            Views.efDoReplace | Views.efReplaceAll
            | Views.efPromptOnReplace | Views.efCaseSensitive);
        int prompts = 0;
        TEditor.editorDialog = (dlg, info) =>
        {
            if (dlg == Views.edReplacePrompt) { prompts++; return Views.cmYes; }
            return Views.cmCancel;
        };
        ed.DoSearchReplace();
        Assert.Equal("ZZ bb ZZ cc", ReadAll(ed));
        Assert.Equal(2, prompts);
    }

    [Fact]
    public void DoSearchReplace_Prompt_CmCancel_LeavesTextUnchanged()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("aa bb aa cc");
        Array.Copy(AsCStr("aa"), TEditor.findStr, 3);
        Array.Copy(AsCStr("ZZ"), TEditor.replaceStr, 3);
        TEditor.editorFlags = (ushort)(
            Views.efDoReplace | Views.efReplaceAll
            | Views.efPromptOnReplace | Views.efCaseSensitive);
        TEditor.editorDialog = (_, _) => Views.cmCancel;
        ed.DoSearchReplace();
        Assert.Equal("aa bb aa cc", ReadAll(ed));
    }

    [Fact]
    public void DoSearchReplace_Miss_DispatchesEdSearchFailed()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("hello");
        Array.Copy(AsCStr("zzz"), TEditor.findStr, 4);
        TEditor.editorFlags = Views.efCaseSensitive;
        bool fired = false;
        TEditor.editorDialog = (dlg, _) =>
        {
            if (dlg == Views.edSearchFailed) fired = true;
            return Views.cmCancel;
        };
        ed.DoSearchReplace();
        Assert.True(fired);
    }

    // ── Find() ───────────────────────────────────────────────────────────────

    [Fact]
    public void Find_RoutesEdFindDialog()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("hello world");
        bool dispatched = false;
        TEditor.editorDialog = (dlg, info) =>
        {
            if (dlg == Views.edFind && info is TFindDialogRec rec)
            {
                dispatched = true;
                Array.Clear(rec.Find, 0, rec.Find.Length);
                byte[] src = Encoding.ASCII.GetBytes("world");
                Array.Copy(src, rec.Find, src.Length);
                rec.Options = Views.efCaseSensitive;
                return Views.cmYes;
            }
            return Views.cmCancel;
        };
        ed.Find();
        Assert.True(dispatched);
        Assert.Equal((byte)'w', TEditor.findStr[0]);
        Assert.Equal(6u, ed.selStart);
    }

    [Fact]
    public void Find_Cancelled_NoSearch()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("hello world");
        TEditor.editorDialog = (_, _) => Views.cmCancel;
        ed.Find();
        Assert.Equal(0u, ed.selStart);
        Assert.Equal(0u, ed.selEnd);
    }

    // ── Replace() ────────────────────────────────────────────────────────────

    [Fact]
    public void Replace_SetsEfDoReplaceAndReplaces()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("dog cat dog");
        TEditor.editorDialog = (dlg, info) =>
        {
            if (dlg == Views.edReplace && info is TReplaceDialogRec rec)
            {
                Array.Clear(rec.Find,    0, rec.Find.Length);
                Array.Clear(rec.Replace, 0, rec.Replace.Length);
                Array.Copy(Encoding.ASCII.GetBytes("dog"), rec.Find,    3);
                Array.Copy(Encoding.ASCII.GetBytes("PIG"), rec.Replace, 3);
                rec.Options = (ushort)(Views.efCaseSensitive | Views.efReplaceAll);
                return Views.cmYes;
            }
            return Views.cmCancel;
        };
        ed.Replace();
        Assert.NotEqual(0, TEditor.editorFlags & Views.efDoReplace);
        Assert.Equal("PIG cat PIG", ReadAll(ed));
    }

    // ── HandleEvent cmFind / cmSearchAgain ───────────────────────────────────

    [Fact]
    public void HandleEvent_CmFind_HitsEdFindDialog()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("alpha beta");
        bool dispatched = false;
        TEditor.editorDialog = (dlg, _) =>
        {
            if (dlg == Views.edFind) dispatched = true;
            return Views.cmCancel;
        };
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmFind;
        ed.HandleEvent(ref ev);
        Assert.True(dispatched);
    }

    [Fact]
    public void HandleEvent_CmSearchAgain_RerunsDoSearchReplace()
    {
        using var scope = new EditorClipboardScope();
        var ed = MakeEditor("foo bar foo");
        Array.Copy(AsCStr("foo"), TEditor.findStr, 4);
        Array.Copy(AsCStr("BAZ"), TEditor.replaceStr, 4);
        TEditor.editorFlags = (ushort)(
            Views.efDoReplace | Views.efReplaceAll | Views.efCaseSensitive);
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmSearchAgain;
        ed.HandleEvent(ref ev);
        Assert.Equal("BAZ bar BAZ", ReadAll(ed));
    }
}
