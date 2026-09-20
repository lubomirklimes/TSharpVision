using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class TVDemoIntegrationTests
{
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
                TVDemoCmd.cmHeap, ShowcaseCmd.Tetris };
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
