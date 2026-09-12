namespace TSharpVision;

/// Base class for reading streamable objects. Mirrors upstream
/// <c>Ipstream</c>. .NET <see cref="Stream"/> replaces <c>CLY_streambuf</c>;
/// the read-objects table is kept as a 1-based <see cref="List{T}"/>
/// indexed by the upstream <c>P_id_type</c> identifier.
public class Ipstream : Pstream
{
    // Index 0 is unused so that the upstream 1-based ids map directly.
    private readonly List<object> _objs = new() { null };

    /// <summary>Creates an input wrapper without an attached underlying stream.</summary>
    public Ipstream() { }

    /// <summary>Wraps the supplied stream for object and primitive deserialization.</summary>
    public Ipstream(Stream sb) : base(sb) { }

    // Upstream's seekg() removes the read-objects table and clears the EOF bit
    /// <summary>Returns the underlying stream position in bytes.</summary>
    public long Tellg() => bp.Position;

    /// <summary>Seeks to an absolute byte position and clears stream state and recorded object identities.</summary>
    public Ipstream Seekg(long pos)
    {
        _objs.Clear();
        _objs.Add(null);
        bp.Seek(pos, System.IO.SeekOrigin.Begin);
        Clear();
        return this;
    }

    /// <summary>Seeks by a byte offset from the specified origin and clears stream state and object identities.</summary>
    public Ipstream Seekg(long off, System.IO.SeekOrigin origin)
    {
        _objs.Clear();
        _objs.Add(null);
        bp.Seek(off, origin);
        Clear();
        return this;
    }

    /// <summary>Reads one byte; at end of stream returns zero and sets the EOF state bit.</summary>
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
    /// <summary>Reads an unsigned 16-bit value in little-endian byte order.</summary>
    public ushort ReadShort() => Read16();
    /// <summary>Reads an unsigned 32-bit value in little-endian byte order.</summary>
    public uint ReadInt() => Read32();
    /// <summary>Reads an unsigned 32-bit value in little-endian byte order.</summary>
    public uint ReadLong() => Read32();
    /// <summary>Reads an unsigned 16-bit value in little-endian byte order.</summary>
    public ushort ReadWord() => ReadShort();

    /// <summary>Reads an unsigned 16-bit value in little-endian byte order.</summary>
    public ushort Read16()
    {
        byte b0 = ReadByte();
        byte b1 = ReadByte();
        return (ushort)(b0 | (b1 << 8));
    }

    /// <summary>Reads an unsigned 32-bit value in little-endian byte order.</summary>
    public uint Read32()
    {
        uint b0 = ReadByte();
        uint b1 = ReadByte();
        uint b2 = ReadByte();
        uint b3 = ReadByte();
        return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
    }
    /// <summary>Reads an unsigned 64-bit value in little-endian byte order.</summary>
    public ulong Read64()
    {
        uint lo = Read32();
        uint hi = Read32();
        return ((ulong)hi << 32) | lo;
    }

    /// <summary>Reads into the first sz array positions, zero-filling an unread suffix and setting EOF on a short read.</summary>
    public void ReadBytes(byte[] data, int sz)
    {
        int got = bp.Read(data, 0, sz);
        if (got < sz)
        {
            SetState(IOSEOFBit);
            for (int i = got; i < sz; i++) data[i] = 0;
        }
    }

    /// <summary>Reads sz bytes into the array at offset, zero-filling an unread suffix and setting EOF on a short read.</summary>
    public void ReadBytes(byte[] data, int offset, int sz)
    {
        int got = bp.Read(data, offset, sz);
        if (got < sz)
        {
            SetState(IOSEOFBit);
            for (int i = offset + got; i < offset + sz; i++) data[i] = 0;
        }
    }

    /// <summary>Reads a nullable, length-prefixed UTF-16 string in little-endian order.</summary>
    public string ReadString()
    {
        byte len0 = ReadByte();
        if (len0 == 0xFF) return null;
        int len = len0;
        if (len == 0xfe) len = (int)Read32();
        var chars = new char[len];
        for (int i = 0; i < len; i++)
            chars[i] = (char)Read16();
        return new string(chars);
    }

    /// <summary>Restores the supplied object from a type prefix, data, and closing marker; returns this wrapper.</summary>
    public Ipstream ReadObject(TStreamable t)
    {
        var pc = ReadPrefix();
        ReadData(pc, t);
        ReadSuffix();
        return this;
    }

    /// <summary>Reads a null, an existing object reference, or a newly constructed registered object.</summary>
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

    /// <summary>Reads the object opening marker and resolves the following type name in the stream registry.</summary>
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

    /// <summary>Restores the supplied instance, or constructs one using the descriptor when null, recording its identity before reading.</summary>
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

    /// <summary>Reads and validates the closing object marker, recording a stream error on mismatch.</summary>
    protected void ReadSuffix()
    {
        byte ch = ReadByte();
        if (ch != (byte)']') Error(StreamableError.peInvalidType);
    }

    /// <summary>Returns the object with the recorded one-based identifier, or null for an unknown identifier.</summary>
    protected object Find(uint id)
    {
        if (id == 0 || id >= _objs.Count) return null;
        return _objs[(int)id];
    }
    /// <summary>Records an object at the next one-based stream identifier.</summary>
    protected void RegisterObject(object adr)
    {
        _objs.Add(adr);
    }

    // TPoint = 8 bytes (ReadInt x; ReadInt y) — 32-bit RHIDE convention.
    /// <summary>Reads x and y from two little-endian 32-bit integers.</summary>
    public TPoint ReadTPoint() { int x = (int)ReadInt(); int y = (int)ReadInt(); return new TPoint(x, y); }
    
    // TRect = 16 bytes (two TPoints: a, b).
    /// <summary>Reads the top-left and bottom-right corners from four little-endian 32-bit integers.</summary>
    public TRect ReadTRect() { TPoint a = ReadTPoint(); TPoint b = ReadTPoint(); return new TRect(a, b); }
}
