using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class TVDemoIntegrationTests
{
    [Theory]
    [InlineData(54)]
    [InlineData(55)]
    public void ButtonRowsCenterWithinClientAreaAndPreserveSpacing(int width)
    {
        var dialog = new TDialog(new TRect(0, 0, width, 14), "Layout");
        var ok = new TButton(new TRect(3, 9, 15, 11), "OK", Views.cmOK, ButtonConstants.bfDefault);
        var cancel = new TButton(new TRect(19, 9, 34, 11), "Cancel", Views.cmCancel, ButtonConstants.bfNormal);
        var close = new TButton(new TRect(2, 5, 12, 7), "Close", Views.cmClose, ButtonConstants.bfNormal);
        try
        {
            dialog.Insert(ok);
            dialog.Insert(cancel);
            dialog.Insert(close);
            DemoButtonLayout.CenterRows(dialog);
            int start = 1 + (width - 2 - 31) / 2;
            Assert.Equal(new TRect(start, 9, start + 12, 11), ok.GetBounds());
            Assert.Equal(new TRect(start + 16, 9, start + 31, 11), cancel.GetBounds());
            int singleStart = 1 + (width - 2 - 10) / 2;
            Assert.Equal(new TRect(singleStart, 5, singleStart + 10, 7), close.GetBounds());
            DemoButtonLayout.CenterRows(dialog);
            Assert.Equal(start, ok.GetBounds().a.x);
        }
        finally { dialog.ShutDown(); }
    }

    [Fact]
    public void ModelessDemosAndHelpWorkInCombinedApplication()
    {
        using var driver = new DriverScope();
        using var registry = new StreamableRegistryScope();
        var app = new TVDemoApp();
        try
        {
            ushort[] commands = { ShowcaseCmd.NewWindow, TVDemoCmd.cmAsciiTable, TVDemoCmd.cmCalculator,
                TVDemoCmd.cmCalendar, TVDemoCmd.cmPuzzle, TVDemoCmd.cmMouseDlg, TVDemoCmd.cmClock,
                TVDemoCmd.cmHeap, ShowcaseCmd.Tetris, ShowcaseCmd.Outline,
                ShowcaseCmd.CodeEditor, ShowcaseCmd.HexView, ShowcaseCmd.TableView };
            foreach (ushort command in commands)
            {
                var ev = new TEvent { What = Events.evCommand };
                ev.message.command = command;
                app.HandleEvent(ref ev);
                Assert.Equal(Events.evNothing, ev.What);
            }
            int count = 0;
            var deskTop = Assert.IsType<TDeskTop>(app.DeskTop);
            deskTop.ForEachView(v =>
            {
                if (v is TWindow) count++;
            });
            Assert.Equal(commands.Length + 1, count); // includes welcome window
            app.Idle();
            Assert.True(app.GetHelpFile().GetTopic(1).numRefs > 0);

            var dialog = app.BuildControlsShowcaseDialog(out var input, out var checks, out var radio, out var list);
            try
            {
                Assert.Equal("Enter name here", input.Data);
                Assert.Equal("Delphi", list.GetText(4, 256));
                Assert.Equal(1u, checks.value);
                Assert.Equal(0u, radio.value);
                var buttons = new List<TButton>();
                dialog.ForEachView(view => { if (view is TButton button) buttons.Add(button); });
                Assert.Equal(2, buttons.Count);
                Assert.Equal(18, buttons.Min(button => button.GetBounds().a.x));
                Assert.Equal(49, buttons.Max(button => button.GetBounds().b.x));
            }
            finally { dialog.ShutDown(); }
        }
        finally
        {
            app.ShutDown();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
