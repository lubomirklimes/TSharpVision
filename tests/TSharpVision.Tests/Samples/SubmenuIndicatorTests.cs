using System.Text.Json;
using TSharpVision.Constants;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class SubmenuIndicatorTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(80, 25)] [InlineData(100, 35)]
    public void DemoKeyboardCascadeKeepsIndicatorsAndCommandRowsDistinct(int width, int height)
    {
        using var scope = new DemoTestScope(width, height); var app = scope.App;
        TMenuBox? parent = null;
        app.Step(() =>
        {
            parent = app.Modal<TMenuBox>();
            // Execute has assigned Current, but its first input has not painted the selection yet.
            Check(parent, parent.Menu!.Items!, selected: false);
            Capture(parent, "initial-demo");
            ComboInput.Send(ComboInput.Key(Keys.kbDown));
        });
        app.Step(() =>
        {
            Assert.Equal('C', parent!.Current!.HotChar()); CheckAll(parent); Capture(parent, "controls-selected");
            ComboInput.Send(ComboInput.Key(Keys.kbEnter));
        });
        app.Step(() =>
        {
            var child = app.Modal<TMenuBox>(); Assert.Same(parent, child.ParentMenu);
            Check(parent!, parent!.Current!); CheckCommandRows(child); Capture(child, "controls-open");
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbUp)));
        app.Step(() =>
        {
            Assert.Equal('D', parent!.Current!.HotChar()); CheckAll(parent); Capture(parent, "dialogs-selected");
            ComboInput.Send(ComboInput.Key(Keys.kbEnter));
        });
        app.Step(() =>
        {
            var child = app.Modal<TMenuBox>(); Assert.Same(parent, child.ParentMenu);
            Check(parent!, parent!.Current!); CheckCommandRows(child); Capture(child, "dialogs-open");
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbEsc)));
        app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbEsc)));
        var ev = ComboInput.Key(Keys.kbAltD); app.HandleEvent(ref ev);
        Assert.Null(TView.ModalTopView);
    }

    [Theory]
    [InlineData(80, 25)] [InlineData(100, 35)]
    public void MouseOpensDemoAndItsSubmenuWithVisibleIndicator(int width, int height)
    {
        using var scope = new DemoTestScope(width, height); var app = scope.App;
        var demo = TVDemoMenuNavigationTests.Items(app.MenuBar!.Menu!).Single(i => i.HotChar() == 'D');
        var r = app.MenuBar.GetItemRect(demo); var point = app.MenuBar.MakeGlobal(new TPoint(r.a.x + 1, r.a.y));
        app.Step(() =>
        {
            var box = app.Modal<TMenuBox>();
            var controls = TVDemoMenuNavigationTests.Items(box.Menu!).Single(i => i.HotChar() == 'C');
            var row = box.GetItemRect(controls); var p = box.MakeGlobal(new TPoint(row.a.x + 1, row.a.y));
            ComboInput.Click(p);
        });
        app.Step(() =>
        {
            var child = app.Modal<TMenuBox>(); var parent = Assert.IsType<TMenuBox>(child.ParentMenu);
            Assert.Equal('C', parent.Current!.HotChar()); Check(parent, parent.Current); CheckCommandRows(child);
            Capture(child, "mouse-controls"); ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbEsc)));
        app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbEsc)));
        var ev = ComboInput.Mouse(Events.evMouseDown, point); app.HandleEvent(ref ev);
        Assert.Null(TView.ModalTopView);
    }

    public static IEnumerable<object[]> ThemesAndSizes()
    {
        foreach (var theme in Enum.GetValues<TProgram.AP>())
            foreach (var size in new[] { (80, 25), (100, 35) })
                yield return new object[] { theme, size.Item1, size.Item2 };
    }

    [Theory]
    [MemberData(nameof(ThemesAndSizes))]
    public void DeepAndDisabledSubmenusRetainGlyphAndLogicalAttributeAtScreenEdge(TProgram.AP theme, int width, int height)
    {
        using var scope = new DemoTestScope(width, height); var app = scope.App; app.AppPalette = theme;
        var leaf = new TMenu(new TMenuItem("Leaf", Views.cmOK, Keys.kbNoKey));
        var deep = new TMenuItem("~D~eeper submenu", Keys.kbNoKey, leaf);
        var disabled = new TMenuItem("~U~navailable", Keys.kbNoKey, leaf) { Disabled = true };
        var command = new TMenuItem("~N~ormal", Views.cmOK, Keys.kbNoKey, Views.hcNoContext, "Enter");
        var separator = TMenuItem.NewLine(); deep.Next = disabled; disabled.Next = separator; separator.Next = command;
        var box = new SelectableBox(new TRect(width - 2, height - 2, width, height), new TMenu(deep));
        app.Insert(box);
        Assert.InRange(box.origin.x, 0, width - box.size.x); Assert.InRange(box.origin.y, 0, height - box.size.y);
        Assert.Equal("Deeper submenu".Length + 9, box.size.x);
        CheckAll(box);
        foreach (var item in new[] { deep, disabled, command }) { box.SelectItem(item); CheckAll(box); }
        var row = box.GetItemRect(separator); var pos = box.MakeGlobal(new TPoint(1, row.a.y));
        Assert.Equal('\u251C', RootScreenCopy.CopyRoot()[pos.x, pos.y].Character);
        // Force actual left clipping without changing the menu's natural width.
        box.MoveTo(-2, 1); box.DrawView(); CheckAll(box);
        Assert.Equal('\u2502', RootScreenCopy.CopyRoot()[box.size.x - 4, 2].Character);
    }

    [Theory]
    [InlineData(80, 25)] [InlineData(100, 35)]
    public void ThirdPopupLevelKeepsItsOwnSubmenuIndicator(int width, int height)
    {
        using var scope = new DemoTestScope(width, height); var app = scope.App;
        var terminal = new TMenu(new TMenuItem("Leaf", Views.cmOK, Keys.kbNoKey));
        var third = new TMenu(new TMenuItem("Third", Keys.kbNoKey, terminal));
        var second = new TMenu(new TMenuItem("Second", Keys.kbNoKey, third));
        var root = new SelectableBox(new TRect(1, 1, width, height), second);
        app.Insert(root); root.SelectItem(second.Items!); CheckAll(root);
        var child = new SelectableBox(new TRect(root.size.x, 2, width, height), third, root);
        app.Insert(child); child.SelectItem(third.Items!); CheckAll(child); Capture(child, "deep-popup");
        var last = new SelectableBox(new TRect(width - 1, 3, width, height), terminal, child);
        app.Insert(last); last.SelectItem(terminal.Items!); CheckCommandRows(last);
    }

    private sealed class SelectableBox(TRect bounds, TMenu menu, TMenuView? parent = null) : TMenuBox(bounds, menu, parent)
    {
        public void SelectItem(TMenuItem item) { Current = item; DrawView(); }
    }

    private void CheckAll(TMenuBox box)
    {
        foreach (var item in TVDemoMenuNavigationTests.Items(box.Menu!).Where(i => !string.IsNullOrEmpty(i.Name)))
            if (item.IsSubMenu) Check(box, item); else CheckNoArrow(box, item);
    }
    private void CheckCommandRows(TMenuBox box)
    {
        foreach (var item in TVDemoMenuNavigationTests.Items(box.Menu!).Where(i => !string.IsNullOrEmpty(i.Name)))
        { Assert.False(item.IsSubMenu); CheckNoArrow(box, item); }
    }
    private static void CheckNoArrow(TMenuBox box, TMenuItem item)
    {
        var row = box.GetItemRect(item); var pos = box.MakeGlobal(new TPoint(box.size.x - 4, row.a.y));
        Assert.NotEqual(TSharpVisionGlyphs.MenuSubmenuArrow, RootScreenCopy.CopyRoot()[pos.x, pos.y].Character);
    }
    private void Capture(TMenuBox box, string label)
    {
        var screen = RootScreenCopy.CopyRoot();
        output.WriteLine($"CAPTURE {label}: origin={box.origin.x},{box.origin.y} size={box.size.x},{box.size.y}");
        for (int y = 0; y < TScreen.ScreenHeight; y++)
            output.WriteLine(new string(Enumerable.Range(0, TScreen.ScreenWidth).Select(x => screen[x, y].Character).ToArray()));
        var buffer = TScreen.ScreenBuffer!;
        output.WriteLine("CELL_CAPTURE " + JsonSerializer.Serialize(new { label, width = TScreen.ScreenWidth, height = TScreen.ScreenHeight,
            chars = new string(buffer.Data.ToArray().Select(c => c.Character).ToArray()), attrs = Convert.ToHexString(buffer.Data.ToArray().Select(c => (byte)c.Attr).ToArray()) }));
    }
    private void Check(TMenuBox box, TMenuItem item, bool? selected = null)
    {
        var rect = box.GetItemRect(item); var pos = box.MakeGlobal(new TPoint(box.size.x - 4, rect.a.y));
        var cell = RootScreenCopy.CopyRoot()[pos.x, pos.y];
        bool active = selected ?? item == box.Current;
        ushort role = item.Disabled ? (active ? (ushort)5 : (ushort)2) : (active ? (ushort)4 : (ushort)1);
        byte expected = (byte)box.GetColor(role);
        output.WriteLine($"{item.Name}: command={item.Command} submenu={item.IsSubMenu} selected={active} indicator=U+{(int)cell.Character:X4} attr={(byte)cell.Attr:X2}");
        Assert.Equal(TSharpVisionGlyphs.MenuSubmenuArrow, cell.Character);
        Assert.Equal(expected, (byte)cell.Attr);
        Assert.NotEqual(expected & 15, (expected >> 4) & 7);
    }
}
