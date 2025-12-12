using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Menus;

[Collection("NonParallel")]
public sealed class MenuStatusLineTests
{
    // ── constants ─────────────────────────────────────────────────────────────
    private const ushort cmOpenFile = 100;
    private const ushort cmExitTest = 101;
    private const ushort cmCopyTest = 102;
    private const ushort cmTileTest = 103;

    // ── factory ───────────────────────────────────────────────────────────────

    private static TMenuBar MakeBar()
    {
        var fileMenu = new TMenu(
            new TMenuItem("~O~pen", cmOpenFile, Keys.kbF3, Views.hcNoContext, "F3",
                new TMenuItem("e~x~it", cmExitTest, Keys.kbAltX, Views.hcNoContext, "Alt-X")));
        var editMenu = new TMenu(
            new TMenuItem("~C~opy", cmCopyTest, Keys.kbCtrlIns, Views.hcNoContext, "Ctrl-Ins"));
        var windowMenu = new TMenu(
            new TMenuItem("~T~ile", cmTileTest, Keys.kbNoKey));

        return new TMenuBar(new TRect(0, 0, 80, 1), new TMenu(
            new TMenuItem("~F~ile", Keys.kbAltF, fileMenu,
                Views.hcNoContext, new TMenuItem("~E~dit", Keys.kbAltE, editMenu,
                    Views.hcNoContext, new TMenuItem("~W~indow", Keys.kbAltW, windowMenu)))));
    }

    // ── TMenuItem HotChar ─────────────────────────────────────────────────────

    [Fact]
    public void MenuItem_HotChar_ReturnsTildeChar()
    {
        using var driver = new DriverScope();
        var item = new TMenuItem("~F~ile", cmOpenFile, Keys.kbF3);
        Assert.Equal('F', item.HotChar());
    }

    [Fact]
    public void MenuItem_HotChar_NoTilde_ReturnsNull()
    {
        using var driver = new DriverScope();
        var item = new TMenuItem("Plain", cmOpenFile, Keys.kbF3);
        Assert.Equal('\0', item.HotChar());
    }

    // ── TMenuBar FindItem ─────────────────────────────────────────────────────

    [Fact]
    public void MenuBar_FindItem_LowerCase_FindsFileMenu()
    {
        using var driver = new DriverScope();
        var bar = MakeBar();
        var found = bar.FindItem('f');
        Assert.NotNull(found);
        Assert.Equal("~F~ile", found!.Name);
    }

    [Fact]
    public void MenuBar_FindItem_UpperCase_FindsFileMenu()
    {
        using var driver = new DriverScope();
        var bar = MakeBar();
        var found = bar.FindItem('F');
        Assert.NotNull(found);
        Assert.Equal("~F~ile", found!.Name);
    }

    [Fact]
    public void MenuBar_FindItem_Absent_ReturnsNull()
    {
        using var driver = new DriverScope();
        var bar = MakeBar();
        Assert.Null(bar.FindItem('Z'));
    }

    // ── TMenuBar HotKey ───────────────────────────────────────────────────────

    [Fact]
    public void MenuBar_HotKey_F3_FindsOpen()
    {
        using var driver = new DriverScope();
        var bar = MakeBar();
        var hk = bar.HotKey(Keys.kbF3);
        Assert.NotNull(hk);
        Assert.Equal(cmOpenFile, hk!.Command);
    }

    [Fact]
    public void MenuBar_HotKey_CtrlIns_FindsCopy_Recursive()
    {
        using var driver = new DriverScope();
        var bar = MakeBar();
        var hk = bar.HotKey(Keys.kbCtrlIns);
        Assert.NotNull(hk);
        Assert.Equal(cmCopyTest, hk!.Command);
    }

    [Fact]
    public void MenuBar_HotKey_AltX_FindsExit()
    {
        using var driver = new DriverScope();
        var bar = MakeBar();
        var hk = bar.HotKey(Keys.kbAltX);
        Assert.NotNull(hk);
        Assert.Equal(cmExitTest, hk!.Command);
    }

    [Fact]
    public void MenuBar_HotKey_F12_ReturnsNull()
    {
        using var driver = new DriverScope();
        var bar = MakeBar();
        Assert.Null(bar.HotKey(Keys.kbF12));
    }

    // ── Disabled items skipped ────────────────────────────────────────────────

    [Fact]
    public void MenuBar_FindItem_SkipsDisabled()
    {
        using var driver = new DriverScope();
        var item = new TMenuItem("~F~ile", cmOpenFile, Keys.kbF3);
        item.Disabled = true;
        var dm = new TMenu(item);
        var disBar = new TMenuBar(new TRect(0, 0, 40, 1), dm);
        Assert.Null(disBar.FindItem('F'));
    }

    [Fact]
    public void MenuBar_HotKey_SkipsDisabled()
    {
        using var driver = new DriverScope();
        var item = new TMenuItem("~F~ile", cmOpenFile, Keys.kbF3);
        item.Disabled = true;
        var dm = new TMenu(item);
        var disBar = new TMenuBar(new TRect(0, 0, 40, 1), dm);
        Assert.Null(disBar.HotKey(Keys.kbF3));
    }

    // ── GetAltChar ────────────────────────────────────────────────────────────

    [Fact]
    public void GetAltChar_AltF_ReturnsF()
    {
        using var driver = new DriverScope();
        Assert.Equal('F', TMenuView.GetAltChar(Keys.kbAltF, 0, Keys.kbAltShift));
    }

    [Fact]
    public void GetAltChar_Alt1_Returns1()
    {
        using var driver = new DriverScope();
        Assert.Equal('1', TMenuView.GetAltChar(Keys.kbAlt1, 0, Keys.kbAltShift));
    }

    [Fact]
    public void GetAltChar_PlainLetter_NoAlt_ReturnsNull()
    {
        using var driver = new DriverScope();
        Assert.Equal('\0', TMenuView.GetAltChar(0x1E61, (byte)'a', 0));
    }

    [Fact]
    public void GetAltChar_PlainLetter_WithAlt_ReturnsNull()
    {
        using var driver = new DriverScope();
        Assert.Equal('\0', TMenuView.GetAltChar(0, (byte)'a', Keys.kbAltShift));
    }

    // ── TMenuBar.GetItemRect ──────────────────────────────────────────────────

    [Fact]
    public void MenuBar_GetItemRect_FileMenu_StartsAt1()
    {
        using var driver = new DriverScope();
        var bar = MakeBar();
        var fileRect = bar.GetItemRect(bar.Menu.Items);
        Assert.Equal(1, fileRect.a.x);
    }

    [Fact]
    public void MenuBar_GetItemRect_EditMenu_FollowsFile()
    {
        using var driver = new DriverScope();
        var bar = MakeBar();
        var fileRect = bar.GetItemRect(bar.Menu.Items);
        var editRect = bar.GetItemRect(bar.Menu.Items.Next);
        Assert.Equal(fileRect.b.x, editRect.a.x);
    }

    // ── KeyToHotKey ───────────────────────────────────────────────────────────

    [Fact]
    public void KeyToHotKey_F3_RoutesAndClearsEvent()
    {
        using var driver = new DriverScope();
        var bar = MakeBar();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbF3;
        bool routed = bar.KeyToHotKey(ref ev);
        Assert.True(routed);
        Assert.Equal(Events.evNothing, ev.What);
    }

    [Fact]
    public void KeyToHotKey_Unbound_LeavesEventAlone()
    {
        using var driver = new DriverScope();
        var bar = MakeBar();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbF12;
        bool routed = bar.KeyToHotKey(ref ev);
        Assert.False(routed);
    }

    // ── KeyToItem ─────────────────────────────────────────────────────────────

    [Fact]
    public void KeyToItem_AltF_FindsFileMenu()
    {
        using var driver = new DriverScope();
        var bar = MakeBar();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbAltF;
        ev.keyDown.shiftState = Keys.kbAltShift;
        bool routed = bar.KeyToItem(ref ev);
        Assert.True(routed);
    }

    // ── TStatusLine ───────────────────────────────────────────────────────────

    [Fact]
    public void StatusLine_F1_ConvertsToCmOpenFile()
    {
        using var driver = new DriverScope();
        var items = new TStatusItem("~F1~ Help", Keys.kbF1, cmOpenFile,
            new TStatusItem("~Alt-X~ Exit", Keys.kbAltX, cmExitTest, null));
        var def = new TStatusDef(0, 0xFFFF, items);
        var status = new TStatusLine(new TRect(0, 24, 80, 25), def);
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbF1;
        status.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(cmOpenFile, ev.message.command);
    }

    [Fact]
    public void StatusLine_AltX_ConvertsToCmExit()
    {
        using var driver = new DriverScope();
        var items = new TStatusItem("~F1~ Help", Keys.kbF1, cmOpenFile,
            new TStatusItem("~Alt-X~ Exit", Keys.kbAltX, cmExitTest, null));
        var def = new TStatusDef(0, 0xFFFF, items);
        var status = new TStatusLine(new TRect(0, 24, 80, 25), def);
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbAltX;
        status.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(cmExitTest, ev.message.command);
    }

    [Fact]
    public void StatusLine_MouseClick_FirstItem_ConvertsToCmOpenFile()
    {
        using var driver = new DriverScope();
        var items = new TStatusItem("~F1~ Help", Keys.kbF1, cmOpenFile,
            new TStatusItem("~Alt-X~ Exit", Keys.kbAltX, cmExitTest, null));
        var def = new TStatusDef(0, 0xFFFF, items);
        var status = new TStatusLine(new TRect(0, 24, 80, 25), def);
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(2, 24);
        ev.mouse.buttons = 0x01;
        status.HandleEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(cmOpenFile, ev.message.command);
    }

    [Fact]
    public void StatusLine_MouseClick_OffItem_DoesNothing()
    {
        using var driver = new DriverScope();
        var items = new TStatusItem("~F1~ Help", Keys.kbF1, cmOpenFile,
            new TStatusItem("~Alt-X~ Exit", Keys.kbAltX, cmExitTest, null));
        var def = new TStatusDef(0, 0xFFFF, items);
        var status = new TStatusLine(new TRect(0, 24, 80, 25), def);
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(70, 24);
        ev.mouse.buttons = 0x01;
        status.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What);
    }
}
