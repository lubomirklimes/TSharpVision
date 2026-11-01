namespace TSharpVision.Drivers;

/// <summary>Kind of a <see cref="GraphicsPrimitive"/>.</summary>
public enum GraphicsPrimitiveKind : byte
{
    /// <summary>A one-logical-pixel line from (X1, Y1) to (X2, Y2), both end points included. Pixel set: for an
    /// x-major line (|dx| ≥ |dy|) each x of the span gets <c>y = y1 + floor((2(x − x1)dy + dx) / 2dx)</c> taken with
    /// the end points ordered so that dx &gt; 0 (y-major symmetrically). Lines with |dx| or |dy| above
    /// <see cref="GraphicsPrimitive.MaxLineExtent"/> are not drawn.</summary>
    Line,
    /// <summary>A filled rectangle covering <c>[X1, X2) x [Y1, Y2)</c>.</summary>
    FillRect,
    /// <summary>The layer image at <see cref="GraphicsPrimitive.ImageIndex"/> scaled into <c>[X1, X2) x [Y1, Y2)</c>
    /// with <see cref="GraphicsPrimitive.Filter"/>; the alpha byte of <see cref="GraphicsPrimitive.Rgba"/> is the
    /// opacity (RGB is ignored). An index outside the layer's images draws nothing.</summary>
    Image,
}

/// <summary>How an <see cref="GraphicsPrimitiveKind.Image"/> primitive samples its image.</summary>
public enum GraphicsImageFilter : byte
{
    /// <summary>Nearest texel: crisp pixel art. Each destination pixel shows the texel under its centre.</summary>
    Nearest,
    /// <summary>Bilinear between the four texels around the destination pixel centre (edge texels clamped).</summary>
    Linear,
}

/// <summary>One pixel primitive of an <see cref="IPixelGraphicsDriver"/> layer, in logical pixels.</summary>
/// <param name="Kind">Line, filled rectangle or image.</param>
/// <param name="X1">Line start x, or rectangle left edge.</param>
/// <param name="Y1">Line start y, or rectangle top edge.</param>
/// <param name="X2">Line end x, or rectangle right edge (exclusive).</param>
/// <param name="Y2">Line end y, or rectangle bottom edge (exclusive).</param>
/// <param name="Rgba">Colour as 0xRRGGBBAA; alpha 0xFF is opaque, lower values blend source-over. For an image only
/// the alpha byte (opacity) is used.</param>
/// <param name="ImageIndex">For <see cref="GraphicsPrimitiveKind.Image"/>: index into the layer's images.</param>
/// <param name="Filter">For <see cref="GraphicsPrimitiveKind.Image"/>: the sampling filter.</param>
public readonly record struct GraphicsPrimitive(
    GraphicsPrimitiveKind Kind, int X1, int Y1, int X2, int Y2, uint Rgba,
    int ImageIndex = 0, GraphicsImageFilter Filter = GraphicsImageFilter.Nearest)
{
    /// <summary>Largest |dx| or |dy| of a drawn line, in logical pixels.</summary>
    public const int MaxLineExtent = 16384;

    /// <summary>A line from (<paramref name="x1"/>, <paramref name="y1"/>) to (<paramref name="x2"/>, <paramref name="y2"/>).</summary>
    public static GraphicsPrimitive Line(int x1, int y1, int x2, int y2, uint rgba) =>
        new(GraphicsPrimitiveKind.Line, x1, y1, x2, y2, rgba);

    /// <summary>A filled rectangle at (<paramref name="x"/>, <paramref name="y"/>) of the given size.</summary>
    public static GraphicsPrimitive FillRect(int x, int y, int width, int height, uint rgba) =>
        new(GraphicsPrimitiveKind.FillRect, x, y, x + width, y + height, rgba);

    /// <summary>The layer image <paramref name="imageIndex"/> drawn into the given rectangle.</summary>
    public static GraphicsPrimitive Image(int imageIndex, int x, int y, int width, int height,
        GraphicsImageFilter filter = GraphicsImageFilter.Nearest, byte opacity = 255) =>
        new(GraphicsPrimitiveKind.Image, x, y, x + width, y + height, 0xFFFFFF00u | opacity, imageIndex, filter);
}
