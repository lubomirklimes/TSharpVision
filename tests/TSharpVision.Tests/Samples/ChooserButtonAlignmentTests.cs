using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using Xunit;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class ChooserButtonAlignmentTests
{
    [Theory]
    [InlineData(ShowcaseCmd.FileOpenDialog, 80, 25)]
    [InlineData(ShowcaseCmd.FileSaveDialog, 80, 25)]
    [InlineData(ShowcaseCmd.ChangeDirectory, 80, 25)]
    [InlineData(ShowcaseCmd.FileOpenDialog, 100, 35)]
    [InlineData(ShowcaseCmd.FileSaveDialog, 100, 35)]
    [InlineData(ShowcaseCmd.ChangeDirectory, 100, 35)]
    public void Actual_modal_actions_align_with_input_and_clear_other_controls(ushort command, int width, int height)
    {
        using var scope = new DemoTestScope(width, height);
        scope.App.Step(() =>
        {
            var dialog = scope.App.Modal<TDialog>();
            var children = ScriptedDemoApp.Descendants(dialog).ToArray();
            var input = Assert.Single(children.OfType<TInputLine>());
            var history = Assert.Single(children.OfType<THistory>());
            Assert.Same(input, history.Link);
            Assert.Equal(input.GetBounds().b.x, history.origin.x);
            Assert.Equal(new TPoint(74, 19), dialog.size);
            var buttons = children.OfType<TButton>().OrderBy(b => b.origin.y).ToArray();
            Assert.Equal(command == ShowcaseCmd.ChangeDirectory ? 3 : 2, buttons.Length);
            Assert.Equal(input.origin.y, buttons[0].origin.y);
            Assert.Equal(3, input.origin.y);
            for (int i = 0; i < buttons.Length; i++)
            {
                var bounds = buttons[i].GetBounds();
                Assert.Equal(new TRect(61, 3 + 3 * i, 71, 5 + 3 * i), bounds);
                Assert.True(bounds.b.x < dialog.size.x - 1 && bounds.b.y < dialog.size.y - 1);
                if (i > 0) Assert.Equal(buttons[i - 1].GetBounds().b.y + 1, bounds.a.y);
                foreach (var other in children.Where(v => v is not TFrame && v != buttons[i]))
                {
                    var r = other.GetBounds();
                    Assert.True(bounds.b.x <= r.a.x || r.b.x <= bounds.a.x || bounds.b.y <= r.a.y || r.b.y <= bounds.a.y,
                        $"Button overlaps {other.GetType().Name}");
                }
            }
            if (dialog is TFileDialog file)
            {
                var info = Assert.Single(children.OfType<TFileInfoPane>());
                Assert.Equal(file.fileList!.GetBounds().b.y + 1, info.origin.y);
                ComboInput.Click(buttons[1].MakeGlobal(new TPoint(2, 0)));
            }
            else ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        scope.App.DismissResult(command == ShowcaseCmd.ChangeDirectory ? "Directory cancelled" : "cancelled");
        scope.App.RunCommand(command);
    }
}
