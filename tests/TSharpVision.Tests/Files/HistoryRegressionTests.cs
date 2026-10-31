using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Dialogs;
using TSharpVision.Tests.Infrastructure;
using TSharpVision.Tests.Samples;
using Xunit;

namespace TSharpVision.Tests.Files;

[Collection("NonParallel")]
public sealed class HistoryRegressionTests
{
    [Theory]
    [InlineData(0, false)] [InlineData(0, true)]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    public void HistoryOpensSelectsCancelsAndRestoresFocus(int kind, bool mouse)
    {
        using var scope = new DemoTestScope();
        var app = scope.App;
        TDialog dialog = kind switch
        {
            0 => new DemoDirectoryDialog(),
            1 => new TChDirDialog(ChDirDialogOptions.cdNoLoadDir, 77),
            _ => app.BuildHistoryDemoDialog()
        };
        app.DeskTop!.Insert(dialog);
        var history = Assert.Single(ScriptedDemoApp.Descendants(dialog).OfType<THistory>());
        var input = Assert.IsType<TInputLine>(history.Link);
        Assert.Same(dialog, history.owner);
        Assert.Equal((ushort)(kind == 0 ? 113 : kind == 1 ? 77 : 1), history.HistoryId);
        if (dialog is TChDirDialog directory) Assert.Same(directory.dirInput, input);
        Assert.Equal(input.GetBounds().b.x, history.origin.x);
        Assert.Equal(input.origin.y, history.origin.y);
        Assert.Equal(new TPoint(3, 1), history.size);
        Assert.True(history.GetBounds().b.x < dialog.size.x - 1);
        THistoryList.Clear();
        string saved = Path.GetTempPath();
        THistoryList.Add(history.HistoryId, saved);
        input.SetData("current"); input.Select();
        CheckDrawing();
        ScriptedDemoApp.Descendants(dialog).OfType<TButton>().First().Select();
        CheckDrawing();
        input.Select();
        app.Step(() =>
        {
            var popup = app.Modal<THistoryWindow>();
            Assert.Equal(saved, popup.GetSelection());
            Assert.Same(dialog, popup.owner);
            if (mouse)
            {
                var viewer = popup.Viewer!;
                var click = ComboInput.Mouse(Events.evMouseDown,
                    viewer.MakeGlobal(new TPoint(1, viewer.focused - viewer.topItem)));
                click.mouse.doubleClick = true;
                ComboInput.Send(click);
            }
            else ComboInput.Send(ComboInput.Key(Keys.kbEnter));
        });
        Open();
        Assert.Equal(saved, input.Data);
        Assert.Equal(0, input.SelStart); Assert.Equal(saved.Length, input.SelEnd);
        CheckClosed();
        Assert.Equal("current", THistoryList.Str(history.HistoryId, 0));
        app.Step(() =>
        {
            Assert.IsType<THistoryWindow>(TView.ModalTopView);
            ComboInput.Send(ComboInput.Key(Keys.kbEsc));
        });
        Open();
        Assert.Equal(saved, input.Data); CheckClosed();
        dialog.ShutDown();

        void Open()
        {
            var ev = mouse
                ? ComboInput.Mouse(Events.evMouseDown, history.MakeGlobal(new TPoint(1, 0)))
                : ComboInput.Key(Keys.kbDown);
            app.HandleEvent(ref ev);
            Assert.Equal(Events.evNothing, ev.What);
        }
        void CheckClosed()
        {
            Assert.Null(TView.ModalTopView);
            Assert.Empty(ScriptedDemoApp.Descendants(dialog).OfType<THistoryWindow>());
            Assert.Same(input, dialog.current);
            Assert.True(input.GetState(Views.sfFocused));
            CheckDrawing();
        }
        void CheckDrawing()
        {
            app.Redraw();
            var screen = RootScreenCopy.CopyRoot();
            var point = history.MakeGlobal(new TPoint(0, 0));
            Assert.InRange(point.x + 3, 1, 80); Assert.InRange(point.y, 0, 24);
            for (int x = 0; x < 3; x++)
            {
                var cell = screen[point.x + x, point.y];
                Assert.Equal(" \u2193 "[x], cell.Character);
                byte color = (byte)cell.Attr;
                Assert.Equal((byte)history.GetColor(x == 1 && input.GetState(Views.sfFocused) ? (ushort)2 : (ushort)1), color);
                Assert.NotEqual(color & 15, (color >> 4) & 15);
            }
        }
    }
}
