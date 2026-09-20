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

    /// <summary>Creates an output wrapper without an attached underlying stream.</summary>
    public Opstream() { }
    /// <summary>Wraps the supplied stream for object and primitive serialization.</summary>
    public Opstream(Stream sb) : base(sb) { }

    /// <summary>Writes one byte at the current stream position.</summary>
    public void WriteByte(byte ch) { Buffer.WriteByte(ch); }

    /// <summary>Writes the first sz bytes of the supplied array.</summary>
    public void WriteBytes(byte[] data, int sz) { Buffer.Write(data, 0, sz); }
    /// <summary>Writes sz bytes starting at the supplied array offset.</summary>
    public void WriteBytes(byte[] data, int offset, int sz) { Buffer.Write(data, offset, sz); }

    // Upstream's Short=2 / Int=4 / Long depend on host int sizes; on the
    // target Borland 16-bit they were 2/4/4 bytes respectively. We pick
    // those sizes so .tvr files stay format-compatible with classic Turbo
    // Vision output. The endian swap below keeps everything little-endian.
    /// <summary>Writes an unsigned 16-bit value in little-endian byte order.</summary>
    public void WriteShort(ushort val) { Write16(val); }
    /// <summary>Writes an unsigned 32-bit value in little-endian byte order.</summary>
    public void WriteInt(uint val) { Write32(val); }
    /// <summary>Writes an unsigned 32-bit value in little-endian byte order.</summary>
    public void WriteLong(uint val) { Write32(val); }
    /// <summary>Writes an unsigned 16-bit value in little-endian byte order.</summary>
    public void WriteWord(ushort val) { WriteShort(val); }

    /// <summary>Writes an unsigned 16-bit value in little-endian byte order.</summary>
    public void Write16(ushort v)
    {
        Buffer.WriteByte((byte)v);
        Buffer.WriteByte((byte)(v >> 8));
    }
    /// <summary>Writes an unsigned 32-bit value in little-endian byte order.</summary>
    public void Write32(uint v)
    {
        Buffer.WriteByte((byte)v);
        Buffer.WriteByte((byte)(v >> 8));
        Buffer.WriteByte((byte)(v >> 16));
        Buffer.WriteByte((byte)(v >> 24));
    }
    /// <summary>Writes an unsigned 64-bit value in little-endian byte order.</summary>
    public void Write64(ulong v)
    {
        Write32((uint)v);
        Write32((uint)(v >> 32));
    }

    // Encoding: strings are stored as UTF-16 code units in little-endian order.
    // Length is the number of UTF-16 chars, not the number of bytes.
    /// <summary>Writes a nullable, length-prefixed UTF-16 string in little-endian order; length counts code units.</summary>
    public void WriteString(string? str)
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
    /// <summary>Returns the underlying stream position in bytes.</summary>
    public long Tellp() => Buffer.Position;

    /// <summary>Seeks to an absolute byte position and clears recorded object identities.</summary>
    public Opstream Seekp(long pos)
    {
        _objs.Clear();
        _curId = 0;
        Buffer.Seek(pos, System.IO.SeekOrigin.Begin);
        return this;
    }

    /// <summary>Seeks by a byte offset from the specified origin and clears recorded object identities.</summary>
    public Opstream Seekp(long off, System.IO.SeekOrigin origin)
    {
        _objs.Clear();
        _curId = 0;
        Buffer.Seek(off, origin);
        return this;
    }

    /// <summary>Flushes the underlying stream and returns this wrapper.</summary>
    public Opstream Flush()
    {
        Buffer.Flush();
        return this;
    }

    /// <summary>Writes a type prefix, registered object data, and closing marker; returns this wrapper.</summary>
    public Opstream WriteObject(TStreamable t)
    {
        WritePrefix(t);
        WriteData(t);
        WriteSuffix(t);
        return this;
    }

    /// <summary>Writes null, a reference to an already written object, or a new serialized object while preserving identity.</summary>
    /// <param name="t">Object to serialize; null is written using the existing null-pointer representation.</param>
    public Opstream WritePointer(TStreamable? t)
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

    /// <summary>Writes the opening marker and streamable type name.</summary>
    protected void WritePrefix(TStreamable t)
    {
        WriteByte((byte)'[');
        WriteString(t.streamableName);
    }

    /// <summary>Registers the object's identity and writes its state, or records an error for an unregistered type.</summary>
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

    /// <summary>Writes the closing object marker.</summary>
    protected void WriteSuffix(TStreamable _) { WriteByte((byte)']'); }

    /// <summary>Returns the recorded one-based object identifier, or uint.MaxValue when not recorded.</summary>
    protected uint Find(object adr) =>
        _objs.TryGetValue(adr, out uint v) ? v : uint.MaxValue;
    
    /// <summary>Assigns the next one-based stream identifier to an object.</summary>
    protected void RegisterObject(object adr)
    {
        // curId starts at 0 and increments before use,
        // i.e. the first registered object is id 1.
        _objs[adr] = ++_curId;
    }

    // TPoint = 8 bytes (WriteInt x; WriteInt y) — 32-bit RHIDE convention.
    /// <summary>Writes x and y as two little-endian 32-bit integers, totaling eight bytes.</summary>
    public void WriteTPoint(TPoint p) { WriteInt((uint)p.x); WriteInt((uint)p.y); }
    // TRect = 16 bytes (two TPoints: a, b).
    /// <summary>Writes both corners as four little-endian 32-bit coordinates, totaling sixteen bytes.</summary>
    public void WriteTRect(TRect r) { WriteTPoint(r.a); WriteTPoint(r.b); }
}
