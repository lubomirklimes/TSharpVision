using System.Collections.Concurrent;
using System.Text;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TSharpVision.HexView.Tests;

/// <summary>
/// A headless screen with one <see cref="THexView"/> on it, drawn into a real screen buffer and read
/// back as text, with the posted-event queue pumped by hand.
/// </summary>
internal sealed class HexHarness : IDisposable
{
    private readonly DriverScope _driver;
    private readonly SynchronizationContext? _savedContext = SynchronizationContext.Current;

    public HexHarness(int width = 80, int height = 10, bool inlineReads = true, int? cacheCapacity = null, TScrollBar? scrollBar = null)
    {
        // xUnit runs a test under its own SynchronizationContext, and an await continuation is never
        // inlined where a non-default context is current. Without one, completing a gated read on the
        // test thread runs the view's continuation - and queues its post - before Complete returns.
        SynchronizationContext.SetSynchronizationContext(null);
        _driver = new DriverScope((ushort)Math.Max(width, 1), (ushort)Math.Max(height, 1));
        TEventQueue.ClearPosted();
        TEventQueue.ClaimUiThread();

        Width = width;
        Height = height;
        Host = new TestGroup(new TRect(0, 0, width, height))
        {
            buffer = new ScreenBuffer(width * height * ScreenBuffer.GetSize()),
        };
        Host.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        View = new ScheduledHexView(
            new TRect(0, 0, width, height), scrollBar, cacheCapacity ?? HexPageCache.DefaultCapacity,
            inlineReads ? work => work() : null);
        Host.Insert(View);
    }

    public int Width { get; }

    public int Height { get; }

    public TestGroup Host { get; }

    public THexView View { get; }

    /// <summary>Draws, delivers every post the reads produced, and draws again, until nothing is left.</summary>
    public void Settle()
    {
        for (int round = 0; round < 64; round++)
        {
            Host.Redraw();
            if (!Pump()) return;
        }
    }

    /// <summary>Delivers every queued post. Returns whether anything was delivered.</summary>
    public static bool Pump()
    {
        bool any = false;
        while (TEventQueue.DeliverPostedEvent()) any = true;
        return any;
    }

    public void Press(ushort keyCode)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        View.HandleEvent(ref ev);
    }

    public IReadOnlyList<string> Rows
    {
        get
        {
            Span<TScreenChar> cells = Host.buffer!.Data;
            var rows = new List<string>(Height);
            for (int y = 0; y < Height; y++)
            {
                var line = new StringBuilder(Width);
                for (int x = 0; x < Width; x++)
                {
                    char c = cells[(y * Width) + x].Character;
                    line.Append(c == '\0' ? ' ' : c);
                }

                rows.Add(line.ToString().TrimEnd());
            }

            return rows;
        }
    }

    public void Dispose()
    {
        TEventQueue.ClearPosted();
        _driver.Dispose();
        SynchronizationContext.SetSynchronizationContext(_savedContext);
    }
}

/// <summary>Bytes computed from their offset, completing synchronously, with every read recorded.</summary>
internal sealed class SyntheticSource : IHexDataSource
{
    private readonly Func<long, byte> _byteAt;
    private readonly ConcurrentQueue<(long Offset, int Count)> _reads = new();
    private long _bytesRead;

    public SyntheticSource(long length, Func<long, byte>? byteAt = null)
    {
        TotalBytes = length;
        _byteAt = byteAt ?? (offset => (byte)(offset & 0xFF));
    }

    public long TotalBytes { get; }

    /// <summary>When false, the source reports an unknown length and is discovered by reading.</summary>
    public bool LengthIsKnown { get; init; } = true;

    public long? Length => LengthIsKnown ? TotalBytes : null;

    /// <summary>When set, reads stop returning data at this offset although <see cref="Length"/> claims more.</summary>
    public long? RealEnd { get; init; }

    public IReadOnlyCollection<(long Offset, int Count)> Reads => _reads;

    public long BytesRead => Interlocked.Read(ref _bytesRead);

    public int LargestRequest => _reads.IsEmpty ? 0 : _reads.Max(r => r.Count);

    public ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _reads.Enqueue((offset, destination.Length));

        long end = Math.Min(TotalBytes, RealEnd ?? long.MaxValue);
        int count = (int)Math.Clamp(end - offset, 0, destination.Length);
        Span<byte> span = destination.Span;
        for (int i = 0; i < count; i++) span[i] = _byteAt(offset + i);
        Interlocked.Add(ref _bytesRead, count);
        return ValueTask.FromResult(count);
    }
}

/// <summary>A source whose every read waits until the test completes it.</summary>
internal sealed class GatedSource : IHexDataSource
{
    private readonly ConcurrentQueue<Request> _requests = new();
    private readonly SemaphoreSlim _arrived = new(0);

    public GatedSource(long length) => Length = length;

    public long? Length { get; }

    public sealed record Request(long Offset, int Count, CancellationToken Token, TaskCompletionSource<int> Completion, Memory<byte> Destination)
    {
        /// <summary>Completes the read with bytes equal to <c>offset &amp; 0xFF</c>, on the calling thread.</summary>
        public void Complete()
        {
            Span<byte> span = Destination.Span;
            for (int i = 0; i < Count; i++) span[i] = (byte)((Offset + i) & 0xFF);
            Completion.SetResult(Count);
        }
    }

    public ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken)
    {
        // Synchronous continuations on purpose: completing the task on the test thread runs the view's
        // continuation — and therefore its post — before Complete returns.
        var completion = new TaskCompletionSource<int>();
        _requests.Enqueue(new Request(offset, destination.Length, cancellationToken, completion, destination));
        _arrived.Release();
        return new ValueTask<int>(completion.Task);
    }

    /// <summary>Waits for the next read the view starts. A signal, not a sleep.</summary>
    public Request Next()
    {
        Assert.True(_arrived.Wait(TimeSpan.FromSeconds(30)), "the view never asked for a page");
        Assert.True(_requests.TryDequeue(out Request? request));
        return request!;
    }

    public int Outstanding => _requests.Count;
}

/// <summary>
/// A hex view whose reads start through a test-chosen scheduler: inline, so a synchronous source
/// completes before the call returns, or never, so nothing is read at all. Null keeps the thread pool.
/// </summary>
internal sealed class ScheduledHexView : THexView
{
    private readonly Func<Func<Task>, Task>? _schedule;

    public ScheduledHexView(TRect bounds, TScrollBar? scrollBar, int cacheCapacity, Func<Func<Task>, Task>? schedule)
        : base(bounds, scrollBar, cacheCapacity)
    {
        _schedule = schedule;
    }

    protected override Task StartRead(Func<Task> read) => _schedule is null ? base.StartRead(read) : _schedule(read);
}
