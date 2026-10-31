using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class ProgressDemoLayoutTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(80, 25)] [InlineData(100, 35)]
    public void ProgressActionsEndAtBottomFrameWithoutLosingBreathingRows(int width, int height)
    {
        using var scope = new DemoTestScope(width, height);
        ProgressDemoDialog? dialog = null;
        scope.App.Step(() =>
        {
            dialog = scope.App.Modal<ProgressDemoDialog>();
            Assert.Equal(new TPoint(58, 11), dialog.size);
            Assert.Equal((byte)0, dialog.growMode);
            Assert.Equal(0, dialog.flags & (Views.wfGrow | Views.wfZoom));
            var buttons = ScriptedDemoApp.Descendants(dialog).OfType<TButton>().ToArray();
            Assert.Equal(new ushort[] { Views.cmCancel, 340, 341, 342, 343 }, buttons.Select(b => b.Command).OrderBy(c => c));
            // Half-open bounds: row 9 contains the lower button decoration; row 10 is the frame.
            Assert.All(buttons, b => { Assert.Equal(8, b.origin.y); Assert.Equal(10, b.GetBounds().b.y); });
            Assert.Equal(dialog.size.y - 1, buttons.Max(b => b.GetBounds().b.y));
            Assert.Equal(new TRect(2, 3, 56, 4), dialog.Reading.GetBounds());
            Assert.Equal(new TRect(2, 6, 56, 7), dialog.Activity.GetBounds());
            Assert.InRange(dialog.origin.x + dialog.size.x + TView.shadowSize.x, 0, width);
            Assert.InRange(dialog.origin.y + dialog.size.y + TView.shadowSize.y, 0, scope.App.DeskTop!.size.y);
            scope.App.Redraw(); TVDemoReadabilityTests.AssertReadable(dialog);
            var screen = RootScreenCopy.CopyRoot(); var p = dialog.MakeGlobal(new TPoint(0, 0));
            foreach (int y in new[] { 1, 4, 7 })
                for (int x = 1; x < dialog.size.x - 1; x++) Assert.Equal(' ', screen[p.x + x, p.y + y].Character);
            Assert.Contains(Enumerable.Range(1, dialog.size.x - 2), x => screen[p.x + x, p.y + 9].Character != ' ');
            for (int y = 0; y < dialog.size.y; y++)
                output.WriteLine($"{y:D2}|" + new string(Enumerable.Range(0, dialog.size.x).Select(x => screen[p.x + x, p.y + y].Character).ToArray()) + "|");
            output.WriteLine($"Resolved progress roles: track={dialog.Reading.GetColor(1):X2}, fill={dialog.Reading.GetColor(2):X2}, text={dialog.Reading.GetColor(3):X2}, disabled={dialog.Reading.GetColor(4):X2}");
            ComboInput.Click(ScriptedDemoApp.Button(dialog, Views.cmCancel).MakeGlobal(new TPoint(2, 0)));
        });
        scope.App.RunCommand(ShowcaseCmd.Progress);
        Assert.NotNull(dialog); Assert.Null(dialog.owner); Assert.False(dialog.IsRunning);
    }
}
