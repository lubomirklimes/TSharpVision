using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using Xunit;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class TVDemoMenuNavigationTests
{
    internal static IEnumerable<TMenuItem> Items(TMenu menu)
    {
        for (TMenuItem? item = menu.Items; item != null; item = item.Next) yield return item;
    }

    private static IEnumerable<(string Path, TMenuItem Item)> Walk(TMenu menu, string prefix = "")
    {
        foreach (var item in Items(menu).Where(i => !string.IsNullOrEmpty(i.Name)))
        {
            string path = prefix + item.Name!.Replace("~", "");
            if (item.IsSubMenu)
                foreach (var child in Walk(item.SubMenu, path + " > ")) yield return child;
            else yield return (path, item);
        }
    }

    [Fact]
    public void HierarchyContainsEveryCanonicalActionAndUniqueMnemonicsAndFits80Columns()
    {
        using var scope = new DemoTestScope();
        var menu = scope.App.MenuBar!.Menu!;
        var leaves = Walk(menu).ToArray();
        Assert.Equal(55, leaves.Length); // Existing actions plus Character Map, the three framework palette modes, Help > Search (TV12l), STYX graphics (TV12n), Graphics gallery and Live chart (TV12o)
        Assert.Equal(55, leaves.Select(x => x.Item.Command).Distinct().Count());
        Assert.EndsWith(" > STYX graphics", leaves.Single(x => x.Item.Command == ShowcaseCmd.Styx).Path);
        Assert.Equal($"{TSharpVisionGlyphs.SystemMenu} > Graphics gallery", leaves.Single(x => x.Item.Command == ShowcaseCmd.GraphicsGallery).Path);
        Assert.Equal($"{TSharpVisionGlyphs.SystemMenu} > Live chart", leaves.Single(x => x.Item.Command == ShowcaseCmd.LiveChart).Path);
        Assert.Equal("Help > Search...", leaves.Single(x => x.Item.Command == Views.cmHelpSearch).Path);
        Assert.Equal("Options > Framework palette > (●) Color", leaves.Single(x => x.Item.Command == ShowcaseCmd.PaletteColor).Path);
        foreach (ushort code in Enumerable.Range(500, 7).Select(i => (ushort)i))
            Assert.Single(leaves, x => x.Item.Command == code);
        Assert.Equal("Demo > Dialogs > Open file dialog...", leaves.Single(x => x.Item.Command == 500).Path);
        Assert.Equal("Demo > Controls > Group boxes...", leaves.Single(x => x.Item.Command == ShowcaseCmd.GroupBox).Path);
        Assert.Equal("Demo > Views and tools > Code editor...", leaves.Single(x => x.Item.Command == ShowcaseCmd.CodeEditor).Path);
        Assert.Equal("File > Open file viewer...", leaves.Single(x => x.Item.Command == TVDemoCmd.cmFileViewer).Path);
        Check(menu, true);

        static void Check(TMenu current, bool top = false)
        {
            var items = Items(current).Where(i => !string.IsNullOrEmpty(i.Name)).ToArray();
            Assert.All(items, i => Assert.NotEqual('\0', i.HotChar()));
            Assert.Equal(items.Length, items.Select(i => i.HotChar()).Distinct().Count());
            if (top) Assert.True(1 + items.Sum(i => i.Name!.Replace("~", "").Length + 2) <= 80);
            else
            {
                var box = new TMenuBox(new TRect(1, 1, 80, 24), current, null);
                try
                {
                    Assert.True(box.origin.x >= 0 && box.origin.y >= 0);
                    Assert.True(box.origin.x + box.size.x <= 80);
                    Assert.True(box.origin.y + box.size.y <= 24);
                    Assert.True(box.size.y <= 23);
                }
                finally { box.ShutDown(); }
            }
            foreach (var item in items.Where(i => i.IsSubMenu))
            {
                Assert.Equal((ushort)0, item.Command);
                Check(item.SubMenu);
            }
        }
    }

    [Theory]
    [InlineData('d', 'o', ShowcaseCmd.FileOpenDialog)]
    [InlineData('d', 'v', ShowcaseCmd.FileSaveDialog)]
    [InlineData('d', 'c', ShowcaseCmd.ChangeDirectory)]
    [InlineData('d', 'm', ShowcaseCmd.MessageBoxes)]
    [InlineData('d', 'i', ShowcaseCmd.InputBox)]
    [InlineData('c', 'v', ShowcaseCmd.ValidatedInput)]
    [InlineData('c', 'l', ShowcaseCmd.Memo)]
    public void ActualNestedMenuKeysDispatchEveryNewHandler(char subgroup, char leaf, ushort command)
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        app.Step(() =>
        {
            Assert.IsType<TMenuBox>(TView.ModalTopView);
            ComboInput.Send(ComboInput.Typed(subgroup.ToString()));
        });
        app.Step(() =>
        {
            var box = app.Modal<TMenuBox>();
            Assert.Contains(Items(box.Menu!), i => i.Command == command);
            ComboInput.Send(ComboInput.Typed(leaf.ToString()));
        });
        var altD = ComboInput.Key(Keys.kbAltD);
        app.HandleEvent(ref altD);
        TEvent ev = default;
        app.GetEvent(ref ev);
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(command, ev.message.command);

        app.Step(() =>
        {
            var dialog = app.Modal<TDialog>();
            Assert.Equal(command switch
            {
                ShowcaseCmd.FileOpenDialog => typeof(DemoFileDialog),
                ShowcaseCmd.FileSaveDialog => typeof(DemoFileDialog),
                ShowcaseCmd.ChangeDirectory => typeof(DemoDirectoryDialog),
                ShowcaseCmd.MessageBoxes => typeof(MessageDemoDialog),
                ShowcaseCmd.InputBox => typeof(TDialog),
                ShowcaseCmd.ValidatedInput => typeof(ValidatedInputDemoDialog),
                _ => typeof(MemoDemoDialog)
            }, dialog.GetType());
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        if (command != ShowcaseCmd.MessageBoxes) app.DismissResult(command == ShowcaseCmd.InputBox ? "World" : "ancel");
        app.RunCommand(command);
    }

    [Fact]
    public void EscapeBacksOutOneSubmenuAndEstablishedShortcutsKeepTheirBindings()
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        app.Step(() => ComboInput.Send(ComboInput.Typed("c")));
        app.Step(() =>
        {
            Assert.Contains(Items(app.Modal<TMenuBox>().Menu!), i => i.Command == ShowcaseCmd.ComboBox);
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.Step(() =>
        {
            Assert.Equal(new[] { 'D', 'C', 'V' }, Items(app.Modal<TMenuBox>().Menu!).Select(i => i.HotChar()));
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        // The root menu bar is still modal after dismissing its Demo popup.
        app.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbEsc)));
        var key = ComboInput.Key(Keys.kbAltD);
        app.HandleEvent(ref key);
        Assert.Null(TView.ModalTopView);
        var leaves = Walk(app.MenuBar!.Menu!).ToArray();
        foreach (var (code, shortcut, label) in new (ushort, ushort, string)[]
        {
            (TVDemoCmd.cmFileViewer, Keys.kbF3, "F3"),
            (ShowcaseCmd.NewWindow, Keys.kbF4, "F4"),
            (Views.cmQuit, Keys.kbAltX, "Alt+X"),
            (Views.cmNext, Keys.kbF6, "F6"),
            (Views.cmPrev, Keys.kbShiftF6, "Shift+F6"),
            (Views.cmZoom, Keys.kbF5, "F5"),
            (Views.cmResize, Keys.kbCtrlF5, "Ctrl+F5"),
            (Views.cmClose, Keys.kbAltF3, "Alt+F3")
        })
        {
            var item = Assert.Single(leaves, x => x.Item.Command == code).Item;
            Assert.Equal(shortcut, item.KeyCode);
            Assert.Equal(label, item.Param);
        }
        Assert.Equal(new ushort[] { Keys.kbAltSpace, Keys.kbAltF, Keys.kbAltD, Keys.kbAltI,
            Keys.kbAltO, Keys.kbAltW, Keys.kbAltH }, Items(app.MenuBar.Menu!).Select(i => i.KeyCode));
    }

    [Fact]
    public void NewDialogChildrenAndRenderedFramesStayInside80By25Desktop()
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        var memo = TVDemoApp.CreateMemoDemoData();
        TDialog[] dialogs =
        {
            new DemoFileDialog(false), new DemoFileDialog(true), new DemoDirectoryDialog(),
            new MessageDemoDialog(new TRect(8, 3, 72, 19)),
            new ValidatedInputDemoDialog(new TRect(5, 0, 75, 21)),
            new MemoDemoDialog(new TRect(5, 0, 75, 21), memo),
            app.BuildComboBoxDemoDialog(out _, out _), app.BuildHistoryDemoDialog(),
            TVDemoApp.CreateGroupBoxDemoDialog(new TRect(8, 1, 72, 20))
        };
        foreach (TDialog dialog in dialogs)
        {
            try
            {
                app.DeskTop!.Insert(dialog);
                app.Redraw();
                Assert.True(dialog.origin.x >= 0 && dialog.origin.y >= 0);
                Assert.True(dialog.origin.x + dialog.size.x <= 80);
                Assert.True(dialog.origin.y + dialog.size.y <= 23);
                CheckChildren(dialog);
            }
            finally { dialog.ShutDown(); }
        }
        static void CheckChildren(TGroup parent)
        {
            parent.ForEachView(view =>
            {
                var bounds = view.GetBounds();
                Assert.True(bounds.a.x >= 0 && bounds.a.y >= 0, view.GetType().Name);
                Assert.True(bounds.b.x <= parent.size.x && bounds.b.y <= parent.size.y, view.GetType().Name);
                if (view is TGroup child) CheckChildren(child);
            });
        }
    }

    [Fact]
    public void ComboF4EscEnterAndTabRemainLocalAndHistoryCanRecall()
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        TComboBox? combo = null;
        TDialog? dialog = null;
        app.Step(() =>
        {
            dialog = app.Modal<TDialog>();
            Assert.Contains("Alt+Down / F4", ScriptedDemoApp.Text(dialog));
            combo = ScriptedDemoApp.Descendants(dialog).OfType<TComboBox>().First(c => !c.IsEditable);
            ComboInput.Send(ComboInput.Key(Keys.kbF4), ComboInput.Key(Keys.kbDown));
        });
        app.Step(() =>
        {
            Assert.True(combo!.IsDropDownOpen);
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.Step(() =>
        {
            Assert.Same(dialog, app.Modal<TDialog>());
            Assert.Equal("Keep", combo!.Text);
            ComboInput.Send(ComboInput.Key(Keys.kbF4), ComboInput.Key(Keys.kbDown), ComboInput.Key(Keys.kbEnter));
        });
        app.Step(() =>
        {
            Assert.Same(dialog, app.Modal<TDialog>());
            Assert.Equal("Lower", combo!.Text);
            ComboInput.Send(ComboInput.Key(Keys.kbTab));
        });
        app.Step(() =>
        {
            Assert.True(ScriptedDemoApp.Descendants(dialog!).OfType<TComboBox>().Single(c => c.IsEditable).GetState(Views.sfFocused));
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.RunCommand(ShowcaseCmd.ComboBox);

        app.Step(() =>
        {
            dialog = app.Modal<TDialog>();
            Assert.Contains("Down recalls", ScriptedDemoApp.Text(dialog));
            Assert.Single(ScriptedDemoApp.Descendants(dialog).OfType<TInputLine>()).SetData("recall-this");
            ScriptedDemoApp.Button(dialog, Views.cmOK).Press();
        });
        app.RunCommand(ShowcaseCmd.History);
        app.Step(() =>
        {
            dialog = app.Modal<TDialog>();
            ComboInput.Send(ComboInput.Key(Keys.kbDown));
        });
        app.Step(() =>
        {
            var history = app.Modal<THistoryWindow>();
            Assert.Contains(Enumerable.Range(0, history.Viewer!.range).Select(i => history.Viewer.GetText(i, 100)), s => s == "recall-this");
            int index = Enumerable.Range(0, history.Viewer.range).Single(i => history.Viewer.GetText(i, 100) == "recall-this");
            history.Viewer.FocusItem(index);
            ComboInput.Send(ComboInput.Key(Keys.kbEnter));
        });
        app.Step(() =>
        {
            Assert.Equal("recall-this", Assert.Single(ScriptedDemoApp.Descendants(dialog!).OfType<TInputLine>()).Data);
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.RunCommand(ShowcaseCmd.History);
    }

    [Fact]
    public void GroupChildMnemonicsJumpAcrossGroupsWithoutCloseConflict()
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        TDialog? dialog = null;
        app.Step(() =>
        {
            dialog = app.Modal<TDialog>();
            ComboInput.Send(ComboInput.Key(Keys.kbAltN));
        });
        app.Step(() =>
        {
            Assert.True(Assert.Single(ScriptedDemoApp.Descendants(dialog!).OfType<TInputLine>()).GetState(Views.sfFocused));
            ComboInput.Send(ComboInput.Key(Keys.kbAltD));
        });
        app.Step(() =>
        {
            var radio = Assert.Single(ScriptedDemoApp.Descendants(dialog!).OfType<TRadioButtons>());
            Assert.True(radio.GetState(Views.sfFocused));
            Assert.Equal(1u, radio.value);
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        app.RunCommand(ShowcaseCmd.GroupBox);
    }

    [Fact]
    public void CompiledHelpPreservesTopicsAndLinksAndDescribesTheActualMenu()
    {
        using var scope = new DemoTestScope();
        var file = scope.App.GetHelpFile();
        var ids = new ushort[] { 0, 1, 2, 3, 5, 6, 7, 8, 9, 10 };
        var texts = new List<string>();
        foreach (ushort id in ids)
        {
            var topic = file.GetTopic(id);
            Assert.NotNull(topic.paragraphs);
            for (var paragraph = topic.paragraphs; paragraph != null; paragraph = paragraph.next)
                texts.Add(paragraph.Text);
            for (int i = 0; i < topic.numRefs; i++)
                Assert.Contains((ushort)topic.crossRefs![i]!.@ref, ids);
        }
        string text = string.Join("\n", texts);
        foreach (string expected in new[] { "Dialogs", "Controls", "Views and tools", "Change directory",
            "Validated input", "Multiline memo", "Alt+Down", "Ctrl+Q F", "Save As", "CWD" })
            Assert.Contains(expected, text);
        Assert.DoesNotContain("Sokoban", text);
        Assert.DoesNotContain("Accessories contains", text);
        Assert.DoesNotContain("Games contains", text);
    }
}
