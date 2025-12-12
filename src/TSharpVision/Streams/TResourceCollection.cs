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
    public const string Name = "TResourceCollection";
    public override string streamableName => Name;

    // limit/delta are TCollection bookkeeping fields
    // (initial allocation hint and growth step). They have no functional role
    // in the C# port but are written/read to preserve byte-for-byte
    // compatibility with upstream resource files.
    public ushort limit;
    public ushort delta = 8;

    // Sorted ascending by key. List<> + binary search keeps both
    // ccIndex-style positional access (for KeyAt) and O(log n) lookup.
    private readonly List<TResourceItem> _items = new();

    public TResourceCollection() { }
    public TResourceCollection(ushort aLimit, ushort aDelta)
    {
        limit = aLimit;
        delta = aDelta;
    }

    // Static initializer registers the class with Pstream.types.
    public static readonly TStreamableClass StreamableClass =
        new TStreamableClass(Name, () => new TResourceCollection(), 0);

    public int Count => _items.Count;
    public TResourceItem At(int i) => _items[i];

    // Returns true if found, sets <paramref name="i"/> to the insertion or
    // match index.
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

    public void AtInsert(int i, TResourceItem item) => _items.Insert(i, item);

    public void AtRemove(int i) => _items.RemoveAt(i);

    public override void Write(Opstream s)
    {
        s.WriteShort((ushort)_items.Count);
        s.WriteShort(limit);
        s.WriteShort(delta);
        foreach (var it in _items) WriteItem(it, s);
    }

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
            key  = s.ReadString(),
        };
        return obj;
    }
}
