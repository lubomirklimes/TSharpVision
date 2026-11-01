namespace TSharpVision.Graphics;

/// <summary>A line in logical 10000 × 7500 coordinates; the <c>gLine</c> of the original TVGraph.</summary>
public class GLine : GObject
{
    /// <summary>Start x in logical coordinates.</summary>
    public int Xa { get; set; }
    /// <summary>Start y in logical coordinates.</summary>
    public int Ya { get; set; }
    /// <summary>End x in logical coordinates.</summary>
    public int Xb { get; set; }
    /// <summary>End y in logical coordinates.</summary>
    public int Yb { get; set; }

    /// <summary>Creates a black zero-length line at the origin.</summary>
    public GLine() : base(0) { }

    /// <summary>Creates a line between two logical points.</summary>
    public GLine(int x1, int y1, int x2, int y2, uint color) : base(color)
    {
        Xa = x1; Ya = y1; Xb = x2; Yb = y2;
    }

    /// <summary>Moves both end points.</summary>
    public void ChangeEnds(int x1, int y1, int x2, int y2)
    {
        Xa = x1; Ya = y1; Xb = x2; Yb = y2;
    }

    /// <summary>Copies the end points of <paramref name="other"/>.</summary>
    public void ChangeEnds(GLine other)
    {
        Xa = other.Xa; Ya = other.Ya; Xb = other.Xb; Yb = other.Yb;
    }

    /// <summary>Offsets the end points.</summary>
    public void DeltaEnds(int dXa, int dYa, int dXb, int dYb)
    {
        Xa += dXa; Ya += dYa; Xb += dXb; Yb += dYb;
    }

    /// <inheritdoc />
    public override void GraphDraw(
        GraphicsCanvas canvas,
        int pixelX, int pixelY, int pixelW, int pixelH)
    {
        GraphConstants.GlobalToPhysical(pixelX, pixelY, pixelW, pixelH, Xa, Ya, out int px1, out int py1);
        GraphConstants.GlobalToPhysical(pixelX, pixelY, pixelW, pixelH, Xb, Yb, out int px2, out int py2);
        canvas.DrawLine(px1, py1, px2, py2, Color);
    }
}
