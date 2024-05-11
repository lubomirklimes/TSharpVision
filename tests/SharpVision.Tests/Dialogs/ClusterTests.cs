using SharpVision;
using SharpVision.Constants;
using SharpVision.Tests.Infrastructure;
using Xunit;

namespace SharpVision.Tests.Dialogs;

[Collection("NonParallel")]
public sealed class ClusterTests
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

    // ── TSItem ────────────────────────────────────────────────────────────────

    [Fact]
    public void TSItem_Append_Chains3InOrder()
    {
        using var driver = new DriverScope();
        var head = new TSItem("a", null);
        head.Append(new TSItem("b", null));
        head.Append(new TSItem("c", null));
        Assert.Equal("a", head.Value);
        Assert.NotNull(head.Next);
        Assert.Equal("b", head.Next!.Value);
        Assert.NotNull(head.Next.Next);
        Assert.Equal("c", head.Next.Next!.Value);
        Assert.Null(head.Next.Next.Next);
    }

    // ── TCheckBoxes constructor ────────────────────────────────────────────────

    [Fact]
    public void Cluster_Ctor_Populates3Strings()
    {
        using var driver = new DriverScope();
        var items = new TSItem("One", new TSItem("Two", new TSItem("Three", null)));
        var cb = new TCheckBoxes(new TRect(2, 2, 20, 5), items);
        Assert.Equal(3, cb.Strings.Count);
    }

    [Fact]
    public void Cluster_Ctor_SelAndValueZero()
    {
        using var driver = new DriverScope();
        var items = new TSItem("One", new TSItem("Two", new TSItem("Three", null)));
        var cb = new TCheckBoxes(new TRect(2, 2, 20, 5), items);
        Assert.Equal(0, cb.sel);
        Assert.Equal(0u, cb.value);
    }

    [Fact]
    public void Cluster_Ctor_OptionsHasAllRequired()
    {
        using var driver = new DriverScope();
        var items = new TSItem("One", new TSItem("Two", new TSItem("Three", null)));
        var cb = new TCheckBoxes(new TRect(2, 2, 20, 5), items);
        Assert.NotEqual(0, cb.options & Views.ofSelectable);
        Assert.NotEqual(0, cb.options & Views.ofFirstClick);
        Assert.NotEqual(0, cb.options & Views.ofPreProcess);
        Assert.NotEqual(0, cb.options & Views.ofPostProcess);
    }

    [Fact]
    public void Cluster_Palette_Entry1_0x10()
    {
        using var driver = new DriverScope();
        var items = new TSItem("One", new TSItem("Two", null));
        var cb = new TCheckBoxes(new TRect(2, 2, 20, 5), items);
        Assert.Equal(0x10, cb.GetPalette()[1]);
    }

    [Fact]
    public void Cluster_Palette_Entry5_0x1F()
    {
        using var driver = new DriverScope();
        var items = new TSItem("One", new TSItem("Two", null));
        var cb = new TCheckBoxes(new TRect(2, 2, 20, 5), items);
        Assert.Equal(0x1F, cb.GetPalette()[5]);
    }

    [Fact]
    public void Cluster_DataSize_2()
    {
        using var driver = new DriverScope();
        var items = new TSItem("One", new TSItem("Two", null));
        var cb = new TCheckBoxes(new TRect(2, 2, 20, 5), items);
        Assert.Equal(2, cb.DataSize());
    }

    // ── TCheckBoxes Mark / Press / GetData / SetData ──────────────────────────

    [Fact]
    public void CheckBoxes_Mark_AllFalseInitially()
    {
        using var driver = new DriverScope();
        var items = new TSItem("A", new TSItem("B", new TSItem("C", null)));
        var cb = new TCheckBoxes(new TRect(0, 0, 10, 3), items);
        Assert.False(cb.Mark(0));
        Assert.False(cb.Mark(1));
        Assert.False(cb.Mark(2));
    }

    [Fact]
    public void CheckBoxes_Press0_SetsBit0()
    {
        using var driver = new DriverScope();
        var items = new TSItem("A", new TSItem("B", new TSItem("C", null)));
        var cb = new TCheckBoxes(new TRect(0, 0, 10, 3), items);
        cb.Press(0);
        Assert.Equal(1u, cb.value);
        Assert.True(cb.Mark(0));
        Assert.False(cb.Mark(1));
    }

    [Fact]
    public void CheckBoxes_Press1_SetsBit1()
    {
        using var driver = new DriverScope();
        var items = new TSItem("A", new TSItem("B", new TSItem("C", null)));
        var cb = new TCheckBoxes(new TRect(0, 0, 10, 3), items);
        cb.Press(0); cb.Press(1);
        Assert.Equal(3u, cb.value);
        Assert.True(cb.Mark(0));
        Assert.True(cb.Mark(1));
    }

    [Fact]
    public void CheckBoxes_PressAgain_ClearsBit()
    {
        using var driver = new DriverScope();
        var items = new TSItem("A", new TSItem("B", new TSItem("C", null)));
        var cb = new TCheckBoxes(new TRect(0, 0, 10, 3), items);
        cb.Press(0); cb.Press(1); cb.Press(0);
        Assert.Equal(2u, cb.value);
        Assert.False(cb.Mark(0));
        Assert.True(cb.Mark(1));
    }

    [Fact]
    public void CheckBoxes_GetData_ReturnsUshortValue()
    {
        using var driver = new DriverScope();
        var items = new TSItem("A", new TSItem("B", new TSItem("C", null)));
        var cb = new TCheckBoxes(new TRect(0, 0, 10, 3), items);
        cb.Press(0); cb.Press(1);
        object data = (ushort)0;
        cb.GetData(ref data);
        Assert.IsType<ushort>(data);
        Assert.Equal((ushort)3, (ushort)data);
    }

    [Fact]
    public void CheckBoxes_SetData_SetsValue()
    {
        using var driver = new DriverScope();
        var items = new TSItem("A", new TSItem("B", new TSItem("C", null)));
        var cb = new TCheckBoxes(new TRect(0, 0, 10, 3), items);
        cb.SetData((ushort)5);
        Assert.Equal(5u, cb.value);
    }

    // ── TRadioButtons Mark / Press / MovedTo / SetData ────────────────────────

    [Fact]
    public void RadioButtons_Mark0_TrueInitially()
    {
        using var driver = new DriverScope();
        var items = new TSItem("X", new TSItem("Y", new TSItem("Z", null)));
        var rb = new TRadioButtons(new TRect(0, 0, 10, 3), items);
        Assert.True(rb.Mark(0));
        Assert.False(rb.Mark(1));
    }

    [Fact]
    public void RadioButtons_Press2_SetsValue2()
    {
        using var driver = new DriverScope();
        var items = new TSItem("X", new TSItem("Y", new TSItem("Z", null)));
        var rb = new TRadioButtons(new TRect(0, 0, 10, 3), items);
        rb.Press(2);
        Assert.Equal(2u, rb.value);
        Assert.True(rb.Mark(2));
        Assert.False(rb.Mark(0));
    }

    [Fact]
    public void RadioButtons_MovedTo1_SetsValue1()
    {
        using var driver = new DriverScope();
        var items = new TSItem("X", new TSItem("Y", new TSItem("Z", null)));
        var rb = new TRadioButtons(new TRect(0, 0, 10, 3), items);
        rb.Press(2); rb.MovedTo(1);
        Assert.Equal(1u, rb.value);
        Assert.True(rb.Mark(1));
    }

    [Fact]
    public void RadioButtons_SetData0_ResetsSel()
    {
        using var driver = new DriverScope();
        var items = new TSItem("X", new TSItem("Y", new TSItem("Z", null)));
        var rb = new TRadioButtons(new TRect(0, 0, 10, 3), items);
        rb.Press(2); rb.SetData((ushort)0);
        Assert.Equal(0u, rb.value);
        Assert.Equal(0, rb.sel);
    }

    // ── TCluster arrow-key navigation ────────────────────────────────────────

    [Fact]
    public void Cluster_KbDown_IncrementsSel()
    {
        using var driver = new DriverScope();
        var items = new TSItem("One", new TSItem("Two", new TSItem("Three", null)));
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var cb = new TCheckBoxes(new TRect(2, 2, 20, 5), items);
        host.Insert(cb);
        cb.SetState(Views.sfFocused, true);
        DrainQueue();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbDown;
        cb.HandleEvent(ref ev);
        Assert.Equal(1, cb.sel);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void Cluster_KbUp_DecrementsSel()
    {
        using var driver = new DriverScope();
        var items = new TSItem("One", new TSItem("Two", new TSItem("Three", null)));
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var cb = new TCheckBoxes(new TRect(2, 2, 20, 5), items);
        host.Insert(cb);
        cb.SetState(Views.sfFocused, true);
        DrainQueue();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbDown;
        cb.HandleEvent(ref ev);
        ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbUp;
        cb.HandleEvent(ref ev);
        Assert.Equal(0, cb.sel);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void Cluster_KbUp_WrapsToLast()
    {
        using var driver = new DriverScope();
        var items = new TSItem("One", new TSItem("Two", new TSItem("Three", null)));
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var cb = new TCheckBoxes(new TRect(2, 2, 20, 5), items);
        host.Insert(cb);
        cb.SetState(Views.sfFocused, true);
        DrainQueue();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbUp;
        cb.HandleEvent(ref ev);
        Assert.Equal(2, cb.sel);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void Cluster_KbDown_WrapsTo0()
    {
        using var driver = new DriverScope();
        var items = new TSItem("One", new TSItem("Two", new TSItem("Three", null)));
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var cb = new TCheckBoxes(new TRect(2, 2, 20, 5), items);
        host.Insert(cb);
        cb.SetState(Views.sfFocused, true);
        DrainQueue();
        // Wrap up to last
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbUp;
        cb.HandleEvent(ref ev);
        // Then down again wraps to 0
        ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbDown;
        cb.HandleEvent(ref ev);
        Assert.Equal(0, cb.sel);
        Assert.Equal(Events.evNothing, ev.What);
    }

    // ── TCluster Space key ────────────────────────────────────────────────────

    [Fact]
    public void Cluster_Space_TogglesAndClearsEvent()
    {
        using var driver = new DriverScope();
        var items = new TSItem("One", new TSItem("Two", null));
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var probe = new BroadcastProbe(new TRect(0, 0, 1, 1));
        var cb = new TCheckBoxes(new TRect(2, 2, 20, 5), items);
        host.Insert(probe);
        host.Insert(cb);
        cb.SetState(Views.sfFocused, true);
        probe.Reset();
        DrainQueue();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = 0;
        ev.keyDown.charScan.charCode = (byte)' ';
        cb.HandleEvent(ref ev);
        Assert.Equal(1u, cb.value);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void Cluster_Space_BroadcastsCmClusterPress()
    {
        using var driver = new DriverScope();
        var items = new TSItem("One", new TSItem("Two", null));
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var probe = new BroadcastProbe(new TRect(0, 0, 1, 1));
        var cb = new TCheckBoxes(new TRect(2, 2, 20, 5), items);
        host.Insert(probe);
        host.Insert(cb);
        cb.SetState(Views.sfFocused, true);
        probe.Reset();
        DrainQueue();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = 0;
        ev.keyDown.charScan.charCode = (byte)' ';
        cb.HandleEvent(ref ev);
        Assert.Equal(Views.cmClusterPress, probe.LastBroadcast);
    }

    // ── TCluster Alt-letter hotkey ────────────────────────────────────────────

    [Fact]
    public void Cluster_AltHotkey_SelectsAndPresses()
    {
        using var driver = new DriverScope();
        var items = new TSItem("~O~ne", new TSItem("~T~wo", null));
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        var cb = new TCheckBoxes(new TRect(2, 2, 20, 5), items);
        host.Insert(cb);
        DrainQueue();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbAltT;
        cb.HandleEvent(ref ev);
        Assert.Equal(1, cb.sel);
        Assert.Equal(2u, cb.value);
        Assert.Equal(Events.evNothing, ev.What);
    }

    // ── TLabel ────────────────────────────────────────────────────────────────

    [Fact]
    public void Label_Ctor_StoresLinkAndLightFalse()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(10, 1, 20, 3), "Go", Views.cmOK, 0);
        var lab = new TLabel(new TRect(2, 1, 9, 2), "~G~o:", btn);
        Assert.Same(btn, lab.Link);
        Assert.False(lab.Light);
    }

    [Fact]
    public void Label_Options_HasPreAndPostProcess()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(10, 1, 20, 3), "Go", Views.cmOK, 0);
        var lab = new TLabel(new TRect(2, 1, 9, 2), "~G~o:", btn);
        Assert.NotEqual(0, lab.options & Views.ofPreProcess);
        Assert.NotEqual(0, lab.options & Views.ofPostProcess);
    }

    [Fact]
    public void Label_EventMask_IncludesEvBroadcast()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(10, 1, 20, 3), "Go", Views.cmOK, 0);
        var lab = new TLabel(new TRect(2, 1, 9, 2), "~G~o:", btn);
        Assert.True((lab.eventMask & Events.evBroadcast) != 0);
    }

    [Fact]
    public void Label_Palette_Entry1_0x07()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(10, 1, 20, 3), "Go", Views.cmOK, 0);
        var lab = new TLabel(new TRect(2, 1, 9, 2), "~G~o:", btn);
        Assert.Equal(0x07, lab.GetPalette()[1]);
    }

    [Fact]
    public void Label_CmReceivedFocus_TogglesLightTrue()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(10, 1, 20, 3), "Go", Views.cmOK, 0);
        var lab = new TLabel(new TRect(2, 1, 9, 2), "~G~o:", btn);
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.Insert(btn);
        host.Insert(lab);
        btn.SetState(Views.sfFocused, true);
        DrainQueue();
        var ev = new TEvent { What = Events.evBroadcast };
        ev.message.command = Views.cmReceivedFocus;
        ev.message.infoPtr = btn;
        lab.HandleEvent(ref ev);
        Assert.True(lab.Light);
    }

    [Fact]
    public void Label_CmReleasedFocus_TogglesLightFalse()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(10, 1, 20, 3), "Go", Views.cmOK, 0);
        var lab = new TLabel(new TRect(2, 1, 9, 2), "~G~o:", btn);
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.Insert(btn);
        host.Insert(lab);
        btn.SetState(Views.sfFocused, true);
        DrainQueue();
        var ev = new TEvent { What = Events.evBroadcast };
        ev.message.command = Views.cmReceivedFocus;
        ev.message.infoPtr = btn;
        lab.HandleEvent(ref ev);
        btn.SetState(Views.sfFocused, false);
        ev = new TEvent { What = Events.evBroadcast };
        ev.message.command = Views.cmReleasedFocus;
        ev.message.infoPtr = btn;
        lab.HandleEvent(ref ev);
        Assert.False(lab.Light);
    }

    [Fact]
    public void Label_SetStateDisabled_PropagesToLink()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(10, 1, 20, 3), "Go", Views.cmOK, 0);
        var lab = new TLabel(new TRect(2, 1, 9, 2), "~G~o:", btn);
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.Insert(btn);
        host.Insert(lab);
        lab.SetState(Views.sfDisabled, true);
        Assert.NotEqual(0, btn.state & Views.sfDisabled);
    }

    [Fact]
    public void Label_SetStateEnabled_ClearsLinkDisabled()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(10, 1, 20, 3), "Go", Views.cmOK, 0);
        var lab = new TLabel(new TRect(2, 1, 9, 2), "~G~o:", btn);
        var host = new TestGroup(new TRect(0, 0, 80, 25));
        host.Insert(btn);
        host.Insert(lab);
        lab.SetState(Views.sfDisabled, true);
        lab.SetState(Views.sfDisabled, false);
        Assert.Equal(0, btn.state & Views.sfDisabled);
    }

    [Fact]
    public void Label_ShutDown_NullsLink()
    {
        using var driver = new DriverScope();
        var btn = new TButton(new TRect(10, 1, 20, 3), "Go", Views.cmOK, 0);
        var lab = new TLabel(new TRect(2, 1, 9, 2), "~G~o:", btn);
        lab.ShutDown();
        Assert.Null(lab.Link);
    }
}
