// Migrated from SharpVision.Demo/Program.cs lines 2314-2583.
using SharpVision;
using SharpVision.Constants;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.Dialogs;

[Collection("NonParallel")]
public sealed class InputLineListBoxHistoryTests : IDisposable
{
    private readonly DriverScope _ds;
    public InputLineListBoxHistoryTests() => _ds = new DriverScope();
    public void Dispose() => _ds.Dispose();

    // ── TInputLine ───────────────────────────────────────────────────────

    [Fact]
    public void InputLine_Constructor_Fields()
    {
        var il = new TInputLine(new TRect(0, 0, 12, 1), 16);
        Assert.Equal(15, il.MaxLen);                              // aMaxLen-1
        Assert.Equal(string.Empty, il.Data);
        Assert.True((il.state & Views.sfCursorVis) != 0);
        Assert.True((il.options & Views.ofSelectable) != 0);
        Assert.True((il.options & Views.ofFirstClick) != 0);
        Assert.Equal(0x13, il.GetPalette()[1]);
        Assert.Equal(16, il.DataSize());
    }

    [Fact]
    public void InputLine_SetData_GetData_RoundTrip()
    {
        var il = new TInputLine(new TRect(0, 0, 12, 1), 8);
        il.SetData("hello");
        object got = null;
        il.GetData(ref got);
        Assert.True(got is string g && g == "hello");
        Assert.Equal("hello", il.Data);
    }

    [Fact]
    public void InputLine_SetData_TruncatesToMaxLen()
    {
        var il = new TInputLine(new TRect(0, 0, 12, 1), 8);
        il.SetData("0123456789ABCD");
        Assert.Equal(il.MaxLen, il.Data.Length);
    }

    [Fact]
    public void InputLine_InsertChar_AppendsThenCursorAdvances()
    {
        var il = new TInputLine(new TRect(0, 0, 12, 1), 16);
        il.InsertChar('a'); il.InsertChar('b'); il.InsertChar('c');
        Assert.Equal("abc", il.Data);
        Assert.Equal(3, il.CurPos);
    }

    [Fact]
    public void InputLine_KbBack_RemovesLastChar()
    {
        var il = new TInputLine(new TRect(0, 0, 12, 1), 16);
        il.InsertChar('a'); il.InsertChar('b'); il.InsertChar('c');
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.Insert(il);
        il.SetState(Views.sfSelected, true);

        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbBack;
        il.HandleEvent(ref ev);
        Assert.Equal("ab", il.Data);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void InputLine_KbDel_RemovesCharUnderCursor()
    {
        var il = new TInputLine(new TRect(0, 0, 12, 1), 16);
        il.SetData("ab");
        // Place cursor at start by sending kbHome
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.Insert(il);
        il.SetState(Views.sfSelected, true);
        var home = new TEvent { What = Events.evKeyDown };
        home.keyDown.keyCode = Keys.kbHome;
        il.HandleEvent(ref home);
        Assert.Equal(0, il.CurPos);
        // Now delete character at cursor (position 0 = 'a')
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbDel;
        il.HandleEvent(ref ev);
        Assert.Equal("b", il.Data);
    }

    [Fact]
    public void InputLine_PrintableChar_InsertsViaHandleEvent()
    {
        var il = new TInputLine(new TRect(0, 0, 12, 1), 16);
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.Insert(il);
        il.SetState(Views.sfSelected, true);

        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = 0;
        ev.keyDown.charScan.charCode = (byte)'X';
        il.HandleEvent(ref ev);
        Assert.Equal("X", il.Data);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void InputLine_SelectAll_True_SelectsWhole()
    {
        var il = new TInputLine(new TRect(0, 0, 12, 1), 16);
        il.Data = "hello";
        il.SelectAll(true);
        Assert.Equal(0, il.SelStart);
        Assert.Equal(5, il.SelEnd);
        Assert.Equal(5, il.CurPos);
    }

    [Fact]
    public void InputLine_SelectAll_False_ClearsSelection()
    {
        var il = new TInputLine(new TRect(0, 0, 12, 1), 16);
        il.Data = "hello";
        il.SelectAll(true);
        il.SelectAll(false);
        Assert.Equal(0, il.SelStart);
        Assert.Equal(0, il.SelEnd);
        Assert.Equal(0, il.CurPos);
    }

    // ── TListBox ─────────────────────────────────────────────────────────

    [Fact]
    public void ListBox_NewList_SetsRange()
    {
        var col = new TStringCollection();
        col.Insert("alpha"); col.Insert("bravo"); col.Insert("charlie");
        var lb = new TListBox(new TRect(0, 0, 20, 5), 1, null);
        lb.NewList(col);
        Assert.Equal(3, lb.range);
    }

    [Fact]
    public void ListBox_GetText_ReturnsItems()
    {
        var col = new TStringCollection();
        col.Insert("alpha"); col.Insert("bravo"); col.Insert("charlie");
        var lb = new TListBox(new TRect(0, 0, 20, 5), 1, null);
        lb.NewList(col);
        Assert.Equal("alpha",   lb.GetText(0, 80));
        Assert.Equal("charlie", lb.GetText(2, 80));
    }

    [Fact]
    public void ListBox_List_ReturnsSameCollection()
    {
        var col = new TStringCollection();
        col.Insert("alpha");
        var lb = new TListBox(new TRect(0, 0, 20, 5), 1, null);
        lb.NewList(col);
        Assert.Same(col, lb.List());
    }

    [Fact]
    public void ListBox_GetData_ReturnsTListBoxRec()
    {
        var col = new TStringCollection();
        col.Insert("alpha"); col.Insert("bravo");
        var lb = new TListBox(new TRect(0, 0, 20, 5), 1, null);
        lb.NewList(col);
        object data = null;
        lb.GetData(ref data);
        Assert.IsType<TListBoxRec>(data);
        var rec = (TListBoxRec)data;
        Assert.Same(col, rec.Items);
        Assert.Equal(0, rec.Selection);
    }

    [Fact]
    public void ListBox_SetData_FocusesSelection()
    {
        var col = new TStringCollection();
        col.Insert("alpha"); col.Insert("bravo"); col.Insert("charlie");
        var lb = new TListBox(new TRect(0, 0, 20, 5), 1, null);
        lb.NewList(col);
        var rec = new TListBoxRec { Items = col, Selection = 2 };
        lb.SetData(rec);
        Assert.Equal(2, lb.focused);
    }

    // ── THistoryList ─────────────────────────────────────────────────────

    [Fact]
    public void HistoryList_Add_Count_Str()
    {
        THistoryList.Clear();
        THistoryList.Add(7, "alpha");
        THistoryList.Add(7, "bravo");
        THistoryList.Add(7, "charlie");
        Assert.Equal(3, THistoryList.Count(7));
        Assert.Equal("charlie", THistoryList.Str(7, 0));   // most recent
        Assert.Equal("alpha",   THistoryList.Str(7, 2));   // oldest
    }

    [Fact]
    public void HistoryList_ReAdd_MovesToFront()
    {
        THistoryList.Clear();
        THistoryList.Add(8, "alpha");
        THistoryList.Add(8, "bravo");
        THistoryList.Add(8, "charlie");
        THistoryList.Add(8, "alpha");   // dedupe + bring to front
        Assert.Equal(3, THistoryList.Count(8));
        Assert.Equal("alpha", THistoryList.Str(8, 0));
    }

    [Fact]
    public void HistoryList_UnknownId_ReturnsEmpty()
    {
        THistoryList.Clear();
        Assert.Equal(0, THistoryList.Count(99));
        Assert.Equal(string.Empty, THistoryList.Str(99, 0));
    }

    // ── THistory ─────────────────────────────────────────────────────────

    [Fact]
    public void History_Constructor_Fields()
    {
        var il = new TInputLine(new TRect(0, 0, 12, 1), 16);
        var h  = new THistory(new TRect(12, 0, 15, 1), il, 7);
        Assert.Same(il, h.Link);
        Assert.Equal(7, h.HistoryId);
        Assert.True((h.options & Views.ofPostProcess) != 0);
        Assert.True((h.eventMask & Events.evBroadcast) != 0);
        Assert.Equal(0x16, h.GetPalette()[1]);
    }

    [Fact]
    public void History_CmRecordHistory_AddsLinkData()
    {
        THistoryList.Clear();
        var il = new TInputLine(new TRect(0, 0, 12, 1), 16);
        il.Data = "logged";
        var h = new THistory(new TRect(12, 0, 15, 1), il, 9);
        var ev = new TEvent { What = Events.evBroadcast };
        ev.message.command = Views.cmRecordHistory;
        h.HandleEvent(ref ev);
        Assert.Equal(1, THistoryList.Count(9));
        Assert.Equal("logged", THistoryList.Str(9, 0));
    }

    [Fact]
    public void History_ShutDown_NullsLink()
    {
        var il = new TInputLine(new TRect(0, 0, 12, 1), 16);
        var h = new THistory(new TRect(12, 0, 15, 1), il, 7);
        h.ShutDown();
        Assert.Null(h.Link);
    }

    // ── THistoryViewer ────────────────────────────────────────────────────

    [Fact]
    public void HistoryViewer_Range_And_GetText()
    {
        THistoryList.Clear();
        THistoryList.Add(11, "first");
        THistoryList.Add(11, "second");
        var hv = new THistoryViewer(new TRect(0, 0, 20, 5), null, null, 11);
        Assert.Equal(2, hv.range);
        Assert.Equal("second", hv.GetText(0, 80));  // most recent first
        Assert.Equal(6, hv.HistoryWidth());          // max("second".Length)
    }

    [Fact]
    public void HistoryViewer_Palette()
    {
        THistoryList.Clear();
        THistoryList.Add(12, "x");
        var hv = new THistoryViewer(new TRect(0, 0, 20, 5), null, null, 12);
        Assert.Equal(0x06, hv.GetPalette()[1]);
    }

    [Fact]
    public void HistoryViewer_KbEnter_ClearsEvent()
    {
        THistoryList.Clear();
        THistoryList.Add(13, "x");
        var hv = new THistoryViewer(new TRect(0, 0, 20, 5), null, null, 13);
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbEnter;
        hv.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void HistoryViewer_KbEsc_ClearsEvent()
    {
        THistoryList.Clear();
        THistoryList.Add(14, "x");
        var hv = new THistoryViewer(new TRect(0, 0, 20, 5), null, null, 14);
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbEsc;
        hv.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
    }

    // ── THistoryWindow ────────────────────────────────────────────────────

    [Fact]
    public void HistoryWindow_Constructor()
    {
        THistoryList.Clear();
        THistoryList.Add(17, "saved");
        var hw = new THistoryWindow(new TRect(0, 0, 20, 8), 17);
        Assert.NotNull(hw.Viewer);
        Assert.Equal(Views.wfClose, hw.flags);
        Assert.Equal(0x13, hw.GetPalette()[1]);
        Assert.Equal("saved", hw.GetSelection());
    }

    // ── MsgBox builders ──────────────────────────────────────────────────

    [Fact]
    public void BuildMessageBox_HasButtonAndText()
    {
        var dlg = MsgBox.BuildMessageBox(new TRect(0, 0, 40, 9),
            "Hello", MsgBox.mfInformation | MsgBox.mfOKButton);
        Assert.NotNull(dlg);
        int btnCount = 0, txtCount = 0;
        if (dlg.last != null)
        {
            TView p = dlg.last.Next;
            do
            {
                if (p is TButton) btnCount++;
                else if (p is TStaticText) txtCount++;
                p = p.Next;
            } while (p != dlg.last.Next);
        }
        Assert.Equal(1, btnCount);
        Assert.True(txtCount >= 1);
    }

    [Fact]
    public void BuildInputBox_HasTwoButtonsInputAndLabel()
    {
        var dlg = MsgBox.BuildInputBox(new TRect(0, 0, 40, 7),
            "Title", "Name:", "John", 16);
        int btnCount = 0, ilCount = 0, labCount = 0;
        TInputLine input = null;
        if (dlg.last != null)
        {
            TView p = dlg.last.Next;
            do
            {
                if (p is TButton) btnCount++;
                else if (p is TInputLine il) { ilCount++; input = il; }
                else if (p is TLabel) labCount++;
                p = p.Next;
            } while (p != dlg.last.Next);
        }
        Assert.Equal(2, btnCount);
        Assert.Equal(1, ilCount);
        Assert.Equal(1, labCount);
        Assert.NotNull(input);
        Assert.Equal("John", input.Data);
    }
}
