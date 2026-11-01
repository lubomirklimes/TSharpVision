using TSharpVision.Drivers.SDL.Graphics;

namespace TSharpVision.Drivers.SDL;

// IPixelGraphicsDriver for the SDL_Renderer path: the renderer draws the current frame's layers after the cell
// pass and before present, on the UI thread, so no snapshot handoff is needed.
public partial class SDLDriver : IPixelGraphicsDriver
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

    /// <summary>The layers the renderer draws. Tests.</summary>
    internal PixelGraphicsFrame GraphicsFrameForTesting => _graphicsLayers.Frame;

    private void PublishGraphics()
    {
        if (sdlRenderer != null) sdlRenderer.GraphicsFrame = _graphicsLayers.Frame;
        MarkDirty(SdlDirtyReason.WriteBuf);
    }
}
