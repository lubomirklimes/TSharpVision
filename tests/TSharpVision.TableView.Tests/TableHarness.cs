using System.Collections.Concurrent;
using System.Text;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TSharpVision.TableView.Tests;

/// <summary>
/// A headless screen with one <see cref="TTableView"/> on it, drawn into a real screen buffer and read back as text,
/// with the posted-event queue pumped by hand. Owns the queue for the test's duration and empties it on dispose.
/// </summary>
internal sealed class TableHarness : IDisposable
{
    private readonly DriverScope _driver;
    private readonly SynchronizationContext? _savedContext = SynchronizationContext.Current;

    public TableHarness(
        int width = 60, int height = 12, bool inlineReads = true, int cacheCapacity = 8, int blockRows = TTableView.RowsPerRead,
        bool scrollBars = false)
    {
        // xUnit runs a test under its own SynchronizationContext, and an await continuation is never inlined where a
        // non-default context is current. Without one, completing a gated read on the test thread runs the view's
        // continuation — and queues its post — before Complete returns.
        SynchronizationContext.SetSynchronizationContext(null);

        // Scroll bars sit outside the view — a column to the right, a line below — in the same group, so their
        // broadcasts reach it exactly as they would inside a window.
        Width = width + (scrollBars ? 1 : 0);
        Height = height + (scrollBars ? 1 : 0);
        _driver = new DriverScope((ushort)Math.Max(Width, 1), (ushort)Math.Max(Height, 1));
        TEventQueue.ClearPosted();
        TEventQueue.ClaimUiThread();

        Host = new TestGroup(new TRect(0, 0, Width, Height))
        {
            buffer = new ScreenBuffer(Width * Height * ScreenBuffer.GetSize()),
        };
        Host.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        if (scrollBars)
        {
            Vertical = new TScrollBar(new TRect(width, 0, width + 1, height));
            Horizontal = new TScrollBar(new TRect(0, height, width, height + 1));
            Host.Insert(Vertical);
            Host.Insert(Horizontal);
        }

        View = new ScheduledTableView(
            new TRect(0, 0, width, height), Horizontal, Vertical, cacheCapacity, blockRows,
            inlineReads ? work => work() : null);
        Host.Insert(View);
    }

    public TScrollBar? Vertical { get; }

    public TScrollBar? Horizontal { get; }

    public int Width { get; }

    public int Height { get; }

    public TestGroup Host { get; }

    public ScheduledTableView View { get; }

    /// <summary>Delivers every post the reads produced — each may start more reads — then draws.</summary>
    public void Settle()
    {
        for (int round = 0; round < 256 && Pump(); round++)
        {
        }

        Host.Redraw();
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

    public void Show(ITableDataSource source, long row = 0, int column = 0)
    {
        View.SetDataSource(source, row, column);
        Settle();
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

    /// <summary>The attribute of one screen cell.</summary>
    public ushort AttributeAt(int x, int y) => (ushort)Host.buffer!.Data[(y * Width) + x].Attr;

    public void Dispose()
    {
        View.ShutDown();
        TEventQueue.ClearPosted();
        _driver.Dispose();
        SynchronizationContext.SetSynchronizationContext(_savedContext);
    }
}

/// <summary>
/// A table view whose reads start through a test-chosen scheduler: inline, so a synchronous source completes before
/// the call returns. Null keeps the thread pool. Counts how often it was drawn and how often it asked to read while
/// drawing, which must be never.
/// </summary>
internal sealed class ScheduledTableView : TTableView
{
    private readonly Func<Func<Task>, Task>? _schedule;
    private bool _drawing;

    public ScheduledTableView(TRect bounds, TScrollBar? horizontal, TScrollBar? vertical, int cacheCapacity, int blockRows,
        Func<Func<Task>, Task>? schedule)
        : base(bounds, horizontal, vertical, cacheCapacity, blockRows)
    {
        _schedule = schedule;
    }

    public int ReadsStartedWhileDrawing { get; private set; }

    public int ViewChanges { get; private set; }

    public override void Draw()
    {
        _drawing = true;
        try
        {
            base.Draw();
        }
        finally
        {
            _drawing = false;
        }
    }

    protected override void OnViewChanged() => ViewChanges++;

    protected override Task StartRead(Func<Task> read)
    {
        if (_drawing) ReadsStartedWhileDrawing++;
        return _schedule is null ? base.StartRead(read) : _schedule(read);
    }
}

/// <summary>
/// Rows computed from their position (<c>r{row}c{column}</c>), completing synchronously, with every request recorded.
/// Nothing is ever materialised beyond the rows asked for.
/// </summary>
internal sealed class SyntheticTable : ITableDataSource
{
    private readonly ConcurrentQueue<(long Start, int Count)> _requests = new();
    private readonly Func<long, int, TableCell> _cell;
    private long _rowsReturned;

    public SyntheticTable(int columns, long rows, Func<long, int, TableCell>? cell = null, IReadOnlyList<TableColumn>? columnList = null)
    {
        TotalRows = rows;
        Columns = columnList ?? Enumerable.Range(0, columns).Select(c => new TableColumn("C" + c)).ToArray();
        _cell = cell ?? ((row, column) => new TableCell($"r{row}c{column}"));
    }

    public long TotalRows { get; }

    /// <summary>When false, the table reports an unknown row count and is discovered by reading.</summary>
    public bool CountIsKnown { get; init; } = true;

    /// <summary>
    /// When false, the table only says it ended with a short block — as a stream would — so a table ending exactly on
    /// a block boundary is found by one more, empty, read.
    /// </summary>
    public bool ReportsEndEarly { get; init; } = true;

    public IReadOnlyList<TableColumn> Columns { get; }

    public long? RowCount => CountIsKnown ? TotalRows : null;

    public IReadOnlyCollection<(long Start, int Count)> Requests => _requests;

    public long RowsReturned => Interlocked.Read(ref _rowsReturned);

    public int LargestRequest => _requests.IsEmpty ? 0 : _requests.Max(r => r.Count);

    public ValueTask<TableRowBlock> ReadRowsAsync(long start, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _requests.Enqueue((start, count));

        int n = (int)Math.Clamp(TotalRows - start, 0, count);
        var rows = new TableRow[n];
        for (int i = 0; i < n; i++)
        {
            var cells = new TableCell[Columns.Count];
            for (int c = 0; c < cells.Length; c++) cells[c] = _cell(start + i, c);
            rows[i] = new TableRow(cells, tag: start + i);
        }

        Interlocked.Add(ref _rowsReturned, n);
        bool end = ReportsEndEarly ? start + n >= TotalRows : n < count;
        return ValueTask.FromResult(new TableRowBlock(start, rows, end));
    }
}

/// <summary>A table whose every read waits until the test completes it — the deterministic gate for ordering.</summary>
internal sealed class GatedTable : ITableDataSource
{
    private readonly ConcurrentQueue<Request> _requests = new();
    private readonly SemaphoreSlim _arrived = new(0);

    public GatedTable(int columns, long? rows)
    {
        Columns = Enumerable.Range(0, columns).Select(c => new TableColumn("C" + c)).ToArray();
        RowCount = rows;
    }

    public IReadOnlyList<TableColumn> Columns { get; }

    public long? RowCount { get; }

    public sealed record Request(long Start, int Count, CancellationToken Token, TaskCompletionSource<TableRowBlock> Completion)
    {
        /// <summary>Completes the read with rows <c>{tag}{row}</c>, on the calling thread. Ignores cancellation, as a careless source would.</summary>
        public void Complete(string tag, int? rows = null, bool reachedEnd = false)
        {
            int n = rows ?? Count;
            var list = new TableRow[n];
            for (int i = 0; i < n; i++) list[i] = new TableRow(new[] { new TableCell(tag + (Start + i)) });
            Completion.SetResult(new TableRowBlock(Start, list, reachedEnd));
        }
    }

    public ValueTask<TableRowBlock> ReadRowsAsync(long start, int count, CancellationToken cancellationToken)
    {
        // Synchronous continuations on purpose: completing on the test thread runs the view's continuation — and
        // therefore its post — before Complete returns.
        var completion = new TaskCompletionSource<TableRowBlock>();
        _requests.Enqueue(new Request(start, count, cancellationToken, completion));
        _arrived.Release();
        return new ValueTask<TableRowBlock>(completion.Task);
    }

    /// <summary>Waits for the next read the view starts. A signal with a deadlock guard, not a sleep.</summary>
    public Request Next()
    {
        Assert.True(_arrived.Wait(TimeSpan.FromSeconds(30)), "the view never asked for rows");
        Assert.True(_requests.TryDequeue(out Request? request));
        return request!;
    }

    public int Outstanding => _requests.Count;
}
