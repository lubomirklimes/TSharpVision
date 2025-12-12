using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Dialogs;

[Collection("NonParallel")]
public sealed class DialogBasicsTests
{
    // ── local queue helpers ───────────────────────────────────────────────────

    private static void DrainQueue()
    {
        TEventQueue.Resume();
        for (int i = 0; i < 64; i++)
        {
            TEvent d = default;
            TEventQueue.GetMouseEvent(ref d);
            if (d.What == Events.evNothing) break;
        }
    }

    private static TEvent NextQueued()
    {
        TEvent d = default;
        TEventQueue.GetMouseEvent(ref d);
        return d;
    }

    private static bool FirstCommand(ushort cmd)
    {
        for (int i = 0; i < 64; i++)
        {
            TEvent d = default;
            TEventQueue.GetMouseEvent(ref d);
            if (d.What == Events.evNothing) return false;
            if (d.What == Events.evCommand && d.message.command == cmd) return true;
        }
        return false;
    }

    // ── TStaticText ──────────────────────────────────────────────────────────

    [Fact]
    public void StaticText_GetText_ReturnsStoredText()
    {
        using var driver = new DriverScope();
        var st = new TStaticText(new TRect(0, 0, 20, 2), "Hello world");
        st.GetText(out string text);
        Assert.Equal("Hello world", text);
    }

    [Fact]
    public void StaticText_Palette_Entry1_0x06()
    {
        using var driver = new DriverScope();
        var st = new TStaticText(new TRect(0, 0, 20, 2), "Hello world");
        Assert.Equal(0x06, st.GetPalette()[1]);
    }

    // ── TParamText ───────────────────────────────────────────────────────────

    [Fact]
    public void ParamText_SetText_FormatsArgs()
    {
        using var driver = new DriverScope();
        var pt = new TParamText(new TRect(0, 0, 30, 1), "Files: {0}, Used: {1}", 2);
        pt.SetText("Files: {0}, Used: {1}", 5, "12K");
        pt.GetText(out string text);
        Assert.Equal("Files: 5, Used: 12K", text);
    }

    [Fact]
    public void ParamText_DataSize_Zero()
    {
        using var driver = new DriverScope();
        var pt = new TParamText(new TRect(0, 0, 30, 1), "x", 2);
        Assert.Equal(0, pt.DataSize());
    }

    [Fact]
    public void ParamText_NoArgs_ReturnsFormatUnchanged()
    {
        using var driver = new DriverScope();
        var pt2 = new TParamText(new TRect(0, 0, 10, 1), "literal", 0);
        pt2.GetText(out string text);
        Assert.Equal("literal", text);
    }

    // ── TButton ──────────────────────────────────────────────────────────────

    [Fact]
    public void Button_StoresTitle()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(0, 0, 10, 2), "~O~K", Views.cmOK, ButtonConstants.bfDefault);
        Assert.Equal("~O~K", btn.Title);
    }

    [Fact]
    public void Button_StoresCommand()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(0, 0, 10, 2), "~O~K", Views.cmOK, ButtonConstants.bfDefault);
        Assert.Equal(Views.cmOK, btn.Command);
    }

    [Fact]
    public void Button_BfDefault_SetsAmDefault()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(0, 0, 10, 2), "~O~K", Views.cmOK, ButtonConstants.bfDefault);
        Assert.True(btn.AmDefault);
    }

    [Fact]
    public void Button_HasOfSelectable()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(0, 0, 10, 2), "~O~K", Views.cmOK, ButtonConstants.bfDefault);
        Assert.NotEqual(0, btn.options & Views.ofSelectable);
    }

    [Fact]
    public void Button_HasOfFirstClick()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(0, 0, 10, 2), "~O~K", Views.cmOK, ButtonConstants.bfDefault);
        Assert.NotEqual(0, btn.options & Views.ofFirstClick);
    }

    [Fact]
    public void Button_HasOfPreAndPostProcess()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(0, 0, 10, 2), "~O~K", Views.cmOK, ButtonConstants.bfDefault);
        Assert.NotEqual(0, btn.options & Views.ofPreProcess);
        Assert.NotEqual(0, btn.options & Views.ofPostProcess);
    }

    [Fact]
    public void Button_EventMask_IncludesEvBroadcast()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(0, 0, 10, 2), "~O~K", Views.cmOK, ButtonConstants.bfDefault);
        Assert.True((btn.eventMask & Events.evBroadcast) != 0);
    }

    [Fact]
    public void Button_Palette_Entry1_0x0A()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(0, 0, 10, 2), "~O~K", Views.cmOK, ButtonConstants.bfDefault);
        Assert.Equal(0x0A, btn.GetPalette()[1]);
    }

    [Fact]
    public void Button_DisabledWhenCommandDisabled()
    {
        using var driver = new DriverScope();
        const ushort TestCmd = 250;
        TView.DisableCommand(TestCmd);
        TCommandSet saved = TView.curCommandSet;
        try
        {
            var btnDis = new TButton(new TRect(0, 0, 10, 2), "X", TestCmd, 0);
            Assert.NotEqual(0, btnDis.state & Views.sfDisabled);
        }
        finally
        {
            TView.curCommandSet = saved;
            TView.EnableCommand(TestCmd);
        }
    }

    [Fact]
    public void Button_Press_PostsEvCommand()
    {
        using var driver = new DriverScope();
        const ushort cmOpen = 200;
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var btn2 = new TButton(new TRect(2, 2, 12, 4), "~G~o", cmOpen, 0);
        host.Insert(btn2);
        DrainQueue();
        btn2.Press();
        var qe = NextQueued();
        Assert.Equal(Events.evCommand, qe.What);
        Assert.Equal(cmOpen, qe.message.command);
    }

    [Fact]
    public void Button_AltHotkey_TriggersPress()
    {
        using var driver = new DriverScope();
        const ushort cmOpen = 200;
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var btn2 = new TButton(new TRect(2, 2, 12, 4), "~G~o", cmOpen, 0);
        host.Insert(btn2);
        DrainQueue();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbAltG;
        btn2.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
        var qe = NextQueued();
        Assert.Equal(Events.evCommand, qe.What);
        Assert.Equal(cmOpen, qe.message.command);
    }

    [Fact]
    public void Button_EvBroadcastCmDefault_TriggersPress()
    {
        using var driver = new DriverScope();
        const ushort cmOpen = 200;
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var btn2 = new TButton(new TRect(2, 2, 12, 4), "~G~o", cmOpen, 0);
        host.Insert(btn2);
        DrainQueue();
        btn2.MakeDefault(true);
        Assert.True(btn2.AmDefault);
        var ev = new TEvent { What = Events.evBroadcast };
        ev.message.command = Views.cmDefault;
        btn2.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
        Assert.True(FirstCommand(cmOpen));
    }

    [Fact]
    public void Button_CmGrabDefault_ClearsAmDefault()
    {
        using var driver = new DriverScope();
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var defBtn = new TButton(new TRect(0, 0, 10, 2), "OK", Views.cmOK, ButtonConstants.bfDefault);
        host.Insert(defBtn);
        var ev = new TEvent { What = Events.evBroadcast };
        ev.message.command = Views.cmReleaseDefault;
        defBtn.HandleEvent(ref ev);
        Assert.True(defBtn.AmDefault); // bfDefault restores on cmReleaseDefault
        ev = new TEvent { What = Events.evBroadcast };
        ev.message.command = Views.cmGrabDefault;
        defBtn.HandleEvent(ref ev);
        Assert.False(defBtn.AmDefault);
    }

    // ── TDialog ──────────────────────────────────────────────────────────────

    [Fact]
    public void Dialog_GetTitle()
    {
        using var driver = new DriverScope();
        var dlg = new TDialog(new TRect(5, 5, 45, 18), "Settings");
        Assert.Equal("Settings", dlg.GetTitle(20));
    }

    [Fact]
    public void Dialog_Flags_MovePlusClose()
    {
        using var driver = new DriverScope();
        var dlg = new TDialog(new TRect(5, 5, 45, 18), "Settings");
        Assert.Equal((byte)(Views.wfMove | Views.wfClose), dlg.flags);
    }

    [Fact]
    public void Dialog_GrowMode_Zero()
    {
        using var driver = new DriverScope();
        var dlg = new TDialog(new TRect(5, 5, 45, 18), "Settings");
        Assert.Equal(0, dlg.growMode);
    }

    [Fact]
    public void Dialog_Palette_Size32()
    {
        using var driver = new DriverScope();
        var dlg = new TDialog(new TRect(5, 5, 45, 18), "Settings");
        Assert.Equal(32, dlg.GetPalette().Size);
    }

    [Fact]
    public void Dialog_Palette_Entry1_0x20()
    {
        using var driver = new DriverScope();
        var dlg = new TDialog(new TRect(5, 5, 45, 18), "Settings");
        Assert.Equal(0x20, dlg.GetPalette()[1]);
    }

    [Fact]
    public void Dialog_KbEsc_PostsCmCancel()
    {
        using var driver = new DriverScope();
        var dlgHost = new TestGroup(new TRect(0, 0, 80, 25));
        var dlg2 = new TDialog(new TRect(2, 2, 40, 15), "T");
        dlgHost.Insert(dlg2);
        DrainQueue();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbEsc;
        dlg2.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
        var qe = NextQueued();
        Assert.Equal(Events.evCommand, qe.What);
        Assert.Equal(Views.cmCancel, qe.message.command);
    }

    [Fact]
    public void Dialog_KbEnter_PostsCmDefault()
    {
        using var driver = new DriverScope();
        var dlgHost = new TestGroup(new TRect(0, 0, 80, 25));
        var dlg3 = new TDialog(new TRect(2, 2, 40, 15), "T");
        dlgHost.Insert(dlg3);
        DrainQueue();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbEnter;
        dlg3.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
        var qe = NextQueued();
        Assert.Equal(Events.evBroadcast, qe.What);
        Assert.Equal(Views.cmDefault, qe.message.command);
    }

    [Fact]
    public void Dialog_Valid_CmCancel_True()
    {
        using var driver = new DriverScope();
        var dlg = new TDialog(new TRect(5, 5, 45, 18), "Settings");
        Assert.True(dlg.Valid(Views.cmCancel));
    }

    [Fact]
    public void Dialog_Valid_Zero_True()
    {
        using var driver = new DriverScope();
        var dlg = new TDialog(new TRect(5, 5, 45, 18), "Settings");
        Assert.True(dlg.Valid(0));
    }
}
