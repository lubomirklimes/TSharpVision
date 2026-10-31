using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Menus;

// TW-F1: menu entries follow the framework command set. TMenuView subscribes to evBroadcast, so the cmCommandSetChanged
// broadcast sent by TProgram.Idle reaches the bar and every open drop-down, refreshes TMenuItem.Disabled and redraws.
// Both release assertions and development inspection read the composed logical screen.
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
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = code;
        ev.keyDown.charScan = new CharScanType(code);
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
        var cells = RootScreenCopy.CopyRoot();
        var inspected = Entry(ReadMenu(app, cells).Bar!, "Quick");
        Assert.True(inspected.Disabled);
        Assert.False(inspected.CommandEnabled);
        Assert.Equal("match", inspected.Observed);
        Assert.Equal((byte)bar.GetColor(0x0202), LabelAttr(cells, inspected));
        Assert.True(nested.Disabled);
        Assert.Equal((TColorAttr)(byte)bar.GetColor(0x0202), LabelAttr(bar, quick));
        Assert.NotEqual(normal, LabelAttr(bar, quick));
        TView.EnableCommand(Quick); TView.EnableCommand(Export);
        app.Idle();
        Assert.False(quick.Disabled);
        cells = RootScreenCopy.CopyRoot();
        inspected = Entry(ReadMenu(app, cells).Bar!, "Quick");
        Assert.False(inspected.Disabled);
        Assert.True(inspected.CommandEnabled);
        Assert.Equal(Normal(bar), LabelAttr(cells, inspected));
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
            var cells = RootScreenCopy.CopyRoot();
            var state = ReadMenu(app, cells);
            var inspected = Entry(state.Open[0], "Open");
            Assert.True(inspected.Disabled);
            Assert.Equal((byte)popup.GetColor(0x0505), LabelAttr(cells, inspected));
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
            var state = ReadMenu(app, RootScreenCopy.CopyRoot());
            Assert.Equal(2, state.Depth);
            Assert.Equal("More", state.Open[1].ParentItem);
            Assert.True(Entry(state.Open[1], "Export").Disabled);
            Assert.True(Entry(state.Open[0], "Open").Disabled);
            Assert.Same(export, box!.Current);
            Assert.Equal((TColorAttr)(byte)box.GetColor(0x0505), LabelAttr(box, export));
            TView.EnableCommand(Export);
        });
        app.Do(() =>
        {
            Assert.False(export.Disabled);
            var cells = RootScreenCopy.CopyRoot();
            var inspected = Entry(ReadMenu(app, cells).Open[1], "Export");
            Assert.True(inspected.CommandEnabled);
            Assert.Equal("match", inspected.Observed);
            Assert.Equal((byte)box!.GetColor(0x0604), LabelAttr(cells, inspected));
            Assert.Equal((TColorAttr)(byte)box!.GetColor(0x0604), LabelAttr(box, export));
        });
        app.Key(Keys.kbEnter);
        Assert.Equal(Export, app.ExecView(app.MenuBar));
    }

    // Only the model fields and rendered label used by these regressions are copied.
    private sealed record MenuEntry(string Text, bool Disabled, bool CommandEnabled, TPoint Screen, string Observed);
    private sealed record MenuViewState(string? SelectedText, string? ParentItem, MenuEntry[] Items);
    private sealed record MenuState(MenuViewState? Bar, MenuViewState[] Open)
    {
        public int Depth => Open.Length;
    }

    private static string? Label(TMenuItem? item) => item?.Name?.Replace("~", "", StringComparison.Ordinal);

    private static MenuState ReadMenu(TProgram app, RootScreenCopy cells)
    {
        MenuViewState Read(TMenuView view)
        {
            var items = new List<MenuEntry>();
            for (var item = view.Menu?.Items; item is not null; item = item.Next)
            {
                if (string.IsNullOrEmpty(item.Name)) continue;
                string label = Label(item)!;
                var rect = view.GetItemRect(item);
                var at = view.MakeGlobal(rect.a);
                string rendered = new(Enumerable.Range(0, label.Length)
                    .Select(x => cells[at.x + 1 + x, at.y].Character).ToArray());
                items.Add(new(label, item.Disabled, item.Command != 0 && TView.CommandEnabled(item.Command),
                    at, rendered == label ? "match" : "mismatch"));
            }
            return new(Label(view.Current), Label(view.ParentMenu?.Current), items.ToArray());
        }
        var open = new List<TMenuView>();
        for (var view = app.TopView() as TMenuView; view is not null; view = view.ParentMenu)
            if (!ReferenceEquals(view, app.MenuBar)) open.Insert(0, view);
        return new(app.MenuBar is { } bar ? Read(bar) : null, open.Select(Read).ToArray());
    }

    private static MenuEntry Entry(MenuViewState level, string text) => level.Items.Single(i => i.Text == text);

    // Attribute of the label's second character; the first is the mnemonic.
    private static byte LabelAttr(RootScreenCopy cells, MenuEntry e) => cells[e.Screen.x + 2, e.Screen.y].Attr;
    private static byte Normal(TView v) => (byte)v.GetColor(0x0301);

    [Fact]
    public void DisabledEntryIsRefusedByEnterMnemonicAndAcceleratorAndReenablingRestoresIt()
    {
        using var app = NewApp();
        TView.DisableCommand(Print);
        app.Idle();
        Assert.True(app.MenuBar!.Menu!.Items!.SubMenu!.Items!.Next!.Disabled);

        // Outside the modal loop: the F5 accelerator is not dispatched.
        TEvent f5 = KeyDown(Keys.kbF5);
        app.MenuBar.HandleEvent(ref f5);
        Assert.Equal(Events.evKeyDown, f5.What);

        MenuState? open = null; RootScreenCopy? shown = null;
        app.Key(Keys.kbAltF);
        app.Key(Keys.kbDown);            // onto Print
        app.Key(Keys.kbEnter);           // refused: the drop-down stays open
        app.Key('p');                    // mnemonic of a disabled entry: no match
        app.Key(Keys.kbF5);              // accelerator inside the loop: refused
        app.Do(() => { shown = RootScreenCopy.CopyRoot(); open = ReadMenu(app, shown); });
        app.Key(Keys.kbEsc);
        Assert.Equal((ushort)0, app.ExecView(app.MenuBar));

        Assert.NotNull(open);
        Assert.Equal(1, open!.Depth);
        var level = open.Open[0];
        Assert.Equal("Print", level.SelectedText);
        var print = Entry(level, "Print");
        Assert.True(print.Disabled);
        Assert.Equal((byte)app.MenuBar.GetColor(0x0505), LabelAttr(shown!, print)); // selected + disabled
        Assert.Equal(Normal(app.MenuBar), LabelAttr(shown!, Entry(level, "Open")));

        // Re-enabled: the same keys now select the command.
        TView.EnableCommand(Print);
        app.Idle();
        Assert.False(app.MenuBar.Menu!.Items!.SubMenu!.Items!.Next!.Disabled);
        app.Key(Keys.kbAltF);            // the drop-down reopens on its remembered entry, Print
        app.Key(Keys.kbHome);
        app.Key(Keys.kbDown);
        app.Key(Keys.kbEnter);
        Assert.Equal(Print, app.ExecView(app.MenuBar));
    }

    [Fact]
    public void NestedEntryDisabledBeforeOpeningIsDrawnDisabledAndIsNotExecutable()
    {
        using var app = NewApp();
        TView.DisableCommand(Export);
        app.Idle();
        MenuState? nested = null; RootScreenCopy? cells = null;
        app.Key(Keys.kbAltF); app.Key(Keys.kbDown); app.Key(Keys.kbDown); app.Key(Keys.kbEnter);
        app.Key(Keys.kbEnter); // Export refused
        app.Do(() => { cells = RootScreenCopy.CopyRoot(); nested = ReadMenu(app, cells); });
        app.Key(Keys.kbEsc); app.Key(Keys.kbEsc);
        Assert.Equal((ushort)0, app.ExecView(app.MenuBar!));

        Assert.Equal(2, nested!.Depth);
        Assert.Equal("More", nested.Open[1].ParentItem);
        var export = Entry(nested.Open[1], "Export");
        Assert.True(export.Disabled);
        Assert.Equal((byte)app.MenuBar!.GetColor(0x0505), LabelAttr(cells!, export));
        // Navigation stayed intact around the refused entry.
        Assert.Equal("More", nested.Open[0].SelectedText);
        Assert.Equal("File", nested.Bar!.SelectedText);
    }
}
