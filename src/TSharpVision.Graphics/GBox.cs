namespace TSharpVision.Graphics;

/// <summary>
/// A wire rectangle in logical 10000 × 7500 coordinates; the <c>gBox</c> of the original TVGraph (four lines).
/// </summary>
public class GBox : GLine
{
    /// <summary>Creates a black empty box at the origin.</summary>
    public GBox() { }

    /// <summary>Creates a box with the given opposite corners.</summary>
    public GBox(int x1, int y1, int x2, int y2, uint color)
        : base(x1, y1, x2, y2, color) { }

    /// <inheritdoc />
    public override void GraphDraw(
        GraphicsCanvas canvas,
        int pixelX, int pixelY, int pixelW, int pixelH)
    {
        GraphConstants.GlobalToPhysical(pixelX, pixelY, pixelW, pixelH, Xa, Ya, out int px1, out int py1);
        GraphConstants.GlobalToPhysical(pixelX, pixelY, pixelW, pixelH, Xb, Yb, out int px2, out int py2);

        canvas.DrawLine(px1, py1, px2, py1, Color);
        canvas.DrawLine(px2, py1, px2, py2, Color);
        canvas.DrawLine(px2, py2, px1, py2, Color);
        canvas.DrawLine(px1, py2, px1, py1, Color);
    }
}
