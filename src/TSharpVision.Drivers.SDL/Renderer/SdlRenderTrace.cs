using TSharpVision.Drivers.SDL.Renderer;

namespace TSharpVision.Drivers.SDL;

/// <summary>What a renderer was about to hand to SDL at one call site.</summary>
internal enum SdlRenderOpKind
{
    /// <summary>A frame started; <see cref="SdlRenderOp.Width"/> x <see cref="SdlRenderOp.Height"/> is the region in cells.</summary>
    BeginFrame,
    /// <summary>A solid rectangle in device pixels (cell background, cursor underline bar).</summary>
    Fill,
    /// <summary>A glyph drawn into the cell rectangle in device pixels.</summary>
    Glyph,
    /// <summary>A glyph the renderer wanted but could not produce, so nothing was drawn.</summary>
    GlyphMissing,
    /// <summary>The frame was presented.</summary>
    Present,
    /// <summary>A pixel-graphics rectangle in device pixels, already clipped (IPixelGraphicsDriver layer). The
    /// rectangles of one primitive and clip rectangle are consecutive and go to SDL as one SDL_RenderFillRects batch.</summary>
    GraphicsFill,
    /// <summary>A pixel-graphics image: X, Y, Width, Height is the visible (clipped) device rectangle, the alpha
    /// byte of <see cref="SdlRenderOp.Argb"/> the opacity, plus <see cref="SdlRenderOp.ImageId"/> and
    /// <see cref="SdlRenderOp.ImageFilter"/>.</summary>
    GraphicsImage,
    /// <summary>A pixel-graphics image that got no texture (over the size limit or budget, or creation failed), so
    /// nothing was drawn; fields as for <see cref="GraphicsImage"/>.</summary>
    GraphicsImageSkipped,
}

/// <summary>One renderer-facing operation, recorded beside the real SDL call that performs it.</summary>
internal readonly record struct SdlRenderOp(
    SdlRenderOpKind Kind,
    int X,
    int Y,
    int Width,
    int Height,
    uint Argb,
    char Character = '\0',
    SdlGlyphRenderClass GlyphClass = SdlGlyphRenderClass.Space,
    long ImageId = 0,
    GraphicsImageFilter ImageFilter = GraphicsImageFilter.Nearest);

/// <summary>
/// Opt-in record of what <see cref="SDLRenderer"/> asks SDL to draw. Null in normal operation;
/// driver-output qualification installs one to observe the real cell-to-renderer translation
/// without intercepting SDL itself.
/// </summary>
internal sealed class SdlRenderTrace
{
    /// <summary>Operations in call order; bounded so a forgotten trace cannot grow without limit.</summary>
    internal List<SdlRenderOp> Operations { get; } = new();

    /// <summary>Upper bound on recorded operations; later ones are counted in <see cref="Dropped"/>.</summary>
    internal int Capacity { get; init; } = 1_000_000;

    /// <summary>Operations not recorded because <see cref="Capacity"/> was reached.</summary>
    internal int Dropped { get; private set; }

    internal void Record(in SdlRenderOp op)
    {
        if (Operations.Count >= Capacity) { Dropped++; return; }
        Operations.Add(op);
    }
}
