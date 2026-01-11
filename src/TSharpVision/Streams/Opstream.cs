using System.Collections.Generic;
using System.IO;
namespace TSharpVision;

/// Base class for writing streamable objects. Mirrors upstream
/// <c>Opstream</c>. .NET <see cref="Stream"/> replaces <c>CLY_streambuf</c>;
/// .NET object identity replaces the upstream <c>void*</c> address keys.
public class Opstream : Pstream
{
    // Maps already-written objects to their 1-based stream index. C# uses
    // reference-equality semantics for the dictionary (the default for
    // class instances) which mirrors the upstream pointer comparison.
    private readonly Dictionary<object, uint> _objs =
        new(ReferenceEqualityComparer.Instance);
    private uint _curId;

    public Opstream() { }
    public Opstream(Stream sb) : base(sb) { }

    public void WriteByte(byte ch) { bp.WriteByte(ch); }

    public void WriteBytes(byte[] data, int sz) { bp.Write(data, 0, sz); }
    public void WriteBytes(byte[] data, int offset, int sz) { bp.Write(data, offset, sz); }

    // Upstream's Short=2 / Int=4 / Long depend on host int sizes; on the
    // target Borland 16-bit they were 2/4/4 bytes respectively. We pick
    // those sizes so .tvr files stay format-compatible with classic Turbo
    // Vision output. The endian swap below keeps everything little-endian.
    public void WriteShort(ushort val) { Write16(val); }
    public void WriteInt(uint val) { Write32(val); }
    public void WriteLong(uint val) { Write32(val); }
    public void WriteWord(ushort val) { WriteShort(val); }

    public void Write16(ushort v)
    {
        bp.WriteByte((byte)v);
        bp.WriteByte((byte)(v >> 8));
    }
    public void Write32(uint v)
    {
        bp.WriteByte((byte)v);
        bp.WriteByte((byte)(v >> 8));
        bp.WriteByte((byte)(v >> 16));
        bp.WriteByte((byte)(v >> 24));
    }
    public void Write64(ulong v)
    {
        Write32((uint)v);
        Write32((uint)(v >> 32));
    }

    // Encoding: strings are stored as UTF-16 code units in little-endian order.
    // Length is the number of UTF-16 chars, not the number of bytes.
    public void WriteString(string str)
    {
        if (str == null)
        {
            WriteByte(0xFF);
            return;
        }
        int len = str.Length;
        if (len > 0xfd)
        {
            WriteByte(0xfe);
            Write32((uint)len);
        }
        else
        {
            WriteByte((byte)len);
        }
        for (int i = 0; i < len; i++)
            Write16(str[i]);
    }
    // Upstream's seekp() drops the written-objects table; we mirror that so a
    // seek midway through a stream cannot accidentally produce a ptIndexed
    // reference to an object that is no longer at the cursor's vantage point.
    public long Tellp() => bp.Position;

    public Opstream Seekp(long pos)
    {
        _objs.Clear();
        _curId = 0;
        bp.Seek(pos, System.IO.SeekOrigin.Begin);
        return this;
    }

    public Opstream Seekp(long off, System.IO.SeekOrigin origin)
    {
        _objs.Clear();
        _curId = 0;
        bp.Seek(off, origin);
        return this;
    }

    public Opstream Flush()
    {
        bp.Flush();
        return this;
    }

    public Opstream WriteObject(TStreamable t)
    {
        WritePrefix(t);
        WriteData(t);
        WriteSuffix(t);
        return this;
    }

    public Opstream WritePointer(TStreamable t)
    {
        if (t == null)
        {
            WriteByte(ptNull);
        }
        else
        {
            uint index = Find(t);
            if (index != uint.MaxValue)
            {
                WriteByte(ptIndexed);
                WriteWord((ushort)index);
            }
            else
            {
                WriteByte(ptObject);
                WriteObject(t);
            }
        }
        return this;
    }

    protected void WritePrefix(TStreamable t)
    {
        WriteByte((byte)'[');
        WriteString(t.streamableName);
    }

    protected void WriteData(TStreamable t)
    {
        if (types.Lookup(t.streamableName) == null)
        {
            Error(StreamableError.peNotRegistered, t);
        }
        else
        {
            RegisterObject(t);
            t.Write(this);
        }
    }

    protected void WriteSuffix(TStreamable _) { WriteByte((byte)']'); }

    protected uint Find(object adr) =>
        _objs.TryGetValue(adr, out uint v) ? v : uint.MaxValue;
    
    protected void RegisterObject(object adr)
    {
        // curId starts at 0 and increments before use,
        // i.e. the first registered object is id 1.
        _objs[adr] = ++_curId;
    }

    // TPoint = 8 bytes (WriteInt x; WriteInt y) — 32-bit RHIDE convention.
    public void WriteTPoint(TPoint p) { WriteInt((uint)p.x); WriteInt((uint)p.y); }
    // TRect = 16 bytes (two TPoints: a, b).
    public void WriteTRect(TRect r) { WriteTPoint(r.a); WriteTPoint(r.b); }
}
