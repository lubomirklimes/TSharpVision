namespace TSharpVision;

/// <summary>An integer rectangle with inclusive top-left and exclusive bottom-right edges.</summary>
public struct TRect : IEquatable<TRect>
{
    /// <summary>Inclusive top-left corner.</summary>
    public TPoint a;
    /// <summary>Exclusive bottom-right corner.</summary>
    public TPoint b;

    /// <summary>Zero-size rectangle at the origin.</summary>
    public static readonly TRect Empty = default;

    /// <summary>Creates a rectangle from inclusive left/top and exclusive right/bottom coordinates.</summary>
    public TRect(int ax, int ay, int bx, int by)
    {
        a = new TPoint(ax, ay);
        b = new TPoint(bx, by);
    }

    /// <summary>Creates a rectangle from inclusive top-left and exclusive bottom-right corners.</summary>
    public TRect(TPoint p1, TPoint p2)
    {
        a = p1;
        b = p2;
    }

    /// <summary>Translates both corners by the supplied horizontal and vertical displacement.</summary>
    public void Move(int aDX, int aDY)
    {
        a.x += aDX;
        a.y += aDY;
        b.x += aDX;
        b.y += aDY;
    }

    /// <summary>Expands both horizontal edges and both vertical edges by the supplied amounts; negative amounts shrink the rectangle.</summary>
    public void Grow(int aDX, int aDY)
    {
        a.x -= aDX;
        a.y -= aDY;
        b.x += aDX;
        b.y += aDY;
    }

    /// <summary>Replaces this rectangle with the overlap of both rectangles; disjoint inputs produce an empty rectangle.</summary>
    public void Intersect(TRect r)
    {
        a.x = Math.Max(a.x, r.a.x);
        a.y = Math.Max(a.y, r.a.y);
        b.x = Math.Min(b.x, r.b.x);
        b.y = Math.Min(b.y, r.b.y);
    }

    /// <summary>Expands this rectangle to enclose both pairs of corners.</summary>
    public void Union(TRect r)
    {
        a.x = Math.Min(a.x, r.a.x);
        a.y = Math.Min(a.y, r.a.y);
        b.x = Math.Max(b.x, r.b.x);
        b.y = Math.Max(b.y, r.b.y);
    }

    /// <summary>Tests whether a point is inside the inclusive left/top and exclusive right/bottom edges.</summary>
    public bool Contains(TPoint p)
    {
        return p.x >= a.x && p.x < b.x && p.y >= a.y && p.y < b.y;
    }

    /// <summary>Returns whether the width or height is zero or negative.</summary>
    public bool IsEmpty()
    {
        return a.x >= b.x || a.y >= b.y;
    }

    /// <summary>Tests whether both corners match those of another rectangle.</summary>
    public bool Equals(TRect other) => a == other.a && b == other.b;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TRect other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(a.GetHashCode(), b.GetHashCode());

    /// <summary>Tests whether both rectangles have identical corners.</summary>
    public static bool operator ==(TRect left, TRect right) => left.Equals(right);
    /// <summary>Tests whether either corner differs between two rectangles.</summary>
    public static bool operator !=(TRect left, TRect right) => !left.Equals(right);

    /// <summary>Returns the two corners as space-separated coordinate pairs.</summary>
    public override string ToString() => $"{a} {b}";
}
