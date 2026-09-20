namespace TSharpVision;

/// <summary>An integer coordinate pair used for character-cell positions and dimensions.</summary>
public struct TPoint
{
    /// <summary>Horizontal coordinate or width, in the units of the containing API.</summary>
    public int x;
    /// <summary>Vertical coordinate or height, in the units of the containing API.</summary>
    public int y;

    /// <summary>Creates a coordinate pair with the supplied horizontal and vertical components.</summary>
    public TPoint(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    /// <summary>Returns the component-wise sum of two coordinate pairs.</summary>
    public static TPoint operator +(TPoint p1, TPoint p2)
    {
        return new TPoint(p1.x + p2.x, p1.y + p2.y);
    }

    /// <summary>Returns the component-wise displacement between two coordinate pairs.</summary>
    public static TPoint operator -(TPoint p1, TPoint p2)
    {
        return new TPoint(p1.x - p2.x, p1.y - p2.y);
    }

    /// <summary>Tests whether both coordinate components match.</summary>
    public static bool operator ==(TPoint p1, TPoint p2)
    {
        return p1.x == p2.x && p1.y == p2.y;
    }

    /// <summary>Tests whether either coordinate component differs.</summary>
    public static bool operator !=(TPoint p1, TPoint p2)
    {
        return !(p1 == p2);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is TPoint)
        {
            TPoint p = (TPoint)obj;
            return this == p;
        }
        return false;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return x.GetHashCode() ^ y.GetHashCode();
    }

    /// <summary>Adds a displacement to this coordinate pair in place.</summary>
    public void Add(TPoint other)
    {
        x += other.x;
        y += other.y;
    }

    /// <summary>Subtracts a displacement from this coordinate pair in place.</summary>
    public void Subtract(TPoint other)
    {
        x -= other.x;
        y -= other.y;
    }

    /// <summary>Returns the horizontal and vertical components separated by a space.</summary>
    public override string ToString()
    {
        return $"{x} {y}";
    }

    /// <summary>Parses the first two space- or comma-separated integer components into a coordinate pair.</summary>
    public static TPoint Parse(string input)
    {
        var parts = input.Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new FormatException("Nedostatečný počet čísel pro TPoint.");
        int x = int.Parse(parts[0]);
        int y = int.Parse(parts[1]);
        return new TPoint(x, y);
    }
}
