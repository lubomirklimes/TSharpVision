using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class TVDemoLayoutPolishTests
{
    public static IEnumerable<object[]> HistoryCases()
    {
        foreach (TProgram.AP theme in Enum.GetValues<TProgram.AP>())
            foreach (int context in new[] { 0, 1, 2 })
                yield return new object[] { theme, context };
    }

    [Theory]
    [MemberData(nameof(HistoryCases))]
    public void HistoryUsesComboSurfaceAndParentRolesInEveryState(TProgram.AP theme, int context)
    {
        using var scope = new DemoTestScope();
        scope.App.AppPalette = theme;
        TDialog dialog = context switch
        {
            0 => new DemoDirectoryDialog(),
            1 => scope.App.BuildHistoryDemoDialog(),
            _ => new AlternateDialog()
        };
        if (context == 2)
        {
            var line = new TInputLine(new TRect(2, 2, 20, 3), 30);
            dialog.Insert(line); dialog.Insert(new THistory(new TRect(20, 2, 23, 3), line, 78));
        }
        var history = Assert.Single(ScriptedDemoApp.Descendants(dialog).OfType<THistory>());
        var input = history.Link!;
        var combo = new TComboBox(new TRect(3, 1, 22, 2), new[] { "Compare" });
        dialog.Insert(combo);
        scope.App.DeskTop!.Insert(dialog);
        input.SetData("selected text"); input.SelectAll(true);
        foreach (int state in new[] { 0, 1, 2, 3 })
        {
            input.SetState(Views.sfDisabled, false); history.SetState(Views.sfDisabled, false);
            combo.SetState(Views.sfDisabled, false);
            if (state == 1) input.Select(); else combo.Select();
            if (state == 2) { input.SetState(Views.sfDisabled, true); combo.SetState(Views.sfDisabled, true); }
            if (state == 3) { history.SetState(Views.sfDisabled, true); combo.SetState(Views.sfDisabled, true); }
            history.DrawView(); // Linked input state has no sibling-change notification.
            scope.App.Redraw();
            var screen = RootScreenCopy.CopyRoot();
            var h = history.MakeGlobal(new TPoint(0, 0));
            var c = combo.MakeGlobal(new TPoint(combo.size.x - 3, 0));
            byte surface = (byte)screen[c.x, c.y].Attr;
            Assert.Equal((byte)dialog.GetColor(state >= 2 ? (ushort)0x1F : (ushort)0x0A), surface);
            if (theme == TProgram.AP.Color && context != 2 && state < 2) Assert.Equal((byte)0x20, surface);
            for (int x = 0; x < 3; x++)
            {
                var cell = screen[h.x + x, h.y];
                Assert.Equal(" \u2193 "[x], cell.Character);
                Assert.Equal(x == 1 && state == 1 ? (byte)dialog.GetColor(0x0E) : surface, (byte)cell.Attr);
            }
        }
        dialog.ShutDown();
    }

    // Reproduce the previous draw path in a test-only view, then compare real screen cells.
    [Fact]
    public void OldHistorySurfaceWasGrayAndNewNeighborsAreGreen()
    {
        using var scope = new DemoTestScope();
        var dialog = new TDialog(new TRect(0, 0, 40, 9), "Palette comparison");
        var input = new TInputLine(new TRect(2, 2, 20, 3), 30);
        var old = new LegacyHistory(new TRect(20, 2, 23, 3), input);
        var current = new THistory(new TRect(24, 2, 27, 3), input, 79);
        dialog.Insert(input); dialog.Insert(old); dialog.Insert(current);
        scope.App.DeskTop!.Insert(dialog); scope.App.Redraw();
        var screen = RootScreenCopy.CopyRoot();
        var a = old.MakeGlobal(new TPoint(0, 0)); var b = current.MakeGlobal(new TPoint(0, 0));
        foreach (int x in new[] { 0, 2 })
        {
            Assert.Equal(' ', screen[a.x + x, a.y].Character);
            Assert.Equal((byte)dialog.GetColor(0x17), (byte)screen[a.x + x, a.y].Attr);
            Assert.Equal((byte)0x20, (byte)screen[b.x + x, b.y].Attr);
            Assert.NotEqual((byte)screen[a.x + x, a.y].Attr, (byte)screen[b.x + x, b.y].Attr);
        }
        dialog.ShutDown();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ChoosersMatchDirectoryReflowAllChildrenAndRejectZoom(bool save)
    {
        using var scope = new DemoTestScope();
        var reference = new DemoDirectoryDialog();
        var dialog = new DemoFileDialog(save);
        scope.App.DeskTop!.Insert(dialog);
        Assert.Equal(reference.size, dialog.size);
        Assert.Equal(reference.flags, dialog.flags);
        Assert.Equal((byte)0, dialog.growMode);
        Assert.Equal(0, dialog.flags & (Views.wfGrow | Views.wfZoom));
        var children = Children(dialog).Where(v => v is not TFrame).ToArray();
        Assert.DoesNotContain(children, v => v is TStaticText && v is not TLabel);
        foreach (var child in children)
        {
            var r = child.GetBounds();
            Assert.InRange(r.a.x, 1, dialog.size.x - 2); Assert.InRange(r.a.y, 1, dialog.size.y - 2);
            Assert.InRange(r.b.x, r.a.x + 1, dialog.size.x - 1); Assert.InRange(r.b.y, r.a.y + 1, dialog.size.y - 1);
            foreach (var other in children.Where(v => !ReferenceEquals(v, child)))
            {
                var s = other.GetBounds();
                Assert.False(r.a.x < s.b.x && s.a.x < r.b.x && r.a.y < s.b.y && s.a.y < r.b.y,
                    child.GetType().Name + " overlaps " + other.GetType().Name);
            }
        }
        Assert.Equal(dialog.fileList!.GetBounds().b.x, dialog.fileList.vScrollBar!.origin.x);
        Assert.Equal(dialog.fileList.GetBounds().b.y + 1, Assert.Single(children.OfType<TFileInfoPane>()).origin.y);
        Assert.All(children.OfType<TButton>(), b => Assert.Equal(71, b.GetBounds().b.x));
        var before = dialog.GetBounds();
        var zoom = ScriptedDemoApp.CommandEvent(Views.cmZoom); scope.App.HandleEvent(ref zoom);
        Assert.Equal(before, dialog.GetBounds());
        // Fixed geometry also survives a larger desktop grid.
        scope.App.DeskTop!.ChangeBounds(new TRect(0, 1, 100, 34));
        Assert.Equal(reference.size, dialog.size);
        reference.ShutDown(); dialog.ShutDown();
    }

    [Theory]
    [InlineData(ShowcaseCmd.SimpleDialog, 50, 9, 6)]
    [InlineData(ShowcaseCmd.Selection, 54, 13, 9)]
    public void CompactDialogsAreReachableAndHaveOneSeparatorAndBottomRow(ushort command, int width, int height, int buttonY)
    {
        using var scope = new DemoTestScope();
        scope.App.Step(() =>
        {
            var dialog = scope.App.Modal<TDialog>();
            Assert.Equal(new TPoint(width, height), dialog.size);
            var children = Children(dialog).Where(v => v is not TFrame).ToArray();
            var buttons = children.OfType<TButton>().ToArray();
            Assert.All(buttons, b => Assert.Equal(buttonY, b.origin.y));
            Assert.Equal(command == ShowcaseCmd.SimpleDialog ? 0 : 1, height - 1 - buttons.Max(b => b.GetBounds().b.y));
            Assert.Equal(1, buttonY - children.Where(v => v is not TButton).Max(v => v.GetBounds().b.y));
            ComboInput.Click(buttons.Single(b => b.Command == Views.cmCancel).MakeGlobal(new TPoint(2, 0)));
        });
        scope.App.RunCommand(command);
    }

    [Theory]
    [InlineData("Short result")]
    [InlineData("One\nTwo\nThree")]
    [InlineData("A long path " + "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz")]
    public void ResultBoundsUseActualWrappedContent(string text)
    {
        using var scope = new DemoTestScope();
        var r = TVDemoApp.ResultBounds(scope.App.DeskTop, text);
        Assert.Equal(TStaticText.CountRows(text, r.b.x - r.a.x - 5) + 6, r.b.y - r.a.y);
        Assert.InRange(r.b.x, 1, 80); Assert.InRange(r.b.y, 1, 23);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ResourceDialogCompactsNewAndPreviouslySavedSample(bool legacy)
    {
        using var scope = new DemoTestScope();
        var dialog = TVDemoApp.BuildResourceSampleDialog();
        if (legacy)
        {
            dialog.ChangeBounds(new TRect(0, 0, 56, 13));
            foreach (var button in Children(dialog).OfType<TButton>()) button.MoveTo(button.origin.x, 10);
        }
        TVDemoApp.CompactLegacyResourceDialog(dialog);
        Assert.Equal(new TPoint(56, 12), dialog.size);
        Assert.All(Children(dialog).OfType<TButton>(), button => Assert.Equal(8, button.origin.y));
        var line = Assert.Single(Children(dialog).OfType<TInputLine>());
        Assert.Equal("type something here", line.Data);
        scope.App.Step(() => ComboInput.Send(ComboInput.Key(Keys.kbEsc)));
        Assert.Equal(Views.cmCancel, scope.App.DeskTop!.ExecView(dialog));
        dialog.ShutDown();
    }

    [Fact]
    public void SaveChooserAcceptsExistingFileByMouseWithoutOverwriting()
    {
        using var temp = new TempDirectory();
        string path = Path.Combine(temp.Path, "keep.txt"); File.WriteAllText(path, "Unchanged content");
        using var scope = new DemoTestScope();
        scope.App.Step(() =>
        {
            var dialog = scope.App.Modal<DemoFileDialog>(); dialog.SetData(path);
            ComboInput.Click(ScriptedDemoApp.Button(dialog, Views.cmFileOpen).MakeGlobal(new TPoint(2, 0)));
        });
        scope.App.DismissResult(path);
        scope.App.RunCommand(ShowcaseCmd.FileSaveDialog);
        Assert.Equal("Unchanged content", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(temp.Path));
    }

    private static TView[] Children(TGroup group)
    {
        var views = new List<TView>(); group.ForEachView(views.Add); return views.ToArray();
    }

    private sealed class LegacyHistory : THistory
    {
        internal LegacyHistory(TRect bounds, TInputLine input) : base(bounds, input, 79) { }
        public override TPalette GetPalette() => new("\x16\x17", 2);
        public override void Draw()
        {
            Span<TScreenChar> row = stackalloc TScreenChar[3];
            var buffer = new TDrawBuffer(row); buffer.moveCStr(0, " \u2193 ", GetColor(0x0102));
            WriteLine(0, 0, 3, 1, buffer);
        }
    }
    private sealed class AlternateDialog : TDialog
    {
        private readonly TPalette _colors;
        internal AlternateDialog() : base(new TRect(0, 0, 40, 10), "Alternate")
        {
            _colors = base.GetPalette().Clone();
            _colors[0x0A] = 0x13; _colors[0x0E] = 0x14; _colors[0x1F] = 0x15;
        }
        public override TPalette GetPalette() => _colors ?? base.GetPalette();
    }
}
