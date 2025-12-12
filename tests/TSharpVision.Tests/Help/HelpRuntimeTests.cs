//             TParagraph / TCrossRef / THelpViewer / THelpWindow).
using System;
using System.IO;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Help;

[Collection("NonParallel")]
public sealed class HelpRuntimeTests : IDisposable
{
    // ── lifecycle ─────────────────────────────────────────────────────────────

    private readonly StreamableRegistryScope _streams;
    private readonly TempDirectory _tmp;

    public HelpRuntimeTests()
    {
        _streams = new StreamableRegistryScope();
        Pstream.DeInitTypes();
        Pstream.RegisterType(THelpTopic.StreamableClass);
        Pstream.RegisterType(THelpIndex.StreamableClass);
        _tmp = new TempDirectory();
    }

    public void Dispose()
    {
        _tmp.Dispose();
        _streams.Dispose();
    }

    // ── helper ────────────────────────────────────────────────────────────────

    private static bool ContainsViewer(TGroup g)
    {
        var v = g.last;
        if (v == null) return false;
        var start = v;
        do
        {
            if (v is THelpViewer) return true;
            v = v.Next;
        } while (v != null && v != start);
        return false;
    }

    private string BuildHelpFile()
    {
        string path = Path.Combine(_tmp.Path, "topics.hlp");
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var t1 = new THelpTopic();
        var b1 = Encoding.Latin1.GetBytes("Topic 1 body.\n");
        t1.AddParagraph(new TParagraph { text = b1, size = (ushort)b1.Length, wrap = false });

        var t2 = new THelpTopic();
        var b2 = Encoding.Latin1.GetBytes("Topic 2 second body.\n");
        t2.AddParagraph(new TParagraph { text = b2, size = (ushort)b2.Length, wrap = false });
        t2.AddCrossRef(new TCrossRef { @ref = 1, offset = 3, length = 5 });

        hf.RecordPositionInIndex(1);
        hf.PutTopic(t1);
        hf.RecordPositionInIndex(2);
        hf.PutTopic(t2);
        hf.Flush();
        fp.Close();
        return path;
    }

    // ── StreamableClass registrations ─────────────────────────────────────────

    [Fact]
    public void THelpTopic_Registered()
    {
        Assert.NotNull(Pstream.types.Lookup(THelpTopic.TypeName));
    }

    [Fact]
    public void THelpIndex_Registered()
    {
        Assert.NotNull(Pstream.types.Lookup(THelpIndex.TypeName));
    }

    [Fact]
    public void MagicHeader_Is_0x46484246()
    {
        Assert.Equal(0x46484246u, THelpFile.magicHeader);
    }

    // ── THelpIndex ────────────────────────────────────────────────────────────

    [Fact]
    public void HelpIndex_Empty_Position_ReturnsNeg1()
    {
        var idx = new THelpIndex();
        Assert.Equal(-1, idx.Position(0));
    }

    [Fact]
    public void HelpIndex_Add_GrowsTo10()
    {
        var idx = new THelpIndex();
        idx.Add(0, 100);
        idx.Add(2, 300);
        Assert.Equal(10, idx.size);
    }

    [Fact]
    public void HelpIndex_Add_ReadsBack()
    {
        var idx = new THelpIndex();
        idx.Add(0, 100);
        idx.Add(2, 300);
        Assert.Equal(100, idx.Position(0));
        Assert.Equal(-1, idx.Position(1));
        Assert.Equal(300, idx.Position(2));
        Assert.Equal(-1, idx.Position(9));
    }

    [Fact]
    public void HelpIndex_AddBeyond10_GrowsTo20()
    {
        var idx = new THelpIndex();
        idx.Add(0, 100);
        idx.Add(2, 300);
        idx.Add(15, 999);
        Assert.Equal(20, idx.size);
        Assert.Equal(999, idx.Position(15));
        Assert.Equal(-1, idx.Position(11));
    }

    // ── THelpTopic ────────────────────────────────────────────────────────────

    [Fact]
    public void HelpTopic_AddParagraph_Links()
    {
        var t = new THelpTopic();
        var bytes = Encoding.Latin1.GetBytes("Hello world\nSecond line text here\n");
        var p = new TParagraph { text = bytes, size = (ushort)bytes.Length, wrap = false };
        t.AddParagraph(p);
        Assert.Same(p, t.paragraphs);
    }

    [Fact]
    public void HelpTopic_AddCrossRef_Count()
    {
        var t = new THelpTopic();
        var bytes = Encoding.Latin1.GetBytes("Hello world\nSecond line text here\n");
        t.AddParagraph(new TParagraph { text = bytes, size = (ushort)bytes.Length, wrap = false });
        t.AddCrossRef(new TCrossRef { @ref = 7, offset = 6, length = 5 });
        t.AddCrossRef(new TCrossRef { @ref = 9, offset = 19, length = 4 });
        Assert.Equal(2, t.GetNumCrossRefs());
    }

    [Fact]
    public void HelpTopic_CrossRef_FirstEntry()
    {
        var t = new THelpTopic();
        var bytes = Encoding.Latin1.GetBytes("Hello world\nSecond line text here\n");
        t.AddParagraph(new TParagraph { text = bytes, size = (ushort)bytes.Length, wrap = false });
        t.AddCrossRef(new TCrossRef { @ref = 7, offset = 6, length = 5 });
        t.AddCrossRef(new TCrossRef { @ref = 9, offset = 19, length = 4 });
        Assert.Equal(7, t.crossRefs[0].@ref);
        Assert.Equal(19, t.crossRefs[1].offset);
    }

    [Fact]
    public void HelpTopic_SetWidth_NumLines_AtLeast2()
    {
        var t = new THelpTopic();
        var bytes = Encoding.Latin1.GetBytes("Hello world\nSecond line text here\n");
        t.AddParagraph(new TParagraph { text = bytes, size = (ushort)bytes.Length, wrap = false });
        t.SetWidth(40);
        Assert.True(t.NumLines() >= 2);
    }

    [Fact]
    public void HelpTopic_GetLine1_StartsWithHello()
    {
        var t = new THelpTopic();
        var bytes = Encoding.Latin1.GetBytes("Hello world\nSecond line text here\n");
        t.AddParagraph(new TParagraph { text = bytes, size = (ushort)bytes.Length, wrap = false });
        t.SetWidth(40);
        var line = new byte[256];
        t.GetLine(1, line);
        int len = 0; while (len < line.Length && line[len] != 0) len++;
        string s = Encoding.Latin1.GetString(line, 0, len);
        Assert.StartsWith("Hello", s);
    }

    [Fact]
    public void HelpTopic_SetNumCrossRefs_Shrinks()
    {
        var t = new THelpTopic();
        var bytes = Encoding.Latin1.GetBytes("x\n");
        t.AddParagraph(new TParagraph { text = bytes, size = (ushort)bytes.Length, wrap = false });
        t.AddCrossRef(new TCrossRef { @ref = 7, offset = 6, length = 5 });
        t.AddCrossRef(new TCrossRef { @ref = 9, offset = 19, length = 4 });
        t.SetNumCrossRefs(1);
        Assert.Equal(1, t.GetNumCrossRefs());
        Assert.Equal(7, t.crossRefs[0].@ref);
    }

    [Fact]
    public void HelpTopic_SetNumCrossRefs_Grows()
    {
        var t = new THelpTopic();
        var bytes = Encoding.Latin1.GetBytes("x\n");
        t.AddParagraph(new TParagraph { text = bytes, size = (ushort)bytes.Length, wrap = false });
        t.AddCrossRef(new TCrossRef { @ref = 7, offset = 6, length = 5 });
        t.SetNumCrossRefs(3);
        Assert.Equal(3, t.GetNumCrossRefs());
    }

    // ── THelpTopic streaming roundtrip ────────────────────────────────────────

    [Fact]
    public void HelpTopic_Roundtrip_NonNull()
    {
        using var ms = new MemoryStream();
        var oo = new Opstream(ms);
        var topic = new THelpTopic();
        var b1 = Encoding.Latin1.GetBytes("First paragraph.");
        topic.AddParagraph(new TParagraph { text = b1, size = (ushort)b1.Length, wrap = true });
        var b2 = Encoding.Latin1.GetBytes("Second.\n");
        topic.AddParagraph(new TParagraph { text = b2, size = (ushort)b2.Length, wrap = false });
        topic.AddCrossRef(new TCrossRef { @ref = 42, offset = 5, length = 3 });
        oo.WritePointer(topic);
        oo.Flush();
        ms.Position = 0;
        var ii = new Ipstream(ms);
        var rt = (THelpTopic)ii.ReadPointer();
        Assert.NotNull(rt);
    }

    [Fact]
    public void HelpTopic_Roundtrip_2Paragraphs()
    {
        using var ms = new MemoryStream();
        var oo = new Opstream(ms);
        var topic = new THelpTopic();
        var b1 = Encoding.Latin1.GetBytes("First paragraph.");
        topic.AddParagraph(new TParagraph { text = b1, size = (ushort)b1.Length, wrap = true });
        var b2 = Encoding.Latin1.GetBytes("Second.\n");
        topic.AddParagraph(new TParagraph { text = b2, size = (ushort)b2.Length, wrap = false });
        oo.WritePointer(topic);
        oo.Flush();
        ms.Position = 0;
        var ii = new Ipstream(ms);
        var rt = (THelpTopic)ii.ReadPointer();
        int paraCount = 0;
        for (var pp = rt!.paragraphs; pp != null; pp = pp.next) paraCount++;
        Assert.Equal(2, paraCount);
    }

    [Fact]
    public void HelpTopic_Roundtrip_CrossRef()
    {
        using var ms = new MemoryStream();
        var oo = new Opstream(ms);
        var topic = new THelpTopic();
        var b1 = Encoding.Latin1.GetBytes("First paragraph.");
        topic.AddParagraph(new TParagraph { text = b1, size = (ushort)b1.Length, wrap = true });
        topic.AddCrossRef(new TCrossRef { @ref = 42, offset = 5, length = 3 });
        oo.WritePointer(topic);
        oo.Flush();
        ms.Position = 0;
        var ii = new Ipstream(ms);
        var rt = (THelpTopic)ii.ReadPointer();
        Assert.Equal(1, rt!.numRefs);
        Assert.Equal(42, rt.crossRefs[0].@ref);
        Assert.Equal(5, rt.crossRefs[0].offset);
        Assert.Equal(3, rt.crossRefs[0].length);
    }

    // ── THelpIndex streaming roundtrip ────────────────────────────────────────

    [Fact]
    public void HelpIndex_Roundtrip_Size()
    {
        using var ms = new MemoryStream();
        var oo = new Opstream(ms);
        var idx = new THelpIndex();
        idx.Add(0, 12);
        idx.Add(3, 256);
        oo.WritePointer(idx);
        oo.Flush();
        ms.Position = 0;
        var ii = new Ipstream(ms);
        var idx2 = (THelpIndex)ii.ReadPointer();
        Assert.Equal(10, idx2!.size);
    }

    [Fact]
    public void HelpIndex_Roundtrip_Positions()
    {
        using var ms = new MemoryStream();
        var oo = new Opstream(ms);
        var idx = new THelpIndex();
        idx.Add(0, 12);
        idx.Add(3, 256);
        oo.WritePointer(idx);
        oo.Flush();
        ms.Position = 0;
        var ii = new Ipstream(ms);
        var idx2 = (THelpIndex)ii.ReadPointer();
        Assert.Equal(12, idx2!.Position(0));
        Assert.Equal(256, idx2.Position(3));
        Assert.Equal(-1, idx2.Position(1));
    }

    // ── THelpFile end-to-end ──────────────────────────────────────────────────

    [Fact]
    public void HelpFile_FreshFile_Modified()
    {
        string path = Path.Combine(_tmp.Path, "fresh.hlp");
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        Assert.True(hf.modified);
        fp.Close();
    }

    [Fact]
    public void HelpFile_FreshFile_IndexPos12()
    {
        string path = Path.Combine(_tmp.Path, "fresh2.hlp");
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        Assert.Equal(12, hf.indexPos);
        fp.Close();
    }

    [Fact]
    public void HelpFile_MagicBytes_Correct()
    {
        string path = BuildHelpFile();
        using var f = File.OpenRead(path);
        var hdr = new byte[12];
        f.ReadExactly(hdr, 0, 12);
        Assert.Equal((byte)'F', hdr[0]);
        Assert.Equal((byte)'B', hdr[1]);
        Assert.Equal((byte)'H', hdr[2]);
        Assert.Equal((byte)'F', hdr[3]);
    }

    [Fact]
    public void HelpFile_Reopen_NotModified()
    {
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        Assert.False(hf.modified);
        fp.Close();
    }

    [Fact]
    public void HelpFile_Reopen_IndexPosGT12()
    {
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        Assert.True(hf.indexPos > 12);
        fp.Close();
    }

    [Fact]
    public void HelpFile_GetTopic1_HasBody()
    {
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var t1 = hf.GetTopic(1);
        Assert.NotNull(t1);
        Assert.NotNull(t1!.paragraphs);
        Assert.Equal("Topic 1 body.\n", Encoding.Latin1.GetString(t1.paragraphs!.text));
        fp.Close();
    }

    [Fact]
    public void HelpFile_GetTopic2_CrossRef()
    {
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var t2 = hf.GetTopic(2);
        Assert.NotNull(t2);
        Assert.Equal(1, t2!.numRefs);
        Assert.Equal(1, t2.crossRefs[0].@ref);
        Assert.Equal(5, t2.crossRefs[0].length);
        fp.Close();
    }

    [Fact]
    public void HelpFile_GetTopic_InvalidCtx_FallbackText()
    {
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var bad = hf.GetTopic(99);
        Assert.NotNull(bad);
        Assert.NotNull(bad!.paragraphs);
        string fallback = Encoding.Latin1.GetString(bad.paragraphs!.text);
        Assert.Contains("No help available", fallback);
        fp.Close();
    }

    // ── THelpWindow ───────────────────────────────────────────────────────────

    [Fact]
    public void HelpWindow_HasOfCentered()
    {
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        Assert.True((hw.options & Views.ofCentered) != 0);
        fp.Close();
    }

    [Fact]
    public void HelpWindow_ContainsTHelpViewer()
    {
        using var driver = new DriverScope();
        string path = BuildHelpFile();
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp);
        var hw = new THelpWindow(hf, 1);
        Assert.True(ContainsViewer(hw));
        fp.Close();
    }
}
