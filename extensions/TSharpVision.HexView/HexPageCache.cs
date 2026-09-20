namespace TSharpVision.HexView;

/// <summary>One page of bytes as a read produced it, or the reason it could not be read.</summary>
internal sealed class HexPage
{
    public HexPage(long index, byte[] data, int count, bool reachedEnd, Exception? error)
    {
        Index = index;
        Data = data;
        Count = count;
        ReachedEnd = reachedEnd;
        Error = error;
    }

    /// <summary>Page number; the page starts at <c>Index * PageSize</c>.</summary>
    public long Index { get; }

    public byte[] Data { get; }

    /// <summary>How many bytes of <see cref="Data"/> are real.</summary>
    public int Count { get; }

    /// <summary>The source returned no more bytes before the page was full.</summary>
    public bool ReachedEnd { get; }

    /// <summary>Why the page is unreadable, or null.</summary>
    public Exception? Error { get; }

    public long Offset => Index * HexPageCache.PageSize;
}

/// <summary>
/// A bounded, least-recently-used set of pages.
/// </summary>
/// <remarks>
/// Touched only on the event-loop thread: loaded pages reach it through a post, and drawing reads it.
/// The bound is a page count, independent of the source length, so navigating a multi-gigabyte source
/// never holds more than <see cref="Capacity"/> pages.
/// </remarks>
internal sealed class HexPageCache
{
    /// <summary>Bytes per page, and the largest single read the view ever asks a source for.</summary>
    public const int PageSize = 4096;

    /// <summary>64 pages, 256 KiB: many screens of rows at any supported width.</summary>
    public const int DefaultCapacity = 64;

    private readonly Dictionary<long, LinkedListNode<HexPage>> _map = new();
    private readonly LinkedList<HexPage> _lru = new();

    public HexPageCache(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Count => _map.Count;

    public IEnumerable<long> Indices => _map.Keys;

    public bool Contains(long index) => _map.ContainsKey(index);

    /// <summary>Returns a page and marks it most recently used.</summary>
    public bool TryGet(long index, out HexPage page)
    {
        if (_map.TryGetValue(index, out LinkedListNode<HexPage>? node))
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
    public void Add(HexPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (_map.TryGetValue(page.Index, out LinkedListNode<HexPage>? existing))
        {
            _lru.Remove(existing);
            _map.Remove(page.Index);
        }

        _map[page.Index] = _lru.AddFirst(page);

        while (_map.Count > Capacity && _lru.Last is { } oldest)
        {
            _lru.RemoveLast();
            _map.Remove(oldest.Value.Index);
        }
    }

    public void Clear()
    {
        _map.Clear();
        _lru.Clear();
    }
}
