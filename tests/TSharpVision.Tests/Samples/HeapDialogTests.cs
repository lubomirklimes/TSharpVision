using TSharpVision.Constants;
using TSharpVision.Samples.TVDemo;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Samples;

[Collection("NonParallel")]
public sealed class HeapDialogTests
{
    [Fact]
    public void SamplingIsTimedAndHistoryRollsEvenWhenMemoryIsUnchanged()
    {
        long now = 0;
        long memory = 1024;
        int reads = 0;
        var view = new HeapContentView(new TRect(0, 0, 52, 11))
        {
            GetMilliseconds = () => now,
            GetMemory = () => { reads++; return memory; },
            GetCollectionCount = gen => gen + 7
        };
        try
        {
            Assert.True(view.Tick());
            now = 499;
            memory = 2048;
            Assert.False(view.Tick());
            Assert.Equal(1, reads);
            now = 500;
            Assert.True(view.Tick());
            Assert.Equal(2048, view.Peak);
            Assert.Contains("Gen0 7  Gen1 8  Gen2 9", view.TextLine(1));
            memory = 512;
            for (int i = 0; i < 48; i++)
            {
                now += 500;
                Assert.True(view.Tick());
            }
            Assert.Equal(48, view.History.Count);
            Assert.All(view.History, sample => Assert.Equal(512, sample));
            Assert.Equal(2048, view.Peak);
            Assert.Equal(512, view.Current);
        }
        finally { view.ShutDown(); }
    }

    [Fact]
    public void GraphHandlesZeroAndResetPreservesHistoryScale()
    {
        long now = 0;
        long memory = 0;
        var view = new HeapContentView(new TRect(0, 0, 52, 11))
        {
            GetMilliseconds = () => now,
            GetMemory = () => memory
        };
        try
        {
            view.Tick();
            Assert.DoesNotContain('#', view.TextLine(8));
            now += 500;
            memory = 1000;
            view.Tick();
            Assert.EndsWith("#", view.TextLine(4));
            now += 500;
            memory = 200;
            view.Tick();
            view.ResetPeak();
            Assert.Equal(200, view.Peak);
            Assert.Equal(1000, view.Scale);
            Assert.EndsWith("# ", view.TextLine(4));
            Assert.EndsWith("##", view.TextLine(8));
            for (int row = 0; row < HeapContentView.ViewH; row++)
                Assert.True(view.TextLine(row).Length <= HeapContentView.ViewW);
        }
        finally { view.ShutDown(); }
    }

    [Fact]
    public void DialogRendersSnapshotResetsPeakAndClosesNormally()
    {
        using var driver = new DriverScope();
        var root = new TGroup(new TRect(0, 0, 80, 25)) { buffer = new ScreenBuffer(80 * 25) };
        root.state |= Views.sfVisible | Views.sfExposed;
        var dialog = new HeapDialog(0, 0);
        root.Insert(dialog);
        dialog.state |= Views.sfExposed | Views.sfVisible | Views.sfActive;
        try
        {
            long now = Environment.TickCount64 + 1000;
            long memory = 1024 * 1024;
            dialog.View.GetMilliseconds = () => now;
            dialog.View.GetMemory = () => memory;
            dialog.Tick();
            now += 500;
            memory /= 2;
            dialog.Tick();
            var reset = new TEvent { What = Events.evCommand };
            reset.message.command = HeapDialog.ResetPeakCommand;
            dialog.HandleEvent(ref reset);
            Assert.Equal(Events.evNothing, reset.What);
            Assert.Equal(memory, dialog.View.Peak);
            dialog.View.GetMemory = () => throw new InvalidOperationException("Draw must not sample");
            dialog.DrawView();
            var buffer = Assert.IsType<ScreenBuffer>(root.buffer);
            string row = new(buffer.Data.Slice(80, 56).ToArray().Select(c => c.Character).ToArray());
            Assert.Contains("Current: 512 KB   Peak: 512 KB", row);
            var close = new TEvent { What = Events.evCommand };
            close.message.command = Views.cmClose;
            dialog.HandleEvent(ref close);
            Assert.Null(dialog.owner);
            Assert.Equal(Events.evNothing, close.What);
        }
        finally { root.ShutDown(); }
    }
}
