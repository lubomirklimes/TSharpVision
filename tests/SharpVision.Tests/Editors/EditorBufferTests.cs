using System;
using System.Text;
using SharpVision;
using SharpVision.Constants;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.Editors;

public sealed class EditorBufferTests
{
    // ── editor construction helper ───────────────────────────────────────────
    static TEditor MakeEditor(string text)
    {
        var ed = new TEditor(new TRect(0, 0, 40, 10), null, null, null, 1024);
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        ed.bufLen = (uint)bytes.Length;
        ed.gapLen = ed.bufSize - ed.bufLen;
        Array.Copy(bytes, 0, ed.buffer, (int)ed.gapLen, bytes.Length);
        ed.curPtr  = 0;
        ed.curPos  = default;
        ed.delta   = default;
        ed.drawLine = 0;
        ed.drawPtr  = 0;
        ed.limit.x  = Views.maxLineLength;
        int lines = 1;
        foreach (byte b in bytes) if (b == 0x0A) lines++;
        ed.limit.y = lines;
        return ed;
    }

    // ── editor constants ─────────────────────────────────────────────────────

    [Fact] public void Const_ufUpdate()    => Assert.Equal(0x01, Views.ufUpdate);
    [Fact] public void Const_ufLine()      => Assert.Equal(0x02, Views.ufLine);
    [Fact] public void Const_ufView()      => Assert.Equal(0x04, Views.ufView);
    [Fact] public void Const_smExtend()    => Assert.Equal(0x01, Views.smExtend);
    [Fact] public void Const_smDouble()    => Assert.Equal(0x02, Views.smDouble);
    [Fact] public void Const_cmCharLeft()  => Assert.Equal(500, (int)Views.cmCharLeft);
    [Fact] public void Const_cmCharRight() => Assert.Equal(501, (int)Views.cmCharRight);
    [Fact] public void Const_cmLineUp()    => Assert.Equal(506, (int)Views.cmLineUp);
    [Fact] public void Const_cmTextEnd()   => Assert.Equal(511, (int)Views.cmTextEnd);
    [Fact] public void Const_cmNewLine()   => Assert.Equal(512, (int)Views.cmNewLine);
    [Fact] public void Const_maxLineLength()   => Assert.Equal(256, (int)Views.maxLineLength);
    [Fact] public void Const_sfSearchFailed()  => Assert.Equal(uint.MaxValue, Views.sfSearchFailed);

    // ── constructor fields ───────────────────────────────────────────────────

    [Fact]
    public void Ctor_BufferAllocated()
    {
        var ed = new TEditor(new TRect(0, 0, 40, 10), null, null, null, 512);
        Assert.Equal(512u, ed.bufSize);
        Assert.NotNull(ed.buffer);
        Assert.Equal(512, ed.buffer.Length);
    }

    [Fact]
    public void Ctor_IsValid() => Assert.True(new TEditor(new TRect(0,0,40,10), null,null,null, 256).isValid);

    [Fact]
    public void Ctor_BufLenZero() => Assert.Equal(0u, new TEditor(new TRect(0,0,40,10), null,null,null, 256).bufLen);

    [Fact]
    public void Ctor_GapLen_EqualsBufSize()
    {
        var ed = new TEditor(new TRect(0,0,40,10), null,null,null, 256);
        Assert.Equal(256u, ed.gapLen);
    }

    [Fact]
    public void Ctor_CurPtr_Zero() => Assert.Equal(0u, new TEditor(new TRect(0,0,40,10), null,null,null, 256).curPtr);

    [Fact]
    public void Ctor_GrowMode()
    {
        var ed = new TEditor(new TRect(0,0,40,10), null,null,null, 256);
        Assert.Equal((byte)(Views.gfGrowHiX | Views.gfGrowHiY), ed.growMode);
    }

    [Fact]
    public void Ctor_CanUndo_True() => Assert.True(new TEditor(new TRect(0,0,40,10), null,null,null, 256).canUndo);

    [Fact]
    public void Ctor_NotModified() => Assert.False(new TEditor(new TRect(0,0,40,10), null,null,null, 256).modified);

    [Fact]
    public void Ctor_Valid_Returns_IsValid()
    {
        var ed = new TEditor(new TRect(0,0,40,10), null,null,null, 256);
        Assert.True(ed.Valid(Views.cmCancel));
    }

    // ── BufChar / BufPtr ────────────────────────────────────────────────────

    [Fact]
    public void BufChar_First() => Assert.Equal('h', (char)MakeEditor("hello").BufChar(0));

    [Fact]
    public void BufChar_Last() => Assert.Equal('o', (char)MakeEditor("hello").BufChar(4));

    [Fact]
    public void BufPtr_CrossesGap()
    {
        var ed = MakeEditor("hello");
        Assert.Equal(ed.gapLen, ed.BufPtr(0));
    }

    // ── LineStart / LineEnd / NextLine / PrevLine ────────────────────────────

    [Fact]
    public void LineEnd_Line0() => Assert.Equal(5u, MakeEditor("alpha\nbeta\ngamma").LineEnd(0));

    [Fact]
    public void LineEnd_Line1() => Assert.Equal(10u, MakeEditor("alpha\nbeta\ngamma").LineEnd(6));

    [Fact]
    public void LineStart_Line0() => Assert.Equal(0u, MakeEditor("alpha\nbeta\ngamma").LineStart(0));

    [Fact]
    public void LineStart_Mid() => Assert.Equal(6u, MakeEditor("alpha\nbeta\ngamma").LineStart(8));

    [Fact]
    public void NextLine_Line0() => Assert.Equal(6u, MakeEditor("alpha\nbeta\ngamma").NextLine(0));

    [Fact]
    public void PrevLine_Line2() => Assert.Equal(6u, MakeEditor("alpha\nbeta\ngamma").PrevLine(11));

    // ── NextChar / PrevChar ──────────────────────────────────────────────────

    [Fact]
    public void PrevChar_AtStart() => Assert.Equal(0u, MakeEditor("ab").PrevChar(0));

    [Fact]
    public void NextChar_AtEnd()
    {
        var ed = MakeEditor("ab");
        Assert.Equal(ed.bufLen, ed.NextChar(ed.bufLen));
    }

    [Fact]
    public void NextChar_Mid() => Assert.Equal(1u, MakeEditor("ab").NextChar(0));

    [Fact]
    public void PrevChar_Mid() => Assert.Equal(1u, MakeEditor("ab").PrevChar(2));

    // ── LineMove ─────────────────────────────────────────────────────────────

    [Fact]
    public void LineMove_Down1() => Assert.Equal(6u, MakeEditor("alpha\nbeta\ngamma").LineMove(0, 1));

    [Fact]
    public void LineMove_Down2() => Assert.Equal(11u, MakeEditor("alpha\nbeta\ngamma").LineMove(0, 2));

    [Fact]
    public void LineMove_Up1() => Assert.Equal(6u, MakeEditor("alpha\nbeta\ngamma").LineMove(11, -1));

    [Fact]
    public void LineMove_Up2() => Assert.Equal(0u, MakeEditor("alpha\nbeta\ngamma").LineMove(11, -2));

    // ── NextWord / PrevWord ──────────────────────────────────────────────────

    [Fact]
    public void NextWord_SkipsWord()  => Assert.Equal(4u, MakeEditor("foo bar  baz").NextWord(0));

    [Fact]
    public void NextWord_PastBar()    => Assert.Equal(9u, MakeEditor("foo bar  baz").NextWord(4));

    [Fact]
    public void PrevWord_FindsLast()  => Assert.Equal(9u, MakeEditor("foo bar  baz").PrevWord(12));

    [Fact]
    public void PrevWord_FindsBar()   => Assert.Equal(4u, MakeEditor("foo bar  baz").PrevWord(7));

    // ── CharPos with tab ─────────────────────────────────────────────────────

    [Fact]
    public void CharPos_TabExpands()
    {
        var ed = MakeEditor("\tx");
        Assert.Equal(8, (int)ed.CharPos(0, 1));
    }

    [Fact]
    public void CharPos_AfterTab()
    {
        var ed = MakeEditor("\tx");
        Assert.Equal(9, (int)ed.CharPos(0, 2));
    }

    // ── HideSelect / HasSelection ────────────────────────────────────────────

    [Fact]
    public void HasSelection_True_AfterSetSelect()
    {
        var ed = MakeEditor("hello world");
        ed.SetSelect(2, 5, false);
        Assert.True(ed.HasSelection());
    }

    [Fact]
    public void HideSelect_ClearsSelection()
    {
        var ed = MakeEditor("hello world");
        ed.SetSelect(2, 5, false);
        ed.HideSelect();
        Assert.False(ed.HasSelection());
    }

    [Fact]
    public void HideSelect_ClearsSelectingFlag()
    {
        var ed = MakeEditor("hello world");
        ed.SetSelect(2, 5, false);
        ed.HideSelect();
        Assert.False(ed.selecting);
    }

    // ── SetCurPtr ────────────────────────────────────────────────────────────

    [Fact]
    public void SetCurPtr_NoExtend()
    {
        var ed = MakeEditor("hello world");
        ed.SetCurPtr(2, 0);
        Assert.Equal(2u, ed.curPtr);
        Assert.Equal(ed.selStart, ed.selEnd);
    }

    [Fact]
    public void SetCurPtr_ExtendRight()
    {
        var ed = MakeEditor("hello world");
        ed.SetCurPtr(2, 0);
        ed.SetCurPtr(7, Views.smExtend);
        Assert.Equal(2u, ed.selStart);
        Assert.Equal(7u, ed.selEnd);
        Assert.Equal(7u, ed.curPtr);
    }

    // ── SetSelect ────────────────────────────────────────────────────────────

    [Fact]
    public void SetSelect_CurStart_False()
    {
        var ed = MakeEditor("hello world");
        ed.SetSelect(3, 8, false);
        Assert.Equal(ed.selEnd, ed.curPtr);
    }

    [Fact]
    public void SetSelect_CurStart_True()
    {
        var ed = MakeEditor("hello world");
        ed.SetSelect(3, 8, true);
        Assert.Equal(ed.selStart, ed.curPtr);
    }

    // ── ScrollTo ─────────────────────────────────────────────────────────────

    [Fact]
    public void ScrollTo_UpdatesDelta()
    {
        var ed = MakeEditor("");
        ed.limit.x = 200; ed.limit.y = 100;
        ed.ScrollTo(20, 30);
        Assert.Equal(20, ed.delta.x);
        Assert.Equal(30, ed.delta.y);
    }

    [Fact]
    public void ScrollTo_ClampsToZero()
    {
        var ed = MakeEditor("");
        ed.limit.x = 200; ed.limit.y = 100;
        ed.ScrollTo(-5, -5);
        Assert.Equal(0, ed.delta.x);
        Assert.Equal(0, ed.delta.y);
    }

    [Fact]
    public void ScrollTo_ClampsToMax()
    {
        var ed = MakeEditor("");
        ed.limit.x = 200; ed.limit.y = 100;
        ed.ScrollTo(10000, 10000);
        Assert.Equal(ed.limit.x - ed.size.x, ed.delta.x);
        Assert.Equal(ed.limit.y - ed.size.y, ed.delta.y);
    }

    // ── Lock / Unlock ────────────────────────────────────────────────────────

    [Fact]
    public void Lock_DefersUpdate()
    {
        var ed = MakeEditor("");
        ed.Lock();
        ed.Update(Views.ufLine);
        Assert.NotEqual(0, ed.updateFlags & Views.ufLine);
    }

    [Fact]
    public void Unlock_FlushesFlags()
    {
        var ed = MakeEditor("");
        ed.Lock();
        ed.Update(Views.ufLine);
        ed.Unlock();
        Assert.Equal(0, ed.updateFlags);
    }

    // ── ToggleInsMode ────────────────────────────────────────────────────────

    [Fact]
    public void ToggleInsMode_FlipsOverwrite()
    {
        var ed = MakeEditor("");
        Assert.False(ed.overwrite);
        ed.ToggleInsMode();
        Assert.True(ed.overwrite);
    }

    [Fact]
    public void ToggleInsMode_FlipsCursorIns()
    {
        var ed = MakeEditor("");
        bool was = ed.GetState(Views.sfCursorIns);
        ed.ToggleInsMode();
        Assert.NotEqual(was, ed.GetState(Views.sfCursorIns));
    }

    // ── ConvertEvent ─────────────────────────────────────────────────────────

    [Fact]
    public void ConvertEvent_kbRight_ToCmCharRight()
    {
        var ed = MakeEditor("");
        var kev = new TEvent { What = Events.evKeyDown };
        kev.keyDown.keyCode = Keys.kbRight;
        ed.ConvertEvent(ref kev);
        Assert.Equal(Events.evCommand, kev.What);
        Assert.Equal(Views.cmCharRight, kev.message.command);
    }

    [Fact]
    public void ConvertEvent_kbCtrlPgDn_ToCmTextEnd()
    {
        var ed = MakeEditor("");
        var kev = new TEvent { What = Events.evKeyDown };
        kev.keyDown.keyCode = Keys.kbCtrlPgDn;
        ed.ConvertEvent(ref kev);
        Assert.Equal(Events.evCommand, kev.What);
        Assert.Equal(Views.cmTextEnd, kev.message.command);
    }

    // ── HandleEvent navigation ───────────────────────────────────────────────

    [Fact]
    public void HandleEvent_CmCharRight_AdvancesCurPtr()
    {
        var ed = MakeEditor("hello");
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmCharRight;
        ed.HandleEvent(ref ev);
        Assert.Equal(1u, ed.curPtr);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void HandleEvent_CmTextEnd_JumpsToBufLen()
    {
        var ed = MakeEditor("alpha\nbeta");
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmTextEnd;
        ed.HandleEvent(ref ev);
        Assert.Equal(ed.bufLen, ed.curPtr);
    }

    [Fact]
    public void HandleEvent_CmLineDown_MovesDown()
    {
        var ed = MakeEditor("alpha\nbeta\ngamma");
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmLineDown;
        ed.HandleEvent(ref ev);
        Assert.Equal(6u, ed.curPtr);
    }
}
