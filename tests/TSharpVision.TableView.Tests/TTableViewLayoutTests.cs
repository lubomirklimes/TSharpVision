using System.Reflection;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.TableView.Tests;

/// <summary>Header, cells, clipping and the empty states — what the screen shows.</summary>
public sealed class TTableViewLayoutTests
{
    [Fact]
    public void TheHeaderTheLineUnderItAndTheRowsShareOneColumnLayout()
    {
        using var h = new TableHarness(width: 30, height: 6);
        h.Show(new SyntheticTable(3, 3));

        Assert.Equal("C0  │C1  │C2  │", h.Rows[0]);
        Assert.Equal("────┼────┼────┼" + new string('─', 15), h.Rows[1]);
        Assert.Equal("r0c0│r0c1│r0c2│", h.Rows[2]);
        Assert.Equal("r2c0│r2c1│r2c2│", h.Rows[4]);
        Assert.Equal(string.Empty, h.Rows[5]);                      // no row 3: nothing drawn, nothing read
        Assert.Equal(0, h.View.ReadsStartedWhileDrawing);
    }

    [Fact]
    public void AnEmptyTableKeepsItsHeadersAndSaysItIsEmptyWithoutReading()
    {
        using var h = new TableHarness(width: 30, height: 6);
        var table = new SyntheticTable(2, 0);
        h.Show(table);

        Assert.Equal("C0  │C1  │", h.Rows[0]);
        Assert.Equal("<empty>", h.Rows[2]);
        Assert.Empty(table.Requests);
        Assert.Equal(-1, h.View.CurrentRow);
        Assert.Equal(0, h.View.CurrentColumn);
        Assert.Equal(0, h.View.RowCount);

        h.Press(Keys.kbDown);
        h.Press(Keys.kbCtrlEnd);
        Assert.Equal(-1, h.View.CurrentRow);
    }

    [Fact]
    public void ATableWithNoColumnsShowsThatAndNeverReadsOrIndexesAColumn()
    {
        using var h = new TableHarness(width: 30, height: 6);
        var table = new SyntheticTable(0, 1000);
        h.Show(table);

        Assert.Equal("<no columns>", h.Rows[0]);
        Assert.Empty(table.Requests);
        Assert.Equal(-1, h.View.CurrentColumn);
        Assert.Equal(-1, h.View.CurrentRow);

        foreach (ushort key in new[] { Keys.kbDown, Keys.kbRight, Keys.kbEnd, Keys.kbCtrlEnd, Keys.kbPgDn })
            h.Press(key);
        h.View.MoveTo(10, 3);
        h.Settle();

        Assert.Equal(-1, h.View.CurrentColumn);
        Assert.Empty(table.Requests);
    }

    [Fact]
    public void LongTextIsClippedToTheWidestColumnWithAnEllipsis()
    {
        using var h = new TableHarness(width: 60, height: 5);
        h.Show(new SyntheticTable(1, 1, (_, _) => new TableCell(new string('x', 100_000))));

        Assert.Equal(TTableView.MaxColumnWidth, h.View.GetColumnWidth(0));
        Assert.Equal(new string('x', TTableView.MaxColumnWidth - 1) + "…│", h.Rows[2]);
    }

    [Fact]
    public void AnOverLongHeaderIsClippedTheSameWay()
    {
        using var h = new TableHarness(width: 60, height: 5);
        h.Show(new SyntheticTable(0, 1, columnList: new[] { new TableColumn(new string('H', 500)) }));

        Assert.Equal(new string('H', TTableView.MaxColumnWidth - 1) + "…│", h.Rows[0]);
    }

    [Fact]
    public void ControlCharactersInACellAreMadeVisibleInsteadOfDrawn()
    {
        using var h = new TableHarness(width: 30, height: 5);
        h.Show(new SyntheticTable(1, 1, (_, _) => new TableCell("a\tbc")));

        Assert.StartsWith("a·b·c", h.Rows[2]);
    }

    [Fact]
    public void ARightAlignedColumnEndsItsTextAtTheColumnEdge()
    {
        using var h = new TableHarness(width: 30, height: 5);
        h.Show(new SyntheticTable(0, 1, (_, _) => new TableCell("12"),
            new[] { new TableColumn("Total", 6, TableAlignment.Right) }));

        Assert.Equal("Total │", h.Rows[0]);
        Assert.Equal("    12│", h.Rows[2]);
    }

    [Fact]
    public void NullAndErrorCellsAreDrawnInTheirOwnRoleColoursAwayFromTheCursor()
    {
        using var h = new TableHarness(width: 30, height: 6);
        h.Show(new SyntheticTable(3, 2, (row, column) => column switch
        {
            0 => new TableCell("ok"),
            1 => new TableCell("NULL", TableCellRole.Null),
            _ => new TableCell("!bad", TableCellRole.Error),
        }));

        // Row 1 is not the current row; its three cells carry three different roles.
        ushort normal = h.AttributeAt(0, 3);
        ushort nullColour = h.AttributeAt(5, 3);
        ushort error = h.AttributeAt(10, 3);
        Assert.NotEqual(normal, nullColour);
        Assert.NotEqual(normal, error);
        Assert.StartsWith("ok  │NULL│!bad│", h.Rows[3]);           // the text says it too, not only the colour
    }

    [Fact]
    public void ManyColumnsAreReachedByKeyboardAndHeaderAndRowsScrollTogether()
    {
        using var h = new TableHarness(width: 60, height: 6);
        h.Show(new SyntheticTable(500, 10));

        h.Press(Keys.kbEnd);
        h.Settle();

        Assert.Equal(499, h.View.CurrentColumn);
        int left = h.View.LeftColumn;
        Assert.True(left > 0);
        Assert.StartsWith("C" + left + " ", h.Rows[0]);
        Assert.StartsWith("r0c" + left, h.Rows[2]);
        Assert.Contains("C499", h.Rows[0]);
        Assert.Contains("r0c499", h.Rows[2]);
        Assert.All(h.Rows, row => Assert.True(row.Length <= 60));

        h.Press(Keys.kbHome);
        Assert.Equal(0, h.View.LeftColumn);
        h.Settle();
        Assert.StartsWith("C0 ", h.Rows[0]);
    }

    [Fact]
    public void ColumnWidthsComeFromTheHeaderAHintAndTheFirstBlockOnly()
    {
        using var h = new TableHarness(width: 80, height: 6, blockRows: 4);
        var columns = new[] { new TableColumn("Id"), new TableColumn("Fixed", 12), new TableColumn("A") };
        var table = new SyntheticTable(0, 1_000_000, (row, column) => new TableCell(row < 4 ? "short" : new string('w', 30)), columns);
        h.Show(table);

        Assert.Equal(5, h.View.GetColumnWidth(0));                   // "short", sampled from the first block
        Assert.Equal(12, h.View.GetColumnWidth(1));                  // the hint wins, no sampling
        Assert.Equal(5, h.View.GetColumnWidth(2));

        h.View.MoveTo(500_000, 0);
        h.Settle();
        Assert.Equal(5, h.View.GetColumnWidth(0));                   // later, wider rows never re-size
        Assert.True(table.RowsReturned <= 4 * 4, $"read {table.RowsReturned} rows to size columns");
    }

    [Theory]
    [InlineData(40, 10)]
    [InlineData(60, 24)]
    [InlineData(200, 50)]
    public void AtAnySizeTheViewWritesOnlyInsideItsBounds(int width, int height)
    {
        using var driver = new DriverScope((ushort)width, (ushort)height);
        TEventQueue.ClearPosted();
        TEventQueue.ClaimUiThread();
        var host = new TestGroup(new TRect(0, 0, width, height)) { buffer = new ScreenBuffer(width * height) };
        host.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        int from = width / 4;
        int to = width - (width / 4);
        var left = new TStaticText(new TRect(0, 0, from, height), new string('L', from * height));
        var right = new TStaticText(new TRect(to, 0, width, height), new string('R', (width - to) * height));
        var view = new ScheduledTableView(new TRect(from, 1, to, height - 1), null, null, 8, TTableView.RowsPerRead, work => work());
        host.Insert(left);
        host.Insert(right);
        host.Insert(view);

        view.SetDataSource(new SyntheticTable(50, 5_000_000_000, (row, column) => new TableCell(new string('z', 60))));
        view.MoveTo(4_000_000_000, 25);
        while (TEventQueue.DeliverPostedEvent())
        {
        }

        host.Redraw();

        Span<TScreenChar> cells = host.buffer.Data;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < from; x++) Assert.Equal('L', cells[(y * width) + x].Character);
            for (int x = to; x < width; x++) Assert.Equal('R', cells[(y * width) + x].Character);
        }

        Assert.Equal(height - 2 - TTableView.HeaderLines, view.VisibleRowCount);
        Assert.Equal(4_000_000_000, view.CurrentRow);
        Assert.Equal(25, view.CurrentColumn);
        view.ShutDown();
        TEventQueue.ClearPosted();
    }

    [Fact]
    public void TinySizesAreSafeAndReadNothingWhenNoRowFits()
    {
        using var h = new TableHarness(width: 40, height: 10);
        var table = new SyntheticTable(3, 100);
        h.Show(table);
        h.View.MoveTo(50, 2);
        h.Settle();
        int before = table.Requests.Count;

        foreach ((int w, int ht) in new[] { (1, 1), (3, 2), (5, 3), (0, 0), (2, 1) })
        {
            h.View.ChangeBounds(new TRect(0, 0, w, ht));
            h.Settle();
            Assert.Equal(50, h.View.CurrentRow);
            Assert.Equal(2, h.View.CurrentColumn);
        }

        // Row 50's block was already cached, and a view with no row under the header asks for nothing.
        Assert.Equal(before, table.Requests.Count);
        h.View.ChangeBounds(new TRect(0, 0, 40, 10));
        h.Settle();
        Assert.StartsWith("r50c", h.Rows[2 + (int)(50 - h.View.TopRow)]);
    }

    [Fact]
    public void TheRowHeaderShowsEachRowsOwnLabelAndAMutedRowIsMarkedBeyondColour()
    {
        using var h = new TableHarness(width: 30, height: 6);
        h.View.RowHeaderWidth = 4;
        h.Show(new LabelledTable());

        Assert.Equal("    │C0  │", h.Rows[0]);
        Assert.Equal("────┼────┼" + new string('─', 20), h.Rows[1]);
        Assert.Equal("  #1│a   │", h.Rows[2]);
        Assert.Equal(" #2*│b   │", h.Rows[3]);                              // the mark is text, not only colour
        Assert.Equal("  #3│c   │", h.Rows[4]);
        Assert.NotEqual(h.AttributeAt(5, 4), h.AttributeAt(5, 3));          // row 1 is muted, row 2 is not

        h.Press(Keys.kbEnd);
        Assert.Equal(0, h.View.CurrentColumn);
        Assert.Equal(4, h.View.RowHeaderWidth);
        h.View.RowHeaderWidth = 500;
        Assert.Equal(TTableView.MaxRowHeaderWidth, h.View.RowHeaderWidth);
    }

    [Fact]
    public void APositionHandedBackRestoresTheCellAndTheScrollExactly()
    {
        using var h = new TableHarness(width: 30, height: 8);
        var table = new SyntheticTable(20, 10_000);
        h.Show(table);
        h.View.MoveTo(5000, 12);
        h.Settle();
        h.View.MoveTo(4998, 11);                                             // inside the screen: the top stays
        h.Settle();
        TableViewPosition position = h.View.Position;
        Assert.Equal((4998L, 11), (position.Row, position.Column));

        h.View.SetDataSource(null);
        Assert.Equal(-1, h.View.CurrentRow);
        h.View.SetDataSource(table, position);
        h.Settle();

        Assert.Equal((position.Row, position.Column, position.TopRow, position.LeftColumn),
            (h.View.CurrentRow, h.View.CurrentColumn, h.View.TopRow, h.View.LeftColumn));
        Assert.True(h.View.TryGetRow(position.TopRow, out TableRow? top));
        Assert.Equal(position.TopRow, (long)top!.Tag!);
        Assert.Equal(position.ColumnWidths, Enumerable.Range(0, 20).Select(h.View.GetColumnWidth));   // the same layout
    }

    private sealed class LabelledTable : ITableDataSource
    {
        public IReadOnlyList<TableColumn> Columns { get; } = new[] { new TableColumn("C0") };

        public long? RowCount => 3;

        public ValueTask<TableRowBlock> ReadRowsAsync(long start, int count, CancellationToken cancellationToken)
        {
            var rows = new[]
            {
                new TableRow(new[] { new TableCell("a") }, null, TableRowRole.Normal, "#1"),
                new TableRow(new[] { new TableCell("b") }, null, TableRowRole.Muted, "#2*"),
                new TableRow(new[] { new TableCell("c") }, null, TableRowRole.Normal, "#3"),
            };
            return ValueTask.FromResult(new TableRowBlock(start, rows.Skip((int)start).Take(count).ToArray(), true));
        }
    }

    [Fact]
    public void TheExtensionReferencesNoCommanderAssembly()
    {
        Assembly assembly = typeof(TTableView).Assembly;
        string[] references = assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.DoesNotContain(references, name => name.StartsWith("TSharpCommander", StringComparison.Ordinal));
        Assert.Contains("TSharpVision", references);
        Assert.All(references, name => Assert.True(
            name == "TSharpVision" || name.StartsWith("System", StringComparison.Ordinal) || name == "netstandard",
            $"unexpected reference {name}"));
    }
}
