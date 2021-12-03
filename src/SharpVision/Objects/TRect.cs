namespace SharpVision;

public class TRect
{
    public TPoint a;
    public TPoint b;

    public TRect(int ax, int ay, int bx, int by)
    {
        a = new TPoint(ax, ay);
        b = new TPoint(bx, by);
    }

    public TRect(TPoint p1, TPoint p2)
    {
        a = p1;
        b = p2;
    }

    public void Move(int aDX, int aDY)
    {
        a.x += aDX;
        a.y += aDY;
        b.x += aDX;
        b.y += aDY;
    }

    public void Grow(int aDX, int aDY)
    {
        a.x -= aDX;
        a.y -= aDY;
        b.x += aDX;
        b.y += aDY;
    }

    public void Intersect(TRect r)
    {
        a.x = Math.Max(a.x, r.a.x);
        a.y = Math.Max(a.y, r.a.y);
        b.x = Math.Min(b.x, r.b.x);
        b.y = Math.Min(b.y, r.b.y);
    }

    public void Union(TRect r)
    {
        a.x = Math.Min(a.x, r.a.x);
        a.y = Math.Min(a.y, r.a.y);
        b.x = Math.Max(b.x, r.b.x);
        b.y = Math.Max(b.y, r.b.y);
    }

    public bool Contains(TPoint p)
    {
        return p.x >= a.x && p.x < b.x && p.y >= a.y && p.y < b.y;
    }

    public static bool operator ==(TRect r1, TRect r2)
    {
        return r1.a == r2.a && r1.b == r2.b;
    }
    public static bool operator !=(TRect r1, TRect r2)
    {
        return !(r1 == r2);
    }

    public override bool Equals(object obj)
    {
        if (obj is TRect)
            return this == (TRect)obj;
        return false;
    }

    public override int GetHashCode()
    {
        return a.GetHashCode() ^ b.GetHashCode();
    }

    public bool IsEmpty()
    {
        return a.x >= b.x || a.y >= b.y;
    }

    public override string ToString()
    {
        return $"{a} {b}";
    }
}
