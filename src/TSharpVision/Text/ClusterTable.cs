using System.Collections.Concurrent;

namespace TSharpVision.Text;

/// <summary>
/// Process-wide intern table for the text of multi-scalar grapheme clusters held by screen cells
/// (<see cref="TScreenChar"/> leads). Append-only: a handle, once issued, names the same text for the life of the
/// process, so cells cached in group buffers can never resolve to another cluster. Lookups are lock-free and
/// allocation-free; additions take a lock.
/// </summary>
/// <remarks>
/// When <see cref="Capacity"/> entries exist, <see cref="TryIntern"/> fails and callers draw U+FFFD. A cluster
/// longer than <see cref="MaxClusterLength"/> UTF-16 units is stored cut at the last scalar boundary within that
/// length (display only; text models keep the whole cluster).
/// </remarks>
internal sealed class ClusterTable
{
    public const int DefaultCapacity = 65536;
    public const int MaxClusterLength = 128;

    public static ClusterTable Shared { get; } = new(DefaultCapacity);

    private readonly ConcurrentDictionary<string, int> _handles = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private readonly string?[] _entries;
    private readonly object _gate = new();
    private int _count;

    public ClusterTable(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Capacity = capacity;
        _entries = new string?[capacity + 1];
        _lookup = _handles.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>Maximum number of entries.</summary>
    public int Capacity { get; }

    /// <summary>Number of entries; handles are 1..Count.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>True after an intern request was refused because the table is full.</summary>
    public bool Exhausted { get; private set; }

    /// <summary>Returns the handle of <paramref name="cluster"/>, adding it when absent and room remains.</summary>
    public bool TryIntern(ReadOnlySpan<char> cluster, out int handle)
    {
        if (cluster.Length > MaxClusterLength)
        {
            int cut = MaxClusterLength;
            if (char.IsLowSurrogate(cluster[cut]) && char.IsHighSurrogate(cluster[cut - 1])) cut--;
            cluster = cluster.Slice(0, cut);
        }
        if (_lookup.TryGetValue(cluster, out handle)) return true;
        lock (_gate)
        {
            if (_lookup.TryGetValue(cluster, out handle)) return true;
            if (_count >= Capacity)
            {
                Exhausted = true;
                handle = 0;
                return false;
            }
            string text = cluster.ToString();
            handle = _count + 1;
            _entries[handle] = text;
            Volatile.Write(ref _count, handle);
            _handles[text] = handle;
            return true;
        }
    }

    /// <summary>The text of <paramref name="handle"/>, or null for an unknown handle.</summary>
    public string? Get(int handle) =>
        (uint)(handle - 1) < (uint)Volatile.Read(ref _count) ? _entries[handle] : null;
}
