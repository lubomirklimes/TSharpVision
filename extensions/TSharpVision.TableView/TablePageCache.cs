namespace TSharpVision.TableView;

/// <summary>One block of rows as a read produced it, or the reason it could not be read.</summary>
internal sealed class TablePage
{
    public TablePage(long index, long start, int requested, TableRow?[] rows, bool reachedEnd, string? error)
    {
        Index = index;
        Start = start;
        Requested = requested;
        Rows = rows;
        ReachedEnd = reachedEnd;
        Error = error;
    }

    /// <summary>Page number; the page starts at <c>Index * PageRows</c>.</summary>
    public long Index { get; }

    public long Start { get; }

    /// <summary>How many rows were asked for.</summary>
    public int Requested { get; }

    /// <summary>The rows returned, never more than <see cref="Requested"/>; a null entry is a row the source did not supply.</summary>
    public TableRow?[] Rows { get; }

    /// <summary>The source said no rows follow the last one returned.</summary>
    public bool ReachedEnd { get; }

    /// <summary>Why the page is unreadable, or null.</summary>
    public string? Error { get; }
}

/// <summary>
/// A bounded, least-recently-used set of row pages.
/// </summary>
/// <remarks>
/// Touched only on the event-loop thread: loaded pages reach it through a post, and drawing reads it. The bound is a
/// page count, independent of the row count, so browsing a table of a hundred million rows never holds more than
/// <see cref="Capacity"/> pages. The view may raise the capacity to what its height needs — never lower it below the
/// configured value, and never beyond what is visible plus a small margin.
/// </remarks>
internal sealed class TablePageCache
{
    /// <summary>
    /// Eight pages of 64 rows: several screens of a tall terminal, so moving back and forth near the current position
    /// re-reads nothing, while memory stays proportional to a few hundred rows whatever the table's size.
    /// </summary>
    public const int DefaultCapacity = 8;

    private readonly Dictionary<long, LinkedListNode<TablePage>> _map = new();
    private readonly LinkedList<TablePage> _lru = new();

    public TablePageCache(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Configured = capacity;
        Capacity = capacity;
    }

    /// <summary>The capacity asked for at construction; the floor of <see cref="Capacity"/>.</summary>
    public int Configured { get; }

    public int Capacity { get; private set; }

    public int Count => _map.Count;

    public IEnumerable<long> Indices => _map.Keys;

    /// <summary>Sets the capacity to at least <paramref name="needed"/> pages (never below the configured one), evicting if it shrank.</summary>
    public void Fit(int needed)
    {
        Capacity = Math.Max(Configured, needed);
        Trim();
    }

    public bool Contains(long index) => _map.ContainsKey(index);

    /// <summary>Returns a page and marks it most recently used.</summary>
    public bool TryGet(long index, out TablePage page)
    {
        if (_map.TryGetValue(index, out LinkedListNode<TablePage>? node))
        {
            if (!ReferenceEquals(_lru.First, node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
            }

            page = node.Value;
            return true;
        }

        page = null!;
        return false;
    }

    /// <summary>Adds or replaces a page, evicting the least recently used pages beyond the bound.</summary>
    public void Add(TablePage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        Remove(page.Index);
        _map[page.Index] = _lru.AddFirst(page);
        Trim();
    }

    public bool Remove(long index)
    {
        if (!_map.Remove(index, out LinkedListNode<TablePage>? node)) return false;
        _lru.Remove(node);
        return true;
    }

    public IReadOnlyList<long> FailedIndices()
    {
        var failed = new List<long>();
        foreach (TablePage page in _lru)
            if (page.Error is not null) failed.Add(page.Index);
        return failed;
    }

    public void Clear()
    {
        _map.Clear();
        _lru.Clear();
    }

    private void Trim()
    {
        while (_map.Count > Capacity && _lru.Last is { } oldest)
        {
            _lru.RemoveLast();
            _map.Remove(oldest.Value.Index);
        }
    }
}
