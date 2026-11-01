namespace TSharpVision.Graphics;

/// <summary>
/// Base of the objects drawn inside a <see cref="GView"/>; the <c>gObject</c> of the original TVGraph.
/// <see cref="GraphDraw"/> receives the view's pixel rectangle; subclasses map their logical
/// 10000 × 7500 geometry onto it with <see cref="GraphConstants.GlobalToPhysical"/>.
/// Both methods run on the UI thread.
/// </summary>
public abstract class GObject
{
    /// <summary>Colour as 0xRRGGBBAA.</summary>
    public uint Color { get; set; }

    /// <summary>Creates an object with the given colour.</summary>
    protected GObject(uint color) { Color = color; }

    /// <summary>Draws the object into the given pixel rectangle of the view.</summary>
    public abstract void GraphDraw(
        GraphicsCanvas canvas,
        int pixelX, int pixelY, int pixelW, int pixelH);

    /// <summary>
    /// Animation tick ("prod"). Does nothing by default; moving objects override it.
    /// <see cref="GView"/> calls it at <see cref="GView.TickHz"/> on the UI thread.
    /// </summary>
    public virtual void GraphProd() { }

    /// <summary>Changes <see cref="Color"/>.</summary>
    public void ChangeColor(uint color) { Color = color; }
}
