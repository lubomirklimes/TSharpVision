using System.Globalization;
using TSharpVision.Constants;

namespace TSharpVision.HexView;

/// <summary>
/// A read-only hexadecimal view: an offset column, the bytes in hexadecimal and their printable
/// characters, over any <see cref="IHexDataSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Paged, never whole.</b> The view reads fixed-size pages (at most 4 KiB per read) for the rows
/// it shows plus one page of read-ahead, and keeps a bounded least-recently-used set of them. Nothing
/// is ever sized by the source length, so a source larger than 2 GiB — or larger than memory — is
/// navigated exactly like a small one. Offsets are 64-bit throughout.
/// </para>
/// <para>
/// <b>Drawing never waits.</b> <see cref="Draw"/> is synchronous and only reads pages that are
/// already cached. A missing page is requested on a thread-pool thread and drawn blank until it
/// arrives; the loaded page comes back through <see cref="TView.Post"/> and is applied on the
/// event-loop thread, which then redraws.
/// </para>
/// <para>
/// <b>Stale results are rejected.</b> Every request carries the data-source generation and a request
/// id. Replacing the source, shutting the view down, or scrolling so far that a pending page is no
/// longer near the visible rows cancels that request, and a result that still arrives afterwards is
/// discarded rather than cached.
/// </para>
/// <para>
/// <b>Layout.</b> The number of bytes per row adapts to the width: the largest of 32, 24, 16, 8 or 4
/// that fits, grouped in eights. The offset column has 8 hexadecimal digits, or 16 once the source is
/// longer than 4 GiB.
/// </para>
/// <para>
/// <b>Read-only.</b> There is no cursor and no editing; nothing here writes to a source.
/// </para>
/// </remarks>
public class THexView : TView
{
    // Delivered only to this view through TView.Post; the payload type identifies it, so the value
    // cannot collide with a command the application routes.
    internal const ushort cmPageLoaded = 0xFE10;

    private const int WheelRows = 3;
    private const int ScrollBarRange = 1_000_000;
    private static readonly int[] RowWidths = { 32, 24, 16, 8, 4 };
    private static readonly TPalette Palette = new("\x06\x07", 2);

    private readonly HexPageCache _cache;
    private readonly Dictionary<long, PendingRead> _pending = new();

    private TScrollBar? _scrollBar;
    private IHexDataSource? _source;
    private CancellationTokenSource _lifetime = new();
    private int _generation;
    private long _nextRequestId;
    private long _top;
    private long _observedLength = long.MaxValue;
    private long _discoveredExtent;
    private bool _updatingScrollBar;
    private bool _scrollBarScaled;
    private long _scrollBarMaxRow;

    /// <summary>Creates an empty view; call <see cref="SetDataSource"/> to show bytes.</summary>
    /// <param name="bounds">Owner-relative bounds.</param>
    /// <param name="verticalScrollBar">Optional vertical scroll bar kept in step with the view.</param>
    public THexView(TRect bounds, TScrollBar? verticalScrollBar)
        : this(bounds, verticalScrollBar, HexPageCache.DefaultCapacity)
    {
    }

    internal THexView(TRect bounds, TScrollBar? verticalScrollBar, int cacheCapacity)
        : base(bounds)
    {
        _scrollBar = verticalScrollBar;
        _cache = new HexPageCache(cacheCapacity);
        options |= Views.ofSelectable;
        eventMask |= (ushort)(Events.evBroadcast | Events.evMouseWheel);
        growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
    }

    /// <summary>Gets the source being shown, or null.</summary>
    public IHexDataSource? DataSource => _source;

    /// <summary>
    /// Gets the number of bytes the view can currently show.
    /// </summary>
    /// <remarks>
    /// For a source of known length: that length, shortened to where a read found the real end of the data.
    /// For a source whose length is not known yet: the end of the bytes read so far, until a short read
    /// establishes the end — so it grows as the source is explored and never pretends to a total.
    /// </remarks>
    public long DataLength
    {
        get
        {
            if (_source is null) return 0;
            if (_source.Length is long declared) return Math.Max(0, Math.Min(declared, _observedLength));
            return _observedLength != long.MaxValue ? _observedLength : _discoveredExtent;
        }
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="DataLength"/> is the total: the source declared its length,
    /// or a read reached the end of the data. False while an unknown-length source is still being explored.
    /// </summary>
    public bool IsLengthKnown => _source is null || _source.Length.HasValue || _observedLength != long.MaxValue;

    /// <summary>Gets the offset of the first byte of the top row.</summary>
    public long TopOffset => _top;

    /// <summary>Gets how many bytes each row shows at the current width.</summary>
    public int BytesPerRow => GetBytesPerRow(size.x, DataLength);

    /// <summary>Gets the message of the most recent failed page read, or null.</summary>
    public string? LastError { get; private set; }

    // Diagnostics for the paging tests.
    internal int CachedPageCount => _cache.Count;

    internal int CacheCapacity => _cache.Capacity;

    internal int PendingPageCount => _pending.Count;

    internal IEnumerable<long> CachedPageIndices => _cache.Indices;

    internal IEnumerable<long> PendingPageIndices => _pending.Keys;

    /// <summary>
    /// Shows <paramref name="dataSource"/>, or nothing when null, scrolled to the row containing
    /// <paramref name="initialOffset"/>.
    /// </summary>
    /// <remarks>
    /// Every outstanding read for the previous source is cancelled and its result will be ignored.
    /// The view does not take ownership of either source.
    /// </remarks>
    public void SetDataSource(IHexDataSource? dataSource, long initialOffset = 0)
    {
        ResetReads();
        _source = dataSource;
        _top = 0;
        LastError = null;
        _observedLength = long.MaxValue;
        _discoveredExtent = 0;

        // Aligned first, then clamped: for a source of unknown length the clamp is relative to the
        // position asked for, so opening at a far offset is kept and reading starts there.
        _top = Align(initialOffset);
        _top = Clamp(_top);
        UpdateScrollBar();
        DrawView();
        OnViewChanged();
    }

    /// <summary>Scrolls so that the row containing <paramref name="offset"/> is the top row, as far as the data allows.</summary>
    public void ScrollToOffset(long offset) => SetTop(Align(offset));

    /// <summary>Gets the bytes per row that fit <paramref name="width"/> columns for a source of <paramref name="length"/> bytes.</summary>
    internal static int GetBytesPerRow(int width, long length)
    {
        foreach (int candidate in RowWidths)
            if (GetRowWidth(candidate, length) <= width)
                return candidate;

        return RowWidths[^1];
    }

    /// <summary>Gets the columns a row of <paramref name="bytesPerRow"/> bytes occupies for a source of <paramref name="length"/> bytes.</summary>
    internal static int GetRowWidth(int bytesPerRow, long length)
        => OffsetDigits(length) + 2 + HexAreaWidth(bytesPerRow) + 1 + bytesPerRow;

    /// <summary>
    /// Formats <paramref name="offset"/> the way the offset column shows it: 8 uppercase hexadecimal
    /// digits, or 16 when <paramref name="length"/> exceeds <c>0xFFFFFFFF</c>.
    /// </summary>
    internal static string FormatOffset(long offset, long length)
        => offset.ToString(OffsetDigits(length) == 16 ? "X16" : "X8", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override TPalette GetPalette() => Palette;

    /// <inheritdoc />
    public override void Draw()
    {
        int width = Math.Max(1, size.x);
        var cells = new TScreenChar[width];
        char[] text = new char[width];
        ushort color = GetColor(1);

        long length = DataLength;
        int bytesPerRow = GetBytesPerRow(size.x, length);
        int rows = Math.Max(0, size.y);

        for (int y = 0; y < rows; y++)
        {
            Array.Fill(text, ' ');
            long rowOffset = _top + ((long)y * bytesPerRow);
            if (_source is not null && rowOffset < length)
                FormatRow(text, rowOffset, bytesPerRow, length);

            var buffer = new TDrawBuffer(cells);
            buffer.moveBuf(0, text, color, width);
            WriteLine(0, y, size.x, 1, buffer);
        }

        RequestVisiblePages(ReadLimit, bytesPerRow, rows);
    }

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);

        switch (ev.What)
        {
            case Events.evCommand when ev.message.command == cmPageLoaded && ev.message.infoPtr is HexPageResult result:
                ApplyPageResult(result);
                ClearEvent(ref ev);
                return;

            case Events.evBroadcast when ev.message.command == Views.cmScrollBarChanged
                                         && _scrollBar is not null
                                         && ReferenceEquals(ev.message.infoPtr, _scrollBar):
                if (!_updatingScrollBar) ScrollBarMoved();
                return;

            case Events.evMouseWheel:
            {
                bool up = (ev.mouse.eventFlags & Events.meWheelUp) != 0;
                bool down = (ev.mouse.eventFlags & Events.meWheelDown) != 0;
                if (!up && !down) return;
                SetTop(_top + ((up ? -WheelRows : WheelRows) * (long)BytesPerRow));
                ClearEvent(ref ev);
                return;
            }

            case Events.evKeyDown:
                if (HandleKey(ev.keyDown.keyCode)) ClearEvent(ref ev);
                return;
        }
    }

    /// <inheritdoc />
    public override void ChangeBounds(TRect bounds)
    {
        SetBounds(bounds);
        _top = Clamp(Align(_top));
        UpdateScrollBar();
        DrawView();
        OnViewChanged();
    }

    /// <inheritdoc />
    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if ((aState & (Views.sfActive | Views.sfSelected)) != 0 && _scrollBar is not null)
        {
            if (GetState((ushort)(Views.sfActive | Views.sfSelected))) _scrollBar.Show();
            else _scrollBar.Hide();
        }
    }

    /// <inheritdoc />
    public override void ShutDown()
    {
        ResetReads();
        _source = null;
        _scrollBar = null;
        base.ShutDown();
    }

    /// <summary>
    /// Called on the event-loop thread after the top offset, the data length or the read error
    /// changed, so an owner can refresh whatever describes the position.
    /// </summary>
    protected virtual void OnViewChanged()
    {
    }

    /// <summary>
    /// Starts one page read. The default runs it on the thread pool, so a source that does synchronous
    /// work before its first await still cannot hold up drawing; an override may use another scheduler but
    /// must not run <paramref name="read"/> synchronously inside <see cref="Draw"/> unless the source is known
    /// to complete without blocking.
    /// </summary>
    /// <param name="read">The read, which posts its own result back to the view.</param>
    /// <returns>The started work; the view does not wait for it.</returns>
    protected virtual Task StartRead(Func<Task> read) => Task.Run(read);

    // ── keyboard and scrolling ───────────────────────────────────────────────

    private bool HandleKey(ushort keyCode)
    {
        long row = BytesPerRow;
        long page = Math.Max(1, size.y - 1) * row;

        switch (keyCode)
        {
            case Keys.kbUp: SetTop(_top - row); return true;
            case Keys.kbDown: SetTop(_top + row); return true;
            case Keys.kbPgUp: SetTop(_top - page); return true;
            case Keys.kbPgDn: SetTop(_top + page); return true;
            case Keys.kbHome:
            case Keys.kbCtrlHome:
            case Keys.kbCtrlPgUp: SetTop(0); return true;
            case Keys.kbEnd:
            case Keys.kbCtrlEnd:
            case Keys.kbCtrlPgDn: SetTop(MaxTop()); return true;
            default: return false;
        }
    }

    private void SetTop(long offset)
    {
        long top = Clamp(Align(offset));
        if (top == _top) return;

        _top = top;
        CancelPendingOutsideVisibleWindow();
        UpdateScrollBar();
        DrawView();
        OnViewChanged();
    }

    private long Align(long offset)
    {
        int row = BytesPerRow;
        return offset <= 0 ? 0 : offset - (offset % row);
    }

    private long Clamp(long offset) => Math.Clamp(offset, 0, MaxTop());

    /// <summary>How far reads may go: the data length when known, otherwise unbounded until a short read.</summary>
    private long ReadLimit => IsLengthKnown ? DataLength : long.MaxValue;

    private long MaxTop()
    {
        // An unknown-length source can be scrolled one page past what is known, which is what asks for the
        // next page; nothing assumes a total the source has not reported.
        long length = IsLengthKnown ? DataLength : Math.Max(_discoveredExtent, _top) + HexPageCache.PageSize;
        int row = BytesPerRow;
        long totalRows = (length + row - 1) / row;
        long maxRow = Math.Max(0, totalRows - Math.Max(1, size.y));
        return maxRow * row;
    }

    private void UpdateScrollBar()
    {
        if (_scrollBar is null) return;

        int row = BytesPerRow;
        long maxRow = MaxTop() / row;
        long topRow = _top / row;
        _scrollBarMaxRow = maxRow;
        _scrollBarScaled = maxRow > ScrollBarRange;

        int value = _scrollBarScaled ? (int)(topRow * (double)ScrollBarRange / maxRow) : (int)topRow;
        int max = _scrollBarScaled ? ScrollBarRange : (int)maxRow;

        _updatingScrollBar = true;
        try
        {
            _scrollBar.SetParams(value, 0, max, Math.Max(1, size.y - 1), 1);
        }
        finally
        {
            _updatingScrollBar = false;
        }
    }

    private void ScrollBarMoved()
    {
        if (_scrollBar is null) return;

        int row = BytesPerRow;
        long topRow = _scrollBarScaled
            ? (long)(_scrollBar.value * (double)_scrollBarMaxRow / ScrollBarRange)
            : _scrollBar.value;
        SetTop(topRow * row);
    }

    // ── paging ───────────────────────────────────────────────────────────────

    private void RequestVisiblePages(long limit, int bytesPerRow, int rows)
    {
        if (_source is null || limit <= 0) return;

        (long first, long last) = VisiblePages(limit, bytesPerRow, rows);
        for (long page = first; page <= last + 1; page++)
            RequestPage(page, limit);
    }

    private (long First, long Last) VisiblePages(long length, int bytesPerRow, int rows)
    {
        long end = Math.Min(length, _top + (Math.Max(1, rows) * (long)bytesPerRow));
        long first = _top / HexPageCache.PageSize;
        long last = Math.Max(first, (end - 1) / HexPageCache.PageSize);
        return (first, last);
    }

    private void RequestPage(long index, long length)
    {
        IHexDataSource? source = _source;
        if (source is null || _cache.Contains(index) || _pending.ContainsKey(index)) return;

        long start = index * HexPageCache.PageSize;
        if (start >= length) return;

        int wanted = (int)Math.Min(HexPageCache.PageSize, length - start);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var pending = new PendingRead(++_nextRequestId, cancellation);
        _pending[index] = pending;

        int generation = _generation;
        CancellationToken token = cancellation.Token;
        _ = StartRead(() => ReadPageAsync(source, index, wanted, generation, pending.Id, token));
    }

    private async Task ReadPageAsync(
        IHexDataSource source, long index, int wanted, int generation, long requestId, CancellationToken token)
    {
        HexPage page;
        try
        {
            token.ThrowIfCancellationRequested();
            byte[] data = new byte[wanted];
            int total = 0;
            long start = index * HexPageCache.PageSize;
            while (total < wanted)
            {
                int read = await source.ReadAsync(start + total, data.AsMemory(total, wanted - total), token)
                    .ConfigureAwait(false);
                if (read <= 0) break;
                total += Math.Min(read, wanted - total);
            }

            page = new HexPage(index, data, total, total < wanted, error: null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            page = new HexPage(index, Array.Empty<byte>(), 0, reachedEnd: false, ex);
        }

        // A request cancelled while its read was finishing is not wanted any more; the check on the
        // event-loop thread would discard it too, but there is no reason to queue it.
        if (token.IsCancellationRequested) return;

        Post(cmPageLoaded, new HexPageResult(generation, requestId, page));
    }

    /// <summary>Applies a loaded page on the event-loop thread. Returns whether it was accepted.</summary>
    internal bool ApplyPageResult(HexPageResult result)
    {
        if (result.Generation != _generation) return false;
        if (!_pending.TryGetValue(result.Page.Index, out PendingRead? pending) || pending.Id != result.RequestId)
            return false;

        _pending.Remove(result.Page.Index);
        HexPage page = result.Page;
        _cache.Add(page);
        if (page.Error is null) _discoveredExtent = Math.Max(_discoveredExtent, page.Offset + page.Count);

        if (page.Error is not null)
        {
            LastError = page.Error.Message;
        }
        else
        {
            if (page.ReachedEnd) _observedLength = Math.Min(_observedLength, page.Offset + page.Count);
            _top = Clamp(Align(_top));
            UpdateScrollBar();
        }

        DrawView();
        OnViewChanged();
        return true;
    }

    private void CancelPendingOutsideVisibleWindow()
    {
        if (_pending.Count == 0) return;

        (long first, long last) = VisiblePages(ReadLimit, BytesPerRow, size.y);

        List<long>? stale = null;
        foreach (KeyValuePair<long, PendingRead> entry in _pending)
            if (entry.Key < first - 1 || entry.Key > last + 1)
                (stale ??= new List<long>()).Add(entry.Key);

        if (stale is null) return;

        foreach (long index in stale)
        {
            _pending[index].Cancellation.Cancel();
            _pending.Remove(index);
        }
    }

    private void ResetReads()
    {
        // Cancelling the lifetime source cancels every linked request token. The sources are not
        // disposed: a data source may still be registering on a token, and nothing here holds a
        // timer or a wait handle that needs releasing.
        _lifetime.Cancel();
        _lifetime = new CancellationTokenSource();
        _pending.Clear();
        _cache.Clear();
        _generation++;
    }

    // ── row formatting ───────────────────────────────────────────────────────

    private void FormatRow(char[] text, long rowOffset, int bytesPerRow, long length)
    {
        int digits = OffsetDigits(length);
        string offset = rowOffset.ToString(digits == 16 ? "X16" : "X8", CultureInfo.InvariantCulture);
        Put(text, 0, offset);

        int hexStart = digits + 2;
        int asciiStart = hexStart + HexAreaWidth(bytesPerRow) + 1;

        for (int i = 0; i < bytesPerRow; i++)
        {
            long at = rowOffset + i;
            if (at >= length) break;

            int hex = hexStart + (i * 3) + (bytesPerRow >= 16 ? i / 8 : 0);
            int ascii = asciiStart + i;

            switch (TryGetByte(at, out byte value))
            {
                case ByteState.Available:
                    Put(text, hex, HexDigit(value >> 4));
                    Put(text, hex + 1, HexDigit(value & 0xF));
                    Put(text, ascii, value is >= 0x20 and < 0x7F ? (char)value : '.');
                    break;

                case ByteState.Failed:
                    Put(text, hex, '!');
                    Put(text, hex + 1, '!');
                    Put(text, ascii, '!');
                    break;
            }
        }
    }

    private ByteState TryGetByte(long offset, out byte value)
    {
        value = 0;
        long index = offset / HexPageCache.PageSize;
        if (!_cache.TryGet(index, out HexPage page)) return ByteState.Pending;
        if (page.Error is not null) return ByteState.Failed;

        int at = (int)(offset - page.Offset);
        if (at >= page.Count) return ByteState.Missing;

        value = page.Data[at];
        return ByteState.Available;
    }

    private static int OffsetDigits(long length) => length > 0xFFFF_FFFFL ? 16 : 8;

    private static int HexAreaWidth(int bytesPerRow)
        => (bytesPerRow * 3) + (bytesPerRow >= 16 ? (bytesPerRow / 8) - 1 : 0);

    private static char HexDigit(int nibble) => (char)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);

    private static void Put(char[] text, int at, char c)
    {
        if ((uint)at < (uint)text.Length) text[at] = c;
    }

    private static void Put(char[] text, int at, string s)
    {
        for (int i = 0; i < s.Length; i++) Put(text, at + i, s[i]);
    }

    private enum ByteState
    {
        Available,
        Pending,
        Missing,
        Failed,
    }

    private sealed class PendingRead
    {
        public PendingRead(long id, CancellationTokenSource cancellation)
        {
            Id = id;
            Cancellation = cancellation;
        }

        public long Id { get; }

        public CancellationTokenSource Cancellation { get; }
    }
}

/// <summary>A finished page read on its way back to the event-loop thread.</summary>
internal sealed class HexPageResult : IInfo
{
    public HexPageResult(int generation, long requestId, HexPage page)
    {
        Generation = generation;
        RequestId = requestId;
        Page = page;
    }

    public int Generation { get; }

    public long RequestId { get; }

    public HexPage Page { get; }
}
