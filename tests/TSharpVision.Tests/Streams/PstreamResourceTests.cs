// Migrated from TSharpVision.Demo/Program.cs lines 4258-4685.
using TSharpVision;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Streams;

// File-local streamable helper (registered on demand).
file sealed class Foo9a : TStreamable
{
    public ushort value;
    public string label;
    public override string streamableName => "Foo9a";
    public override void Write(Opstream s) { s.Write16(value); s.WriteString(label); }
    public override object Read(Ipstream s) { value = s.Read16(); label = s.ReadString(); return this; }
}

file sealed class Unregistered9a : TStreamable
{
    public int x;
    public override string streamableName => "Unregistered9a";
    public override void Write(Opstream s) { s.Write32((uint)x); }
    public override object Read(Ipstream s) { x = (int)s.Read32(); return this; }
}

[Collection("NonParallel")]
public sealed class PstreamResourceTests : IDisposable
{
    private readonly StreamableRegistryScope _reg;
    public PstreamResourceTests() => _reg = new StreamableRegistryScope();
    public void Dispose() => _reg.Dispose();

    private TStreamableClass RegisterFoo9a()
    {
        Pstream.DeInitTypes();
        return new TStreamableClass("Foo9a", () => new Foo9a(), 0);
    }

    // ── Pstream constants and type registry ────────────────────

    [Fact]
    public void Pstream_Constants()
    {
        Assert.Equal(0, Pstream.ptNull);
        Assert.Equal(1, Pstream.ptIndexed);
        Assert.Equal(2, Pstream.ptObject);
        Assert.Equal(1, Pstream.IOSEOFBit);
        Assert.Equal(2, Pstream.IOSFailBit);
        Assert.Equal(4, Pstream.IOSBadBit);
    }

    [Fact]
    public void TStreamableClass_RegistersInTypes()
    {
        Pstream.DeInitTypes();
        var fooClass = new TStreamableClass("Foo9a", () => new Foo9a(), 0);
        Assert.Same(fooClass, Pstream.types.Lookup("Foo9a"));
        Assert.Null(Pstream.types.Lookup("DoesNotExist"));
        Assert.Equal(0, fooClass.delta);
        Assert.Equal("Foo9a", fooClass.name);
        Pstream.DeInitTypes();
    }

    [Fact]
    public void DeInitTypes_ClearsRegistry()
    {
        Pstream.DeInitTypes();
        new TStreamableClass("Foo9a", () => new Foo9a(), 0);
        Pstream.DeInitTypes();
        Assert.Null(Pstream.types.Lookup("Foo9a"));
    }

    // ── Primitive round-trip via MemoryStream ──────────────────

    [Fact]
    public void Primitive_RoundTrip()
    {
        using var ms = new System.IO.MemoryStream();
        var op = new Opstream(ms);
        op.WriteByte(0x42);
        op.Write16(0x1234);
        op.Write32(0xDEADBEEFu);
        op.Write64(0x0123456789ABCDEFul);
        op.WriteShort(0xABCD);
        op.WriteInt(0xCAFEBABEu);
        op.WriteLong(0x12345678u);
        op.WriteString("hello");
        op.WriteString("Příliš Привет");
        op.WriteString(null);
        op.WriteString(new string('x', 300));
        op.Flush();

        ms.Position = 0;
        var ip = new Ipstream(ms);
        Assert.Equal((byte)0x42,            ip.ReadByte());
        Assert.Equal((ushort)0x1234,        ip.Read16());
        Assert.Equal(0xDEADBEEFu,           ip.Read32());
        Assert.Equal(0x0123456789ABCDEFul,  ip.Read64());
        Assert.Equal((ushort)0xABCD,        ip.ReadShort());
        Assert.Equal(0xCAFEBABEu,           ip.ReadInt());
        Assert.Equal(0x12345678u,           ip.ReadLong());
        Assert.Equal("hello",               ip.ReadString());
        Assert.Equal("Příliš Привет",       ip.ReadString());
        Assert.Null(ip.ReadString());
        Assert.Equal(new string('x', 300),  ip.ReadString());
        Assert.True(ip.Good());
    }

    [Fact]
    public void Write16_LittleEndian_Layout()
    {
        using var ms = new System.IO.MemoryStream();
        var op = new Opstream(ms);
        op.Write16(0x1234);
        op.Write32(0xDEADBEEFu);
        var bytes = ms.ToArray();
        Assert.Equal(0x34, bytes[0]);
        Assert.Equal(0x12, bytes[1]);
        Assert.Equal(0xEF, bytes[2]);
        Assert.Equal(0xDE, bytes[5]);
    }

    [Fact]
    public void WriteString_LengthEncoding()
    {
        using var ms = new System.IO.MemoryStream();
        var op = new Opstream(ms);
        op.WriteString(null);
        op.WriteString("");
        op.WriteString("ab");
        var bytes = ms.ToArray();
        Assert.Equal(0xFF, bytes[0]);   // null → 0xFF
        Assert.Equal(0x00, bytes[1]);   // "" → 0
        Assert.Equal(0x02, bytes[2]);   // "ab" length
        Assert.Equal((byte)'a', bytes[3]);
        Assert.Equal(0x00, bytes[4]);
        Assert.Equal((byte)'b', bytes[5]);
        Assert.Equal(0x00, bytes[6]);
    }

    [Fact]
    public void WriteString_StoresUtf16CodeUnits()
    {
        using var ms = new System.IO.MemoryStream();
        var op = new Opstream(ms);
        op.WriteString("čЯ");

        var bytes = ms.ToArray();
        Assert.Equal(2, bytes[0]);
        Assert.Equal(0x0D, bytes[1]);
        Assert.Equal(0x01, bytes[2]);
        Assert.Equal(0x2F, bytes[3]);
        Assert.Equal(0x04, bytes[4]);
    }

    [Fact]
    public void TMemo_StreamRoundTrip_PreservesUnicode()
    {
        using var ms = new System.IO.MemoryStream();
        var src = new TMemo(new TRect(0, 0, 40, 5), null, null, null, 1024);
        src.InsertText("Příliš Привет");

        var op = new Opstream(ms);
        src.Write(op);
        op.Flush();

        ms.Position = 0;
        var dst = new TMemo(new TRect(0, 0, 40, 5), null, null, null, 1);
        dst.Read(new Ipstream(ms));

        Assert.Equal("Příliš Привет", ReadEditorText(dst));
    }

    private static string ReadEditorText(TEditor ed)
    {
        var chars = new char[ed.bufLen];
        for (uint p = 0; p < ed.bufLen; p++)
            chars[(int)p] = ed.BufChar(p);
        return new string(chars);
    }

    // ── Object round-trip ──────────────────────────────────────

    [Fact]
    public void Object_RoundTrip()
    {
        Pstream.DeInitTypes();
        new TStreamableClass("Foo9a", () => new Foo9a(), 0);

        using var ms = new System.IO.MemoryStream();
        var op = new Opstream(ms);
        var src = new Foo9a { value = 0xCAFE, label = "hello" };
        op.WriteObject(src);
        op.Flush();
        var bytes = ms.ToArray();
        Assert.Equal((byte)'[', bytes[0]);
        Assert.Equal((byte)']', bytes[^1]);

        ms.Position = 0;
        var ip = new Ipstream(ms);
        var dst = new Foo9a();
        ip.ReadObject(dst);
        Assert.Equal((ushort)0xCAFE, dst.value);
        Assert.Equal("hello",         dst.label);
        Assert.True(ip.Good());

        Pstream.DeInitTypes();
    }

    [Fact]
    public void Pointer_SharedRef_Deduplicated()
    {
        Pstream.DeInitTypes();
        new TStreamableClass("Foo9a", () => new Foo9a(), 0);

        using var ms = new System.IO.MemoryStream();
        var op = new Opstream(ms);
        var shared = new Foo9a { value = 7, label = "shared" };
        op.WritePointer(shared);
        op.WritePointer(shared);
        op.WritePointer(null);
        op.Flush();

        var bytes = ms.ToArray();
        Assert.Equal(Pstream.ptObject, bytes[0]);
        Assert.Equal(Pstream.ptNull,   bytes[^1]);

        ms.Position = 0;
        var ip = new Ipstream(ms);
        var first  = ip.ReadPointer() as Foo9a;
        var second = ip.ReadPointer() as Foo9a;
        var third  = ip.ReadPointer();
        Assert.NotNull(first);
        Assert.Equal(7, first.value);
        Assert.Same(first, second);
        Assert.Null(third);

        Pstream.DeInitTypes();
    }

    [Fact]
    public void WriteObject_Unregistered_SetsFailBit()
    {
        Pstream.DeInitTypes();
        using var ms = new System.IO.MemoryStream();
        var op = new Opstream(ms);
        var unreg = new Unregistered9a { x = 1 };
        op.WriteObject(unreg);
        Assert.NotEqual(0, op.Fail());
        Assert.Equal(Pstream.StreamableError.peNotRegistered, op.lastError);
        Pstream.DeInitTypes();
    }

    [Fact]
    public void Ipstream_EOF_FlipsEofBit()
    {
        var ms = new System.IO.MemoryStream(new byte[] { 0x01 });
        var ip = new Ipstream(ms);
        Assert.Equal((byte)1, ip.ReadByte());
        ip.ReadByte();   // past EOF
        Assert.True(ip.Eof() != 0 || !ip.Good());
    }

    // ── Fpstream file I/O ──────────────────────────────────────

    [Fact]
    public void Ofpstream_Ifpstream_Primitive_RoundTrip()
    {
        Pstream.DeInitTypes();
        new TStreamableClass("Foo9a", () => new Foo9a(), 0);

        using var tmp = new TempDirectory();
        string path = System.IO.Path.Combine(tmp.Path, "prims.bin");

        var op = new Ofpstream(path);
        op.WriteByte(0x77);
        op.Write32(0x12345678u);
        op.WriteString("disk");
        op.Close();

        Assert.True(System.IO.File.Exists(path));
        Assert.Equal(14L, new System.IO.FileInfo(path).Length);

        var ip = new Ifpstream(path);
        Assert.Equal((byte)0x77,     ip.ReadByte());
        Assert.Equal(0x12345678u,    ip.Read32());
        Assert.Equal("disk",         ip.ReadString());
        Assert.True(ip.Good());
        ip.Close();

        Pstream.DeInitTypes();
    }

    [Fact]
    public void Ofpstream_Ifpstream_Graph_RoundTrip()
    {
        Pstream.DeInitTypes();
        new TStreamableClass("Foo9a", () => new Foo9a(), 0);

        using var tmp = new TempDirectory();
        string path = System.IO.Path.Combine(tmp.Path, "graph.bin");

        var op = new Ofpstream(path);
        var shared = new Foo9a { value = 42, label = "shared" };
        op.WritePointer(shared);
        op.WritePointer(shared);
        op.WritePointer(null);
        op.Close();

        var ip = new Ifpstream(path);
        var a = ip.ReadPointer() as Foo9a;
        var b = ip.ReadPointer() as Foo9a;
        var c = ip.ReadPointer();
        ip.Close();

        Assert.NotNull(a);
        Assert.Equal(42, a.value);
        Assert.Equal("shared", a.label);
        Assert.Same(a, b);
        Assert.Null(c);

        Pstream.DeInitTypes();
    }

    [Fact]
    public void Fpstream_InOut_ShareFile()
    {
        Pstream.DeInitTypes();
        new TStreamableClass("Foo9a", () => new Foo9a(), 0);

        using var tmp = new TempDirectory();
        string path = System.IO.Path.Combine(tmp.Path, "rw.bin");

        var fp = new Fpstream(path);
        var src = new Foo9a { value = 0x0BAD, label = "rw" };
        fp.Out.WriteObject(src);
        fp.Out.Flush();
        long pos = fp.Out.Tellp();
        Assert.True(pos > 0);

        fp.In.Seekg(0);
        var dst = new Foo9a();
        fp.In.ReadObject(dst);
        Assert.Equal((ushort)0x0BAD, dst.value);
        Assert.Equal("rw", dst.label);

        long len = fp.Filelength();
        Assert.Equal(new System.IO.FileInfo(path).Length, len);
        fp.Close();

        Pstream.DeInitTypes();
    }

    [Fact]
    public void Ifpstream_Close_ReleasesFileHandle()
    {
        Pstream.DeInitTypes();
        new TStreamableClass("Foo9a", () => new Foo9a(), 0);

        using var tmp = new TempDirectory();
        string path = System.IO.Path.Combine(tmp.Path, "handle.bin");

        var op = new Ofpstream(path);
        op.WriteByte(0x01);
        op.Close();

        var ip = new Ifpstream(path);
        ip.Close();

        bool reopenOK = false;
        try { using var fs = System.IO.File.OpenRead(path); reopenOK = true; } catch { }
        Assert.True(reopenOK);

        Pstream.DeInitTypes();
    }

    // ── TResourceFile ──────────────────────────────────────────

    [Fact]
    public void ResourceCollection_SortedInsert_And_Lookup()
    {
        var col = new TResourceCollection(0, 8);
        var a = new TResourceItem { key = "AAA", pos = 2, size = 2 };
        var b = new TResourceItem { key = "BBB", pos = 1, size = 1 };
        var c = new TResourceItem { key = "CCC", pos = 3, size = 3 };
        col.Search("BBB", out int ib); col.AtInsert(ib, b);
        col.Search("AAA", out int ia); col.AtInsert(ia, a);
        col.Search("CCC", out int ic); col.AtInsert(ic, c);

        Assert.Equal(3, col.Count);
        Assert.Equal("AAA", col.At(0).key);
        Assert.Equal("BBB", col.At(1).key);
        Assert.Equal("CCC", col.At(2).key);
        Assert.True(col.Search("BBB", out int foundIdx) && foundIdx == 1);
        Assert.False(col.Search("ZZZ", out int notFound));
        Assert.Equal(3, notFound);
    }

    [Fact]
    public void ResourceFile_Magic()
    {
        Assert.Equal(0x52504246u, TResourceFile.rStreamMagic);
    }

    [Fact]
    public void ResourceFile_EndToEnd_RoundTrip()
    {
        Pstream.DeInitTypes();
        new TStreamableClass("Foo9a", () => new Foo9a(), 0);
        Pstream.RegisterType(TResourceCollection.StreamableClass);

        using var tmp = new TempDirectory();
        string path = System.IO.Path.Combine(tmp.Path, "items.tvr");

        // Write
        {
            var fp = new Fpstream(path);
            var rf = new TResourceFile(fp);
            Assert.Equal(0, rf.Count());
            rf.Put(new Foo9a { value = 1, label = "one"   }, "alpha");
            rf.Put(new Foo9a { value = 2, label = "two"   }, "bravo");
            rf.Put(new Foo9a { value = 3, label = "three" }, "charlie");
            Assert.Equal(3, rf.Count());
            rf.Flush();
            fp.Close();
        }

        // Header magic
        {
            using var f = System.IO.File.OpenRead(path);
            var hdr = new byte[12];
            f.ReadExactly(hdr, 0, 12);
            Assert.Equal((byte)'F', hdr[0]);
            Assert.Equal((byte)'B', hdr[1]);
            Assert.Equal((byte)'P', hdr[2]);
            Assert.Equal((byte)'R', hdr[3]);
        }

        // Read back
        {
            var fp = new Fpstream(path);
            var rf = new TResourceFile(fp);
            Assert.Equal(3, rf.Count());
            Assert.Equal("alpha",   rf.KeyAt(0));
            Assert.Equal("bravo",   rf.KeyAt(1));
            Assert.Equal("charlie", rf.KeyAt(2));
            var got = rf.Get("bravo") as Foo9a;
            Assert.NotNull(got);
            Assert.Equal((ushort)2, got.value);
            Assert.Equal("two", got.label);
            Assert.Null(rf.Get("nope"));
            fp.Close();
        }

        Pstream.DeInitTypes();
    }

    [Fact]
    public void ResourceFile_Remove_ReducesCount()
    {
        Pstream.DeInitTypes();
        new TStreamableClass("Foo9a", () => new Foo9a(), 0);
        Pstream.RegisterType(TResourceCollection.StreamableClass);

        using var tmp = new TempDirectory();
        string path = System.IO.Path.Combine(tmp.Path, "del.tvr");

        {
            var fp = new Fpstream(path);
            var rf = new TResourceFile(fp);
            rf.Put(new Foo9a { value = 1, label = "one"   }, "alpha");
            rf.Put(new Foo9a { value = 2, label = "two"   }, "bravo");
            rf.Put(new Foo9a { value = 3, label = "three" }, "charlie");
            rf.Remove("bravo");
            Assert.Equal(2, rf.Count());
            rf.Flush();
            fp.Close();
        }

        {
            var fp = new Fpstream(path);
            var rf = new TResourceFile(fp);
            Assert.Equal(2, rf.Count());
            Assert.Null(rf.Get("bravo"));
            var alpha = rf.Get("alpha") as Foo9a;
            Assert.NotNull(alpha);
            Assert.Equal((ushort)1, alpha.value);
            fp.Close();
        }

        Pstream.DeInitTypes();
    }

    [Fact]
    public void ResourceFile_Overwrite_ReplacesItem()
    {
        Pstream.DeInitTypes();
        new TStreamableClass("Foo9a", () => new Foo9a(), 0);
        Pstream.RegisterType(TResourceCollection.StreamableClass);

        using var tmp = new TempDirectory();
        string path = System.IO.Path.Combine(tmp.Path, "over.tvr");

        {
            var fp = new Fpstream(path);
            var rf = new TResourceFile(fp);
            rf.Put(new Foo9a { value = 1, label = "orig" }, "alpha");
            rf.Flush();
            fp.Close();
        }

        {
            var fp = new Fpstream(path);
            var rf = new TResourceFile(fp);
            rf.Put(new Foo9a { value = 99, label = "replaced" }, "alpha");
            Assert.Equal(1, rf.Count());
            rf.Flush();
            fp.Close();
        }

        {
            var fp = new Fpstream(path);
            var rf = new TResourceFile(fp);
            var alpha = rf.Get("alpha") as Foo9a;
            Assert.NotNull(alpha);
            Assert.Equal((ushort)99, alpha.value);
            Assert.Equal("replaced", alpha.label);
            fp.Close();
        }

        Pstream.DeInitTypes();
    }
}
