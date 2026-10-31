using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class TVDemoSpacingAcceptanceTests(ITestOutputHelper output)
{
    // All TRect bounds are half-open: b.y is the first row outside the control.
    // Button height includes its lower decoration; frame and external shadow are never padding.
    [Theory]
    [InlineData(80, 25)] [InlineData(100, 35)]
    public void SampleHasExactRequestedRowsAndAcceptsEditedName(int width, int height)
    {
        using var scope = new DemoTestScope(width, height);
        scope.App.Step(() =>
        {
            var dialog = scope.App.Modal<TDialog>();
            Assert.Equal(new TPoint(50, 9), dialog.size);
            var children = ScriptedDemoApp.Descendants(dialog).ToArray();
            var label = Assert.Single(children.OfType<TLabel>());
            var input = Assert.Single(children.OfType<TInputLine>());
            var intro = Assert.Single(children.OfType<TStaticText>(), v => v is not TLabel);
            Assert.Equal(new TRect(2, 1, 46, 2), intro.GetBounds());
            Assert.Equal(intro.GetBounds().b.y + 1, label.origin.y);
            Assert.Equal(label.GetBounds().b.y, input.origin.y);
            var buttons = children.OfType<TButton>().ToArray();
            Assert.All(buttons, b => Assert.Equal(input.GetBounds().b.y + 1, b.origin.y));
            Assert.InRange(dialog.size.y - 1 - buttons.Max(b => b.GetBounds().b.y), 0, 1);
            Assert.True(input.GetState(Views.sfFocused));
            scope.App.Redraw();
            BlankRow(dialog, 2); BlankRow(dialog, 5);
            Assert.Contains("Enter your name below:", Row(dialog, 1));
            Assert.Contains("Name:", Row(dialog, 3));
            Assert.Contains("World", Row(dialog, 4));
            CheckAndDump(scope, dialog);
            input.SetData("Spacing accepted");
            ComboInput.Send(ComboInput.Key(Keys.kbEnter));
        });
        scope.App.DismissResult("Hello, Spacing accepted!");
        scope.App.RunCommand(ShowcaseCmd.SimpleDialog);
    }

    [Theory]
    [InlineData(false, 80, 25)] [InlineData(true, 80, 25)]
    [InlineData(false, 100, 35)] [InlineData(true, 100, 35)]
    public void ChooserHasOneEmptyRowAboveCompleteMetadata(bool save, int width, int height)
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "visible.txt"); File.WriteAllText(file, "metadata");
        using var scope = new DemoTestScope(width, height);
        scope.App.Step(() =>
        {
            var dialog = scope.App.Modal<DemoFileDialog>();
            dialog.SetData(Path.Combine(temp.Path, "*.txt"));
            var list = dialog.fileList!;
            list.Select();
            int index = Enumerable.Range(0, list.range).Single(i => list.GetText(i, 260) == "visible.txt");
            list.FocusItem(index);
            var info = Assert.Single(ScriptedDemoApp.Descendants(dialog).OfType<TFileInfoPane>());
            Assert.Equal(new TPoint(74, 19), dialog.size);
            Assert.Equal(0, dialog.flags & (Views.wfZoom | Views.wfGrow));
            Assert.Equal(new TRect(3, 6, 58, 14), list.GetBounds());
            Assert.Equal(new TRect(58, 6, 59, 14), list.vScrollBar!.GetBounds());
            Assert.Equal(list.GetBounds().b.y + 1, info.origin.y);
            Assert.Equal(new TRect(1, 15, 73, 18), info.GetBounds());
            Assert.Equal(new TRect(61, 3, 71, 5), ScriptedDemoApp.Button(dialog, Views.cmFileOpen).GetBounds());
            Assert.Equal(new TRect(61, 6, 71, 8), ScriptedDemoApp.Button(dialog, Views.cmCancel).GetBounds());
            Assert.Equal("visible.txt", info.FileBlock.name);
            scope.App.Redraw(); BlankRow(dialog, 14);
            Assert.Equal("*.txt", dialog.WildCard);
            // Long paths retain the existing horizontal clipping contract; no metadata row is clipped vertically.
            string pathRow = (" " + dialog.Directory + dialog.WildCard).PadRight(info.size.x)[..info.size.x];
            Assert.Equal(pathRow, Row(dialog, 15));
            Assert.Contains("visible.txt", Row(dialog, 16));
            Assert.Contains("8", Row(dialog, 17));
            CheckAndDump(scope, dialog);
            ComboInput.Send(ComboInput.Key(Keys.kbEnter)); // file-list keyboard acceptance
        });
        scope.App.DismissResult(file);
        scope.App.RunCommand(save ? ShowcaseCmd.FileSaveDialog : ShowcaseCmd.FileOpenDialog);
        Assert.Equal("metadata", File.ReadAllText(file));
        Assert.Single(Directory.GetFiles(temp.Path));
    }

    [Theory]
    [InlineData(80, 25)] [InlineData(100, 35)]
    public void MessageChoicesStatusAndActionsFitCompactRows(int width, int height)
    {
        using var scope = new DemoTestScope(width, height);
        scope.App.Step(() =>
        {
            var dialog = scope.App.Modal<MessageDemoDialog>();
            Assert.Equal(new TPoint(64, 14), dialog.size);
            Assert.Equal(new TRect(3, 4, 29, 8), dialog.Category.GetBounds());
            Assert.Equal(new TRect(33, 4, 61, 7), dialog.Buttons.GetBounds());
            Assert.Equal(new TRect(3, 9, 61, 10), dialog.Result.GetBounds());
            var buttons = ScriptedDemoApp.Descendants(dialog).OfType<TButton>().ToArray();
            Assert.All(buttons, b => Assert.Equal(dialog.Result.GetBounds().b.y + 1, b.origin.y));
            Assert.Equal(0, dialog.size.y - 1 - buttons.Max(b => b.GetBounds().b.y));
            scope.App.Redraw(); BlankRow(dialog, 8); BlankRow(dialog, 10);
            Assert.Contains("No message shown yet.", Row(dialog, 9));
            foreach (ushort code in new[] { Views.cmOK, Views.cmCancel, Views.cmYes, Views.cmNo })
            {
                string result = $"Response: {TVDemoApp.DemoResultName(code)} ({code})";
                Assert.Equal(1, TStaticText.CountRows(result, dialog.Result.size.x));
                dialog.Result.SetText(result);
                Assert.Contains(result, Row(dialog, 9));
                BlankRow(dialog, 10);
            }
            CheckAndDump(scope, dialog);
            ComboInput.Send(ComboInput.Key(Keys.kbAltC));
        });
        scope.App.RunCommand(ShowcaseCmd.MessageBoxes);
    }

    private static string Row(TDialog dialog, int y)
    {
        var screen = RootScreenCopy.CopyRoot(); var origin = dialog.MakeGlobal(new TPoint(0, y));
        return new string(Enumerable.Range(1, dialog.size.x - 2).Select(x => screen[origin.x + x, origin.y].Character).ToArray());
    }
    private static void BlankRow(TDialog dialog, int y) => Assert.All(Row(dialog, y), c => Assert.Equal(' ', c));
    private void CheckAndDump(DemoTestScope scope, TDialog dialog)
    {
        scope.App.Redraw(); TVDemoReadabilityTests.AssertReadable(dialog);
        Assert.InRange(dialog.origin.x, 0, scope.App.DeskTop!.size.x - dialog.size.x);
        Assert.InRange(dialog.origin.y, 0, scope.App.DeskTop.size.y - dialog.size.y);
        output.WriteLine($"{dialog.title}: {dialog.size.x}x{dialog.size.y}, interior rows (frame excluded horizontally)");
        for (int y = 0; y < dialog.size.y; y++) output.WriteLine($"{y:D2}|{Row(dialog, y)}|");
    }
}
