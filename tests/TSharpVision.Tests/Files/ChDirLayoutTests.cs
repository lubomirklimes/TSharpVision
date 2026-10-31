using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using TSharpVision.Tests.Samples;
using Xunit;

namespace TSharpVision.Tests.Files;

[Collection("NonParallel")]
public sealed class ChDirLayoutTests
{
    [Fact]
    public void DemoPreservesOuterSizeAndCentersWithCleanRightAlignedActions()
    {
        using var scope = new DemoTestScope();
        var dialog = new DemoDirectoryDialog(); scope.App.DeskTop!.Insert(dialog);
        Assert.Equal(new TPoint(74, 19), dialog.size);
        Assert.True((dialog.options & Views.ofCentered) != 0);
        Assert.True((dialog.flags & Views.wfGrow) == 0); // remains a fixed-size modal dialog
        Assert.DoesNotContain(ScriptedDemoApp.Descendants(dialog).OfType<TStaticText>(), v => v is not TLabel);
        var tree = dialog.dirList!;
        Assert.True(tree.size.x > 29); // reclaimed width improves the viewport, not just empty padding
        Assert.Equal(tree.origin.x, dialog.dirInput!.origin.x);
        Assert.True(dialog.dirInput.size.x >= tree.size.x - 3);
        var history = Assert.Single(ScriptedDemoApp.Descendants(dialog).OfType<THistory>());
        Assert.Same(dialog.dirInput, history.Link);
        Assert.Equal(dialog.dirInput.GetBounds().b.x, history.origin.x);
        Assert.Equal(tree.vScrollBar!.GetBounds().b.x, history.GetBounds().b.x);
        var buttons = ScriptedDemoApp.Descendants(dialog).OfType<TButton>().ToArray();
        Assert.Equal(new ushort[] { Views.cmOK, Views.cmChangeDir, Views.cmRevert }, buttons.Select(b => b.Command).OrderBy(c => c));
        foreach (var button in buttons)
        {
            Assert.Equal(new TPoint(10, 2), button.size);
            Assert.Equal(dialog.size.x - 3, button.origin.x + button.size.x);
            Assert.True(button.origin.x > tree.vScrollBar!.origin.x + tree.vScrollBar.size.x);
        }
        Assert.Equal(new[] { 3, 6, 9 }, buttons.Select(b => (int)b.origin.y).OrderBy(y => y));
        CheckBars(tree);
        Assert.InRange(dialog.origin.x + dialog.size.x, 1, 80);
        Assert.InRange(dialog.origin.y + dialog.size.y, 1, 23);
        Assert.Equal(DemoHelpCtx.MainMenu, dialog.GetHelpCtx());
        var topic = scope.App.GetHelpFile().GetTopic(DemoHelpCtx.MainMenu);
        string help = "";
        for (var p = topic.paragraphs; p != null; p = p.next) help += p.Text;
        foreach (string text in new[] { "Chdir browses only", "Revert refreshes", "OK changes process CWD", "Esc/frame close cancels", "relative file paths" }) Assert.Contains(text, help);
        scope.App.Redraw(); TVDemoReadabilityTests.AssertReadable(dialog);
    }

    [Fact]
    public void HorizontalRangeUsesRenderedWidthIncludingMarginGlyphIndentAndGap()
    {
        using var scope = new DemoTestScope();
        var dialog = new TChDirDialog(ChDirDialogOptions.cdNoLoadDir, 0); scope.App.DeskTop!.Insert(dialog);
        var tree = dialog.dirList!; var bar = tree.hScrollBar!; int viewport = tree.size.x - 1;
        tree.Select();
        string fits = "\u229F " + new string('f', viewport - 2);
        Load(tree, fits); Assert.Equal(0, bar.maxVal); Assert.False(bar.GetState(Views.sfVisible));
        dialog.dirInput!.Select(); tree.Select(); Assert.False(bar.GetState(Views.sfVisible));
        string longRow = "  \u229E " + new string('z', viewport - 3); // one cell wider than the usable viewport
        Load(tree, fits, longRow); Assert.Equal(1, bar.maxVal); Assert.True(bar.GetState(Views.sfVisible));
        Assert.Equal(viewport, bar.pgStep); Assert.Equal(1, bar.arStep);
        tree.Hide(); Assert.False(bar.GetState(Views.sfVisible));
        tree.Show(); Assert.True(bar.GetState(Views.sfVisible));
        dialog.okButton!.Select(); Assert.True(bar.GetState(Views.sfVisible));
        tree.Select(); tree.FocusItem(1); bar.SetValue(100); Assert.Equal(1, bar.value);
        scope.App.Redraw(); AssertVisibleRow(tree, 1, longRow[1..]);
        Load(tree, "\u229F r"); Assert.Equal(0, bar.value); Assert.Equal(0, bar.maxVal); Assert.False(bar.GetState(Views.sfVisible));
        Load(tree); Assert.Equal(0, bar.maxVal); Assert.False(bar.GetState(Views.sfVisible));
    }

    [Fact]
    public void OwnerAndViewportResizingRecomputeGeometryAndClampScroll()
    {
        using var scope = new DemoTestScope();
        var dialog = new TChDirDialog(ChDirDialogOptions.cdNoLoadDir | ChDirDialogOptions.cdHelpButton, 0); scope.App.DeskTop!.Insert(dialog);
        var tree = dialog.dirList!; var bar = tree.hScrollBar!;
        bar.growMode = Views.gfGrowLoY | Views.gfGrowHiX | Views.gfGrowHiY; // legacy/restored configuration
        string row = "\u229E " + new string('x', 38); Load(tree, row); tree.Select();
        Assert.Equal((byte)0, bar.growMode);
        Assert.Equal(row.Length - (tree.size.x - 1), bar.maxVal); bar.SetValue(bar.maxVal);
        dialog.ChangeBounds(new TRect(0, 0, 74, 19)); CheckBars(tree);
        Assert.Equal(0, bar.maxVal); Assert.Equal(0, bar.value); Assert.False(bar.GetState(Views.sfVisible));
        foreach (var button in ScriptedDemoApp.Descendants(dialog).OfType<TButton>()) Assert.Equal(71, button.GetBounds().b.x);
        dialog.ChangeBounds(new TRect(0, 0, 48, 19)); CheckBars(tree);
        Assert.True(bar.maxVal > 0); Assert.True(bar.GetState(Views.sfVisible));
        // Direct viewport updates also work without requiring a dialog resize.
        tree.ChangeBounds(new TRect(3, 6, 15, 16)); CheckHorizontal(tree);
        Assert.Equal(row.Length - 11, bar.maxVal);
        tree.ChangeBounds(new TRect(3, 6, 60, 16)); CheckHorizontal(tree);
        Assert.Equal(0, bar.maxVal); Assert.False(bar.GetState(Views.sfVisible));
    }

    [Fact]
    public void ScrollKeysMouseHitTestingAndVerticalFocusRemainAssociated()
    {
        using var scope = new DemoTestScope();
        var dialog = new TChDirDialog(ChDirDialogOptions.cdNoLoadDir, 0); scope.App.DeskTop!.Insert(dialog);
        var tree = dialog.dirList!; var bar = tree.hScrollBar!;
        string[] rows = Enumerable.Range(0, 20).Select(i => "  \u229E folder-" + i + new string('x', 40)).ToArray();
        Load(tree, rows); tree.Select(); Assert.NotSame(tree.vScrollBar, bar);
        Assert.Equal(19, tree.vScrollBar!.maxVal);
        var key = ComboInput.Key(Keys.kbRight); scope.App.HandleEvent(ref key); Assert.Equal(1, bar.value);
        key = ComboInput.Key(Keys.kbDown); scope.App.HandleEvent(ref key); Assert.Equal(1, tree.focused);
        Assert.Equal(1, tree.vScrollBar.value);
        bar.SetValue(0);
        var point = bar.MakeGlobal(new TPoint(bar.size.x - 1, 0));
        ComboInput.Send(ComboInput.Mouse(Events.evMouseUp, point));
        var click = ComboInput.Mouse(Events.evMouseDown, point); scope.App.HandleEvent(ref click);
        Assert.Equal(1, bar.value);
        point = tree.MakeGlobal(new TPoint(5, 3));
        ComboInput.Send(ComboInput.Mouse(Events.evMouseUp, point));
        click = ComboInput.Mouse(Events.evMouseDown, point); scope.App.HandleEvent(ref click);
        Assert.Equal(tree.topItem + 3, tree.focused);
        tree.FocusItem(19); Assert.InRange(19 - tree.topItem, 0, tree.size.y - 1);
        Assert.Equal(19, tree.vScrollBar.value);
        scope.App.Redraw(); AssertVisibleRow(tree, 19, rows[19][bar.value..]);
    }

    [Fact]
    public void BrowsingLongNamesAndRevertReplaceWidthWithoutChangingCwd()
    {
        using var temp = new TempDirectory(); using var scope = new DemoTestScope();
        Directory.SetCurrentDirectory(temp.Path);
        string currentDirectory = Directory.GetCurrentDirectory();
        string child = Directory.CreateDirectory(Path.Combine(currentDirectory, "short")).FullName;
        Directory.CreateDirectory(Path.Combine(child, new string('q', 100)));
        var dialog = new DemoDirectoryDialog(); scope.App.DeskTop!.Insert(dialog);
        var tree = dialog.dirList!;
        int childIndex = Enumerable.Range(0, tree.range).Single(i => tree.List()!.At(i).Dir().TrimEnd('/', '\\').Replace('\\', '/') == child.Replace('\\', '/'));
        tree.FocusItem(childIndex);
        var command = ScriptedDemoApp.CommandEvent(Views.cmChangeDir); dialog.HandleEvent(ref command);
        Assert.Equal(currentDirectory, Directory.GetCurrentDirectory());
        Assert.Equal(child, Path.GetFullPath(dialog.dirInput!.Data));
        int longest = Enumerable.Range(0, tree.range).Max(i => tree.List()!.At(i).Text().Length);
        Assert.Equal(longest - (tree.size.x - 1), tree.hScrollBar!.maxVal);
        Assert.True(tree.hScrollBar.maxVal > 0); tree.hScrollBar.SetValue(tree.hScrollBar.maxVal);
        command = ScriptedDemoApp.CommandEvent(Views.cmRevert); dialog.HandleEvent(ref command);
        Assert.Equal(currentDirectory, dialog.dirInput.Data); Assert.Equal(currentDirectory, Directory.GetCurrentDirectory());
        longest = Enumerable.Range(0, tree.range).Max(i => tree.List()!.At(i).Text().Length);
        Assert.Equal(Math.Max(0, longest - (tree.size.x - 1)), tree.hScrollBar.maxVal);
        Assert.Equal(0, tree.hScrollBar.value); CheckBars(tree);
    }


    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RelocatedButtonsWorkThroughMouseAndMnemonics(bool mouse)
    {
        using var temp = new TempDirectory(); using var scope = new DemoTestScope();
        Directory.SetCurrentDirectory(temp.Path);
        string currentDirectory = Directory.GetCurrentDirectory();
        string child = Directory.CreateDirectory(Path.Combine(currentDirectory, "child")).FullName;
        var app = scope.App; DemoDirectoryDialog? dialog = null;
        app.Step(() =>
        {
            dialog = app.Modal<DemoDirectoryDialog>();
            var list = dialog.dirList!;
            int index = Enumerable.Range(0, list.range).Single(i => Path.GetFullPath(Path.TrimEndingDirectorySeparator(list.List()!.At(i).Dir())) == child);
            list.FocusItem(index); Activate(Views.cmChangeDir, Keys.kbAltC);
        });
        app.Step(() =>
        {
            Assert.Equal(currentDirectory, Directory.GetCurrentDirectory());
            Assert.Equal(child, Path.GetFullPath(dialog!.dirInput!.Data));
            Activate(Views.cmRevert, Keys.kbAltR);
        });
        app.Step(() =>
        {
            Assert.Equal(currentDirectory, dialog!.dirInput!.Data);
            dialog.dirInput.SetData(child); Activate(Views.cmOK, Keys.kbAltO);
        });
        app.DismissResult("Directory accepted");
        app.RunCommand(ShowcaseCmd.ChangeDirectory);
        Assert.Equal(child, Directory.GetCurrentDirectory()); Assert.Null(dialog!.dirList);
        void Activate(ushort command, ushort key)
        {
            if (mouse)
            {
                var button = ScriptedDemoApp.Button(dialog!, command);
                ComboInput.Click(button.MakeGlobal(new TPoint(button.size.x / 2, 0)));
            }
            else ComboInput.Send(ComboInput.Key(key));
        }
    }


    [Fact]
    public void DirectorySelectionHidesHardwareCaretButPathEditingStillShowsIt()
    {
        using var scope = new DemoTestScope();
        var dialog = new DemoDirectoryDialog(); scope.App.DeskTop!.Insert(dialog);
        var tree = dialog.dirList!;
        Load(tree, "\u229F root", "  \u229E child", "  \u229E " + new string('x', 100));
        var input = dialog.dirInput!; input.SetData("path"); input.Select();
        scope.App.Redraw(); scope.App.ResetCursor();
        Assert.True(input.GetState(Views.sfCursorVis));
        Assert.NotEqual((ushort)0, TScreen.driver!.GetCursorType());
        tree.Select(); AssertTreeCaretHidden();
        var key = ComboInput.Key(Keys.kbDown); scope.App.HandleEvent(ref key);
        Assert.Equal(1, tree.focused); AssertTreeCaretHidden();
        var origin = tree.MakeGlobal(new TPoint(1, 1 - tree.topItem));
        var screen = RootScreenCopy.CopyRoot();
        for (int x = 0; x < "  \u229E child".Length; x++)
            Assert.Equal((byte)tree.GetColor(3), (byte)screen[origin.x + x, origin.y].Attr);
        Assert.Equal('\u229E', screen[origin.x + 2, origin.y].Character);
        tree.hScrollBar!.SetValue(3); AssertTreeCaretHidden();
        tree.UpdateCursorPos(); tree.ShowCursor(); AssertTreeCaretHidden();
        dialog.ChangeBounds(new TRect(0, 0, 74, 19)); AssertTreeCaretHidden();
        input.Select(); scope.App.Redraw(); scope.App.ResetCursor();
        Assert.NotEqual((ushort)0, TScreen.driver!.GetCursorType());
        var typed = ComboInput.Typed("edited-path"); scope.App.HandleEvent(ref typed);
        Assert.Contains("edited-path", input.Data);
        tree.Select(); AssertTreeCaretHidden();
        void AssertTreeCaretHidden()
        {
            scope.App.Redraw(); scope.App.ResetCursor();
            Assert.True(tree.GetState(Views.sfFocused));
            Assert.False(tree.GetState(Views.sfCursorVis));
            Assert.Equal((ushort)0, TScreen.driver!.GetCursorType());
        }
    }

    [Fact]
    public void FileDialogStillOwnsItsHistory()
    {
        using var scope = new DemoTestScope();
        var file = new DemoFileDialog(false);
        try { Assert.Single(ScriptedDemoApp.Descendants(file).OfType<THistory>()); }
        finally { file.ShutDown(); }
    }

    private static void Load(TDirListBox tree, params string[] rows)
    {
        var collection = new TDirCollection();
        foreach (string row in rows) collection.Insert(new TDirEntry(row, "/", row.IndexOf(' ') + 1));
        tree.NewList(collection);
    }
    private static void CheckBars(TDirListBox tree)
    {
        CheckHorizontal(tree);
        Assert.Equal(tree.origin.x + tree.size.x, tree.vScrollBar!.origin.x);
        Assert.Equal(tree.origin.y, tree.vScrollBar.origin.y);
        Assert.Equal(tree.size.y, tree.vScrollBar.size.y);
    }
    private static void CheckHorizontal(TDirListBox tree)
    {
        Assert.Equal(tree.origin.x, tree.hScrollBar!.origin.x);
        Assert.Equal(tree.origin.y + tree.size.y, tree.hScrollBar.origin.y);
        Assert.Equal(tree.size.x, tree.hScrollBar.size.x);
        Assert.Equal(1, tree.hScrollBar.size.y); Assert.Same(tree.owner, tree.hScrollBar.owner);
    }
    private static void AssertVisibleRow(TDirListBox tree, int index, string expected)
    {
        var screen = RootScreenCopy.CopyRoot(); var origin = tree.MakeGlobal(new TPoint(1, index - tree.topItem));
        string actual = new(Enumerable.Range(0, tree.size.x - 1).Select(x => screen[origin.x + x, origin.y].Character).ToArray());
        Assert.Equal(expected.PadRight(tree.size.x - 1)[..(tree.size.x - 1)], actual);
    }
}
