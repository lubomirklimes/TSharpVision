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
    public void ComboBoxDemoOffersASelectionAndAnEditableComboBox()
    {
        using var driver = new DriverScope();
        using var registry = new StreamableRegistryScope();
        var app = new TVDemoApp();
        try
        {
            var dialog = app.BuildComboBoxDemoDialog(out var caseBox, out var encoding);
            try
            {
                Assert.False(caseBox.IsEditable);
                Assert.Equal(new[] { "Keep", "Lower", "Upper" }, caseBox.Items);
                Assert.Equal("Keep", caseBox.Text);
                Assert.True(encoding.IsEditable);
                Assert.Equal(new[] { "UTF-8", "UTF-16 LE", "Windows-1250", "ISO-8859-2" }, encoding.Items);
                Assert.Equal("UTF-8", encoding.Text);
                Assert.Equal(0, encoding.SelectedIndex);
                Assert.Same(caseBox, dialog.current);
            }
            finally { dialog.ShutDown(); }
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void ProgressDemoStepsResetsAndStopsItsWorker()
    {
        using var driver = new DriverScope();
        using var registry = new StreamableRegistryScope();
        var app = new TVDemoApp();
        try
        {
            var dialog = app.BuildProgressDemoDialog();
            try
            {
                Assert.False(dialog.Reading.IsIndeterminate);
                Assert.True(dialog.Reading.ShowPercentage);
                Assert.Equal(ProgressDemoDialog.TotalBytes, dialog.Reading.Maximum);
                Assert.True(dialog.Activity.IsIndeterminate);

                Command(dialog, ProgressDemoDialog.cmStep);
                Command(dialog, ProgressDemoDialog.cmStep);
                Assert.Equal(10, dialog.Reading.Percent);
                Assert.Equal(2, dialog.Activity.ActivityStep);

                Command(dialog, ProgressDemoDialog.cmTick);   // a tick without a running worker is ignored
                Assert.Equal(10, dialog.Reading.Percent);

                Command(dialog, ProgressDemoDialog.cmRun);
                Assert.True(dialog.IsRunning);
                Command(dialog, ProgressDemoDialog.cmTick);
                Assert.Equal(11, dialog.Reading.Percent);
                Command(dialog, ProgressDemoDialog.cmStop);
                Assert.False(dialog.IsRunning);

                Command(dialog, ProgressDemoDialog.cmReset);
                Assert.Equal(0, dialog.Reading.Value);
                Assert.Equal(0, dialog.Activity.ActivityStep);

                Command(dialog, ProgressDemoDialog.cmRun);
            }
            finally { dialog.ShutDown(); }
            Assert.False(dialog.IsRunning);
        }
        finally { app.ShutDown(); }

        static void Command(TDialog dialog, ushort command)
        {
            var ev = new TEvent { What = Events.evCommand };
            ev.message.command = command;
            dialog.HandleEvent(ref ev);
            Assert.Equal(Events.evNothing, ev.What);
        }
    }

    [Fact]
    public void ProgressDemoWorkerTicksReachTheModalDialogOnTheUiThread()
    {
        using var driver = new DriverScope(80, 25);
        TSharpVision.Tests.Dialogs.ComboInput.ResetQueue();
        var app = new TSharpVision.Tests.Dialogs.ComboApp();
        var dialog = new ProgressDemoDialog(new TRect(5, 3, 63, 15));
        int uiThread = Environment.CurrentManagedThreadId;
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        try
        {
            app.Step(() =>
            {
                var run = new TEvent { What = Events.evCommand };
                run.message.command = ProgressDemoDialog.cmRun;
                TEventQueue.Enqueue(run);
            });
            Action? poll = null;
            poll = () =>
            {
                Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
                if (dialog.Reading.Percent < 3 && DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(10);
                    app.Step(poll!);
                    return;
                }
                Assert.True(dialog.IsRunning);
                Assert.True(dialog.Reading.Percent >= 3, "the worker's ticks did not arrive");
                Assert.True(dialog.Activity.ActivityStep >= 3);
                TEventQueue.Enqueue(TSharpVision.Tests.Dialogs.ComboInput.Key(Keys.kbEsc));
            };
            app.Step(poll);

            ushort result = app.DeskTop!.ExecView(dialog);
            Assert.Equal(Views.cmCancel, result);
            Assert.False(app.TimedOut);
        }
        finally
        {
            dialog.ShutDown();
            Assert.False(dialog.IsRunning);
            app.ShutDown();
            TSharpVision.Tests.Dialogs.ComboInput.ResetQueue();
        }
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
