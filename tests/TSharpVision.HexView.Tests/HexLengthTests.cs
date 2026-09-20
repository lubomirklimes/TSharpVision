using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.HexView.Tests;

/// <summary>
/// The three length situations of <see cref="IHexDataSource.Length"/>: known empty, known, and not known
/// until reads discover the end.
/// </summary>
public sealed class HexLengthTests
{
    private const int Page = HexPageCache.PageSize;

    [Fact]
    public void AKnownEmptySourceIsEmptyAtOnceAndIsNeverRead()
    {
        using var h = new HexHarness(height: 4);
        var source = new SyntheticSource(0);

        h.View.SetDataSource(source);
        Assert.True(h.View.IsLengthKnown);
        Assert.Equal(0, h.View.DataLength);

        h.Settle();
        Assert.Empty(source.Reads);
        Assert.All(h.Rows, row => Assert.Equal(string.Empty, row));
    }

    [Fact]
    public void AKnownSmallSourceShowsItsExactLengthBeforeAnyRead()
    {
        using var h = new HexHarness(height: 4);
        h.View.SetDataSource(new SyntheticSource(37));

        Assert.True(h.View.IsLengthKnown);
        Assert.Equal(37, h.View.DataLength);
        h.Settle();
        Assert.Equal(37, h.View.DataLength);
        Assert.StartsWith("00000020  20 21 22 23 24", h.Rows[2]);
    }

    [Fact]
    public void AKnownSourceBeyond4GiBIsKnownWithoutReadingToItsEnd()
    {
        using var h = new HexHarness(height: 4);
        long length = 6L << 30;
        var source = new SyntheticSource(length);

        h.View.SetDataSource(source);
        h.Settle();
        h.Press(Keys.kbCtrlPgDn);
        h.Settle();

        Assert.True(h.View.IsLengthKnown);
        Assert.Equal(length, h.View.DataLength);
        Assert.True(source.BytesRead <= 4L * Page);
    }

    [Fact]
    public void AnUnknownSourceIsNotMistakenForAnEmptyOneAndReachesItsEndByReading()
    {
        using var h = new HexHarness(height: 4);
        var source = new SyntheticSource(100) { LengthIsKnown = false };

        h.View.SetDataSource(source);
        Assert.False(h.View.IsLengthKnown);                             // not "empty": unknown
        Assert.Equal(0, h.View.DataLength);

        h.Settle();

        Assert.True(h.View.IsLengthKnown);                              // the short read found the end
        Assert.Equal(100, h.View.DataLength);
        Assert.StartsWith("00000000  00 01 02", h.Rows[0]);
        Assert.Contains(source.Reads, r => r.Offset == 0 && r.Count == Page);
    }

    [Fact]
    public void AnUnknownEmptySourceBecomesKnownEmptyAfterItsFirstRead()
    {
        using var h = new HexHarness(height: 4);
        var source = new SyntheticSource(0) { LengthIsKnown = false };

        h.View.SetDataSource(source);
        Assert.False(h.View.IsLengthKnown);

        h.Settle();

        Assert.True(h.View.IsLengthKnown);
        Assert.Equal(0, h.View.DataLength);
        Assert.All(h.Rows, row => Assert.Equal(string.Empty, row));
    }

    [Fact]
    public void AnUnknownSourceIsDiscoveredPageByPageWithoutAFullScan()
    {
        using var h = new HexHarness(height: 4);
        long total = (6L * Page) + 5;
        var source = new SyntheticSource(total) { LengthIsKnown = false };

        h.View.SetDataSource(source);
        h.Settle();

        // The visible page and one page of read-ahead are known; nothing claims a total.
        Assert.False(h.View.IsLengthKnown);
        Assert.Equal(2L * Page, h.View.DataLength);
        Assert.True(source.BytesRead <= 2L * Page);

        // End goes to what is known, which asks for the next page; repeating it reaches the real end.
        for (int i = 0; i < 16 && !h.View.IsLengthKnown; i++)
        {
            h.Press(Keys.kbCtrlPgDn);
            h.Settle();
        }

        Assert.True(h.View.IsLengthKnown);
        Assert.Equal(total, h.View.DataLength);
        Assert.StartsWith(THexView.FormatOffset((total - 1) / 16 * 16, total), h.Rows.Last(r => r.Length > 0));
        Assert.True(source.BytesRead <= total);
        Assert.True(source.LargestRequest <= Page);
    }

    [Fact]
    public void AnUnknownSourceOpenedAtAFarOffsetKeepsThePosition()
    {
        using var h = new HexHarness(height: 4);
        var source = new SyntheticSource(10L * Page) { LengthIsKnown = false };

        h.View.SetDataSource(source, initialOffset: (3L * Page) + 7);
        Assert.Equal(3L * Page, h.View.TopOffset);

        h.Settle();
        Assert.Equal(3L * Page, h.View.TopOffset);
        Assert.StartsWith("00003000  00 01", h.Rows[0]);
        Assert.DoesNotContain(source.Reads, r => r.Offset == 0);        // no scan from the start
    }
}
