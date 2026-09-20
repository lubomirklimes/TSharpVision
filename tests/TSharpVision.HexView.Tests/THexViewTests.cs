using System.Reflection;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.HexView.Tests;

public sealed class THexViewTests
{
    private const int Page = HexPageCache.PageSize;

    private static byte Ascii(long offset) => (byte)('A' + (offset % 26));

    // ── layout ───────────────────────────────────────────────────────────────

    [Fact]
    public void AnEmptySourceDrawsNoRowsAndReadsNothing()
    {
        using var h = new HexHarness();
        var source = new SyntheticSource(0);

        h.View.SetDataSource(source);
        h.Settle();

        Assert.All(h.Rows, row => Assert.Equal(string.Empty, row));
        Assert.Empty(source.Reads);
        Assert.Equal(0, h.View.DataLength);
    }

    [Fact]
    public void ASourceShorterThanOneRowShowsOnlyItsBytesWithTheColumnsAligned()
    {
        using var h = new HexHarness();
        h.View.SetDataSource(new SyntheticSource(5, Ascii));
        h.Settle();

        Assert.Equal(16, h.View.BytesPerRow);
        string expected = "00000000  41 42 43 44 45" + new string(' ', 60 - 24) + "ABCDE";
        Assert.Equal(expected, h.Rows[0]);
        Assert.Equal(string.Empty, h.Rows[1]);
    }

    [Fact]
    public void MultipleRowsShowOffsetsHexAndPrintableCharacters()
    {
        using var h = new HexHarness();
        h.View.SetDataSource(new SyntheticSource(40));
        h.Settle();

        Assert.Equal("00000000  00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F  ................", h.Rows[0]);
        Assert.Equal("00000010  10 11 12 13 14 15 16 17  18 19 1A 1B 1C 1D 1E 1F  ................", h.Rows[1]);
        Assert.Equal("00000020  20 21 22 23 24 25 26 27" + new string(' ', 60 - 33) + " !\"#$%&'", h.Rows[2]);
        Assert.Equal(string.Empty, h.Rows[3]);
    }

    [Theory]
    [InlineData(200, 32)]
    [InlineData(120, 24)]
    [InlineData(80, 16)]
    [InlineData(60, 8)]
    [InlineData(40, 4)]
    [InlineData(10, 4)]
    public void BytesPerRowAdaptsToTheWidth(int width, int expected)
    {
        Assert.Equal(expected, THexView.GetBytesPerRow(width, 1000));
        if (expected > 4) Assert.True(THexView.GetRowWidth(expected, 1000) <= width);
    }

    [Theory]
    [InlineData(0L, 100L, "00000000")]
    [InlineData(0xFFFF_FFF0L, 0xFFFF_FFFFL, "FFFFFFF0")]
    [InlineData(0x1_0000_0000L, 0x2_0000_0000L, "0000000100000000")]
    [InlineData(0x7FFF_FFFF_FFFF_FFF0L, long.MaxValue, "7FFFFFFFFFFFFFF0")]
    public void OffsetsAreFormattedWith8Or16Digits(long offset, long length, string expected)
        => Assert.Equal(expected, THexView.FormatOffset(offset, length));

    [Fact]
    public void ANarrowViewNeverWritesOutsideItsBounds()
    {
        using var driver = new DriverScope(40, 6);
        TEventQueue.ClearPosted();
        TEventQueue.ClaimUiThread();
        var host = new TestGroup(new TRect(0, 0, 40, 6)) { buffer = new ScreenBuffer(40 * 6) };
        host.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        var left = new TStaticText(new TRect(0, 0, 10, 6), "LLLLLLLLLL");
        var right = new TStaticText(new TRect(22, 0, 40, 6), "RRRRRRRRRRRRRRRRRR");
        var view = new ScheduledHexView(new TRect(10, 0, 22, 6), null, HexPageCache.DefaultCapacity, work => work());
        host.Insert(left);
        host.Insert(right);
        host.Insert(view);
        view.SetDataSource(new SyntheticSource(1000));

        host.Redraw();
        HexHarness.Pump();
        host.Redraw();

        Span<TScreenChar> cells = host.buffer.Data;
        for (int x = 0; x < 10; x++) Assert.Equal('L', cells[x].Character);
        for (int x = 22; x < 40; x++) Assert.Equal('R', cells[x].Character);
        Assert.Equal('0', cells[10].Character);   // the view's own first cell is its offset column
    }

    [Fact]
    public void ResizingRedrawsWithTheNewRowWidthAndKeepsTheTopByteVisible()
    {
        using var h = new HexHarness(width: 140, height: 5);
        h.View.SetDataSource(new SyntheticSource(10_000));
        h.Settle();
        h.View.ScrollToOffset(24 * 10);
        h.Settle();
        Assert.Equal(24, h.View.BytesPerRow);
        Assert.Equal(240, h.View.TopOffset);

        h.View.ChangeBounds(new TRect(0, 0, 80, 5));
        h.Settle();

        Assert.Equal(16, h.View.BytesPerRow);
        Assert.Equal(240, h.View.TopOffset);                     // 240 is a multiple of 16 too
        Assert.StartsWith("000000F0  F0 F1", h.Rows[0]);

        h.View.ChangeBounds(new TRect(0, 0, 60, 5));
        h.Settle();
        Assert.Equal(8, h.View.BytesPerRow);
        Assert.StartsWith("000000F0  F0 F1 F2 F3 F4 F5 F6 F7  ", h.Rows[0]);
    }

    // ── paging ───────────────────────────────────────────────────────────────

    [Fact]
    public void ASourceOfExactlyOnePageIsReadWithOneRequest()
    {
        using var h = new HexHarness(height: 300);
        var source = new SyntheticSource(Page);
        h.View.SetDataSource(source);
        h.Settle();

        var read = Assert.Single(source.Reads);
        Assert.Equal((0L, Page), read);
        Assert.Equal(Page, h.View.DataLength);
    }

    [Fact]
    public void RowsCrossingAPageBoundaryShowBothPagesContinuously()
    {
        using var h = new HexHarness(height: 4);
        var source = new SyntheticSource(3 * Page, offset => (byte)((offset * 7) & 0xFF));
        h.View.SetDataSource(source);
        h.View.ScrollToOffset(Page - 32);
        h.Settle();

        Assert.Equal(Page - 32, h.View.TopOffset);
        Assert.Contains(source.Reads, r => r.Offset == 0);
        Assert.Contains(source.Reads, r => r.Offset == Page);

        // Row 2 is the first row of page 1; its first byte is (4096 * 7) & 0xFF.
        Assert.StartsWith("00001000  " + ((Page * 7) & 0xFF).ToString("X2"), h.Rows[2]);
        Assert.StartsWith("00000FF0  " + (((Page - 16) * 7) & 0xFF).ToString("X2"), h.Rows[1]);
    }

    [Fact]
    public void TheFinalShortPageIsReadForItsRemainderOnly()
    {
        using var h = new HexHarness(height: 4);
        var source = new SyntheticSource(Page + 10);
        h.View.SetDataSource(source);
        h.Press(Keys.kbCtrlPgDn);
        h.Settle();

        Assert.Contains((Page, 10), source.Reads);
        string last = h.Rows.Last(row => row.Length > 0);
        Assert.StartsWith("00001000  00 01 02 03 04 05 06 07  08 09", last);
    }

    [Fact]
    public void AShortReadIsTreatedAsTheRealEndOfTheData()
    {
        using var h = new HexHarness(height: 4);
        var source = new SyntheticSource(10_000) { RealEnd = 50 };
        h.View.SetDataSource(source);
        h.Settle();

        Assert.Equal(50, h.View.DataLength);
        h.Press(Keys.kbCtrlPgDn);
        h.Settle();
        Assert.Equal(0, h.View.TopOffset);                         // 4 rows × 16 already covers 50 bytes
        Assert.StartsWith("00000030  30 31", h.Rows[3]);
    }

    [Fact]
    public void ALogicalLengthBeyond4GiBIsNavigatedWith64BitOffsetsAndBoundedReads()
    {
        using var h = new HexHarness(height: 5);
        long length = 5L * 1024 * 1024 * 1024 + 3;                 // 5 GiB and a bit, never allocated
        var source = new SyntheticSource(length);
        h.View.SetDataSource(source);
        h.Settle();

        Assert.Equal(8, h.View.BytesPerRow);                       // 16 offset digits leave room for 8
        Assert.StartsWith("0000000000000000  00 01", h.Rows[0]);

        h.View.ScrollToOffset(0x1_0000_0000L + 0x20);
        h.Settle();
        Assert.Equal(0x1_0000_0020L, h.View.TopOffset);
        Assert.StartsWith("0000000100000020  20 21 22", h.Rows[0]);

        h.Press(Keys.kbCtrlPgDn);
        h.Settle();
        long lastRow = (length - 1) / 8 * 8;
        Assert.StartsWith(THexView.FormatOffset(lastRow, length) + "  " + ((lastRow & 0xFF)).ToString("X2"), h.Rows.Last(r => r.Length > 0));

        Assert.True(source.LargestRequest <= Page);
        Assert.True(source.BytesRead <= 8L * Page, $"read {source.BytesRead} bytes of a {length}-byte source");
    }

    [Fact]
    public void NavigatingALargeSourceNeverReadsItWhole()
    {
        using var h = new HexHarness(height: 20);
        long length = 1L << 30;
        var source = new SyntheticSource(length);
        h.View.SetDataSource(source);
        h.Settle();

        for (int i = 0; i < 50; i++) { h.Press(Keys.kbPgDn); h.Settle(); }
        h.Press(Keys.kbCtrlPgDn); h.Settle();
        for (int i = 0; i < 50; i++) { h.Press(Keys.kbPgUp); h.Settle(); }
        h.Press(Keys.kbCtrlPgUp); h.Settle();

        Assert.True(source.LargestRequest <= Page);
        Assert.True(source.BytesRead < 64L * Page, $"read {source.BytesRead} bytes");
    }

    [Fact]
    public void ScrollingRequestsExactlyThePagesThatBecomeVisible()
    {
        using var h = new HexHarness(height: 10);
        var source = new SyntheticSource(100 * Page);
        h.View.SetDataSource(source);
        h.Settle();
        Assert.Equal(new long[] { 0, Page }, source.Reads.Select(r => r.Offset).Order().ToArray());

        h.View.ScrollToOffset(50L * Page);
        h.Settle();

        long[] offsets = source.Reads.Select(r => r.Offset).Skip(2).Order().ToArray();
        Assert.Equal(new long[] { 50L * Page, 51L * Page }, offsets);
    }

    [Fact]
    public void TheCacheStaysBoundedWhileEveryPageIsVisited()
    {
        using var h = new HexHarness(height: 40, cacheCapacity: 8);
        var source = new SyntheticSource(200L * Page);
        h.View.SetDataSource(source);
        h.Settle();

        for (long page = 0; page < 200; page += 3)
        {
            h.View.ScrollToOffset(page * Page);
            h.Settle();
            Assert.True(h.View.CachedPageCount <= 8, $"{h.View.CachedPageCount} pages cached");
        }

        Assert.Equal(8, h.View.CacheCapacity);
    }

    // ── asynchronous reads ───────────────────────────────────────────────────

    [Fact]
    public void DrawingDoesNotWaitForAReadThatBlocks()
    {
        using var h = new HexHarness(inlineReads: false);
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var source = new BlockingSource(1000, entered, release);

        h.View.SetDataSource(source);
        h.Host.Redraw();                                            // would hang here if Draw waited

        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)), "the read never started");
        Assert.StartsWith("00000000", h.Rows[0]);                   // offsets drawn, bytes still blank
        Assert.DoesNotContain("00 01", h.Rows[0]);

        release.Set();
        Assert.True(source.Finished.Wait(TimeSpan.FromSeconds(30)));
        Assert.True(SpinWait.SpinUntil(() => TEventQueue.PostedCount > 0, TimeSpan.FromSeconds(30)));
        h.Settle();
        Assert.StartsWith("00000000  00 01 02", h.Rows[0]);
    }

    [Fact]
    public void AStaleReadCompletingAfterNavigationDoesNotReplaceTheNewerVisibleData()
    {
        using var h = new HexHarness(height: 4);
        var source = new GatedSource(1000L * Page);
        h.View.SetDataSource(source);
        h.Host.Redraw();

        GatedSource.Request first = source.Next();                  // page 0
        GatedSource.Request readAhead = source.Next();              // page 1
        Assert.Equal(0, first.Offset);

        h.View.ScrollToOffset(500L * Page);                         // far away: page 0 is no longer wanted
        Assert.True(first.Token.IsCancellationRequested);
        Assert.True(readAhead.Token.IsCancellationRequested);

        h.Host.Redraw();
        GatedSource.Request visible = source.Next();
        Assert.Equal(500L * Page, visible.Offset);
        visible.Complete();
        Assert.True(TEventQueue.PostedCount > 0, $"no post; pending={h.View.PendingPageCount} cached={h.View.CachedPageCount} task={visible.Completion.Task.Status}");
        HexHarness.Pump();

        // The stale read finishes late anyway.
        first.Complete();
        HexHarness.Pump();

        Assert.DoesNotContain(0L, h.View.CachedPageIndices);
        Assert.Contains(500L, h.View.CachedPageIndices);
        h.Host.Redraw();
        Assert.StartsWith("001F4000  00 01 02", h.Rows[0]);
    }

    [Fact]
    public void AResultForAReplacedSourceIsIgnored()
    {
        using var h = new HexHarness(height: 4);
        var old = new GatedSource(10 * Page);
        h.View.SetDataSource(old);
        h.Host.Redraw();
        GatedSource.Request pending = old.Next();

        var replacement = new SyntheticSource(10, offset => 0xEE);
        h.View.SetDataSource(replacement);
        Assert.True(pending.Token.IsCancellationRequested);

        var accepted = h.View.ApplyPageResult(new HexPageResult(-1, 1, new HexPage(0, new byte[] { 1 }, 1, false, null)));
        Assert.False(accepted);

        pending.Complete();
        h.Settle();
        Assert.StartsWith("00000000  EE EE", h.Rows[0]);
    }

    [Fact]
    public void ShuttingTheViewDownCancelsReadsAndALateResultChangesNothing()
    {
        using var h = new HexHarness(height: 4);
        var source = new GatedSource(10 * Page);
        h.View.SetDataSource(source);
        h.Host.Redraw();
        GatedSource.Request pending = source.Next();

        h.Host.Remove(h.View);
        h.View.ShutDown();
        Assert.True(pending.Token.IsCancellationRequested);
        Assert.Null(h.View.DataSource);

        pending.Completion.SetResult(pending.Count);                // the source ignored cancellation
        HexHarness.Pump();

        Assert.Equal(0, h.View.CachedPageCount);
        Assert.Equal(0, TEventQueue.PostedCount);
    }

    [Fact]
    public void AFailedPageIsShownAsUnreadableAndReported()
    {
        using var h = new HexHarness(height: 4);
        var source = new GatedSource(100);
        h.View.SetDataSource(source);
        h.Host.Redraw();

        GatedSource.Request request = source.Next();
        request.Completion.SetException(new IOException("sector not found"));
        h.Settle();

        Assert.Equal("sector not found", h.View.LastError);
        Assert.StartsWith("00000000  !! !!", h.Rows[0]);
        Assert.Equal(0, source.Outstanding);                         // not retried in a loop
    }

    // ── keyboard, scroll bar ─────────────────────────────────────────────────

    [Fact]
    public void KeysMoveByRowsPagesAndToEitherEnd()
    {
        using var h = new HexHarness(height: 5);
        h.View.SetDataSource(new SyntheticSource(1000));
        h.Settle();

        h.Press(Keys.kbDown);
        Assert.Equal(16, h.View.TopOffset);
        h.Press(Keys.kbPgDn);
        Assert.Equal(16 + (4 * 16), h.View.TopOffset);
        h.Press(Keys.kbUp);
        Assert.Equal(64, h.View.TopOffset);
        h.Press(Keys.kbCtrlPgDn);
        Assert.Equal(((1000 + 15) / 16 - 5) * 16, h.View.TopOffset);
        h.Press(Keys.kbPgDn);
        Assert.Equal(((1000 + 15) / 16 - 5) * 16, h.View.TopOffset); // clamped
        h.Press(Keys.kbHome);
        Assert.Equal(0, h.View.TopOffset);
    }

    [Fact]
    public void TheScrollBarFollowsAndDrivesTheViewEvenBeyondIntRange()
    {
        using var driver = new DriverScope(80, 10);
        TEventQueue.ClearPosted();
        var host = new TestGroup(new TRect(0, 0, 80, 10)) { buffer = new ScreenBuffer(800) };
        var bar = new TScrollBar(new TRect(79, 0, 80, 10));
        host.Insert(bar);
        var view = new ScheduledHexView(new TRect(0, 0, 79, 10), bar, HexPageCache.DefaultCapacity, _ => Task.CompletedTask);
        host.Insert(view);

        long length = 64L * 1024 * 1024 * 1024;                     // 64 GiB: more rows than an int scroll bar holds
        view.SetDataSource(new SyntheticSource(length));
        Assert.Equal(0, bar.value);

        bar.SetValue(bar.maxVal / 2);
        long half = length / 2;
        Assert.InRange(view.TopOffset, half - (half / 100), half + (half / 100));

        view.ScrollToOffset(length);
        Assert.Equal(bar.maxVal, bar.value);
    }

    [Fact]
    public void TheExtensionReferencesNoCommanderAssembly()
    {
        Assembly assembly = typeof(THexView).Assembly;
        string[] references = assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.DoesNotContain(references, name => name.StartsWith("TSharpCommander", StringComparison.Ordinal));
        Assert.Contains("TSharpVision", references);
        Assert.All(references, name => Assert.True(
            name == "TSharpVision" || name.StartsWith("System", StringComparison.Ordinal) || name == "netstandard",
            $"unexpected reference {name}"));
    }

    private sealed class BlockingSource : IHexDataSource
    {
        private readonly ManualResetEventSlim _entered;
        private readonly ManualResetEventSlim _release;

        public BlockingSource(long length, ManualResetEventSlim entered, ManualResetEventSlim release)
        {
            Length = length;
            _entered = entered;
            _release = release;
        }

        public long? Length { get; }

        public ManualResetEventSlim Finished { get; } = new();

        public ValueTask<int> ReadAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken)
        {
            _entered.Set();
            _release.Wait(TimeSpan.FromSeconds(60));                // synchronous blocking before returning
            int count = (int)Math.Clamp(Length!.Value - offset, 0, destination.Length);
            for (int i = 0; i < count; i++) destination.Span[i] = (byte)((offset + i) & 0xFF);
            Finished.Set();
            return ValueTask.FromResult(count);
        }
    }
}
