// Requires DriverScope (TEditor.InsertText triggers Draw → needs screen buffer).
//
// Ctrl-K  → enters KeyStateBlock prefix; next key is dispatched via blockKeys.
// Ctrl-Q  → enters KeyStateQuick prefix; next key is dispatched via quickKeys.
// Both sequences are handled by TEditor.ConvertEvent and then TEditor.HandleEvent.
//
// Helper: MakeKey() constructs an evKeyDown TEvent.
//         SendKey() calls HandleEvent with one key event.
//         MakeEd()  creates an editor pre-loaded with text.

using System;
using System.Text;
using SharpVision;
using SharpVision.Constants;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.Editors;

[Collection("NonParallel")]
public sealed class EditorPrefixCommandTests : IDisposable
{
    private readonly DriverScope _ds;

    public EditorPrefixCommandTests()
    {
        _ds = new DriverScope();
        TEventQueue.Resume();
    }

    public void Dispose() => _ds.Dispose();

    // ── helpers ──────────────────────────────────────────────────────────────

    static TEvent MakeKey(ushort keyCode, byte charCode = 0)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        ev.keyDown.charScan.charCode = charCode;
        return ev;
    }

    static TEvent MakeCmd(ushort command)
    {
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = command;
        return ev;
    }

    /// <summary>
    /// Create a TEditor pre-loaded with <paramref name="text"/>, cursor at 0.
    /// </summary>
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

    static void SendKey(TEditor ed, ushort keyCode, byte charCode = 0)
    {
        var ev = MakeKey(keyCode, charCode);
        ed.HandleEvent(ref ev);
    }

    static void SendCmd(TEditor ed, ushort command)
    {
        var ev = MakeCmd(command);
        ed.HandleEvent(ref ev);
    }

    // ── 22d.1 — Ctrl-K alone enters block prefix ──────────────────────────────

    [Fact]
    public void CtrlK_Alone_EntersBlockPrefix()
    {
        var ed = MakeEd("hello");
        SendKey(ed, Keys.kbCtrlK);
        Assert.NotEqual(0, ed.keyState);
        Assert.Equal("hello", ReadAll(ed));
    }

    // ── 22d.2 — Ctrl-Q alone enters quick prefix ──────────────────────────────

    [Fact]
    public void CtrlQ_Alone_EntersQuickPrefix()
    {
        var ed = MakeEd("hello");
        SendKey(ed, Keys.kbCtrlQ);
        Assert.NotEqual(0, ed.keyState);
        Assert.Equal("hello", ReadAll(ed));
    }

    // ── 22d.3 — Ctrl-K + unrecognised key: no throw, keyState cleared ─────────

    [Fact]
    public void CtrlK_UnrecognisedKey_ClearsPrefix()
    {
        var ed = MakeEd("hello");
        SendKey(ed, Keys.kbCtrlK);

        var ex = Record.Exception(() => SendKey(ed, 0x0037, (byte)'Z'));
        Assert.Null(ex);
        Assert.Equal(0, ed.keyState);
        Assert.Equal("hello", ReadAll(ed));
    }

    // ── 22d.4 — Ctrl-Q + unrecognised key: no throw, keyState cleared ─────────

    [Fact]
    public void CtrlQ_UnrecognisedKey_ClearsPrefix()
    {
        var ed = MakeEd("hello");
        SendKey(ed, Keys.kbCtrlQ);

        var ex = Record.Exception(() => SendKey(ed, 0x0037, (byte)'Z'));
        Assert.Null(ex);
        Assert.Equal(0, ed.keyState);
        Assert.Equal("hello", ReadAll(ed));
    }

    // ── 22d.5 — Ctrl-K B + cmCharRight×4 + Ctrl-K K → marks block ──────────
    // Ctrl-K K in block prefix mode: kbCtrlK (0x010b) → letter = 'K' →
    // MapBlockKey('K') = cmCopy.  selEnd is already set by cursor movement.

    [Fact]
    public void CtrlKB_CtrlKK_MarksBlock()
    {
        var ed = MakeEd("hello world");
        // cursor is at 0 from MakeEd

        // Ctrl-K B → cmStartSelect (selecting = true)
        SendKey(ed, Keys.kbCtrlK);
        SendKey(ed, Keys.kbCtrlB);

        // Advance cursor 4 chars (smExtend extends selection)
        for (int i = 0; i < 4; i++)
            SendCmd(ed, Views.cmCharRight);

        // Ctrl-K K → cmCopy (marks block end; selEnd already = 4)
        SendKey(ed, Keys.kbCtrlK);
        SendKey(ed, Keys.kbCtrlK); // kbCtrlK in prefix → letter='K' → cmCopy

        Assert.Equal(0u, ed.selStart);
        Assert.Equal(4u, ed.selEnd);
        Assert.Equal("hello world", ReadAll(ed));
    }

    // ── 22d.6 — Ctrl-K C: paste from clipboard ───────────────────────────────

    [Fact]
    public void CtrlKC_PastesFromClipboard()
    {
        using var scope = new EditorClipboardScope();

        // Build a clipboard editor with "XYZ"
        var clip = MakeEd("XYZ");
        clip.SetSelect(0, 3, false);
        TEditor.clipboard = clip;

        // Target editor
        var ed = MakeEd("WORLD");
        ed.SetSelect(0, 0, true); // cursor at start

        // Ctrl-K C → cmPaste
        SendKey(ed, Keys.kbCtrlK);
        SendKey(ed, Keys.kbCtrlC); // 'C' prefix key

        string result = ReadAll(ed);
        Assert.StartsWith("XYZ", result);
    }

    // ── 22d.7 — Ctrl-K Y: cut selected block ─────────────────────────────────

    [Fact]
    public void CtrlKY_CutsSelectedBlock()
    {
        using var scope = new EditorClipboardScope();
        // Provide a clipboard editor so ClipCut() succeeds.
        var clip = MakeEd("");
        TEditor.clipboard = clip;

        var ed = MakeEd("HELLO WORLD");
        ed.SetSelect(0, 5, false); // select "HELLO"

        // Ctrl-K Y → cmCut
        SendKey(ed, Keys.kbCtrlK);
        SendKey(ed, Keys.kbCtrlY); // 'Y' prefix key

        Assert.Equal(" WORLD", ReadAll(ed));
        Assert.NotNull(TEditor.clipboard);
        Assert.Equal("HELLO", ReadAll(TEditor.clipboard));
    }

    // ── 22d.8 — Ctrl-K H: hide selection ─────────────────────────────────────

    [Fact]
    public void CtrlKH_HidesSelection()
    {
        var ed = MakeEd("hello world");
        ed.SetSelect(2, 7, false);
        ed.selecting = true;

        // Ctrl-K H → cmHideSelect
        SendKey(ed, Keys.kbCtrlK);
        SendKey(ed, Keys.kbCtrlH); // 'H' prefix key

        Assert.False(ed.selecting);
        Assert.Equal(ed.selStart, ed.selEnd);
    }

    // ── 22d.9 — Ctrl-Q F: invokes Find ───────────────────────────────────────

    [Fact]
    public void CtrlQF_InvokesFind()
    {
        using var scope = new EditorClipboardScope();
        bool invoked = false;
        TEditor.editorDialog = (d, info) =>
        {
            if (d == Views.edFind && info is TFindDialogRec r)
            {
                invoked = true;
                TEditorFindDialog.StringToBytes("needle", r.Find);
                r.Options = Views.efCaseSensitive;
                return Views.cmOK;
            }
            return Views.cmCancel;
        };

        var ed = MakeEd("find the needle here");
        ed.SetSelect(0, 0, true);

        // Ctrl-Q F → cmFind
        SendKey(ed, Keys.kbCtrlQ);
        SendKey(ed, Keys.kbCtrlF); // 'F' prefix key

        Assert.True(invoked);
        Assert.True(ed.selStart > 0);
    }

    // ── 22d.10 — Ctrl-Q A: invokes Replace ───────────────────────────────────

    [Fact]
    public void CtrlQA_InvokesReplace()
    {
        using var scope = new EditorClipboardScope();
        bool invoked = false;
        TEditor.editorDialog = (d, info) =>
        {
            if (d == Views.edReplace && info is TReplaceDialogRec r)
            {
                invoked = true;
                TEditorFindDialog.StringToBytes("cat", r.Find);
                TEditorFindDialog.StringToBytes("dog", r.Replace);
                r.Options = (ushort)(Views.efCaseSensitive | Views.efReplaceAll);
                return Views.cmOK;
            }
            return Views.cmCancel;
        };

        var ed = MakeEd("the cat sat");
        ed.SetSelect(0, 0, true);

        // Ctrl-Q A → cmReplace
        SendKey(ed, Keys.kbCtrlQ);
        SendKey(ed, Keys.kbCtrlA); // 'A' prefix key

        Assert.True(invoked);
        Assert.Equal("the dog sat", ReadAll(ed));
    }

    // ── 22d.11 — Ctrl-Q Y: delete to end of line ─────────────────────────────

    [Fact]
    public void CtrlQY_DeletesToEndOfLine()
    {
        var ed = MakeEd("ABC\nDEF");
        // Move cursor to position 1 ('B')
        ed.SetCurPtr(1, 0);

        // Ctrl-Q Y → cmDelEnd
        SendKey(ed, Keys.kbCtrlQ);
        SendKey(ed, Keys.kbCtrlY); // 'Y' prefix key

        // "A\nDEF" expected — B and C deleted
        Assert.Equal("A\nDEF", ReadAll(ed));
    }

    // ── 22d.12 — Ctrl-Q S: move to line start ────────────────────────────────

    [Fact]
    public void CtrlQS_MovesToLineStart()
    {
        var ed = MakeEd("ABCDE");
        ed.SetCurPtr(3, 0);

        // Ctrl-Q S → cmLineStart
        SendKey(ed, Keys.kbCtrlQ);
        SendKey(ed, Keys.kbCtrlS); // 'S' prefix key

        Assert.Equal(0u, ed.curPtr);
        Assert.Equal("ABCDE", ReadAll(ed));
    }

    // ── 22d.13 — Ctrl-Q D: move to line end ──────────────────────────────────

    [Fact]
    public void CtrlQD_MovesToLineEnd()
    {
        var ed = MakeEd("ABCDE");
        ed.SetCurPtr(2, 0);

        // Ctrl-Q D → cmLineEnd
        SendKey(ed, Keys.kbCtrlQ);
        SendKey(ed, Keys.kbCtrlD); // 'D' prefix key

        Assert.Equal(ed.bufLen, ed.curPtr);
        Assert.Equal("ABCDE", ReadAll(ed));
    }

    // ── 22d.14 — Ctrl-Q C: move to text end ──────────────────────────────────

    [Fact]
    public void CtrlQC_MovesToTextEnd()
    {
        var ed = MakeEd("line1\nline2");
        ed.SetCurPtr(0, 0);

        // Ctrl-Q C → cmTextEnd
        SendKey(ed, Keys.kbCtrlQ);
        SendKey(ed, Keys.kbCtrlC); // 'C' prefix key

        Assert.Equal(ed.bufLen, ed.curPtr);
    }

    // ── 22d.15 — Ctrl-Q R: move to text start ────────────────────────────────

    [Fact]
    public void CtrlQR_MovesToTextStart()
    {
        var ed = MakeEd("line1\nline2");
        ed.SetCurPtr(ed.bufLen, 0);

        // Ctrl-Q R → cmTextStart
        SendKey(ed, Keys.kbCtrlQ);
        SendKey(ed, Keys.kbCtrlR); // 'R' prefix key

        Assert.Equal(0u, ed.curPtr);
    }

    // ── 22d.16 — Esc clears prefix state ──────────────────────────────────────

    [Fact]
    public void Esc_ClearsPrefixState()
    {
        var ed = MakeEd("hello");
        SendKey(ed, Keys.kbCtrlK);  // enter block prefix
        Assert.NotEqual(0, ed.keyState);

        // kbEsc while in prefix → clears state
        SendKey(ed, Keys.kbEsc);
        Assert.Equal(0, ed.keyState);
        Assert.Equal("hello", ReadAll(ed));
    }

    // ── 22d.17 — Normal typing works after completed prefix command ───────────

    [Fact]
    public void NormalTyping_WorksAfterPrefixCommand()
    {
        var ed = MakeEd("hello");
        ed.SetSelect(0, 0, true); // cursor at start

        // Ctrl-K B → marks block start (cmStartSelect)
        SendKey(ed, Keys.kbCtrlK);
        SendKey(ed, Keys.kbCtrlB); // 'B' in prefix

        // keyState should be 0 after prefix consumed
        Assert.Equal(0, ed.keyState);

        // Type 'X' as evKeyDown with charCode 'X'
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.charScan.charCode = (byte)'X';
        ev.keyDown.keyCode = 0x0058; // kbX
        ed.HandleEvent(ref ev);

        Assert.Contains("X", ReadAll(ed));
    }
}
