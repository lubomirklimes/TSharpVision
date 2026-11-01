using TSharpVision.Drivers.SDL.Graphics;

namespace TSharpVision.Drivers.SDL.Gpu;

// IPixelGraphicsDriver: layers live in PixelGraphicsLayers (UI thread); each change publishes the new immutable
// frame to the renderer, which pins it with the next cell snapshot, and requests one more presented frame.
public partial class SDLGpuDriver : IPixelGraphicsDriver
{
    private readonly PixelGraphicsLayers _graphicsLayers = new();

    /// <inheritdoc />
    public int CellPixelWidth => _metrics.LogicalWidth;

    /// <inheritdoc />
    public int CellPixelHeight => _metrics.LogicalHeight;

    /// <inheritdoc />
    public void SetGraphicsLayer(object owner, ReadOnlySpan<PixelRect> clip, ReadOnlySpan<GraphicsPrimitive> primitives,
        ReadOnlySpan<PixelImage> images = default)
    {
        if (_graphicsLayers.Set(owner, clip, primitives, images)) PublishGraphics();
    }

    /// <inheritdoc />
    public void RemoveGraphicsLayer(object owner)
    {
        if (_graphicsLayers.Remove(owner)) PublishGraphics();
    }

    /// <summary>The layers as the renderer receives them. Tests.</summary>
    internal PixelGraphicsFrame GraphicsFrameForTesting => _graphicsLayers.Frame;

    /// <summary>Number of owners with a layer. Tests.</summary>
    internal int GraphicsLayerCount => _graphicsLayers.Count;

    private void PublishGraphics()
    {
        if (_gpuRenderer != null) _gpuRenderer.GraphicsFrame = _graphicsLayers.Frame;
        RequestFullRepaint();
    }
}
