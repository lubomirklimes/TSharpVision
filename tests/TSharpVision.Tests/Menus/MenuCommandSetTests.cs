using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Menus;

// TW-F1: menu entries follow the framework command set. TMenuView subscribes to evBroadcast, so the cmCommandSetChanged
// broadcast sent by TProgram.Idle reaches the bar and every open drop-down, refreshes TMenuItem.Disabled and redraws.
// Release coverage uses the existing screen buffer directly.
[Collection("NonParallel")]
public sealed class MenuCommandSetTests : IDisposable
{
    private const ushort Open = 620, Print = 621, Export = 622, Quick = 623;
    private readonly DriverScope _driver = new(80, 25);
    private readonly TCommandSet _saved = new();

    public MenuCommandSetTests()
    {
        TView.GetCommands(_saved);
        foreach (ushort c in new[] { Open, Print, Export, Quick }) TView.EnableCommand(c);
    }

    public void Dispose()
    {
        TView.SetCommands(_saved);
        TView.commandSetChanged = false;
        _driver.Dispose();
    }

    private static TEvent KeyDown(ushort code)
    {
        TEvent ev = default;
        ev.What = Events.evKeyDown;
        ev.keyDown.keyCode = code;
        ev.keyDown.charScan.charCode = (byte)code;
        return ev;
    }

    /// <summary>Bar: File (Open, Print F5, More ▸ Export) and Quick, a command entry directly on the bar.</summary>
    private sealed class MenuApp : TApplication
    {
        private readonly Queue<(TEvent? Key, Action? Action)> _script = new();
        public readonly List<string> Log = new();

        public override TMenuBar InitMenuBar(TRect r)
        {
            r.b.y = r.a.y + 1;
            var more = new TMenu(new TMenuItem("~E~xport", Export, Keys.kbNoKey));
            var file = new TMenu(
                new TMenuItem("~O~pen", Open, Keys.kbNoKey, Views.hcNoContext, null,
                new TMenuItem("~P~rint", Print, Keys.kbF5, Views.hcNoContext, "F5",
                new TMenuItem("~M~ore", Keys.kbNoKey, more))));
            return new TMenuBar(r, new TMenu(
                new TMenuItem("~F~ile", Keys.kbAltF, file, Views.hcNoContext,
                new TMenuItem("~Q~uick", Quick, Keys.kbNoKey))));
        }

        public void Key(ushort code) => _script.Enqueue((KeyDown(code), null));
        public void Do(Action action) => _script.Enqueue((null, action));

        // Scripted input for nested menu loops. An action step runs, then Idle (the real broadcast path), and yields no event.
        public override void GetEvent(ref TEvent ev)
        {
            if (_script.Count == 0) { ev = KeyDown(Keys.kbEsc); return; }
            var (key, action) = _script.Dequeue();
            if (action is not null) { action(); Idle(); ev = default; return; }
            ev = key!.Value;
        }
    }

    private static MenuApp NewApp()
    {
        var app = new MenuApp();
        app.Idle();
        app.Redraw();
        return app;
    }

    private static TColorAttr LabelAttr(TMenuView view, TMenuItem item)
    {
        TRect rect = view.GetItemRect(item);
        TPoint at = view.MakeGlobal(new TPoint(rect.a.x + 2, rect.a.y));
        return TScreen.ScreenBuffer!.Data[at.y * TScreen.ScreenWidth + at.x].Attr;
    }

    [Fact]
    public void BothConstructorsSubscribeToBroadcasts()
    {
        Assert.NotEqual(0, new TMenuView(new TRect(0, 0, 20, 1)).eventMask & Events.evBroadcast);
        Assert.NotEqual(0, new TMenuView(new TRect(0, 0, 20, 1), null, null).eventMask & Events.evBroadcast);
    }

    [Fact]
    public void IdleBroadcastRefreshesBarAndNestedModelAndRepaintsWithoutExplicitRedraw()
    {
        using var app = NewApp();
        var bar = app.MenuBar!;
        var quick = bar.Menu!.Items!.Next!;
        var nested = bar.Menu.Items.SubMenu!.Items!.Next!.Next!.SubMenu!.Items!;
        var normal = LabelAttr(bar, quick);
        TView.DisableCommand(Quick); TView.DisableCommand(Export);
        app.Idle();
        Assert.True(quick.Disabled);
        Assert.True(nested.Disabled);
        Assert.Equal((TColorAttr)(byte)bar.GetColor(0x0202), LabelAttr(bar, quick));
        Assert.NotEqual(normal, LabelAttr(bar, quick));
        TView.EnableCommand(Quick); TView.EnableCommand(Export);
        app.Idle();
        Assert.False(quick.Disabled);
        Assert.False(nested.Disabled);
        Assert.Equal(normal, LabelAttr(bar, quick));
    }

    [Fact]
    public void PopupCreatedAfterLastBroadcastRefreshesBeforeEnterMnemonicAndAccelerator()
    {
        using var app = NewApp();
        TView.DisableCommand(Open); app.Idle();
        var first = new TMenuItem("~O~pen", Open, Keys.kbF5, Views.hcNoContext, null,
            new TMenuItem("~P~rint", Print, Keys.kbNoKey));
        var popup = new TMenuPopup(new TRect(10, 5, 60, 20), new TMenu(first));
        Assert.False(first.Disabled);
        app.Key(Keys.kbEnter); app.Key('o'); app.Key(Keys.kbF5);
        bool checkedOpen = false;
        app.Do(() =>
        {
            checkedOpen = true;
            Assert.True(first.Disabled);
            Assert.Same(first, popup.Current);
            Assert.Equal((TColorAttr)(byte)popup.GetColor(0x0505), LabelAttr(popup, first));
        });
        app.Key(Keys.kbDown); app.Key(Keys.kbEnter);
        Assert.Equal(Print, app.ExecView(popup));
        Assert.True(checkedOpen);
    }

    [Fact]
    public void OpenDropDownAndNestedMenuReceiveBroadcastsAndRepaint()
    {
        using var app = NewApp();
        var first = app.MenuBar!.Menu!.Items!.SubMenu!.Items!;
        var export = first.Next!.Next!.SubMenu!.Items!;
        TMenuBox? box = null;
        app.Key(Keys.kbAltF);
        app.Do(() => TView.DisableCommand(Open));
        app.Do(() =>
        {
            var file = Assert.IsType<TMenuBox>(TView.ModalTopView);
            Assert.True(first.Disabled);
            Assert.Equal((TColorAttr)(byte)file.GetColor(0x0505), LabelAttr(file, first));
        });
        app.Key(Keys.kbDown); app.Key(Keys.kbDown); app.Key(Keys.kbEnter);
        app.Do(() =>
        {
            box = Assert.IsType<TMenuBox>(TView.ModalTopView);
            TView.DisableCommand(Export);
        });
        app.Key(Keys.kbEnter);
        app.Do(() =>
        {
            Assert.True(export.Disabled);
            Assert.Same(export, box!.Current);
            Assert.Equal((TColorAttr)(byte)box.GetColor(0x0505), LabelAttr(box, export));
            TView.EnableCommand(Export);
        });
        app.Do(() =>
        {
            Assert.False(export.Disabled);
            Assert.Equal((TColorAttr)(byte)box!.GetColor(0x0604), LabelAttr(box, export));
        });
        app.Key(Keys.kbEnter);
        Assert.Equal(Export, app.ExecView(app.MenuBar));
    }
}
