using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Infrastructure;
using TSharpVision.Tests.Samples;
using Xunit;

namespace TSharpVision.Tests.Files;

[Collection("NonParallel")]
public sealed class DirectoryTreeGlyphTests
{
    [Fact]
    public void ActualDirectoryDialogTogglesSingleGlyphAndPreservesOffsetsAndBrowsing()
    {
        using var scope = new DemoTestScope();
        using var temp = new TempDirectory();
        string root = temp.Path;
        string child = Directory.CreateDirectory(Path.Combine(root, "child")).FullName;
        Directory.CreateDirectory(Path.Combine(child, "grandchild"));
        Directory.CreateDirectory(Path.Combine(root, "sibling"));
        string cwd = Directory.GetCurrentDirectory();
        var dialog = new TChDirDialog(ChDirDialogOptions.cdNoLoadDir, 0);
        scope.App.DeskTop!.Insert(dialog);
        try
        {
            var list = dialog.dirList!;
            list.NewDirectory(root);
            int childIndex = Enumerable.Range(0, list.range).Single(i => Path.GetFileName(list.List()!.At(i).Dir().TrimEnd('/', '\\')) == "child");
            var entry = list.List()!.At(childIndex);
            Assert.Equal('\u229E', entry.Text()[entry.Offset() - 2]);
            Assert.Equal("child", entry.Text()[entry.Offset()..]);
            Assert.Equal("\u229E ", entry.Text().Substring(entry.Offset() - 2, 2));
            Assert.Equal(1, entry.Text().Count(c => c == '\u229E'));
            list.FocusItem(childIndex); scope.App.Redraw();
            var point = list.MakeGlobal(new TPoint(1 + entry.Offset() - 2 - (list.hScrollBar?.value ?? 0), childIndex - list.topItem));
            Assert.Equal('\u229E', RootScreenCopy.CopyRoot()[point.x, point.y].Character);
            var ev = ScriptedDemoApp.CommandEvent(Views.cmChangeDir); dialog.HandleEvent(ref ev);
            Assert.Equal(Events.evNothing, ev.What);
            Assert.Equal(cwd, Directory.GetCurrentDirectory()); // Chdir browses; OK alone changes process CWD.
            var expanded = list.List()!.At(list.cur);
            Assert.Equal("child", expanded.Text()[expanded.Offset()..]);
            Assert.Equal("\u229F ", expanded.Text().Substring(expanded.Offset() - 2, 2));
            Assert.Equal(entry.Offset(), expanded.Offset());
            Assert.Equal('\u229F', expanded.Text()[expanded.Offset() - 2]);
            Assert.Equal(1, expanded.Text().Count(c => c == '\u229F'));
            list.FocusItem(list.cur); scope.App.Redraw();
            point = list.MakeGlobal(new TPoint(1 + expanded.Offset() - 2 - (list.hScrollBar?.value ?? 0), list.cur - list.topItem));
            Assert.Equal('\u229F', RootScreenCopy.CopyRoot()[point.x, point.y].Character);
            var grandchild = list.List()!.At(list.cur + 1);
            Assert.Equal('\u229E', grandchild.Text()[grandchild.Offset() - 2]);
            Assert.Equal("grandchild", grandchild.Text()[grandchild.Offset()..]);
            Assert.Equal("\u229E ", grandchild.Text().Substring(grandchild.Offset() - 2, 2));
            Assert.Equal(expanded.Offset() + 2, grandchild.Offset());
            list.NewDirectory(root);
            entry = list.List()!.At(Enumerable.Range(0, list.range).Single(i => list.GetText(i, 1000).EndsWith("child", StringComparison.Ordinal)));
            Assert.Equal('\u229E', entry.Text()[entry.Offset() - 2]);
            foreach (string prefix in new[] { TDirListBox.PathDir, TDirListBox.FirstDir, TDirListBox.MiddleDir, TDirListBox.LastDir })
            {
                Assert.Equal(2, prefix.Length); Assert.Equal(' ', prefix[1]); Assert.DoesNotContain('+', prefix); Assert.DoesNotContain('-', prefix);
            }
            Assert.Contains('\u229E', TDirListBox.Graphics); Assert.Contains('\u229F', TDirListBox.Graphics);
        }
        finally { dialog.ShutDown(); }
    }
    [Fact]
    public void RootAndDriveRowsUseOneCellGapAndCorrectFilenameOffsets()
    {
        using var scope = new DemoTestScope();
        using var temp = new TempDirectory();
        var dialog = new TChDirDialog(ChDirDialogOptions.cdNoLoadDir, 0);
        scope.App.DeskTop!.Insert(dialog);
        try
        {
            var list = dialog.dirList!;
            list.NewDirectory(temp.Path);
            int rootIndex = Path.DirectorySeparatorChar == '\\' ? 1 : 0;
            var root = list.List()!.At(rootIndex);
            Assert.Equal(2, root.Offset());
            Assert.Equal("\u229F " + Path.GetPathRoot(temp.Path)!.Replace('\\', '/'), root.Text());
            list.FocusItem(rootIndex); list.Select(); scope.App.Redraw();
            var origin = list.MakeGlobal(new TPoint(0, rootIndex - list.topItem));
            var screen = RootScreenCopy.CopyRoot();
            Assert.Equal('\u229F', screen[origin.x + 1, origin.y].Character);
            Assert.Equal(' ', screen[origin.x + 2, origin.y].Character);
            Assert.Equal(root.Text()[2], screen[origin.x + 3, origin.y].Character);
            if (Path.DirectorySeparatorChar == '\\')
            {
                list.NewDirectory("Drives");
                Assert.True(list.range >= 2);
                // Keep the existing undecorated "Drives" heading.
                Assert.DoesNotContain('\u229E', list.GetText(0, 100));
                for (int i = 1; i < list.range; i++)
                {
                    var drive = list.List()!.At(i);
                    Assert.Equal(2, drive.Offset());
                    Assert.Equal("\u229E ", drive.Text()[..2]);
                    Assert.Single(drive.Text()[2..]);
                }
            }
        }
        finally { dialog.ShutDown(); }
    }

    [Theory]
    [InlineData(12)] [InlineData(20)]
    public void NestedRowsKeepAlignmentSelectionAndHorizontalScrollingInNarrowViews(int width)
    {
        using var scope = new DemoTestScope();
        using var temp = new TempDirectory();
        string parent = temp.CreateSubDir("parent-with-a-long-name");
        Directory.CreateDirectory(Path.Combine(parent, "child-with-a-long-name"));
        var dialog = new TDialog(new TRect(0, 0, width + 4, 10), "Tree");
        var horizontal = new TScrollBar(new TRect(2, 8, 2 + width, 9));
        var vertical = new TScrollBar(new TRect(2 + width, 1, 3 + width, 7));
        var list = new TDirListBox(new TRect(2, 1, 2 + width, 7), vertical, horizontal);
        dialog.Insert(horizontal); dialog.Insert(vertical); dialog.Insert(list);
        scope.App.DeskTop!.Insert(dialog);
        try
        {
            horizontal.SetRange(0, 200);
            list.NewDirectory(parent);
            int expandedIndex = list.cur, childIndex = list.cur + 1;
            var expanded = list.List()!.At(expandedIndex);
            var child = list.List()!.At(childIndex);
            int markerIndex = expanded.Offset() - 2;
            Assert.Equal(markerIndex + 2, child.Offset() - 2); // nesting remains two cells
            Assert.Equal("\u229F parent-with-a-long-name", expanded.Text()[markerIndex..]);
            Assert.Equal("  \u229E child-with-a-long-name", child.Text()[markerIndex..]);
            list.FocusItem(childIndex); list.Select();
            horizontal.SetValue(markerIndex); scope.App.Redraw();
            AssertRow(expandedIndex, expanded.Text()[markerIndex..]);
            AssertRow(childIndex, child.Text()[markerIndex..]);
            var origin = list.MakeGlobal(new TPoint(0, 0));
            var screen = RootScreenCopy.CopyRoot();
            int row = childIndex - list.topItem;
            // Focus highlighting covers the complete prefix, gap, and clipped name.
            for (int x = 1; x < width; x++)
                Assert.Equal((byte)list.GetColor(3), (byte)screen[origin.x + x, origin.y + row].Attr);
            foreach (int scroll in new[] { child.Offset(), child.Offset() + 3 })
            {
                horizontal.SetValue(scroll); scope.App.Redraw();
                AssertRow(childIndex, child.Text()[scroll..]);
                list.incPos = 1; list.UpdateCursorPos();
                Assert.False(list.GetState(Views.sfCursorVis));
            }
            // Scrolling back must not restore a text caret in the selection-only tree.
            horizontal.SetValue(markerIndex); list.incPos = 3; list.UpdateCursorPos();
            Assert.False(list.GetState(Views.sfCursorVis));
        }
        finally { dialog.ShutDown(); }

        void AssertRow(int index, string expected)
        {
            var origin = list.MakeGlobal(new TPoint(1, index - list.topItem));
            var screen = RootScreenCopy.CopyRoot();
            string actual = new(Enumerable.Range(0, width - 1).Select(x => screen[origin.x + x, origin.y].Character).ToArray());
            Assert.Equal(expected.PadRight(width - 1)[..(width - 1)], actual);
        }
    }

}
