// Covers gaps not present in EditorSearchReplaceTests.cs:
//   §1 TFindDialogRec / TReplaceDialogRec field layout
//   §2 TEditorFindDialog.BytesToString / StringToBytes static helpers
//   §3 TEditorFindDialog.ReadBack — controls → rec
//   §4 TEditorReplaceDialog.ReadBack — controls → rec
//   §5 Replace() cancel path
//   §6 edReplacePrompt cmNo path (skip, not abort)
//   §7 Find() selEnd + editorFlags update after match
//   §8 Command constant canonical values
//   §9 All editorDialog case IDs dispatch without exception

using System;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

[Collection("NonParallel")]
public sealed class EditorFindReplaceDialogTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Populate a TEditor via InsertText, cursor at position 0.</summary>
    static TEditor MakeEd(string text)
    {
        var ed = new TEditor(new TRect(0, 0, 80, 24), null, null, null, 4096);
        byte[] tb = Encoding.ASCII.GetBytes(text);
        ed.InsertText(tb, (uint)tb.Length, false);
        ed.SetSelect(0, 0, true);
        return ed;
    }

    static string ReadAll(TEditor ed)
    {
        var sb = new StringBuilder();
        for (uint k = 0; k < ed.bufLen; k++) sb.Append((char)ed.BufChar(k));
        return sb.ToString();
    }

    // ── §1 — TFindDialogRec / TReplaceDialogRec field layout ─────────────────

    [Fact]
    public void TFindDialogRec_StoresFindBytes()
    {
        byte[] fBytes = new byte[80];
        Encoding.ASCII.GetBytes("hello").CopyTo(fBytes, 0);
        var rec = new TFindDialogRec(fBytes, Views.efCaseSensitive);
        Assert.Equal((byte)'h', rec.Find[0]);
        Assert.Equal((byte)'o', rec.Find[4]);
    }

    [Fact]
    public void TFindDialogRec_StoresOptions()
    {
        var rec = new TFindDialogRec(new byte[80], Views.efCaseSensitive);
        Assert.Equal(Views.efCaseSensitive, rec.Options);
    }

    [Fact]
    public void TReplaceDialogRec_StoresBytesAndOptions()
    {
        byte[] fBytes = new byte[80];
        byte[] rBytes = new byte[80];
        Encoding.ASCII.GetBytes("hello").CopyTo(fBytes, 0);
        Encoding.ASCII.GetBytes("bar").CopyTo(rBytes, 0);
        var rec = new TReplaceDialogRec(fBytes, rBytes, Views.efReplaceAll);
        Assert.Equal((byte)'h', rec.Find[0]);
        Assert.Equal((byte)'b', rec.Replace[0]);
        Assert.Equal((byte)'r', rec.Replace[2]);
        Assert.Equal(Views.efReplaceAll, rec.Options);
    }

    // ── §2 — TEditorFindDialog.BytesToString / StringToBytes ──────────────────

    [Fact]
    public void BytesToString_ReadsToNullTerminator()
    {
        var src = new byte[80];
        src[0] = (byte)'h'; src[1] = (byte)'i'; // src[2] == 0
        Assert.Equal("hi", TEditorFindDialog.BytesToString(src));
    }

    [Fact]
    public void StringToBytes_CopiesAndNullTerminates()
    {
        var dst = new byte[80];
        TEditorFindDialog.StringToBytes("world", dst);
        Assert.Equal((byte)'w', dst[0]);
        Assert.Equal((byte)'d', dst[4]);
        Assert.Equal(0, dst[5]);
    }

    [Fact]
    public void BytesToString_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TEditorFindDialog.BytesToString(null));
    }

    [Fact]
    public void StringToBytes_Null_ClearsDestination()
    {
        var dst = new byte[80];
        dst[0] = (byte)'X'; // pre-fill
        TEditorFindDialog.StringToBytes(null, dst);
        Assert.Equal(0, dst[0]);
    }

    // ── §3 — TEditorFindDialog.ReadBack ───────────────────────────────────────

    [Fact]
    public void FindDialog_ReadBack_WritesFindTextAndFlags()
    {
        var rec = new TFindDialogRec(new byte[80], 0);
        var il  = new TInputLine(new TRect(0, 0, 30, 1), 80);
        il.Data = "search term";
        var cb  = new TCheckBoxes(new TRect(0, 0, 30, 2),
            new TSItem("A", new TSItem("B", null)));
        cb.value = (uint)(Views.efCaseSensitive | Views.efWholeWordsOnly);

        TEditorFindDialog.ReadBack(rec, il, cb);

        Assert.Equal("search term", TEditorFindDialog.BytesToString(rec.Find));
        Assert.NotEqual(0, rec.Options & Views.efCaseSensitive);
        Assert.NotEqual(0, rec.Options & Views.efWholeWordsOnly);
    }

    // ── §4 — TEditorReplaceDialog.ReadBack ────────────────────────────────────

    [Fact]
    public void ReplaceDialog_ReadBack_WritesBothStringsAndFlags()
    {
        var rec    = new TReplaceDialogRec(new byte[80], new byte[80], 0);
        var findIl = new TInputLine(new TRect(0, 0, 30, 1), 80);
        var replIl = new TInputLine(new TRect(0, 0, 30, 1), 80);
        findIl.Data = "alpha";
        replIl.Data = "beta";
        var cb = new TCheckBoxes(new TRect(0, 0, 30, 4),
            new TSItem("A", new TSItem("B", new TSItem("C", new TSItem("D", null)))));
        cb.value = (uint)(Views.efReplaceAll | Views.efPromptOnReplace);

        TEditorReplaceDialog.ReadBack(rec, findIl, replIl, cb);

        Assert.Equal("alpha", TEditorFindDialog.BytesToString(rec.Find));
        Assert.Equal("beta",  TEditorFindDialog.BytesToString(rec.Replace));
        Assert.NotEqual(0, rec.Options & Views.efReplaceAll);
        Assert.NotEqual(0, rec.Options & Views.efPromptOnReplace);
        Assert.Equal(0, rec.Options & Views.efCaseSensitive); // absent flag cleared
    }

    // ── §5 — Replace() cancel path ────────────────────────────────────────────

    [Fact]
    public void Replace_Cancel_BufferUnchanged()
    {
        using var scope = new EditorClipboardScope();
        TEditor.editorDialog = (_, _) => Views.cmCancel;

        var ed = MakeEd("original content");
        ed.Replace();
        Assert.Equal("original content", ReadAll(ed));
    }

    // ── §6 — edReplacePrompt cmNo path (skip, not abort) ─────────────────────

    [Fact]
    public void ReplacePrompt_CmNo_BufferUnchanged()
    {
        using var scope = new EditorClipboardScope();
        TEditor.editorDialog = (d, _) =>
            d == Views.edReplacePrompt ? Views.cmNo : Views.cmCancel;

        Array.Clear(TEditor.findStr,    0, TEditor.findStr.Length);
        Array.Clear(TEditor.replaceStr, 0, TEditor.replaceStr.Length);
        TEditorFindDialog.StringToBytes("hi",  TEditor.findStr);
        TEditorFindDialog.StringToBytes("hey", TEditor.replaceStr);
        TEditor.editorFlags = (ushort)(Views.efDoReplace | Views.efPromptOnReplace);

        var ed = MakeEd("say hi");
        ed.SetSelect(0, 0, true);
        ed.DoSearchReplace();

        Assert.Equal("say hi", ReadAll(ed));
    }

    // ── §7 — Find() selEnd + editorFlags update ────────────────────────────────

    [Fact]
    public void Find_SelEnd_UpdatedAfterMatch()
    {
        using var scope = new EditorClipboardScope();
        TEditor.editorDialog = (d, info) =>
        {
            if (d == Views.edFind && info is TFindDialogRec r)
            {
                TEditorFindDialog.StringToBytes("needle", r.Find);
                r.Options = Views.efCaseSensitive;
                return Views.cmOK;
            }
            return Views.cmCancel;
        };

        var ed = MakeEd("a needle in a haystack");
        ed.Find();

        // "needle" starts at index 2, length 6 → selEnd == 8
        Assert.Equal(2u,  ed.selStart);
        Assert.Equal(8u,  ed.selEnd);
    }

    [Fact]
    public void Find_EditorFlags_UpdatedAfterMatch()
    {
        using var scope = new EditorClipboardScope();
        TEditor.editorDialog = (d, info) =>
        {
            if (d == Views.edFind && info is TFindDialogRec r)
            {
                TEditorFindDialog.StringToBytes("needle", r.Find);
                r.Options = Views.efCaseSensitive;
                return Views.cmOK;
            }
            return Views.cmCancel;
        };

        var ed = MakeEd("a needle in a haystack");
        ed.Find();
        Assert.NotEqual(0, TEditor.editorFlags & Views.efCaseSensitive);
    }

    // ── §8 — Command constant canonical values ─────────────────────────────────

    [Fact]
    public void CommandConstants_CorrectValues()
    {
        Assert.Equal(82,  (int)Views.cmFind);
        Assert.Equal(83,  (int)Views.cmReplace);
        Assert.Equal(84,  (int)Views.cmSearchAgain);
        Assert.Equal(7,   Views.edFind);
        Assert.Equal(9,   Views.edReplace);
        Assert.Equal(8,   Views.edSearchFailed);
        Assert.Equal(10,  Views.edReplacePrompt);
    }

    // ── §9 — All editorDialog case IDs dispatch without exception ─────────────

    [Fact]
    public void AllEditorDialogIds_DispatchWithoutException()
    {
        using var scope = new EditorClipboardScope();
        TEditor.editorDialog = (d, info) => d switch
        {
            Views.edOutOfMemory   => Views.cmOK,
            Views.edReadError     => Views.cmOK,
            Views.edWriteError    => Views.cmOK,
            Views.edCreateError   => Views.cmOK,
            Views.edSaveModify    => Views.cmYes,
            Views.edSaveUntitled  => Views.cmYes,
            Views.edSaveAs        => Views.cmCancel,
            Views.edSearchFailed  => Views.cmOK,
            Views.edReplacePrompt => Views.cmYes,
            Views.edFind          => Views.cmOK,
            Views.edReplace       => Views.cmOK,
            _                     => Views.cmCancel
        };

        var ex = Record.Exception(() =>
        {
            TEditor.editorDialog(Views.edOutOfMemory,   null);
            TEditor.editorDialog(Views.edReadError,     "dummy.txt");
            TEditor.editorDialog(Views.edWriteError,    "dummy.txt");
            TEditor.editorDialog(Views.edCreateError,   "dummy.txt");
            TEditor.editorDialog(Views.edSaveModify,    "dummy.txt");
            TEditor.editorDialog(Views.edSaveUntitled,  null);
            TEditor.editorDialog(Views.edSearchFailed,  null);
            TEditor.editorDialog(Views.edReplacePrompt, new TPoint(0, 0));
            TEditor.editorDialog(Views.edFind,
                new TFindDialogRec(new byte[80], 0));
            TEditor.editorDialog(Views.edReplace,
                new TReplaceDialogRec(new byte[80], new byte[80], 0));
        });
        Assert.Null(ex);
    }
}
