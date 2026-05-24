using TSharpVision;
using Xunit;

namespace TSharpVision.Tests.Core;

public sealed class TRectStructTests
{
    [Fact]
    public void TRect_Empty_IsEmpty()
    {
        Assert.True(TRect.Empty.IsEmpty());
    }

    [Fact]
    public void TRect_Default_EqualsEmpty()
    {
        Assert.Equal(default(TRect), TRect.Empty);
    }

    [Fact]
    public void TRect_CopyIsIndependent()
    {
        var r1 = new TRect(0, 0, 10, 10);
        var r2 = r1;
        r2.Move(5, 5);
        Assert.Equal(new TRect(0, 0, 10, 10), r1);
        Assert.Equal(new TRect(5, 5, 15, 15), r2);
    }

    [Fact]
    public void TRect_IEquatable()
    {
        var r1 = new TRect(1, 2, 3, 4);
        var r2 = new TRect(1, 2, 3, 4);
        Assert.True(((IEquatable<TRect>)r1).Equals(r2));
    }

    [Fact]
    public void TRect_OperatorEquality()
    {
        var r1 = new TRect(0, 0, 5, 5);
        var r2 = new TRect(0, 0, 5, 5);
        var r3 = new TRect(1, 1, 5, 5);
        Assert.True(r1 == r2);
        Assert.False(r1 == r3);
        Assert.True(r1 != r3);
        Assert.False(r1 != r2);
    }

    [Fact]
    public void TRect_Move_Correct()
    {
        var r = new TRect(1, 2, 3, 4);
        r.Move(10, 20);
        Assert.Equal(new TRect(11, 22, 13, 24), r);
    }

    [Fact]
    public void TRect_Grow_Correct()
    {
        var r = new TRect(5, 5, 15, 15);
        r.Grow(2, 3);
        Assert.Equal(new TRect(3, 2, 17, 18), r);
    }

    [Fact]
    public void TRect_Intersect_Correct()
    {
        var r = new TRect(0, 0, 10, 10);
        r.Intersect(new TRect(5, 5, 20, 20));
        Assert.Equal(new TRect(5, 5, 10, 10), r);
    }

    [Fact]
    public void TRect_Union_Correct()
    {
        var r = new TRect(0, 0, 10, 10);
        r.Union(new TRect(5, 5, 20, 20));
        Assert.Equal(new TRect(0, 0, 20, 20), r);
    }

    [Fact]
    public void TRect_IsEmpty_WhenCollapseX()
    {
        var r = new TRect(5, 0, 5, 10);
        Assert.True(r.IsEmpty());
    }

    [Fact]
    public void TRect_IsEmpty_WhenCollapseY()
    {
        var r = new TRect(0, 5, 10, 5);
        Assert.True(r.IsEmpty());
    }
}
