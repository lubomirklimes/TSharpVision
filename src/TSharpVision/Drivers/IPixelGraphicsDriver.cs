namespace TSharpVision.Drivers;

/// <summary>Optional pixel-graphics capability for screen drivers that present the cell grid as pixels.</summary>
/// <remarks>Absent on drivers that do not support it (console, terminal, null); discover it with
/// <c>TDisplay.driver is IPixelGraphicsDriver</c>. A driver decorator hides the capability unless it forwards it.
/// <para><b>Coordinates.</b> Logical (1×) pixels of the cell grid: (0,0) is the top-left corner of cell (0,0) and
/// cell (c, r) covers <c>[c * CellPixelWidth, (c + 1) * CellPixelWidth)</c> horizontally, likewise vertically. The
/// driver scales to device pixels itself, so HiDPI changes need no resubmission.</para>
/// <para><b>Layers.</b> Each owner (normally one view) has at most one layer: a clip region, a primitive list and the
/// images its <see cref="GraphicsPrimitiveKind.Image"/> primitives refer to, replaced as a whole. Primitives are drawn
/// in order over the composed text frame and only inside the clip region; an empty clip region draws nothing. Layers
/// never change the screen buffer, logical attributes or application palettes. Physical palettes recolour only the
/// 16 text colours, not layer colours or images; display effects that post-process the presented frame apply to
/// layers too.</para>
/// <para><b>Images.</b> The driver keeps references to the (immutable) <see cref="PixelImage"/>s of a layer, uploads
/// each image once and releases its resources when no layer refers to it any more.</para>
/// <para><b>Thread.</b> Call from the UI thread. The driver copies the clip and primitive spans before returning.</para></remarks>
public interface IPixelGraphicsDriver
{
    /// <summary>Width of one cell in logical pixels.</summary>
    int CellPixelWidth { get; }

    /// <summary>Height of one cell in logical pixels.</summary>
    int CellPixelHeight { get; }

    /// <summary>Creates or replaces the layer of <paramref name="owner"/>. The next presented frame shows it.</summary>
    /// <param name="owner">Identity of the layer (compared by reference).</param>
    /// <param name="clip">Visible rectangles in logical pixels; primitives outside them are discarded.</param>
    /// <param name="primitives">Primitives in drawing order.</param>
    /// <param name="images">Images the image primitives refer to by index; may be empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> or an image is null.</exception>
    void SetGraphicsLayer(object owner, ReadOnlySpan<PixelRect> clip, ReadOnlySpan<GraphicsPrimitive> primitives,
        ReadOnlySpan<PixelImage> images = default);

    /// <summary>Removes the layer of <paramref name="owner"/>; does nothing when it has none.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is null.</exception>
    void RemoveGraphicsLayer(object owner);
}
