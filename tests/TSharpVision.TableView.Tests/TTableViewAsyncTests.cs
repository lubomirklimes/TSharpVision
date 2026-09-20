using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.TableView.Tests;

/// <summary>
/// Asynchronous reads, ordered by gates the test opens — never by time. A timeout appears only as a deadlock guard.
/// </summary>
public sealed class TTableViewAsyncTests
{
    [Fact]
    public void DrawingNeverReadsAndAnUnloadedRowSaysItIsLoading()
    {
        using var h = new TableHarness(height: 6);
        var table = new GatedTable(1, 1000);
        h.View.SetDataSource(table);

        GatedTable.Request request = table.Next();
        for (int i = 0; i < 5; i++) h.Host.Redraw();

        Assert.Equal(0, table.Outstanding);                           // one read, however often it was drawn
        Assert.Equal(0, h.View.ReadsStartedWhileDrawing);
        Assert.Equal("C0  │", h.Rows[0]);
        Assert.Equal("Loading…", h.Rows[2]);
        Assert.True(h.View.IsLoading);

        request.Complete("a");
        h.Settle();
        Assert.Equal("a0  │", h.Rows[2]);
        Assert.False(h.View.IsLoading);
    }

    [Fact]
    public void ASlowSourceDoesNotHoldUpTheEventLoop()
    {
        using var h = new TableHarness(height: 6, inlineReads: false);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var table = new BlockingTable(entered, release);

        h.View.SetDataSource(table);                                  // returns although the read blocks its thread
        h.Host.Redraw();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)), "the read never started");
        Assert.Equal("Loading…", h.Rows[2]);

        h.Press(Keys.kbDown);                                         // navigation still works meanwhile
        Assert.Equal(1, h.View.CurrentRow);

        release.Set();
        Assert.True(table.Finished.Wait(TimeSpan.FromSeconds(30)));
        Assert.True(SpinWait.SpinUntil(() => TEventQueue.PostedCount > 0, TimeSpan.FromSeconds(30)), "the result was never posted");
        h.Settle();
        Assert.StartsWith("x0", h.Rows[2]);
    }

    /// <summary>
    /// The mutation-sensitive case. Block 0's first result is already queued when the user moves away and back, so
    /// block 0 is pending again — under a new request id. The queued result is for the same block and would pass an
    /// index-only check; only the request identity rejects it. If it were accepted the screen would show <c>old</c>.
    /// </summary>
    [Fact]
    public void AQueuedResultForAnEarlierRequestOfTheSameBlockIsRejected()
    {
        using var h = new TableHarness(height: 6);
        var table = new GatedTable(1, 100_000);
        h.View.SetDataSource(table);

        GatedTable.Request first = table.Next();
        first.Complete("old");                                        // posted, not yet delivered
        Assert.Equal(1, TEventQueue.PostedCount);

        h.View.MoveTo(5000, 0);
        GatedTable.Request far = table.Next();
        h.View.MoveTo(0, 0);
        GatedTable.Request again = table.Next();
        Assert.Equal(0, again.Start);
        Assert.True(far.Token.IsCancellationRequested);

        TableHarness.Pump();                                          // delivers the stale "old" block
        h.Host.Redraw();
        Assert.Equal("Loading…", h.Rows[2]);
        Assert.DoesNotContain(0L, h.View.CachedBlockIndices);

        again.Complete("new");
        h.Settle();
        Assert.StartsWith("new0", h.Rows[2]);
        Assert.StartsWith("new3", h.Rows[5]);
    }

    [Fact]
    public void TheNewestNavigationWinsOverSlowerEarlierReads()
    {
        using var h = new TableHarness(height: 6);
        var table = new GatedTable(1, 100_000);
        h.View.SetDataSource(table);

        GatedTable.Request row0 = table.Next();
        h.View.MoveTo(5000, 0);
        GatedTable.Request row5000 = table.Next();
        h.View.MoveTo(100, 0);
        GatedTable.Request row100 = table.Next();

        Assert.True(row0.Token.IsCancellationRequested);
        Assert.True(row5000.Token.IsCancellationRequested);
        Assert.False(row100.Token.IsCancellationRequested);

        row100.Complete("c");
        h.Settle();

        // The slow ones finish afterwards anyway: a source that ignores cancellation.
        row5000.Complete("b");
        row0.Complete("a");
        Assert.Equal(0, TEventQueue.PostedCount);
        h.Settle();

        Assert.Equal(100, h.View.CurrentRow);
        Assert.Equal(100, h.View.TopRow);                             // reached from below: row 100 is the top row
        Assert.StartsWith("c100", h.Rows[2]);
        Assert.StartsWith("c103", h.Rows[5]);
        Assert.Equal(new[] { 1L }, h.View.CachedBlockIndices);
    }

    [Fact]
    public void AResultForAReplacedSourceIsIgnored()
    {
        using var h = new TableHarness(height: 6);
        var old = new GatedTable(1, 1000);
        h.View.SetDataSource(old);
        GatedTable.Request pending = old.Next();

        h.Show(new SyntheticTable(1, 10));
        Assert.True(pending.Token.IsCancellationRequested);

        var forged = new TableBlockResult(-1, 1, new TablePage(0, 0, 64, new TableRow?[] { new TableRow(new[] { new TableCell("forged") }) }, false, null));
        Assert.False(h.View.ApplyBlockResult(forged));

        pending.Complete("old");
        h.Settle();
        Assert.StartsWith("r0c0", h.Rows[2]);
    }

    [Fact]
    public void AFailedBlockIsABoundedErrorAndLoadedRowsStayUsable()
    {
        using var h = new TableHarness(height: 6);
        var table = new GatedTable(1, 1000);
        h.View.SetDataSource(table);
        table.Next().Complete("a");
        h.Settle();

        h.View.MoveTo(64, 0);                                         // rows 61..64: the end of block 0 and block 1
        GatedTable.Request failing = table.Next();
        Assert.Equal(64, failing.Start);
        failing.Completion.SetException(new IOException(new string('x', 10_000)));
        h.Settle();

        Assert.NotNull(h.View.LastError);
        Assert.True(h.View.LastError!.Length <= 257, $"error of {h.View.LastError.Length} characters");
        Assert.StartsWith("a61", h.Rows[2]);
        Assert.StartsWith("a63", h.Rows[4]);
        Assert.StartsWith("! xxxx", h.Rows[5]);
        Assert.True(h.Rows[5].Length <= h.Width);
        Assert.True(h.View.TryGetRow(61, out _));
        Assert.False(h.View.TryGetRow(64, out _));
        Assert.Equal(0, table.Outstanding);                           // not retried in a loop

        h.Press(Keys.kbUp);                                            // still navigable
        Assert.Equal(63, h.View.CurrentRow);

        h.View.RetryFailedBlocks();
        GatedTable.Request retry = table.Next();
        Assert.Equal(64, retry.Start);
        retry.Complete("b");
        h.Settle();
        Assert.Null(h.View.LastError);
        Assert.StartsWith("b64", h.Rows[5]);
    }

    [Fact]
    public void ARowTheSourceDidNotSupplyIsShownAsUnavailableNotAsTheEnd()
    {
        using var h = new TableHarness(height: 6);
        var table = new GatedTable(1, null);
        h.View.SetDataSource(table);
        table.Next().Complete("s", rows: 2, reachedEnd: false);
        h.Settle();

        Assert.StartsWith("s0", h.Rows[2]);
        Assert.StartsWith("s1", h.Rows[3]);
        Assert.StartsWith("! row not supplied", h.Rows[4]);
        Assert.Null(h.View.RowCount);                                 // not mistaken for the end
        Assert.Equal(64, h.View.KnownRowCount);
    }

    [Fact]
    public void ShuttingDownCancelsReadsAndALateResultChangesNothing()
    {
        using var h = new TableHarness(height: 6);
        var table = new GatedTable(1, 1000);
        h.View.SetDataSource(table);
        GatedTable.Request pending = table.Next();

        h.View.ShutDown();
        Assert.True(pending.Token.IsCancellationRequested);
        Assert.Null(h.View.DataSource);

        pending.Complete("late");
        TableHarness.Pump();
        Assert.Equal(0, h.View.CachedBlockCount);
        Assert.Equal(0, TEventQueue.PostedCount);
    }

    private sealed class BlockingTable : ITableDataSource
    {
        private readonly ManualResetEventSlim _entered;
        private readonly ManualResetEventSlim _release;

        public BlockingTable(ManualResetEventSlim entered, ManualResetEventSlim release)
        {
            _entered = entered;
            _release = release;
        }

        public IReadOnlyList<TableColumn> Columns { get; } = new[] { new TableColumn("X") };

        public long? RowCount => 1000;

        public ManualResetEventSlim Finished { get; } = new();

        public ValueTask<TableRowBlock> ReadRowsAsync(long start, int count, CancellationToken cancellationToken)
        {
            _entered.Set();
            _release.Wait(TimeSpan.FromSeconds(60));                  // synchronous blocking before returning
            var rows = Enumerable.Range(0, count).Select(i => new TableRow(new[] { new TableCell("x" + (start + i)) })).ToArray();
            Finished.Set();
            return ValueTask.FromResult(new TableRowBlock(start, rows, false));
        }
    }
}
