//             TScrollBar, TStringCollection, StreamableClass registrations.
using System.IO;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Streams;

[Collection("NonParallel")]
public sealed class ViewStreamingTests
{
    // ── TPoint roundtrip ──────────────────────────────────────────────────────

    [Fact]
    public void WriteTPoint_ReadTPoint_X()
    {
        using var ms = new MemoryStream();
        var os = new Opstream(ms);
        var ip = new Ipstream(ms);
        os.WriteTPoint(new TPoint(7, 13));
        ms.Position = 0;
        var pt = ip.ReadTPoint();
        Assert.Equal(7, pt.x);
    }

    [Fact]
    public void WriteTPoint_ReadTPoint_Y()
    {
        using var ms = new MemoryStream();
        var os = new Opstream(ms);
        var ip = new Ipstream(ms);
        os.WriteTPoint(new TPoint(7, 13));
        ms.Position = 0;
        var pt = ip.ReadTPoint();
        Assert.Equal(13, pt.y);
    }

    // ── TRect roundtrip ───────────────────────────────────────────────────────

    [Fact]
    public void WriteTRect_ReadTRect_AX()
    {
        using var ms = new MemoryStream();
        var os = new Opstream(ms);
        var ip = new Ipstream(ms);
        os.WriteTRect(new TRect(1, 2, 30, 20));
        ms.Position = 0;
        var rt = ip.ReadTRect();
        Assert.Equal(1, rt.a.x);
    }

    [Fact]
    public void WriteTRect_ReadTRect_AY()
    {
        using var ms = new MemoryStream();
        var os = new Opstream(ms);
        var ip = new Ipstream(ms);
        os.WriteTRect(new TRect(1, 2, 30, 20));
        ms.Position = 0;
        var rt = ip.ReadTRect();
        Assert.Equal(2, rt.a.y);
    }

    [Fact]
    public void WriteTRect_ReadTRect_BX()
    {
        using var ms = new MemoryStream();
        var os = new Opstream(ms);
        var ip = new Ipstream(ms);
        os.WriteTRect(new TRect(1, 2, 30, 20));
        ms.Position = 0;
        var rt = ip.ReadTRect();
        Assert.Equal(30, rt.b.x);
    }

    [Fact]
    public void WriteTRect_ReadTRect_BY()
    {
        using var ms = new MemoryStream();
        var os = new Opstream(ms);
        var ip = new Ipstream(ms);
        os.WriteTRect(new TRect(1, 2, 30, 20));
        ms.Position = 0;
        var rt = ip.ReadTRect();
        Assert.Equal(20, rt.b.y);
    }

    // ── TView roundtrip ───────────────────────────────────────────────────────

    private static (TView original, TView read) RoundtripView()
    {
        using var ms = new MemoryStream();
        var os = new Opstream(ms);
        var ip = new Ipstream(ms);
        var v = new TView(new TRect(3, 4, 23, 14));
        v.options   = 0x12;
        v.eventMask = 0x0001;
        v.helpCtx   = 42;
        v.growMode  = 3;
        v.Write(os);
        var v2 = (TView)TView.Build();
        ms.Position = 0;
        v2.Read(ip);
        return (v, v2);
    }

    [Fact]
    public void TView_Roundtrip_OriginX()
    {
        var (v, v2) = RoundtripView();
        Assert.Equal(v.origin.x, v2.origin.x);
    }

    [Fact]
    public void TView_Roundtrip_OriginY()
    {
        var (v, v2) = RoundtripView();
        Assert.Equal(v.origin.y, v2.origin.y);
    }

    [Fact]
    public void TView_Roundtrip_SizeX()
    {
        var (v, v2) = RoundtripView();
        Assert.Equal(v.size.x, v2.size.x);
    }

    [Fact]
    public void TView_Roundtrip_SizeY()
    {
        var (v, v2) = RoundtripView();
        Assert.Equal(v.size.y, v2.size.y);
    }

    [Fact]
    public void TView_Roundtrip_Options()
    {
        var (v, v2) = RoundtripView();
        Assert.Equal(v.options, v2.options);
    }

    [Fact]
    public void TView_Roundtrip_HelpCtx()
    {
        var (v, v2) = RoundtripView();
        Assert.Equal(v.helpCtx, v2.helpCtx);
    }

    [Fact]
    public void TView_Roundtrip_GrowMode()
    {
        var (v, v2) = RoundtripView();
        Assert.Equal(v.growMode, v2.growMode);
    }

    [Fact]
    public void TView_Roundtrip_OwnerNull()
    {
        var (_, v2) = RoundtripView();
        Assert.Null(v2.owner);
    }

    // ── TScrollBar roundtrip ──────────────────────────────────────────────────

    private static (TScrollBar original, TScrollBar read) RoundtripScrollBar()
    {
        using var ms = new MemoryStream();
        var os = new Opstream(ms);
        var ip = new Ipstream(ms);
        var sb = new TScrollBar(new TRect(0, 0, 10, 1));
        sb.Write(os);
        var sb2 = (TScrollBar)TScrollBar.Build();
        ms.Position = 0;
        sb2.Read(ip);
        return (sb, sb2);
    }

    [Fact]
    public void TScrollBar_Roundtrip_Value()
    {
        var (sb, sb2) = RoundtripScrollBar();
        Assert.Equal(sb.value, sb2.value);
    }

    [Fact]
    public void TScrollBar_Roundtrip_MinVal()
    {
        var (sb, sb2) = RoundtripScrollBar();
        Assert.Equal(sb.minVal, sb2.minVal);
    }

    [Fact]
    public void TScrollBar_Roundtrip_MaxVal()
    {
        var (sb, sb2) = RoundtripScrollBar();
        Assert.Equal(sb.maxVal, sb2.maxVal);
    }

    [Fact]
    public void TScrollBar_Roundtrip_PgStep()
    {
        var (sb, sb2) = RoundtripScrollBar();
        Assert.Equal(sb.pgStep, sb2.pgStep);
    }

    [Fact]
    public void TScrollBar_Roundtrip_ArStep()
    {
        var (sb, sb2) = RoundtripScrollBar();
        Assert.Equal(sb.arStep, sb2.arStep);
    }

    [Fact]
    public void TScrollBar_Roundtrip_Chars()
    {
        var (sb, sb2) = RoundtripScrollBar();
        for (int i = 0; i < 5; i++)
            Assert.Equal(sb.chars[i], sb2.chars[i]);
    }

    // ── TStringCollection roundtrip ───────────────────────────────────────────

    [Fact]
    public void TStringCollection_Roundtrip_Count()
    {
        using var ms = new MemoryStream();
        var os = new Opstream(ms);
        var ip = new Ipstream(ms);
        var sc = new TStringCollection();
        sc.Insert("Alpha"); sc.Insert("Beta"); sc.Insert("Gamma");
        sc.Write(os);
        var sc2 = new TStringCollection();
        ms.Position = 0;
        sc2.Read(ip);
        Assert.Equal(3, sc2.Count);
    }

    [Fact]
    public void TStringCollection_Roundtrip_Item0()
    {
        using var ms = new MemoryStream();
        var os = new Opstream(ms);
        var ip = new Ipstream(ms);
        var sc = new TStringCollection();
        sc.Insert("Alpha"); sc.Insert("Beta"); sc.Insert("Gamma");
        sc.Write(os);
        var sc2 = new TStringCollection();
        ms.Position = 0;
        sc2.Read(ip);
        Assert.Equal("Alpha", sc2[0]);
    }

    [Fact]
    public void TStringCollection_Roundtrip_Item2()
    {
        using var ms = new MemoryStream();
        var os = new Opstream(ms);
        var ip = new Ipstream(ms);
        var sc = new TStringCollection();
        sc.Insert("Alpha"); sc.Insert("Beta"); sc.Insert("Gamma");
        sc.Write(os);
        var sc2 = new TStringCollection();
        ms.Position = 0;
        sc2.Read(ip);
        Assert.Equal("Gamma", sc2[2]);
    }

    // ── StreamableClass registration ──────────────────────────────────────────

    [Fact]
    public void StreamableClass_TView_Registered()
    {
        using var scope = new StreamableRegistryScope();
        Pstream.DeInitTypes();
        Pstream.RegisterType(TView.StreamableClassTView);
        Assert.NotNull(Pstream.types.Lookup("TView"));
    }

    [Fact]
    public void StreamableClass_TGroup_Registered()
    {
        using var scope = new StreamableRegistryScope();
        Pstream.DeInitTypes();
        Pstream.RegisterType(TGroup.StreamableClassTGroup);
        Assert.NotNull(Pstream.types.Lookup("TGroup"));
    }

    [Fact]
    public void StreamableClass_TScrollBar_Registered()
    {
        using var scope = new StreamableRegistryScope();
        Pstream.DeInitTypes();
        Pstream.RegisterType(TScrollBar.StreamableClassTScrollBar);
        Assert.NotNull(Pstream.types.Lookup("TScrollBar"));
    }

    [Fact]
    public void StreamableClass_TStringCollection_Registered()
    {
        using var scope = new StreamableRegistryScope();
        Pstream.DeInitTypes();
        Pstream.RegisterType(TStringCollection.StreamableClass);
        Assert.NotNull(Pstream.types.Lookup("TStringCollection"));
    }

    [Fact]
    public void StreamableClass_TListBox_Registered()
    {
        using var scope = new StreamableRegistryScope();
        Pstream.DeInitTypes();
        Pstream.RegisterType(TListBox.StreamableClassTListBox);
        Assert.NotNull(Pstream.types.Lookup("TListBox"));
    }
}
