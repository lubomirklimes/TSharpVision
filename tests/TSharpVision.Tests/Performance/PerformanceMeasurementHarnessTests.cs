using System.Diagnostics;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace TSharpVision.Tests.Performance;

[Collection("NonParallel")]
public sealed class PerformanceMeasurementHarnessTests : IDisposable
{
    private readonly DriverScope _driverScope;
    private readonly ITestOutputHelper _output;

    public PerformanceMeasurementHarnessTests(ITestOutputHelper output)
    {
        _output = output;
        _driverScope = new DriverScope(100, 30);
        TEventQueue.Resume();
    }

    public void Dispose() => _driverScope.Dispose();

    [Fact]
    [Trait("Category", "PerformanceBaseline")]
    public void RenderingScenarios_WriteAllocationBaselines()
    {
        var root = BuildRenderTree(100, 30);

        WriteSample(Measure("render.redraw-existing-buffer", 100, root.Redraw));
        WriteSample(Measure("render.buffered-full-draw", 30, () =>
        {
            root.FreeBuffer();
            root.Draw();
        }));
    }

    [Fact]
    [Trait("Category", "PerformanceBaseline")]
    public void EventDispatchScenarios_WriteAllocationBaselines()
    {
        var root = new TestGroup(new TRect(0, 0, 100, 30));
        root.state |= Views.sfVisible | Views.sfExposed | Views.sfFocused;

        for (int i = 0; i < 32; i++)
        {
            var view = new ProbeView(new TRect(i % 16, i / 16, i % 16 + 1, i / 16 + 1));
            root.Insert(view);
        }

        WriteSample(Measure("event.broadcast-dispatch", 5_000, () =>
        {
            var ev = new TEvent { What = Events.evBroadcast };
            ev.message.command = Views.cmReceivedFocus;
            root.HandleEvent(ref ev);
        }));

        WriteSample(Measure("event.mouse-positional-dispatch", 5_000, () =>
        {
            var ev = new TEvent { What = Events.evMouseMove };
            ev.mouse.where = new TPoint(3, 1);
            root.HandleEvent(ref ev);
        }));
    }

    [Fact]
    [Trait("Category", "PerformanceBaseline")]
    public void TextInputScenarios_WriteAllocationBaselines()
    {
        var root = new TestGroup(new TRect(0, 0, 100, 30));
        root.buffer = MakeBuffer(100, 30);
        root.state |= Views.sfVisible | Views.sfExposed | Views.sfFocused;

        var input = new TInputLine(new TRect(2, 2, 82, 3), 4_096);
        root.Insert(input);
        input.SetState(Views.sfSelected, true);

        WriteSample(Measure("inputline.printable-char-handleevent", 1_000, () =>
        {
            var ev = new TEvent { What = Events.evKeyDown };
            ev.keyDown.charScan.charCode = (byte)'x';
            input.HandleEvent(ref ev);
        }));
    }

    [Fact]
    [Trait("Category", "PerformanceBaseline")]
    public void TerminalScenarios_WriteAllocationBaselines()
    {
        var root = new TestGroup(new TRect(0, 0, 100, 30));
        root.buffer = MakeBuffer(100, 30);
        root.state |= Views.sfVisible | Views.sfExposed | Views.sfFocused;

        var terminal = new TTerminal(new TRect(1, 1, 99, 29), maxLines: 250);
        terminal.AnsiEnabled = true;
        root.Insert(terminal);

        WriteSample(Measure("terminal.write-line-and-draw", 200, () =>
        {
            terminal.WriteLine("line \u2502 colored-ish payload for allocation baseline");
        }));
    }

    private static PerfSample Measure(string name, int iterations, Action action)
    {
        action();

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        long startAllocated = GC.GetAllocatedBytesForCurrentThread();
        int startGen0 = GC.CollectionCount(0);
        long start = Stopwatch.GetTimestamp();

        for (int i = 0; i < iterations; i++)
            action();

        long elapsedTicks = Stopwatch.GetTimestamp() - start;
        int gen0 = GC.CollectionCount(0) - startGen0;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - startAllocated;

        return new PerfSample(name, iterations, allocated, elapsedTicks, gen0);
    }

    private void WriteSample(PerfSample sample)
    {
        _output.WriteLine(
            "{0}: iterations={1}, allocated={2:N0} B, allocated/iteration={3:N1} B, elapsed={4:N3} ms, elapsed/iteration={5:N3} ms, gen0={6}",
            sample.Name,
            sample.Iterations,
            sample.AllocatedBytes,
            sample.AllocatedPerIteration,
            sample.Elapsed.TotalMilliseconds,
            sample.ElapsedPerIteration.TotalMilliseconds,
            sample.Gen0Collections);

        Assert.True(sample.Iterations > 0);
        Assert.True(sample.AllocatedBytes >= 0);
        Assert.True(sample.ElapsedTicks >= 0);
    }

    private static TestGroup BuildRenderTree(int width, int height)
    {
        var root = new TestGroup(new TRect(0, 0, width, height));
        root.buffer = MakeBuffer(width, height);
        root.state |= Views.sfVisible | Views.sfExposed | Views.sfFocused;

        var background = new TBackground(new TRect(0, 0, width, height), '\u2591');
        background.state |= Views.sfVisible | Views.sfExposed;
        root.Insert(background);

        var window = new TWindow(new TRect(5, 3, 65, 22), "Perf Window", 1);
        window.state |= Views.sfVisible | Views.sfExposed | Views.sfFocused;
        root.Insert(window);

        var staticText = new TStaticText(new TRect(3, 2, 52, 7),
            "TSharpVision performance baseline\nRendering text, input and lists.");
        window.Insert(staticText);

        var input = new TInputLine(new TRect(3, 8, 52, 9), 128);
        input.SetData("input baseline");
        window.Insert(input);

        var list = new TListBox(new TRect(3, 10, 52, 17), 1, null);
        var items = new TStringCollection();
        for (int i = 0; i < 30; i++)
            items.Insert($"Item {i:00} - allocation baseline");
        list.NewList(items);
        window.Insert(list);

        return root;
    }

    private static ScreenBuffer MakeBuffer(int width, int height)
        => new ScreenBuffer(width * height * ScreenBuffer.GetSize());

    private readonly record struct PerfSample(
        string Name,
        int Iterations,
        long AllocatedBytes,
        long ElapsedTicks,
        int Gen0Collections)
    {
        public double AllocatedPerIteration => Iterations == 0 ? 0 : (double)AllocatedBytes / Iterations;
        public TimeSpan Elapsed => TimeSpan.FromSeconds((double)ElapsedTicks / Stopwatch.Frequency);
        public TimeSpan ElapsedPerIteration => Iterations == 0
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(Elapsed.Ticks / Iterations);
    }
}
