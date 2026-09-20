using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.TableView.Tests;

/// <summary>Blocks: which are read, when, how many rows each, how long they are kept — and that nothing is read whole.</summary>
public sealed class TTableViewPagingTests
{
    private const int Block = TTableView.RowsPerRead;

    [Fact]
    public void TheFirstScreenReadsExactlyOneBlock()
    {
        using var h = new TableHarness(height: 12);
        var table = new SyntheticTable(3, 10_000);
        h.Show(table);

        Assert.Equal(new[] { (0L, Block) }, table.Requests);
        Assert.Equal(10, h.View.VisibleRowCount);
    }

    [Fact]
    public void MovingAcrossABlockBoundaryReadsTheNextBlockOnly()
    {
        using var h = new TableHarness(height: 12);
        var table = new SyntheticTable(3, 10_000);
        h.Show(table);

        h.View.MoveTo(Block + 5, 0);                                  // rows 60..69 on screen
        h.Settle();

        Assert.Equal(new[] { (0L, Block), ((long)Block, Block) }, table.Requests);
        Assert.StartsWith("r69c0", h.Rows[^1]);
    }

    [Fact]
    public void MovingUpAcrossABlockBoundaryReadsThePreviousBlock()
    {
        using var h = new TableHarness(height: 12);
        var table = new SyntheticTable(3, 10_000);
        h.Show(table, row: 1000);

        long block = 1000 / Block;
        Assert.Contains((block * Block, Block), table.Requests);
        while (h.View.CurrentRow > block * Block) h.Press(Keys.kbUp);
        Assert.DoesNotContain(((block - 1) * Block, Block), table.Requests);

        h.Press(Keys.kbUp);
        h.Settle();
        Assert.Contains(((block - 1) * Block, Block), table.Requests);
        Assert.StartsWith($"r{(block * Block) - 1}c0", h.Rows[2]);
    }

    [Fact]
    public void ReturningToCachedRowsReadsNothingAgain()
    {
        using var h = new TableHarness(height: 12);
        var table = new SyntheticTable(3, 10_000);
        h.Show(table);
        for (int i = 0; i < 15; i++) { h.Press(Keys.kbPgDn); h.Settle(); }
        int requests = table.Requests.Count;

        h.Press(Keys.kbCtrlPgUp);
        h.Settle();

        Assert.Equal(requests, table.Requests.Count);
        Assert.Single(table.Requests, r => r.Start == 0);
        Assert.StartsWith("r0c0", h.Rows[2]);
    }

    [Fact]
    public void OldBlocksAreEvictedAndTheCacheStaysBounded()
    {
        using var h = new TableHarness(height: 12, cacheCapacity: 4);
        var table = new SyntheticTable(3, 1_000_000);
        h.Show(table);

        for (long block = 0; block < 40; block++)
        {
            h.View.MoveTo(block * Block, 0);
            h.Settle();
            Assert.True(h.View.CachedBlockCount <= 4, $"{h.View.CachedBlockCount} blocks cached");
        }

        Assert.DoesNotContain(0L, h.View.CachedBlockIndices);
        h.View.MoveTo(0, 0);
        h.Settle();
        Assert.Equal(2, table.Requests.Count(r => r.Start == 0));   // evicted, so read again
        Assert.Equal(4, h.View.CacheCapacity);
    }

    [Fact]
    public void ATallViewRaisesTheCacheToWhatItShowsButNoFurther()
    {
        using var h = new TableHarness(height: 300, cacheCapacity: 2);
        h.Show(new SyntheticTable(2, 1_000_000));

        // 298 rows span up to six blocks; the cache holds them plus a margin, not the table.
        Assert.InRange(h.View.CacheCapacity, 6, 8);
        Assert.DoesNotContain(h.Rows.Skip(2), row => row.StartsWith("Loading", StringComparison.Ordinal));
    }

    [Fact]
    public void TheFinalShortBlockOfAKnownCountIsReadForItsRemainderOnly()
    {
        using var h = new TableHarness(height: 6);
        var table = new SyntheticTable(2, 130);
        h.Show(table);

        h.Press(Keys.kbCtrlPgDn);
        h.Settle();

        Assert.Contains((128L, 2), table.Requests);
        Assert.Equal(129, h.View.CurrentRow);
        Assert.StartsWith("r129", h.Rows[^1]);                       // clipped: widths were sampled from "r63c0"
        Assert.True(h.View.TryGetRow(129, out TableRow? last));
        Assert.Equal(129L, (long)last!.Tag!);
    }

    [Fact]
    public void AnUnknownCountIsFoundByReadingAndNeverEstimated()
    {
        using var h = new TableHarness(height: 6);
        var table = new SyntheticTable(2, 100) { CountIsKnown = false };
        h.Show(table);

        Assert.Null(h.View.RowCount);
        Assert.False(h.View.IsRowCountKnown);
        Assert.Equal(Block, h.View.KnownRowCount);

        h.Press(Keys.kbCtrlPgDn);                                    // furthest reachable now: one block past row 63
        h.Settle();

        Assert.Equal(100, h.View.RowCount);
        Assert.True(h.View.IsRowCountKnown);
        Assert.Equal(99, h.View.CurrentRow);                         // pulled back to the real last row
        Assert.StartsWith("r99c0", h.Rows[^1]);
        Assert.All(table.Requests, r => Assert.True(r.Start < 100, $"read at {r.Start}"));
    }

    [Fact]
    public void AnUnknownCountEndingOnABlockBoundaryIsFoundByOneEmptyRead()
    {
        using var h = new TableHarness(height: 6);
        var table = new SyntheticTable(2, 2 * Block) { CountIsKnown = false, ReportsEndEarly = false };
        h.Show(table);

        h.Press(Keys.kbCtrlPgDn);
        h.Settle();
        Assert.Null(h.View.RowCount);                                // a full block does not say it was the last
        Assert.Equal(2 * Block, h.View.KnownRowCount);

        h.Press(Keys.kbCtrlPgDn);
        h.Settle();

        Assert.Contains((2L * Block, Block), table.Requests);
        Assert.Equal(2 * Block, h.View.RowCount);
        Assert.Equal((2 * Block) - 1, h.View.CurrentRow);
        Assert.StartsWith($"r{(2 * Block) - 1}", h.Rows[^1]);
        Assert.True(h.View.TryGetRow((2 * Block) - 1, out TableRow? last));
        Assert.Equal((2L * Block) - 1, (long)last!.Tag!);
    }

    /// <summary>
    /// The T-2 audit of unknown-length navigation: one Ctrl+End (or Ctrl+PgDn) reaches one block past what is known
    /// and stops there — it never walks the source to its end on its own. Each further press reaches one block more.
    /// </summary>
    [Fact]
    public void OneCtrlEndOnAnUnknownLengthTableReachesOneBlockFurtherAndNeverScansToTheEnd()
    {
        using var h = new TableHarness(height: 6);
        var table = new SyntheticTable(2, 1_000_000_000) { CountIsKnown = false };
        h.Show(table);
        int before = table.Requests.Count;

        h.Press(Keys.kbCtrlEnd);
        h.Settle();
        Assert.Equal((2 * Block) - 1, h.View.CurrentRow);
        Assert.Equal(1, table.Requests.Count - before);                      // one block, not the rest of the source
        Assert.Null(h.View.RowCount);

        h.Press(Keys.kbCtrlEnd);
        h.Settle();
        Assert.Equal((3 * Block) - 1, h.View.CurrentRow);
        Assert.Equal(2, table.Requests.Count - before);
        Assert.True(table.RowsReturned <= 3 * Block);
    }

    [Fact]
    public void RowCountBeyondInt32IsNavigatedWith64BitPositionsAndBoundedReads()
    {
        using var h = new TableHarness(width: 40, height: 12);
        const long rows = 5_000_000_000;
        var table = new SyntheticTable(0, rows, columnList: new[] { new TableColumn("Value", 16) });
        h.Show(table);

        h.Press(Keys.kbCtrlPgDn);
        h.Settle();
        Assert.Equal(rows - 1, h.View.CurrentRow);
        Assert.Equal(rows - 10, h.View.TopRow);
        Assert.Equal($"r{rows - 1}c0" + new string(' ', 16 - $"r{rows - 1}c0".Length) + "│", h.Rows[^1]);
        Assert.True(h.View.TryGetRow(rows - 1, out TableRow? last));
        Assert.Equal(rows - 1, (long)last!.Tag!);

        long past = (long)int.MaxValue + 10;
        h.View.MoveTo(past, 0);
        h.Settle();
        Assert.Equal(past, h.View.CurrentRow);
        Assert.StartsWith($"r{past}c0", h.Rows[2 + (int)(past - h.View.TopRow)]);

        h.Press(Keys.kbUp);
        h.Press(Keys.kbPgUp);
        h.Settle();
        Assert.Equal(past - 1 - 10, h.View.CurrentRow);

        Assert.Contains(table.Requests, r => r.Start > int.MaxValue);
        Assert.True(table.LargestRequest <= Block);
        Assert.True(table.RowsReturned <= 6 * Block, $"returned {table.RowsReturned} rows");
    }

    [Fact]
    public void BrowsingAHundredMillionRowsNeverMaterialisesTheTable()
    {
        using var h = new TableHarness(height: 20);
        var table = new SyntheticTable(4, 100_000_000);
        h.Show(table);

        for (int i = 0; i < 50; i++) { h.Press(Keys.kbPgDn); h.Settle(); }
        h.Press(Keys.kbCtrlEnd); h.Settle();
        for (int i = 0; i < 50; i++) { h.Press(Keys.kbPgUp); h.Settle(); }
        h.Press(Keys.kbCtrlHome); h.Settle();

        Assert.True(table.LargestRequest <= Block);
        Assert.True(table.RowsReturned < 64L * Block, $"returned {table.RowsReturned} rows");
        Assert.True(h.View.CachedBlockCount <= h.View.CacheCapacity);
        Assert.Equal(0, h.View.ReadsStartedWhileDrawing);
    }
}
