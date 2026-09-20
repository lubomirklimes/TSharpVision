using System.Collections.Generic;
namespace TSharpVision;

/// Sorted collection of <see cref="TResourceItem"/> keyed by
/// <c>TResourceItem.key</c>. Mirrors upstream
/// <c>class TResourceCollection : public TStringCollection</c>.
///
/// On the wire we re-use the upstream <c>TCollection::write/read</c> layout
/// so resource files written by TSharpVision are readable by classic Turbo
/// Vision: <c>short count, short limit, short delta</c>, then per item
/// <c>long pos, long size, string key</c>.
public sealed class TResourceCollection : TStreamable
{
    /// <summary>Registry name identifying serialized resource indexes.</summary>
    public const string Name = "TResourceCollection";
    /// <inheritdoc />
    public override string streamableName => Name;

    // limit/delta are TCollection bookkeeping fields
    // (initial allocation hint and growth step). They have no functional role
    // in the C# port but are written/read to preserve byte-for-byte
    // compatibility with upstream resource files.
    /// <summary>Persisted allocation hint retained for format compatibility; it does not control the managed list's capacity.</summary>
    public ushort limit;
    /// <summary>Persisted growth-step hint retained for format compatibility; it does not control managed allocation.</summary>
    public ushort delta = 8;

    // Sorted ascending by key. List<> + binary search keeps both
    // ccIndex-style positional access (for KeyAt) and O(log n) lookup.
    private readonly List<TResourceItem> _items = new();

    /// <summary>Creates an empty resource index with a default persisted growth hint of eight.</summary>
    public TResourceCollection() { }
    /// <summary>Creates an empty resource index with persisted allocation and growth hints.</summary>
    public TResourceCollection(ushort aLimit, ushort aDelta)
    {
        limit = aLimit;
        delta = aDelta;
    }

    // Static initializer registers the class with Pstream.types.
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClass =
        new TStreamableClass(Name, () => new TResourceCollection(), 0);

    /// <summary>Number of keyed resource records in the index.</summary>
    public int Count => _items.Count;
    /// <summary>Returns the record at a zero-based position in ordinal key order.</summary>
    public TResourceItem At(int i) => _items[i];

    /// <summary>Returns whether an ordinal key match exists and outputs its zero-based position or the insertion position.</summary>
    public bool Search(string key, out int i)
    {
        int lo = 0, hi = _items.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int cmp = string.CompareOrdinal(_items[mid].key, key);
            if (cmp == 0) { i = mid; return true; }
            if (cmp < 0) lo = mid + 1; else hi = mid - 1;
        }
        i = lo;
        return false;
    }

    /// <summary>Inserts a referenced record at the supplied zero-based position; the caller must preserve ordinal key order.</summary>
    public void AtInsert(int i, TResourceItem item) => _items.Insert(i, item);

    /// <summary>Removes the record at a zero-based index without modifying the resource payload stream.</summary>
    public void AtRemove(int i) => _items.RemoveAt(i);

    /// <inheritdoc />
    public override void Write(Opstream s)
    {
        s.WriteShort((ushort)_items.Count);
        s.WriteShort(limit);
        s.WriteShort(delta);
        foreach (var it in _items) WriteItem(it, s);
    }

    /// <inheritdoc />
    public override object Read(Ipstream s)
    {
        ushort ct = s.ReadShort();
        limit = s.ReadShort();
        delta = s.ReadShort();
        for (int i = 0; i < ct; i++)
        {
            // Items were written sorted, so we can append in order without
            // re-sorting.
            _items.Add((TResourceItem)ReadItem(s));
        }
        return this;
    }

    private void WriteItem(TResourceItem it, Opstream s)
    {
        s.Write32((uint)it.pos);
        s.Write32((uint)it.size);
        s.WriteString(it.key);
    }

    private TResourceItem ReadItem(Ipstream s)
    {
        var obj = new TResourceItem
        {
            pos  = s.Read32(),
            size = s.Read32(),
            key  = s.ReadString() ?? string.Empty,
        };
        return obj;
    }
}
