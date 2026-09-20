using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.TableView.Tests;

/// <summary>The current cell: keys, mouse, scroll bars and resizing, all in logical rows and columns.</summary>
public sealed class TTableViewNavigationTests
{
    [Fact]
    public void RowKeysMoveByRowAndScreenAndToEitherEnd()
    {
        using var h = new TableHarness(height: 12);                   // 10 rows under the header
        h.Show(new SyntheticTable(5, 1000));

        h.Press(Keys.kbDown);
        Assert.Equal((1L, 0L), (h.View.CurrentRow, h.View.TopRow));
        h.Press(Keys.kbPgDn);
        Assert.Equal((11L, 10L), (h.View.CurrentRow, h.View.TopRow)); // the cursor keeps its place on screen
        h.Press(Keys.kbUp);
        Assert.Equal((10L, 10L), (h.View.CurrentRow, h.View.TopRow));
        h.Press(Keys.kbPgUp);
        Assert.Equal((0L, 0L), (h.View.CurrentRow, h.View.TopRow));
        h.Press(Keys.kbCtrlPgDn);
        Assert.Equal((999L, 990L), (h.View.CurrentRow, h.View.TopRow));
        h.Press(Keys.kbPgDn);
        Assert.Equal((999L, 990L), (h.View.CurrentRow, h.View.TopRow)); // clamped
        h.Press(Keys.kbDown);
        Assert.Equal(999, h.View.CurrentRow);
        h.Press(Keys.kbCtrlPgUp);
        Assert.Equal((0L, 0L), (h.View.CurrentRow, h.View.TopRow));
        h.Press(Keys.kbUp);
        Assert.Equal(0, h.View.CurrentRow);
    }

    [Fact]
    public void ColumnKeysMoveByColumnAndToEitherEndAndCtrlHomeEndReachTheCorners()
    {
        using var h = new TableHarness(height: 12);
        h.Show(new SyntheticTable(5, 1000));

        h.Press(Keys.kbRight);
        Assert.Equal(1, h.View.CurrentColumn);
        h.Press(Keys.kbEnd);
        Assert.Equal(4, h.View.CurrentColumn);
        h.Press(Keys.kbRight);
        Assert.Equal(4, h.View.CurrentColumn);
        h.Press(Keys.kbHome);
        Assert.Equal(0, h.View.CurrentColumn);
        h.Press(Keys.kbLeft);
        Assert.Equal(0, h.View.CurrentColumn);

        h.Press(Keys.kbCtrlEnd);
        Assert.Equal((999L, 4), (h.View.CurrentRow, h.View.CurrentColumn));
        h.Press(Keys.kbCtrlHome);
        Assert.Equal((0L, 0), (h.View.CurrentRow, h.View.CurrentColumn));
    }

    [Fact]
    public void TheCurrentCellIsAlwaysKeptOnScreen()
    {
        using var h = new TableHarness(width: 30, height: 8);
        h.Show(new SyntheticTable(20, 500));

        for (int i = 0; i < 40; i++)
        {
            h.Press(i % 3 == 0 ? Keys.kbRight : Keys.kbDown);
            h.Settle();
            long row = h.View.CurrentRow;
            Assert.InRange(row, h.View.TopRow, h.View.TopRow + h.View.VisibleRowCount - 1);

            // The current column's header is on screen, starting at its laid-out position.
            Assert.Contains("C" + h.View.CurrentColumn, h.Rows[0]);
            Assert.InRange(h.View.CurrentColumn, h.View.LeftColumn, 19);
        }

        h.Press(Keys.kbEnd);
        h.Settle();
        Assert.StartsWith("C" + h.View.LeftColumn, h.Rows[0]);
        Assert.Contains("C19", h.Rows[0]);
    }

    [Fact]
    public void ResizingKeepsTheLogicalCellAndItsVisibility()
    {
        using var h = new TableHarness(width: 200, height: 50);
        var columns = Enumerable.Range(0, 30).Select(i => new TableColumn("C" + i, 9)).ToArray();
        var table = new SyntheticTable(0, 10_000, columnList: columns);
        h.Show(table);
        h.View.MoveTo(5000, 20);
        h.Settle();

        foreach ((int w, int ht) in new[] { (40, 10), (60, 24), (200, 50), (40, 10) })
        {
            h.View.ChangeBounds(new TRect(0, 0, w, ht));
            h.Settle();

            Assert.Equal(5000, h.View.CurrentRow);
            Assert.Equal(20, h.View.CurrentColumn);
            Assert.Equal(ht - TTableView.HeaderLines, h.View.VisibleRowCount);
            Assert.InRange(5000, h.View.TopRow, h.View.TopRow + h.View.VisibleRowCount - 1);
            Assert.InRange(20, h.View.LeftColumn, 29);

            string header = h.Rows[0].Length > w ? h.Rows[0][..w] : h.Rows[0];
            Assert.Contains("C20", header);
            string current = h.Rows[TTableView.HeaderLines + (int)(5000 - h.View.TopRow)];
            Assert.Contains("r5000c20", current.Length > w ? current[..w] : current);
        }

        Assert.True(table.RowsReturned <= 8 * TTableView.RowsPerRead, $"resizing read {table.RowsReturned} rows");
    }

    [Fact]
    public void AClickSelectsTheCellUnderItAndTheWheelMovesByRows()
    {
        using var h = new TableHarness(width: 60, height: 12);
        h.Show(new SyntheticTable(3, 100));                           // columns 5 wide: 0..4, 6..10, 12..16

        var click = new TEvent { What = Events.evMouseDown };
        click.mouse.where = new TPoint(13, 5);
        h.View.HandleEvent(ref click);
        Assert.Equal((3L, 2), (h.View.CurrentRow, h.View.CurrentColumn));

        var wheel = new TEvent { What = Events.evMouseWheel };
        wheel.mouse.eventFlags = Events.meWheelDown;
        h.View.HandleEvent(ref wheel);
        Assert.Equal(6, h.View.CurrentRow);
    }

    [Fact]
    public void TheScrollBarsFollowAndDriveTheViewEvenBeyondInt32Rows()
    {
        using var h = new TableHarness(width: 60, height: 12, scrollBars: true);
        const long rows = 5_000_000_000;
        h.Show(new SyntheticTable(3, rows));
        TScrollBar vertical = h.Vertical!;
        TScrollBar horizontal = h.Horizontal!;

        Assert.Equal(0, vertical.value);
        Assert.True(vertical.maxVal > 0 && vertical.maxVal <= 1_000_000);

        vertical.SetValue(vertical.maxVal / 2);
        h.Settle();
        Assert.InRange(h.View.CurrentRow, (rows / 2) - (rows / 100), (rows / 2) + (rows / 100));

        h.Press(Keys.kbCtrlPgDn);
        Assert.Equal(vertical.maxVal, vertical.value);

        h.Press(Keys.kbRight);
        Assert.Equal(1, horizontal.value);
        horizontal.SetValue(2);
        Assert.Equal(2, h.View.CurrentColumn);
    }

    [Fact]
    public void AnUnknownCountNeverGivesTheScrollBarAMadeUpTotal()
    {
        using var h = new TableHarness(width: 60, height: 12, scrollBars: true);
        h.Show(new SyntheticTable(2, 1_000_000) { CountIsKnown = false });
        TScrollBar vertical = h.Vertical!;

        // Rows found so far (64) plus one block that may be explored — not a million.
        Assert.Equal((2 * TTableView.RowsPerRead) - 1, vertical.maxVal);

        h.Press(Keys.kbCtrlPgDn);
        h.Settle();
        Assert.Equal((3 * TTableView.RowsPerRead) - 1, vertical.maxVal);
        Assert.Null(h.View.RowCount);
    }

    [Fact]
    public void TheOwnerIsToldWheneverThePositionOrTheLoadStateChanges()
    {
        using var h = new TableHarness(height: 12);
        h.Show(new SyntheticTable(3, 1000));
        int before = h.View.ViewChanges;

        h.Press(Keys.kbDown);
        h.Press(Keys.kbRight);

        Assert.Equal(before + 2, h.View.ViewChanges);
        Assert.Equal((1L, 1, 1000L), (h.View.CurrentRow, h.View.CurrentColumn, h.View.RowCount!.Value));
        Assert.Equal(3, h.View.ColumnCount);
    }
}
