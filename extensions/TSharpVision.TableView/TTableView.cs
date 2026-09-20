using System.Diagnostics.CodeAnalysis;
using TSharpVision.Constants;

namespace TSharpVision.TableView;

/// <summary>
/// A read-only table (grid) view: a column header, rows of cells, a current cell, and paged reads from any
/// <see cref="ITableDataSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Paged, never whole.</b> Rows are read in blocks of at most <see cref="RowsPerRead"/>, only for the rows on
/// screen, and kept in a bounded least-recently-used set. Nothing is sized by the row count, so a table of ten rows,
/// of a hundred million rows and of unknown length are browsed the same way. Row positions are 64-bit throughout and
/// only become <see cref="int"/> when mapped to a screen line.
/// </para>
/// <para>
/// <b>Drawing never reads.</b> <see cref="Draw"/> is synchronous and only looks at blocks already cached; a missing
/// block is drawn as "Loading…". Blocks are requested when the position, the size or the source changes — never from
/// <see cref="Draw"/> — and read on a thread-pool thread; the result comes back through <see cref="TView.Post"/> and is
/// applied on the event-loop thread, which then redraws.
/// </para>
/// <para>
/// <b>The last navigation wins.</b> Every request carries the data-source generation and a request id. Replacing the
/// source, shutting down, or moving so far that a pending block is no longer near the visible rows cancels it; a
/// result that still arrives afterwards — from a source that ignored the cancellation — is discarded rather than
/// shown. A block that fails is shown as an error line and reported through <see cref="LastError"/>; blocks already
/// loaded stay usable, and <see cref="RetryFailedBlocks"/> asks again.
/// </para>
/// <para>
/// <b>Keys.</b> Up/Down move one row, PgUp/PgDn one screen, Ctrl+PgUp/Ctrl+PgDn to the first/last row; Left/Right
/// move one column, Home/End to the first/last column; Ctrl+Home goes to the first cell and Ctrl+End to the last.
/// For a table of unknown length "the last row" is the furthest one reachable now — one block past the rows found so
/// far — and each press reaches further until the end is found. The view scrolls to keep the current cell visible.
/// </para>
/// <para>
/// <b>Read-only.</b> There is no editing, sorting or filtering here: a view that can only see a few hundred rows of a
/// lazy table cannot sort or filter it truthfully. Those belong to the source.
/// </para>
/// </remarks>
public class TTableView : TView
{
    /// <summary>The most rows the view asks a source for in one read, and the size of one cached block.</summary>
    public const int RowsPerRead = 64;

    /// <summary>The narrowest a column is laid out, in cells.</summary>
    public const int MinColumnWidth = 4;

    /// <summary>The widest a column is laid out, in cells; longer text is clipped with an ellipsis.</summary>
    public const int MaxColumnWidth = 40;

    /// <summary>Screen lines above the rows: the header and the line under it.</summary>
    public const int HeaderLines = 2;

    /// <summary>Glyph between columns. Defaults to the frame's vertical line.</summary>
    public static char ColumnSeparator = TSharpVisionGlyphs.FrameVertical;

    /// <summary>Glyph of the line under the header. Defaults to the frame's horizontal line.</summary>
    public static char HeaderLine = TSharpVisionGlyphs.FrameHorizontal;

    /// <summary>Glyph where the line under the header meets a column separator.</summary>
    public static char HeaderCrossing = '┼';

    // Delivered only to this view through TView.Post; the payload type identifies it.
    internal const ushort cmBlockLoaded = 0xFE11;

    private const int WheelRows = 3;
    private const int ScrollBarRange = 1_000_000;
    private const int MaxErrorLength = 256;

    // 1 normal, 2 header, 3 current row, 4 current cell (focused), 5 current cell (not focused), 6 NULL/special,
    // 7 error, 8 separators and row header, 9 loading, 10 muted row. Mapped onto the owner window's scroller and
    // frame entries; a host that wants other colours overrides GetPalette. Meaning never rests on colour alone: the
    // text says NULL, !, Loading…, and a row's label carries its state.
    private static readonly TPalette Palette = new("\x06\x02\x06\x07\x07\x03\x02\x01\x01\x01", 10);

    /// <summary>The widest row header, in cells.</summary>
    public const int MaxRowHeaderWidth = 16;

    private readonly TablePageCache _cache;
    private readonly int _blockRows;
    private readonly Dictionary<long, PendingRead> _pending = new();

    private TScrollBar? _hScrollBar;
    private TScrollBar? _vScrollBar;
    private ITableDataSource? _source;
    private TableColumn[] _columns = Array.Empty<TableColumn>();
    private int[] _widths = Array.Empty<int>();
    private bool _widthsSampled;
    private CancellationTokenSource _lifetime = new();
    private int _generation;
    private long _nextRequestId;
    private long _row;
    private int _column;
    private long _top;
    private int _left;
    private long _observedEnd = long.MaxValue;
    private long _discovered;
    private long _explorationBase;
    private bool _updatingScrollBars;
    private bool _vScaled;
    private long _vMax;
    private int _rowHeaderWidth;

    /// <summary>Creates an empty view; call <see cref="SetDataSource(ITableDataSource?, long, int)"/> to show a table.</summary>
    /// <param name="bounds">Owner-relative bounds.</param>
    /// <param name="horizontalScrollBar">Optional scroll bar kept in step with the current column.</param>
    /// <param name="verticalScrollBar">Optional scroll bar kept in step with the current row.</param>
    public TTableView(TRect bounds, TScrollBar? horizontalScrollBar, TScrollBar? verticalScrollBar)
        : this(bounds, horizontalScrollBar, verticalScrollBar, TablePageCache.DefaultCapacity, RowsPerRead)
    {
    }

    internal TTableView(TRect bounds, TScrollBar? horizontalScrollBar, TScrollBar? verticalScrollBar, int cacheCapacity, int blockRows)
        : base(bounds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(blockRows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(blockRows, RowsPerRead);
        _hScrollBar = horizontalScrollBar;
        _vScrollBar = verticalScrollBar;
        _cache = new TablePageCache(cacheCapacity);
        _blockRows = blockRows;
        options |= (ushort)(Views.ofSelectable | Views.ofFirstClick);
        eventMask |= (ushort)(Events.evBroadcast | Events.evMouseWheel);
        growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
    }

    /// <summary>Gets the table being shown, or null.</summary>
    public ITableDataSource? DataSource => _source;

    /// <summary>Gets the number of columns shown.</summary>
    public int ColumnCount => _columns.Length;

    /// <summary>
    /// Gets the zero-based position of the current row, or -1 when there is none: no source, no columns, or a table
    /// known to be empty. On a table of unknown length it may be a row still being loaded.
    /// </summary>
    public long CurrentRow => _row;

    /// <summary>Gets the zero-based index of the current column, or -1 when the table has no columns.</summary>
    public int CurrentColumn => _column;

    /// <summary>Gets the position of the first row on screen.</summary>
    public long TopRow => _top;

    /// <summary>Gets the index of the first column on screen.</summary>
    public int LeftColumn => _left;

    /// <summary>Gets how many rows fit under the header at the current height.</summary>
    public int VisibleRowCount => Math.Max(0, size.y - HeaderLines);

    /// <summary>
    /// Gets the number of rows when it is known — declared by the source, or found by a read that reached the end
    /// (the smaller of the two) — or null while it is not. Never estimated.
    /// </summary>
    public long? RowCount
    {
        get
        {
            if (_source is null) return 0;
            long? declared = DeclaredCount;
            if (declared is null && _observedEnd == long.MaxValue) return null;
            return Math.Min(declared ?? long.MaxValue, _observedEnd);
        }
    }

    /// <summary>Gets whether <see cref="RowCount"/> is the total.</summary>
    public bool IsRowCountKnown => RowCount.HasValue;

    /// <summary>Gets how many rows are known to exist: the rows up to the end of the furthest block read so far.</summary>
    public long KnownRowCount => _discovered;

    /// <summary>Gets the message of the most recent failed read, or null.</summary>
    public string? LastError { get; private set; }

    /// <summary>Gets whether any read is outstanding.</summary>
    public bool IsLoading => _pending.Count > 0;

    /// <summary>Gets the current cell and scroll position, to be handed back to <see cref="SetDataSource(ITableDataSource?, TableViewPosition)"/>.</summary>
    public TableViewPosition Position => new(_row, _column, _top, _left, _widths);

    /// <summary>
    /// Gets or sets the width of the row header — a column on the left, outside horizontal scrolling, showing each
    /// row's <see cref="TableRow.Label"/>. 0 (the default) shows none; at most <see cref="MaxRowHeaderWidth"/>.
    /// </summary>
    public int RowHeaderWidth
    {
        get => _rowHeaderWidth;
        set
        {
            int width = Math.Clamp(value, 0, MaxRowHeaderWidth);
            if (width == _rowHeaderWidth) return;
            _rowHeaderWidth = width;
            Refresh();
        }
    }

    /// <summary>Where the first data column starts: after the row header and its separator.</summary>
    private int DataX => _rowHeaderWidth > 0 ? _rowHeaderWidth + 1 : 0;

    // Diagnostics for the paging tests.
    internal int CachedBlockCount => _cache.Count;

    internal int CacheCapacity => _cache.Capacity;

    internal int PendingBlockCount => _pending.Count;

    internal IEnumerable<long> CachedBlockIndices => _cache.Indices;

    internal IEnumerable<long> PendingBlockIndices => _pending.Keys;

    internal int BlockRows => _blockRows;

    /// <summary>
    /// Shows <paramref name="dataSource"/>, or nothing when null, with the given cell current. Every outstanding read
    /// for the previous source is cancelled and its result will be ignored. The view does not take ownership.
    /// </summary>
    /// <param name="dataSource">The table to show.</param>
    /// <param name="initialRow">The row to make current; clamped to what the table allows.</param>
    /// <param name="initialColumn">The column to make current; clamped to the columns there are.</param>
    public void SetDataSource(ITableDataSource? dataSource, long initialRow = 0, int initialColumn = 0)
        => SetDataSource(dataSource, new TableViewPosition(initialRow, initialColumn, 0, 0));

    /// <summary>
    /// Shows <paramref name="dataSource"/> at <paramref name="position"/> — the current cell and the scroll position an
    /// earlier <see cref="Position"/> reported — as far as the table and the view's size allow. Only the rows then on
    /// screen are read.
    /// </summary>
    /// <param name="dataSource">The table to show, or null.</param>
    /// <param name="position">Where to be.</param>
    public void SetDataSource(ITableDataSource? dataSource, TableViewPosition position)
    {
        long initialRow = position.Row;
        int initialColumn = position.Column;
        ResetReads();
        _source = dataSource;
        LastError = null;
        _observedEnd = long.MaxValue;
        _discovered = 0;
        _explorationBase = Math.Max(0, initialRow);
        _top = Math.Max(0, position.TopRow);
        _left = Math.Max(0, position.LeftColumn);
        _widthsSampled = false;

        _columns = dataSource?.Columns is { } columns ? columns.Select(c => c ?? new TableColumn(string.Empty)).ToArray() : Array.Empty<TableColumn>();
        _widths = new int[_columns.Length];
        for (int i = 0; i < _columns.Length; i++) _widths[i] = InitialWidth(_columns[i]);

        // A position handed back brings its layout: the columns keep the widths they had, so the scroll position means
        // what it meant. Widths are still clamped to the view's own bounds.
        if (position.ColumnWidths is { } widths && widths.Count == _widths.Length)
        {
            for (int i = 0; i < _widths.Length; i++) _widths[i] = Math.Clamp(widths[i], MinColumnWidth, MaxColumnWidth);
            _widthsSampled = true;
        }

        _row = Math.Max(0, initialRow);
        _column = Math.Max(0, initialColumn);
        Refresh();
    }

    /// <summary>Makes the given cell current, as far as the table allows, and scrolls it into view.</summary>
    /// <param name="row">Zero-based row position.</param>
    /// <param name="column">Zero-based column index.</param>
    public void MoveTo(long row, int column)
    {
        if (_source is null || _columns.Length == 0) return;

        _row = row;
        _column = column;
        Refresh();
    }

    /// <summary>Gets the width a column is laid out with, in cells.</summary>
    /// <param name="column">Zero-based column index.</param>
    public int GetColumnWidth(int column)
        => (uint)column < (uint)_widths.Length ? _widths[column] : throw new ArgumentOutOfRangeException(nameof(column));

    /// <summary>
    /// Gets a row if its block is loaded and the source supplied it. Never reads: an unloaded row is simply not
    /// available yet.
    /// </summary>
    /// <param name="row">Zero-based row position.</param>
    /// <param name="value">The row, when available.</param>
    public bool TryGetRow(long row, [NotNullWhen(true)] out TableRow? value)
    {
        value = null;
        if (_source is null || row < 0) return false;
        if (!_cache.TryGet(row / _blockRows, out TablePage page) || page.Error is not null) return false;

        long at = row - page.Start;
        if (at >= page.Rows.Length) return false;
        value = page.Rows[at];
        return value is not null;
    }

    /// <summary>Forgets every block that failed and asks for the visible ones again. Loaded blocks are kept.</summary>
    public void RetryFailedBlocks()
    {
        foreach (long index in _cache.FailedIndices()) _cache.Remove(index);
        LastError = null;
        Refresh();
    }

    /// <inheritdoc />
    public override TPalette GetPalette() => Palette;

    /// <inheritdoc />
    public override void Draw()
    {
        if (size.x <= 0 || size.y <= 0) return;

        int width = size.x;
        var cells = new TScreenChar[width];
        char[] text = new char[width];

        DrawHeader(cells, text, width);
        if (size.y > 1) DrawHeaderLine(cells, width);

        bool focused = (state & (Views.sfSelected | Views.sfActive)) == (Views.sfSelected | Views.sfActive);
        for (int y = HeaderLines; y < size.y; y++)
            DrawRow(cells, text, width, y, _top + (y - HeaderLines), focused);
    }

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);

        switch (ev.What)
        {
            case Events.evCommand when ev.message.command == cmBlockLoaded && ev.message.infoPtr is TableBlockResult result:
                ApplyBlockResult(result);
                ClearEvent(ref ev);
                return;

            case Events.evBroadcast when ev.message.command == Views.cmScrollBarChanged && !_updatingScrollBars:
                if (_vScrollBar is not null && ReferenceEquals(ev.message.infoPtr, _vScrollBar)) VerticalBarMoved();
                else if (_hScrollBar is not null && ReferenceEquals(ev.message.infoPtr, _hScrollBar)) MoveTo(_row, _hScrollBar.value);
                return;

            case Events.evBroadcast when ev.message.command == Views.cmScrollBarClicked
                                         && (ReferenceEquals(ev.message.infoPtr, _vScrollBar) || ReferenceEquals(ev.message.infoPtr, _hScrollBar)):
                Select();
                return;

            case Events.evMouseWheel:
            {
                bool up = (ev.mouse.eventFlags & Events.meWheelUp) != 0;
                bool down = (ev.mouse.eventFlags & Events.meWheelDown) != 0;
                if (!up && !down) return;
                MoveTo(_row + (up ? -WheelRows : WheelRows), _column);
                ClearEvent(ref ev);
                return;
            }

            case Events.evMouseDown:
                Click(MakeLocal(ev.mouse.where));
                ClearEvent(ref ev);
                return;

            case Events.evKeyDown:
                if (HandleKey(ev.keyDown.keyCode)) ClearEvent(ref ev);
                return;
        }
    }

    /// <inheritdoc />
    public override void ChangeBounds(TRect bounds)
    {
        SetBounds(bounds);
        Refresh();
    }

    /// <inheritdoc />
    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if ((aState & (Views.sfActive | Views.sfSelected)) == 0) return;

        bool show = GetState((ushort)(Views.sfActive | Views.sfSelected));
        foreach (TScrollBar? bar in new[] { _hScrollBar, _vScrollBar })
        {
            if (bar is null) continue;
            if (show) bar.Show();
            else bar.Hide();
        }

        DrawView();
    }

    /// <inheritdoc />
    public override void ShutDown()
    {
        ResetReads();
        _source = null;
        _hScrollBar = null;
        _vScrollBar = null;
        base.ShutDown();
    }

    /// <summary>
    /// Called on the event-loop thread after the current cell, the scroll position, the row count or the read state
    /// changed, so an owner can refresh whatever describes the position (for example "Row 42 / 1200").
    /// </summary>
    protected virtual void OnViewChanged()
    {
    }

    /// <summary>
    /// Starts one block read. The default runs it on the thread pool, so a source that does synchronous work before
    /// its first await still cannot hold up the event loop; an override may use another scheduler.
    /// </summary>
    /// <param name="read">The read, which posts its own result back to the view.</param>
    /// <returns>The started work; the view does not wait for it.</returns>
    protected virtual Task StartRead(Func<Task> read) => Task.Run(read);

    // ── navigation ───────────────────────────────────────────────────────────

    private bool HandleKey(ushort keyCode)
    {
        if (_source is null || _columns.Length == 0) return false;

        long page = Math.Max(1, VisibleRowCount);
        switch (keyCode)
        {
            case Keys.kbUp: MoveTo(_row - 1, _column); return true;
            case Keys.kbDown: MoveTo(_row + 1, _column); return true;
            case Keys.kbPgUp: Page(-page); return true;
            case Keys.kbPgDn: Page(page); return true;
            case Keys.kbLeft: MoveTo(_row, _column - 1); return true;
            case Keys.kbRight: MoveTo(_row, _column + 1); return true;
            case Keys.kbHome: MoveTo(_row, 0); return true;
            case Keys.kbEnd: MoveTo(_row, _columns.Length - 1); return true;
            case Keys.kbCtrlPgUp: MoveTo(0, _column); return true;
            case Keys.kbCtrlPgDn: MoveTo(LastReachableRow(), _column); return true;
            case Keys.kbCtrlHome: MoveTo(0, 0); return true;
            case Keys.kbCtrlEnd: MoveTo(LastReachableRow(), _columns.Length - 1); return true;
            default: return false;
        }
    }

    /// <summary>Moves the current row and the top row together, so the current row keeps its place on screen.</summary>
    private void Page(long delta)
    {
        long offset = _row - _top;
        _row = Saturate(_row, delta);
        _top = Saturate(_top, delta);
        Clamp();
        _top = Math.Max(0, _row - Math.Clamp(offset, 0, Math.Max(0, VisibleRowCount - 1)));
        Refresh();
    }

    private void Click(TPoint local)
    {
        if (_source is null || _columns.Length == 0) return;

        int column = _column;
        foreach ((int index, int x, int w) in VisibleColumns(size.x))
        {
            if (local.x >= x && local.x <= x + w)
            {
                column = index;
                break;
            }
        }

        long row = local.y >= HeaderLines && local.y < size.y ? _top + (local.y - HeaderLines) : _row;
        MoveTo(row, column);
    }

    private void VerticalBarMoved()
    {
        if (_vScrollBar is null) return;
        long row = _vScaled ? (long)(_vScrollBar.value * (double)_vMax / ScrollBarRange) : _vScrollBar.value;
        MoveTo(row, _column);
    }

    /// <summary>Clamps the position, scrolls the current cell into view, reconciles reads, scroll bars and screen.</summary>
    private void Refresh()
    {
        Clamp();
        ScrollIntoView();
        _cache.Fit(VisibleBlocksSpan() + 2);
        CancelPendingOutsideVisibleWindow();
        RequestVisibleBlocks();
        UpdateScrollBars();
        DrawView();
        OnViewChanged();
    }

    private long? DeclaredCount => _source?.RowCount is long declared && declared >= 0 ? declared : null;

    /// <summary>One past the last row the cursor may reach now.</summary>
    private long NavigationEnd()
    {
        if (RowCount is long known) return known;

        // Unknown length: one block past what is known (or past where the view was opened). Nothing assumes a total
        // the source has not reported; reading further is what finds the end.
        long reachable = Math.Max(_discovered, _explorationBase);
        return reachable > long.MaxValue - _blockRows ? long.MaxValue : reachable + _blockRows;
    }

    private long LastReachableRow() => Math.Max(0, NavigationEnd() - 1);

    private void Clamp()
    {
        if (_source is null || _columns.Length == 0)
        {
            _row = -1;
            _column = -1;
            _top = 0;
            _left = 0;
            return;
        }

        _column = Math.Clamp(_column, 0, _columns.Length - 1);
        _left = Math.Clamp(_left, 0, _columns.Length - 1);

        long end = NavigationEnd();
        if (end <= 0)
        {
            _row = -1;
            _top = 0;
            return;
        }

        _row = Math.Clamp(_row, 0, end - 1);
        long maxTop = RowCount is long known ? Math.Max(0, known - Math.Max(1, VisibleRowCount)) : long.MaxValue;
        _top = Math.Clamp(_top, 0, maxTop);
    }

    private void ScrollIntoView()
    {
        if (_row >= 0)
        {
            int rows = Math.Max(1, VisibleRowCount);
            if (_row < _top) _top = _row;
            else if (_row - _top >= rows) _top = _row - rows + 1;
        }

        if (_column < 0) return;
        if (_column < _left)
        {
            _left = _column;
            return;
        }

        // Bring the current column's right edge inside the view, dropping columns on the left one at a time; a column
        // wider than the view is shown from its start.
        long right = 0;
        for (int c = _left; c <= _column; c++) right += _widths[c] + 1;
        while (_left < _column && right - 1 > size.x - DataX)
        {
            right -= _widths[_left] + 1;
            _left++;
        }
    }

    private static long Saturate(long value, long delta)
    {
        long result = value + delta;
        if (delta > 0 && result < value) return long.MaxValue;
        if (delta < 0 && result > value) return 0;
        return result;
    }

    // ── scroll bars ─────────────────────────────────────────────────────────

    private void UpdateScrollBars()
    {
        _updatingScrollBars = true;
        try
        {
            if (_vScrollBar is not null)
            {
                // Known count: the bar maps to rows. Unknown count: its range is the rows reachable now, and grows as
                // rows are found — never a made-up total.
                long max = Math.Max(0, NavigationEnd() - 1);
                long value = Math.Max(0, _row);
                _vMax = max;
                _vScaled = max > ScrollBarRange;
                int barValue = _vScaled ? (int)(value * (double)ScrollBarRange / max) : (int)value;
                int barMax = _vScaled ? ScrollBarRange : (int)max;
                _vScrollBar.SetParams(barValue, 0, barMax, Math.Max(1, VisibleRowCount - 1), 1);
            }

            if (_hScrollBar is not null)
            {
                int visible = Math.Max(1, VisibleColumns(size.x).Count());
                _hScrollBar.SetParams(Math.Max(0, _column), 0, Math.Max(0, _columns.Length - 1), visible, 1);
            }
        }
        finally
        {
            _updatingScrollBars = false;
        }
    }

    // ── paging ───────────────────────────────────────────────────────────────

    private (long First, long Last) VisibleBlocks()
    {
        long first = _top / _blockRows;
        long lastRow = Math.Max(_top, Saturate(_top, Math.Max(1, VisibleRowCount)) - 1);
        return (first, lastRow / _blockRows);
    }

    private int VisibleBlocksSpan() => (int)Math.Min(int.MaxValue - 2, (Math.Max(1, VisibleRowCount) / _blockRows) + 2);

    private void RequestVisibleBlocks()
    {
        if (_source is null || _columns.Length == 0 || VisibleRowCount == 0) return;

        long end = RowCount ?? long.MaxValue;
        (long first, long last) = VisibleBlocks();
        for (long block = first; block <= last; block++)
        {
            if (block * _blockRows >= end) break;
            RequestBlock(block, end);
        }
    }

    private void RequestBlock(long index, long end)
    {
        ITableDataSource? source = _source;
        if (source is null || _cache.Contains(index) || _pending.ContainsKey(index)) return;

        long start = index * _blockRows;
        int count = (int)Math.Min(_blockRows, end - start);
        if (count <= 0) return;

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var pending = new PendingRead(++_nextRequestId, cancellation);
        _pending[index] = pending;

        int generation = _generation;
        CancellationToken token = cancellation.Token;
        _ = StartRead(() => ReadBlockAsync(source, index, start, count, generation, pending.Id, token));
    }

    private async Task ReadBlockAsync(
        ITableDataSource source, long index, long start, int count, int generation, long requestId, CancellationToken token)
    {
        TablePage page;
        try
        {
            token.ThrowIfCancellationRequested();
            TableRowBlock? block = await source.ReadRowsAsync(start, count, token).ConfigureAwait(false);

            if (block is null)
            {
                page = Failed(index, start, count, "The table returned no rows object.");
            }
            else if (block.Start != start)
            {
                page = Failed(index, start, count, $"The table answered for row {block.Start:N0} instead of {start:N0}.");
            }
            else
            {
                // Never more than was asked for, whatever the source returned.
                int returned = Math.Min(count, block.Rows?.Count ?? 0);
                var rows = new TableRow?[returned];
                for (int i = 0; i < returned; i++) rows[i] = block.Rows![i];
                page = new TablePage(index, start, count, rows, block.ReachedEnd, error: null);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            page = Failed(index, start, count, ex.Message);
        }

        // A request cancelled while its read was finishing is not wanted any more; the check on the event-loop thread
        // would discard it too, but there is no reason to queue it.
        if (token.IsCancellationRequested) return;

        Post(cmBlockLoaded, new TableBlockResult(generation, requestId, page));
    }

    private static TablePage Failed(long index, long start, int count, string? message)
    {
        string text = string.IsNullOrWhiteSpace(message) ? "The rows could not be read." : message;
        if (text.Length > MaxErrorLength) text = text[..MaxErrorLength] + "…";
        return new TablePage(index, start, count, Array.Empty<TableRow?>(), reachedEnd: false, text);
    }

    /// <summary>Applies a loaded block on the event-loop thread. Returns whether it was accepted.</summary>
    internal bool ApplyBlockResult(TableBlockResult result)
    {
        if (result.Generation != _generation) return false;
        if (!_pending.TryGetValue(result.Page.Index, out PendingRead? pending) || pending.Id != result.RequestId)
            return false;

        _pending.Remove(result.Page.Index);
        TablePage page = result.Page;
        _cache.Add(page);

        if (page.Error is not null)
        {
            LastError = page.Error;
        }
        else
        {
            // A short block that does not claim the end still says rows exist up to what was asked for.
            long returnedEnd = page.Start + page.Rows.Length;
            _discovered = Math.Max(_discovered, page.ReachedEnd ? returnedEnd : page.Start + page.Requested);
            if (page.ReachedEnd) _observedEnd = Math.Min(_observedEnd, returnedEnd);
            if (!_widthsSampled) SampleWidths(page);
        }

        Refresh();
        return true;
    }

    private void CancelPendingOutsideVisibleWindow()
    {
        if (_pending.Count == 0) return;

        (long first, long last) = VisibleBlocks();
        List<long>? stale = null;
        foreach (KeyValuePair<long, PendingRead> entry in _pending)
            if (entry.Key < first - 1 || entry.Key > last + 1 || _columns.Length == 0)
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
        // Cancelling the lifetime source cancels every linked request token. The sources are not disposed: a data
        // source may still be registering on a token, and nothing here holds a timer or a wait handle.
        _lifetime.Cancel();
        _lifetime = new CancellationTokenSource();
        _pending.Clear();
        _cache.Clear();
        _generation++;
    }

    // ── layout ──────────────────────────────────────────────────────────────

    private static int InitialWidth(TableColumn column)
    {
        int width = Math.Max(column.Title.Length, column.PreferredWidth ?? 0);
        return Math.Clamp(width, MinColumnWidth, MaxColumnWidth);
    }

    /// <summary>Widens columns once, from the first block that arrives — a bounded sample, never a scan of the table.</summary>
    private void SampleWidths(TablePage page)
    {
        _widthsSampled = true;
        foreach (TableRow? row in page.Rows)
        {
            if (row is null) continue;
            int cells = Math.Min(row.Cells.Count, _widths.Length);
            for (int c = 0; c < cells; c++)
            {
                if (_columns[c].PreferredWidth is not null) continue;
                _widths[c] = Math.Clamp(Math.Max(_widths[c], row.Cells[c].Text.Length), MinColumnWidth, MaxColumnWidth);
            }
        }
    }

    /// <summary>The columns on screen from <see cref="LeftColumn"/>: index, start and visible width (clipped at the edge).</summary>
    private IEnumerable<(int Index, int X, int Width)> VisibleColumns(int width)
    {
        int x = DataX;
        for (int c = Math.Max(0, _left); c < _columns.Length && x < width; c++)
        {
            int w = Math.Min(_widths[c], width - x);
            yield return (c, x, w);
            x += _widths[c] + 1;
        }
    }

    // ── drawing ─────────────────────────────────────────────────────────────

    private void DrawHeader(TScreenChar[] cells, char[] text, int width)
    {
        ushort header = GetColor(2);
        ushort separator = GetColor(8);
        var buffer = new TDrawBuffer(cells);
        buffer.moveChar(0, ' ', header, width);

        if (_source is null)
        {
            WriteLine(0, 0, size.x, 1, buffer);
            return;
        }

        if (_columns.Length == 0)
        {
            buffer.moveStr(0, Clip(TSharpVisionIntl.Get("Table_NoColumns", "<no columns>"), width), GetColor(6));
            WriteLine(0, 0, size.x, 1, buffer);
            return;
        }

        if (DataX > 0 && DataX - 1 < width) buffer.moveChar(DataX - 1, ColumnSeparator, separator, 1);
        foreach ((int index, int x, int w) in VisibleColumns(width))
        {
            PutCell(buffer, text, x, w, _columns[index].Title, TableAlignment.Left, header);
            if (x + w < width) buffer.moveChar(x + w, ColumnSeparator, separator, 1);
        }

        WriteLine(0, 0, size.x, 1, buffer);
    }

    private void DrawHeaderLine(TScreenChar[] cells, int width)
    {
        ushort separator = GetColor(8);
        var buffer = new TDrawBuffer(cells);
        buffer.moveChar(0, HeaderLine, separator, width);
        if (DataX > 0 && DataX - 1 < width) buffer.moveChar(DataX - 1, HeaderCrossing, separator, 1);
        foreach ((_, int x, int w) in VisibleColumns(width))
            if (x + w < width) buffer.moveChar(x + w, HeaderCrossing, separator, 1);

        WriteLine(0, 1, size.x, 1, buffer);
    }

    private void DrawRow(TScreenChar[] cells, char[] text, int width, int y, long row, bool focused)
    {
        var buffer = new TDrawBuffer(cells);
        ushort normal = GetColor(1);
        buffer.moveChar(0, ' ', normal, width);

        if (_source is null || _columns.Length == 0 || row < 0)
        {
            WriteLine(0, y, size.x, 1, buffer);
            return;
        }

        long? count = RowCount;
        if (count is long known && row >= known)
        {
            if (known == 0 && row == 0)
                buffer.moveStr(0, Clip(TSharpVisionIntl.Get("List_Empty", "<empty>"), width), GetColor(6));
            WriteLine(0, y, size.x, 1, buffer);
            return;
        }

        bool current = row == _row;
        ushort rowColor = current ? GetColor(3) : normal;
        TableRow? loaded = null;
        ushort cursor = GetColor((ushort)(focused ? 4 : 5));

        string? line = null;
        ushort lineColor = rowColor;
        TableRow? data = null;
        if (!_cache.TryGet(row / _blockRows, out TablePage page))
        {
            line = TSharpVisionIntl.Get("Table_Loading", "Loading…");
            lineColor = GetColor(9);
        }
        else if (page.Error is not null)
        {
            line = "! " + page.Error;
            lineColor = GetColor(7);
        }
        else
        {
            long at = row - page.Start;
            data = at < page.Rows.Length ? page.Rows[at] : null;
            loaded = data;
            if (data is { Role: TableRowRole.Muted } && !current) rowColor = GetColor(10);
            if (data is null)
            {
                if (page.ReachedEnd)
                {
                    WriteLine(0, y, size.x, 1, buffer);
                    return;
                }

                line = TSharpVisionIntl.Get("Table_RowUnavailable", "! row not supplied");
                lineColor = GetColor(7);
            }
        }

        if (line is not null)
        {
            // A whole-row state: the current cell is still marked, so the position is visible while it loads.
            buffer.moveChar(0, ' ', lineColor, width);
            if (width > DataX) PutCell(buffer, text, DataX, width - DataX, line, TableAlignment.Left, lineColor);
            if (current)
                foreach ((int index, int x, int w) in VisibleColumns(width))
                    if (index == _column) PaintAttribute(buffer, x, w, cursor);

            DrawRowHeader(buffer, text, width, null);
            WriteLine(0, y, size.x, 1, buffer);
            return;
        }

        ushort separator = GetColor(8);
        foreach ((int index, int x, int w) in VisibleColumns(width))
        {
            TableCell cell = index < data!.Cells.Count ? data.Cells[index] : default;
            ushort color = current && index == _column
                ? cursor
                : cell.Role switch
                {
                    TableCellRole.Null or TableCellRole.Special => current ? rowColor : GetColor(6),
                    TableCellRole.Error => current ? rowColor : GetColor(7),
                    _ => rowColor,
                };

            PutCell(buffer, text, x, w, cell.Text, _columns[index].Alignment, color);
            if (x + w < width) buffer.moveChar(x + w, ColumnSeparator, separator, 1);
        }

        DrawRowHeader(buffer, text, width, loaded);
        WriteLine(0, y, size.x, 1, buffer);
    }

    /// <summary>The row header: the row's label (its identity and state mark), clipped, then a separator.</summary>
    private void DrawRowHeader(TDrawBuffer buffer, char[] text, int width, TableRow? row)
    {
        if (_rowHeaderWidth == 0) return;

        int w = Math.Min(_rowHeaderWidth, width);
        ushort color = row?.Role == TableRowRole.Warning ? GetColor(7) : GetColor(8);
        PutCell(buffer, text, 0, w, row?.Label ?? string.Empty, TableAlignment.Right, color);
        if (w < width) buffer.moveChar(w, ColumnSeparator, GetColor(8), 1);
    }

    /// <summary>
    /// Writes <paramref name="value"/> into <paramref name="width"/> cells at <paramref name="x"/>: control characters
    /// made visible, text longer than the cell cut with an ellipsis. Only the visible part is ever copied.
    /// </summary>
    private static void PutCell(TDrawBuffer buffer, char[] scratch, int x, int width, string value, TableAlignment alignment, ushort color)
    {
        if (width <= 0) return;

        bool longer = value.Length > width;
        int shown = longer ? Math.Max(0, width - 1) : value.Length;
        int start = alignment == TableAlignment.Right && !longer ? width - shown : 0;

        Span<char> span = scratch.AsSpan(0, width);
        span.Fill(' ');
        for (int i = 0; i < shown; i++)
        {
            char c = value[i];
            span[start + i] = char.IsControl(c) ? '·' : c;
        }

        if (longer) span[width - 1] = '…';
        buffer.moveBuf(x, span, color, width);
    }

    private static void PaintAttribute(TDrawBuffer buffer, int x, int width, ushort color)
    {
        for (int i = 0; i < width; i++) buffer.putAttribute(x + i, color);
    }

    private static string Clip(string text, int width) => text.Length > width ? text[..width] : text;

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

/// <summary>A finished block read on its way back to the event-loop thread.</summary>
internal sealed class TableBlockResult : IInfo
{
    public TableBlockResult(int generation, long requestId, TablePage page)
    {
        Generation = generation;
        RequestId = requestId;
        Page = page;
    }

    public int Generation { get; }

    public long RequestId { get; }

    public TablePage Page { get; }
}
