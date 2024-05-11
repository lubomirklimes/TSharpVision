using SharpVision;
using SharpVision.Constants;
using Xunit;

namespace SharpVision.Tests.Core;

public sealed class PrimitivesTests
{
    // ── TPoint ────────────────────────────────────────────────────────────

    [Fact]
    public void TPoint_Addition()
    {
        var p1 = new TPoint(1, 2);
        var p2 = new TPoint(3, 4);
        Assert.Equal(new TPoint(4, 6), p1 + p2);
    }

    [Fact]
    public void TPoint_Subtraction()
    {
        var p1 = new TPoint(1, 2);
        var p2 = new TPoint(3, 4);
        Assert.Equal(new TPoint(2, 2), p2 - p1);
    }

    // ── TRect ─────────────────────────────────────────────────────────────

    [Fact]
    public void TRect_Intersect()
    {
        var r1 = new TRect(0, 0, 10, 10);
        var r2 = new TRect(5, 5, 20, 20);
        var inter = new TRect(r1.a, r1.b);
        inter.Intersect(r2);
        Assert.Equal(new TRect(5, 5, 10, 10), inter);
    }

    [Fact]
    public void TRect_Union()
    {
        var r1 = new TRect(0, 0, 10, 10);
        var r2 = new TRect(5, 5, 20, 20);
        var uni = new TRect(r1.a, r1.b);
        uni.Union(r2);
        Assert.Equal(new TRect(0, 0, 20, 20), uni);
    }

    [Fact]
    public void TRect_Contains_Inside()
    {
        var r1 = new TRect(0, 0, 10, 10);
        Assert.True(r1.Contains(new TPoint(5, 5)));
    }

    [Fact]
    public void TRect_Contains_Outside()
    {
        var r1 = new TRect(0, 0, 10, 10);
        Assert.False(r1.Contains(new TPoint(15, 15)));
    }

    // ── TPalette ──────────────────────────────────────────────────────────

    [Fact]
    public void TPalette_SizeFromData()
    {
        var pal = new TPalette("\x07\x08\x09", 3);
        Assert.Equal(3, pal.Size);
    }

    [Fact]
    public void TPalette_IndexedRead()
    {
        var pal = new TPalette("\x07\x08\x09", 3);
        Assert.Equal(0x07, pal[1]);
        Assert.Equal(0x09, pal[3]);
    }

    // ── TColorAttr ────────────────────────────────────────────────────────

    [Fact]
    public void TColorAttr_Foreground()
    {
        var attr = (TColorAttr)(byte)(Colors.fgWhite | Colors.bgBlue);
        Assert.Equal(Colors.fgWhite, attr.Foreground);
    }

    [Fact]
    public void TColorAttr_Background()
    {
        var attr = (TColorAttr)(byte)(Colors.fgWhite | Colors.bgBlue);
        Assert.Equal(Colors.bgBlue, attr.Background);
    }

    // ── TDrawBuffer ───────────────────────────────────────────────────────

    [Fact]
    public void TDrawBuffer_moveChar_TruncatesAtEnd()
    {
        var buf = new TDrawBuffer();
        buf.moveChar(0, 'X', 0x1F, buf.Length + 50);
        Assert.Equal('X', buf.Data[buf.Length - 1].Character);
    }

    [Fact]
    public void TDrawBuffer_moveStr_TruncatesAtEnd()
    {
        var buf = new TDrawBuffer();
        buf.moveStr(0, new string('A', buf.Length + 100), 0x1F);
        Assert.Equal('A', buf.Data[buf.Length - 1].Character);
    }

    [Fact]
    public void TDrawBuffer_moveCStr_WritesFourCells()
    {
        var buf = new TDrawBuffer();
        ushort written = buf.moveCStr(0, "F~i~le", (ushort)((0x1F << 8) | 0x07));
        Assert.Equal(4, written);
    }

    [Fact]
    public void TDrawBuffer_moveCStr_NormalAttr()
    {
        var buf = new TDrawBuffer();
        buf.moveCStr(0, "F~i~le", (ushort)((0x1F << 8) | 0x07));
        Assert.Equal(new TColorAttr(0x07), buf.Data[0].Attr);
    }

    [Fact]
    public void TDrawBuffer_moveCStr_HotAttr()
    {
        var buf = new TDrawBuffer();
        buf.moveCStr(0, "F~i~le", (ushort)((0x1F << 8) | 0x07));
        Assert.Equal(new TColorAttr(0x1F), buf.Data[1].Attr);
    }
}
