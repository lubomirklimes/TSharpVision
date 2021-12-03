using System.Text;

namespace SharpVision;

/// Base class for reading streamable objects. Mirrors upstream
/// <c>Ipstream</c>. .NET <see cref="Stream"/> replaces <c>CLY_streambuf</c>;
/// the read-objects table is kept as a 1-based <see cref="List{T}"/>
/// indexed by the upstream <c>P_id_type</c> identifier.
public class Ipstream : Pstream
{
    // Index 0 is unused so that the upstream 1-based ids map directly.
    private readonly List<object> _objs = new() { null };

    public Ipstream() { }

    public Ipstream(Stream sb) : base(sb) { }

    // Upstream's seekg() removes the read-objects table and clears the EOF bit
    public long Tellg() => bp.Position;

    public Ipstream Seekg(long pos)
    {
        _objs.Clear();
        _objs.Add(null);
        bp.Seek(pos, System.IO.SeekOrigin.Begin);
        Clear();
        return this;
    }

    public Ipstream Seekg(long off, System.IO.SeekOrigin origin)
    {
        _objs.Clear();
        _objs.Add(null);
        bp.Seek(off, origin);
        Clear();
        return this;
    }

    public byte ReadByte()
    {
        int r = bp.ReadByte();
        if (r < 0)
        {
            SetState(IOSEOFBit);
            return 0;
        }
        return (byte)r;
    }

    // See Opstream comments — sizes pinned at 2/4/4 to match Borland 16-bit
    // .tvr layout used on disk.
    public ushort ReadShort() => Read16();
    public uint ReadInt() => Read32();
    public uint ReadLong() => Read32();
    public ushort ReadWord() => ReadShort();

    public ushort Read16()
    {
        byte b0 = ReadByte();
        byte b1 = ReadByte();
        return (ushort)(b0 | (b1 << 8));
    }

    public uint Read32()
    {
        uint b0 = ReadByte();
        uint b1 = ReadByte();
        uint b2 = ReadByte();
        uint b3 = ReadByte();
        return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
    }
    public ulong Read64()
    {
        uint lo = Read32();
        uint hi = Read32();
        return ((ulong)hi << 32) | lo;
    }

    public void ReadBytes(byte[] data, int sz)
    {
        int got = bp.Read(data, 0, sz);
        if (got < sz)
        {
            SetState(IOSEOFBit);
            for (int i = got; i < sz; i++) data[i] = 0;
        }
    }

    public void ReadBytes(byte[] data, int offset, int sz)
    {
        int got = bp.Read(data, offset, sz);
        if (got < sz)
        {
            SetState(IOSEOFBit);
            for (int i = offset + got; i < offset + sz; i++) data[i] = 0;
        }
    }

    public string ReadString()
    {
        byte len0 = ReadByte();
        if (len0 == 0xFF) return null;
        int len = len0;
        if (len == 0xfe) len = (int)Read32();
        var buf = new byte[len];
        ReadBytes(buf, len);
        return Encoding.Latin1.GetString(buf);
    }

    public Ipstream ReadObject(TStreamable t)
    {
        var pc = ReadPrefix();
        ReadData(pc, t);
        ReadSuffix();
        return this;
    }

    public object ReadPointer()
    {
        byte ch = ReadByte();
        switch (ch)
        {
            case ptNull:
                return null;
            case ptIndexed:
                {
                    int index = ReadWord();
                    return Find((uint)index);
                }
            case ptObject:
                {
                    var pc = ReadPrefix();
                    var r = ReadData(pc, null);
                    ReadSuffix();
                    return r;
                }
            default:
                Error(StreamableError.peInvalidType);
                return null;
        }
    }

    protected TStreamableClass ReadPrefix()
    {
        byte ch = ReadByte();
        if (ch != (byte)'[')
        {
            Error(StreamableError.peInvalidType);
            return null;
        }
        var name = ReadString();
        var ret = types.Lookup(name);
        if (ret == null) Error(StreamableError.peNotRegistered);
        return ret;
    }

    protected object ReadData(TStreamableClass c, TStreamable mem)
    {
        if (c == null) return null;
        if (mem == null) mem = c.build();
        // Note: upstream registers (char*)mem - c->delta to compensate for the
        // multiple-inheritance offset; in C# delta is always 0 so we can pass
        // the object directly.
        RegisterObject(mem);
        return mem.Read(this);
    }

    protected void ReadSuffix()
    {
        byte ch = ReadByte();
        if (ch != (byte)']') Error(StreamableError.peInvalidType);
    }

    protected object Find(uint id)
    {
        if (id == 0 || id >= _objs.Count) return null;
        return _objs[(int)id];
    }
    protected void RegisterObject(object adr)
    {
        _objs.Add(adr);
    }

    // TPoint = 8 bytes (ReadInt x; ReadInt y) — 32-bit RHIDE convention.
    public TPoint ReadTPoint() { int x = (int)ReadInt(); int y = (int)ReadInt(); return new TPoint(x, y); }
    
    // TRect = 16 bytes (two TPoints: a, b).
    public TRect ReadTRect() { TPoint a = ReadTPoint(); TPoint b = ReadTPoint(); return new TRect(a, b); }
}
