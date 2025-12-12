using System.IO;
namespace TSharpVision;

/// Indexed, key/value resource file backed by an <see cref="Fpstream"/>.
/// Mirrors upstream <c>TResourceFile</c>.
///
/// On-disk layout (at <c>basePos</c>):
/// <list type="bullet">
/// <item>offset 0..3   long magic = 0x52504246 ('FBPR')</item>
/// <item>offset 4..7   long lenRez (size from offset 8 to end of index)</item>
/// <item>offset 8..11  long indexPos (offset of index relative to basePos)</item>
/// <item>offset 12..   data items written via <c>WritePointer(item)</c></item>
/// <item>offset basePos+indexPos: serialized <see cref="TResourceCollection"/></item>
/// </list>
public sealed class TResourceFile
{
    public const uint rStreamMagic = 0x52504246u;

    private readonly Fpstream _stream;
    private bool _modified;
    private readonly long _basePos;
    private long _indexPos;
    private readonly TResourceCollection _index;

    public TResourceFile(Fpstream aStream)
    {
        _stream = aStream;
        _basePos = aStream.Tellp();
        long streamSize = aStream.Filelength();

        // Scan the header for the resource magic.
        // Upstream also skips DOS MZ executable headers so a TV resource file
        // can be appended to a `.exe`. We honour both signatures.
        bool found = false;
        long basePos = _basePos;
        bool repeat;
        do
        {
            repeat = false;
            if ((ulong)basePos <= (ulong)(streamSize - 8))
            {
                _stream.In.Seekg(basePos);
                ushort signature = _stream.In.Read16();
                if (signature == 0x5a4d) // 'MZ' DOS exe
                {
                    // skip the exe image.
                    ushort lastCount = _stream.In.Read16();
                    ushort pageCount = _stream.In.Read16();
                    basePos += (pageCount * 512L) - (-(int)lastCount & 511);
                    repeat = true;
                }
                else if (signature == 0x4246) // 'FB' (low half of rStreamMagic)
                {
                    ushort infoType = _stream.In.Read16();
                    if (infoType == 0x5250) // 'PR'
                    {
                        found = true;
                    }
                    else
                    {
                        // skip a foreign FB-block.
                        long infoSize = _stream.In.Read32();
                        basePos += infoSize + 16 - (infoSize % 16);
                        repeat = true;
                    }
                }
            }
        } while (repeat);

        if (found)
        {
            // Final basePos is where the magic actually sits.
            _basePos = basePos;
            // read indexPos at basePos+8.
            _stream.In.Seekg(basePos + 8);
            _indexPos = _stream.In.Read32();
            _stream.In.Seekg(basePos + _indexPos);
            _index = (TResourceCollection)_stream.In.ReadPointer();
            if (_index == null) _index = new TResourceCollection(0, 8);
        }
        else
        {
            // Fresh file: reserve the 12-byte header.
            _indexPos = 12;
            _index = new TResourceCollection(0, 8);
        }
    }

    public short Count() => (short)_index.Count;

    // Returns the resource item at the given 0-based index.
    // Used by inspection tools (svres) to enumerate resources without deserialising them.
    public TResourceItem ItemAt(int index) => _index.At(index);

    // Returns the base stream position of the resource data area.
    // Used by inspection tools to compute absolute file positions.
    public long BasePos => _basePos;

    // Persist the index and rewrite the 12-byte header. No-op when nothing has been
    // modified since the last flush, mirroring upstream.
    public void Flush()
    {
        if (!_modified) return;

        _stream.Out.Seekp(_basePos + _indexPos);
        _stream.Out.WritePointer(_index);
        long lenRez = _stream.Out.Tellp() - _basePos - 8;

        _stream.Out.Seekp(_basePos);
        _stream.Out.Write32(rStreamMagic);
        _stream.Out.Write32((uint)lenRez);
        _stream.Out.Write32((uint)_indexPos);
        _stream.Out.Flush();
        _modified = false;
    }

    // Seek to the item's stored pos and ReadPointer the streamable.
    public object Get(string key)
    {
        if (!_index.Search(key, out int i)) return null;
        var it = _index.At(i);
        _stream.In.Seekg(_basePos + it.pos);
        return _stream.In.ReadPointer();
    }

    // Returns the raw payload bytes for the given key without deserialising.
    // Used by inspection tools (svres dump) to show raw content.
    public byte[] GetRawBytes(string key)
    {
        if (!_index.Search(key, out int i)) return null;
        var it = _index.At(i);
        var buf = new byte[(int)it.size];
        _stream.In.Seekg(_basePos + it.pos);
        _stream.In.ReadBytes(buf, buf.Length);
        return buf;
    }

    public string KeyAt(short i) => _index.At(i).key;

    public void Put(TStreamable item, string key)
    {
        TResourceItem p;
        if (_index.Search(key, out int i))
        {
            p = _index.At(i);
        }
        else
        {
            p = new TResourceItem { key = key };
            _index.AtInsert(i, p);
        }
        p.pos = _indexPos;
        _stream.Out.Seekp(_basePos + _indexPos);
        _stream.Out.WritePointer(item);
        _indexPos = _stream.Out.Tellp() - _basePos;
        p.size = _indexPos - p.pos;
        _modified = true;
    }

    public void Remove(string key)
    {
        if (_index.Search(key, out int i))
        {
            _index.AtRemove(i);
            _modified = true;
        }
    }

    // Upstream's switchTo transfers ownership and optionally copies live resources
    // to the new stream in order (pack=True) or not. Here we implement the pack
    // variant as a self-contained in-place operation:
    //
    //   1. Read every live resource blob into memory (raw bytes, no deserialisation).
    //   2. Re-write the blobs sequentially from offset 12 (right after the header).
    //   3. Re-write the index immediately after the last blob.
    //   4. Re-write the 12-byte header with updated lenRez / indexPos.
    //   5. Truncate the file to the new (smaller) length.
    //
    // This is safe because all blobs are buffered before any write occurs, so
    // overlapping source / destination regions cannot corrupt data.
    public void Pack()
    {
        // Flush any pending modifications so that _indexPos is current and all
        // blobs already in the file match the index entries.
        Flush();

        int count = _index.Count;

        // Step 1 — read every live resource blob into memory.
        var blobs = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            var item = _index.At(i);
            var buf = new byte[(int)item.size];
            _stream.In.Seekg(_basePos + item.pos);
            _stream.In.ReadBytes(buf, buf.Length);
            blobs[i] = buf;
        }

        // Step 2 — determine write order: sort by original position so we
        // write sequentially (matches RHIDE behaviour; minimises seeking).
        var order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;
        System.Array.Sort(order, (a, b) => _index.At(a).pos.CompareTo(_index.At(b).pos));

        // Step 3 — write compacted blobs starting immediately after the 12-byte header.
        long writePos = 12; // same starting offset as a freshly-created resource file
        foreach (int idx in order)
        {
            var item = _index.At(idx);
            _stream.Out.Seekp(_basePos + writePos);
            _stream.Out.WriteBytes(blobs[idx], blobs[idx].Length);
            item.pos  = writePos;
            item.size = blobs[idx].Length; // unchanged, but explicit
            writePos += blobs[idx].Length;
        }

        // Step 4 — write the updated index and header.
        _indexPos = writePos;
        _stream.Out.Seekp(_basePos + _indexPos);
        _stream.Out.WritePointer(_index);
        long endPos = _stream.Out.Tellp() - _basePos;
        long lenRez = endPos - 8;

        _stream.Out.Seekp(_basePos);
        _stream.Out.Write32(rStreamMagic);
        _stream.Out.Write32((uint)lenRez);
        _stream.Out.Write32((uint)_indexPos);
        _stream.Out.Flush();

        // Step 5 — truncate the file to remove dead tail bytes.
        _stream.SetLength(_basePos + endPos);

        _modified = false;
    }
}
