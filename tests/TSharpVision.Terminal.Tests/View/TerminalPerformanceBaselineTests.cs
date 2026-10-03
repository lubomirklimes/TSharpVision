using System.Diagnostics;
using System.Text;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace TSharpVision.Terminal.Tests.View;

/// <summary>
/// Allocation and time baselines for the terminal, reported rather than asserted (they vary with the machine): one
/// written-and-drawn line, as the core's performance harness measured before the terminal left the core, and a burst
/// of parsed output as a PTY delivers it.
/// </summary>
[Collection("TerminalView")]
public sealed class TerminalPerformanceBaselineTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "PerformanceBaseline")]
    public void WriteLineAndDraw_WritesAllocationBaseline()
    {
        using var driver = new DriverScope(100, 30);
        var root = new TestGroup(new TRect(0, 0, 100, 30)) { buffer = new ScreenBuffer(100 * 30) };
        root.state |= (ushort)(Views.sfVisible | Views.sfExposed | Views.sfFocused);
        var terminal = new TTerminal(new TRect(1, 1, 99, 29), maxLines: 250) { NewLineMode = true };
        root.Insert(terminal);

        Report(Measure("terminal.write-line-and-draw", 200,
            () => terminal.WriteLine("line │ \u001b[32mcolored\u001b[0m payload for allocation baseline")));
    }

    [Fact]
    [Trait("Category", "PerformanceBaseline")]
    public void ParseBurst_WritesThroughputBaseline()
    {
        var line = new StringBuilder();
        for (int i = 0; i < 20; i++) line.Append($"\u001b[{31 + i % 7}mword{i} čü€中 \u001b[0m");
        byte[] chunk = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(line + "\r\n", 40)));
        var emulator = new TerminalEmulator(120, 40, 1000);

        Report(Measure("terminal.parse-64k-burst", 50, () =>
        {
            for (int at = 0; at < chunk.Length; at += 4096)
                emulator.Feed(chunk.AsSpan(at, Math.Min(4096, chunk.Length - at)));
        }));
        Assert.Equal(1000, emulator.ScrollbackCount);   // bounded however much arrives
    }

    private static (string Name, int Iterations, long Allocated, TimeSpan Elapsed, int Gen0) Measure(string name, int iterations, Action action)
    {
        action();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        long allocated = GC.GetAllocatedBytesForCurrentThread();
        int gen0 = GC.CollectionCount(0);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < iterations; i++) action();
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);

        return (name, iterations, GC.GetAllocatedBytesForCurrentThread() - allocated, elapsed, GC.CollectionCount(0) - gen0);
    }

    private void Report((string Name, int Iterations, long Allocated, TimeSpan Elapsed, int Gen0) sample)
    {
        output.WriteLine("{0}: iterations={1}, allocated/iteration={2:N1} B, elapsed/iteration={3:N3} ms, gen0={4}",
            sample.Name, sample.Iterations, (double)sample.Allocated / sample.Iterations,
            sample.Elapsed.TotalMilliseconds / sample.Iterations, sample.Gen0);
        Assert.True(sample.Allocated >= 0);
    }
}
