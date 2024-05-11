// Dialog keyboard focus, StatusLine mouse interaction, and coordinate fix tests.
using SharpVision;
using SharpVision.Constants;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class DialogFocusStatusLineMouseTests : IDisposable
{
    private readonly DriverScope _ds;
    public DialogFocusStatusLineMouseTests()
    {
        _ds = new DriverScope();
        TEventQueue.Resume();
    }
    public void Dispose() => _ds.Dispose();

    private static void DrainQ()
    {
        for (int i = 0; i < 64; i++)
        {
            var d = new TEvent();
            TEventQueue.GetMouseEvent(ref d);
            if (d.What == Events.evNothing) break;
        }
    }
    private static TEvent PeekQ()
    {
        var e = new TEvent { What = Events.evNothing };
        TEventQueue.GetMouseEvent(ref e);
        return e;
    }

    // ── Dialog Tab focus ───────────────────────────────────────

    private static (TDialog dlg, TStaticText st, TInputLine il1, TInputLine il2, TButton btn)
        MakeTabDialog()
    {
        var dlg = new TDialog(new TRect(0, 0, 50, 10), "Tab Test");
        var st  = new TStaticText(new TRect(2, 1, 20, 2), "Name:");
        var il1 = new TInputLine(new TRect(2, 2, 20, 3), 40);
        var il2 = new TInputLine(new TRect(2, 4, 20, 5), 40);
        var btn = new TButton(new TRect(2, 6, 12, 8), "~O~K", Views.cmOK, ButtonConstants.bfDefault);
        dlg.Insert(st); dlg.Insert(il1); dlg.Insert(il2); dlg.Insert(btn);
        return (dlg, st, il1, il2, btn);
    }

    [Fact]
    public void Dialog_Initial_Current_IsSelectable()
    {
        var (dlg, _, _, _, btn) = MakeTabDialog();
        Assert.NotNull(dlg.current);
        Assert.True((dlg.current.options & Views.ofSelectable) != 0);
    }

    [Fact]
    public void Dialog_Tab_From_BtnDef_To_Il1()
    {
        var (dlg, _, il1, _, _) = MakeTabDialog();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbTab;
        dlg.HandleEvent(ref ev);
        Assert.Same(il1, dlg.current);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void Dialog_Tab_Cycles_Il1_Il2_Btn()
    {
        var (dlg, _, il1, il2, btn) = MakeTabDialog();
        TEvent ev;
        // Tab: btn→il1
        ev = new TEvent { What = Events.evKeyDown }; ev.keyDown.keyCode = Keys.kbTab;
        dlg.HandleEvent(ref ev); Assert.Same(il1, dlg.current);
        // Tab: il1→il2
        ev = new TEvent { What = Events.evKeyDown }; ev.keyDown.keyCode = Keys.kbTab;
        dlg.HandleEvent(ref ev); Assert.Same(il2, dlg.current);
        // Tab: il2→btn (wrap)
        ev = new TEvent { What = Events.evKeyDown }; ev.keyDown.keyCode = Keys.kbTab;
        dlg.HandleEvent(ref ev); Assert.Same(btn, dlg.current);
    }

    [Fact]
    public void Dialog_ShiftTab_From_BtnDef_To_Il2()
    {
        var (dlg, _, _, il2, _) = MakeTabDialog();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbShiftTab;
        dlg.HandleEvent(ref ev);
        Assert.Same(il2, dlg.current);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void Dialog_ShiftTab_Cycles_Il2_Il1_Btn()
    {
        var (dlg, _, il1, il2, btn) = MakeTabDialog();
        TEvent ev;
        // ShiftTab: btn→il2
        ev = new TEvent { What = Events.evKeyDown }; ev.keyDown.keyCode = Keys.kbShiftTab;
        dlg.HandleEvent(ref ev); Assert.Same(il2, dlg.current);
        // ShiftTab: il2→il1
        ev = new TEvent { What = Events.evKeyDown }; ev.keyDown.keyCode = Keys.kbShiftTab;
        dlg.HandleEvent(ref ev); Assert.Same(il1, dlg.current);
        // ShiftTab: il1→btn (wrap)
        ev = new TEvent { What = Events.evKeyDown }; ev.keyDown.keyCode = Keys.kbShiftTab;
        dlg.HandleEvent(ref ev); Assert.Same(btn, dlg.current);
    }

    [Fact]
    public void Dialog_StaticText_NeverBecomesCurrent()
    {
        var (dlg, st, _, _, _) = MakeTabDialog();
        Assert.True((st.options & Views.ofSelectable) == 0);
        // Tab through all positions and verify st never selected
        for (int i = 0; i < 6; i++)
        {
            var ev2 = new TEvent { What = Events.evKeyDown };
            ev2.keyDown.keyCode = Keys.kbTab;
            dlg.HandleEvent(ref ev2);
            Assert.NotSame(st, dlg.current);
        }
    }

    [Fact]
    public void Dialog_Enter_WithInputFocused_QueuesCmDefault()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var dlg  = new TDialog(new TRect(5, 5, 45, 18), "EnterDefault");
        var il   = new TInputLine(new TRect(2, 2, 20, 3), 40);
        var btn  = new TButton(new TRect(2, 5, 12, 7), "~O~K", Views.cmOK, ButtonConstants.bfDefault);
        host.Insert(dlg); dlg.Insert(il); dlg.Insert(btn);
        // btn is current (first selectable inserted last). Navigate to il.
        dlg.SelectNext(false);   // btn→il
        Assert.Same(il, dlg.current);

        DrainQ();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbEnter;
        dlg.HandleEvent(ref ev);

        var q = PeekQ();
        Assert.Equal(Events.evBroadcast, q.What);
        Assert.Equal(Views.cmDefault, q.message.command);
        Assert.Equal(Events.evNothing, ev.What);
        DrainQ();
    }

    [Fact]
    public void Dialog_Enter_OnFocusedButton_QueuesCmOK()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var dlg  = new TDialog(new TRect(5, 5, 45, 18), "EnterButton");
        var btn  = new TButton(new TRect(2, 5, 12, 7), "~O~K", Views.cmOK, ButtonConstants.bfDefault);
        host.Insert(dlg); dlg.Insert(btn);
        dlg.SetState(Views.sfFocused, true);
        Assert.Same(btn, dlg.current);
        Assert.True((btn.state & Views.sfFocused) != 0);

        DrainQ();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbEnter;
        dlg.HandleEvent(ref ev);

        bool foundOK = false;
        for (int i = 0; i < 32; i++)
        {
            var d = PeekQ();
            if (d.What == Events.evNothing) break;
            if (d.What == Events.evCommand && d.message.command == Views.cmOK) { foundOK = true; break; }
        }
        Assert.True(foundOK);
        Assert.Equal(Events.evNothing, ev.What);
        DrainQ();
    }

    [Fact]
    public void Dialog_Esc_PostsCmCancel()
    {
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var dlg  = new TDialog(new TRect(5, 5, 45, 18), "Esc");
        host.Insert(dlg);
        DrainQ();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbEsc;
        dlg.HandleEvent(ref ev);
        var q = PeekQ();
        Assert.Equal(Events.evCommand, q.What);
        Assert.Equal(Views.cmCancel, q.message.command);
        DrainQ();
    }

    // ── StatusLine spacing / click hot areas ───────────────────

    private const ushort CmSL1 = 210, CmSL2 = 211, CmSL3 = 212;

    private TStatusLine MakeSL2(TRect r)
    {
        TView.EnableCommand(CmSL1); TView.EnableCommand(CmSL2); TView.EnableCommand(CmSL3);
        return new TStatusLine(r,
            new TStatusDef(0, 0xFFFF) +
                new TStatusItem("~F1~ Help", Keys.kbF1, CmSL1) +
                new TStatusItem("~F2~ Save", Keys.kbF2, CmSL2));
    }

    [Fact]
    public void StatusLine_Click_X3_HitsItem1()
    {
        var sl = MakeSL2(new TRect(0, 0, 80, 1));
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(3, 0); ev.mouse.buttons = 0x01;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(CmSL1, ev.message.command);
    }

    [Fact]
    public void StatusLine_Click_X8_TrailingEdge_HitsItem1()
    {
        var sl = MakeSL2(new TRect(0, 0, 80, 1));
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(8, 0); ev.mouse.buttons = 0x01;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(CmSL1, ev.message.command);
    }

    [Fact]
    public void StatusLine_Click_X9_LeadingSpaceItem2()
    {
        var sl = MakeSL2(new TRect(0, 0, 80, 1));
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(9, 0); ev.mouse.buttons = 0x01;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(CmSL2, ev.message.command);
    }

    [Fact]
    public void StatusLine_Click_X13_InsideItem2()
    {
        var sl = MakeSL2(new TRect(0, 0, 80, 1));
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(13, 0); ev.mouse.buttons = 0x01;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(CmSL2, ev.message.command);
    }

    [Fact]
    public void StatusLine_Click_X60_BeyondItems_EvNothing()
    {
        var sl = MakeSL2(new TRect(0, 0, 80, 1));
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(60, 0); ev.mouse.buttons = 0x01;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void StatusLine_Click_WrongRow_EvNothing()
    {
        var sl = MakeSL2(new TRect(0, 0, 80, 1));
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(3, 1); ev.mouse.buttons = 0x01;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void StatusLine_KbF1_KeyboardShortcut()
    {
        var sl = MakeSL2(new TRect(0, 0, 80, 1));
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbF1;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(CmSL1, ev.message.command);
    }

    [Fact]
    public void StatusLine_KbF2_KeyboardShortcut()
    {
        var sl = MakeSL2(new TRect(0, 0, 80, 1));
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbF2;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(CmSL2, ev.message.command);
    }

    [Fact]
    public void StatusLine_ThreeItems_X9_HitsItem3()
    {
        TView.EnableCommand(CmSL1); TView.EnableCommand(CmSL2); TView.EnableCommand(CmSL3);
        var sl3 = new TStatusLine(
            new TRect(0, 0, 80, 1),
            new TStatusDef(0, 0xFFFF) +
                new TStatusItem("~A~",   Keys.kbNoKey, CmSL1) +
                new TStatusItem("~BB~",  Keys.kbNoKey, CmSL2) +
                new TStatusItem("~CCC~", Keys.kbNoKey, CmSL3));
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(9, 0); ev.mouse.buttons = 0x01;
        sl3.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(CmSL3, ev.message.command);
    }

    // ── StatusLine MakeLocal coordinate fix ──────────────────────────────

    private const ushort CmHelp19d1 = 213, CmExit19d1 = 214;

    private TStatusLine MakeBottomSL()
    {
        TView.EnableCommand(CmHelp19d1); TView.EnableCommand(CmExit19d1);
        return new TStatusLine(
            new TRect(0, 24, 80, 25),
            new TStatusDef(0, 0xFFFF) +
                new TStatusItem("~F1~ Help",   Keys.kbF1,   CmHelp19d1) +
                new TStatusItem("~Alt-X~ Exit", Keys.kbAltX, CmExit19d1));
    }

    [Fact]
    public void StatusLine_BottomRow_AbsClick3_24_HitsItem1()
    {
        var sl = MakeBottomSL();
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(3, 24); ev.mouse.buttons = 0x01;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(CmHelp19d1, ev.message.command);
    }

    [Fact]
    public void StatusLine_BottomRow_AbsClick12_24_HitsItem2()
    {
        var sl = MakeBottomSL();
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(12, 24); ev.mouse.buttons = 0x01;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(CmExit19d1, ev.message.command);
    }

    [Fact]
    public void StatusLine_BottomRow_WrongAbsRow23_EvNothing()
    {
        var sl = MakeBottomSL();
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(3, 23); ev.mouse.buttons = 0x01;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void StatusLine_BottomRow_TopOfScreen_EvNothing()
    {
        var sl = MakeBottomSL();
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(3, 0); ev.mouse.buttons = 0x01;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void StatusLine_BottomRow_Item1TrailingEdge8_24_HitsItem1()
    {
        var sl = MakeBottomSL();
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(8, 24); ev.mouse.buttons = 0x01;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(CmHelp19d1, ev.message.command);
    }

    [Fact]
    public void StatusLine_BottomRow_Item2LeadingEdge9_24_HitsItem2()
    {
        var sl = MakeBottomSL();
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(9, 24); ev.mouse.buttons = 0x01;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(CmExit19d1, ev.message.command);
    }

    [Fact]
    public void StatusLine_BottomRow_KbF1_Shortcut()
    {
        var sl = MakeBottomSL();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbF1;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(CmHelp19d1, ev.message.command);
    }

    [Fact]
    public void StatusLine_BottomRow_KbAltX_Shortcut()
    {
        var sl = MakeBottomSL();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbAltX;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(CmExit19d1, ev.message.command);
    }

    [Fact]
    public void StatusLine_BottomRow_FarRight_EvNothing()
    {
        var sl = MakeBottomSL();
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(70, 24); ev.mouse.buttons = 0x01;
        sl.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
    }
}
