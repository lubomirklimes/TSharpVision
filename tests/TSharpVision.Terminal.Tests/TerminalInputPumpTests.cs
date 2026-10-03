using System.Collections.Concurrent;
using System.Diagnostics;
using Xunit;

namespace TSharpVision.Terminal.Tests;

/// <summary>
/// Pure tests of the ordered input path shared by the ConPTY and POSIX sessions. No PTY involved:
/// the writer is a fake, so these run identically on every OS.
/// </summary>
public sealed class TerminalInputPumpTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Input_IsWrittenInEnqueueOrder()
    {
        var written = new ConcurrentQueue<byte>();
        var pump = new TerminalInputPump(data => { foreach (byte b in data) written.Enqueue(b); return true; });

        var tasks = new List<Task>();
        for (int i = 0; i < 200; i++) tasks.Add(pump.EnqueueAsync(new[] { (byte)i }));
        await Task.WhenAll(tasks).WaitAsync(Wait);

        Assert.Equal(Enumerable.Range(0, 200).Select(i => (byte)i), written);
    }

    [Fact]
    public async Task A_BlockedWriter_DoesNotBlockTheCaller()
    {
        using var release = new ManualResetEventSlim(false);
        var pump = new TerminalInputPump(_ => { release.Wait(); return true; });

        var stopwatch = Stopwatch.StartNew();
        Task first = pump.EnqueueAsync(new byte[] { 1 });
        Task second = pump.EnqueueAsync(new byte[] { 2 });
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), "EnqueueAsync must return without writing.");
        Assert.False(second.IsCompleted);

        release.Set();
        await Task.WhenAll(first, second).WaitAsync(Wait);
    }

    [Fact]
    public async Task Close_DiscardsQueuedInput_AndCompletesItsTasks()
    {
        using var release = new ManualResetEventSlim(false);
        var written = new ConcurrentQueue<byte>();
        var pump = new TerminalInputPump(data => { release.Wait(); written.Enqueue(data[0]); return true; });

        Task inFlight = pump.EnqueueAsync(new byte[] { 1 });
        Task queued = pump.EnqueueAsync(new byte[] { 2 });
        await WaitUntil(() => pump.PendingBytes == 1); // item 1 is with the writer, item 2 waits

        pump.Close();
        await queued.WaitAsync(Wait); // completed by Close, never written
        release.Set();
        await inFlight.WaitAsync(Wait);

        Assert.True(pump.IsClosed);
        Assert.Equal(new byte[] { 1 }, written);
        Assert.True(pump.EnqueueAsync(new byte[] { 3 }).IsCompleted);
        Assert.Equal(new byte[] { 1 }, written);
    }

    [Fact]
    public async Task Close_IsIdempotent()
    {
        var pump = new TerminalInputPump(_ => true);
        pump.Close();
        pump.Close();
        await pump.EnqueueAsync(new byte[] { 1 }).WaitAsync(Wait);
        Assert.True(pump.IsClosed);
    }

    [Fact]
    public async Task A_WriterReportingThePtyGone_ClosesThePump()
    {
        int calls = 0;
        var pump = new TerminalInputPump(_ => { Interlocked.Increment(ref calls); return false; });

        await pump.EnqueueAsync(new byte[] { 1 }).WaitAsync(Wait);
        await WaitUntil(() => pump.IsClosed);
        await pump.EnqueueAsync(new byte[] { 2 }).WaitAsync(Wait);

        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task A_ThrowingWriter_IsTreatedAsFailure_AndNeverFaultsTheTask()
    {
        var pump = new TerminalInputPump(_ => throw new IOException("pipe broken"));

        await pump.EnqueueAsync(new byte[] { 1 }).WaitAsync(Wait); // does not throw
        await WaitUntil(() => pump.IsClosed);
    }

    [Fact]
    public async Task Input_BeyondTheBound_IsDropped()
    {
        using var release = new ManualResetEventSlim(false);
        long writtenBytes = 0;
        var pump = new TerminalInputPump(data =>
        {
            release.Wait();
            Interlocked.Add(ref writtenBytes, data.Length);
            return true;
        });

        const int Chunk = 1024 * 1024;
        var accepted = new List<Task>();
        Task first = pump.EnqueueAsync(new byte[1]);          // taken by the blocked writer
        await WaitUntil(() => pump.PendingBytes == 0);
        for (int i = 0; i < TerminalInputPump.MaximumPendingBytes / Chunk; i++)
            accepted.Add(pump.EnqueueAsync(new byte[Chunk]));
        Task dropped = pump.EnqueueAsync(new byte[Chunk]);

        Assert.True(dropped.IsCompleted, "Input over the bound completes at once, unwritten.");
        Assert.Equal(TerminalInputPump.MaximumPendingBytes, pump.PendingBytes);

        release.Set();
        await Task.WhenAll(accepted.Append(first)).WaitAsync(Wait);
        Assert.Equal(1 + TerminalInputPump.MaximumPendingBytes, Interlocked.Read(ref writtenBytes));
    }

    [Fact]
    public async Task EmptyInput_IsANoOp()
    {
        int calls = 0;
        var pump = new TerminalInputPump(_ => { calls++; return true; });
        await pump.EnqueueAsync(Array.Empty<byte>());
        Assert.Equal(0, calls);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not reached.");
            await Task.Delay(5);
        }
    }
}
