namespace SharpVision;

public struct TPoint
{
    public int x;
    public int y;

    public TPoint(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    public static TPoint operator +(TPoint p1, TPoint p2)
    {
        return new TPoint(p1.x + p2.x, p1.y + p2.y);
    }

    public static TPoint operator -(TPoint p1, TPoint p2)
    {
        return new TPoint(p1.x - p2.x, p1.y - p2.y);
    }

    public static bool operator ==(TPoint p1, TPoint p2)
    {
        return p1.x == p2.x && p1.y == p2.y;
    }

    public static bool operator !=(TPoint p1, TPoint p2)
    {
        return !(p1 == p2);
    }

    public override bool Equals(object obj)
    {
        if (obj is TPoint)
        {
            TPoint p = (TPoint)obj;
            return this == p;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return x.GetHashCode() ^ y.GetHashCode();
    }

    public void Add(TPoint other)
    {
        x += other.x;
        y += other.y;
    }

    public void Subtract(TPoint other)
    {
        x -= other.x;
        y -= other.y;
    }

    public override string ToString()
    {
        return $"{x} {y}";
    }

    public static TPoint Parse(string input)
    {
        var parts = input.Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new FormatException("Nedostatečný počet čísel pro TPoint.");
        int x = int.Parse(parts[0]);
        int y = int.Parse(parts[1]);
        return new TPoint(x, y);
    }
}
