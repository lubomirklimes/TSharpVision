using TSharpVision.Drivers;
using static TSharpVision.Graphics.GraphConstants;

namespace TSharpVision.Graphics;

/// <summary>
/// A <see cref="PixelImage"/> shown in a logical 10000 × 7500 area of a <see cref="GView"/> (by default the whole
/// view). The image is fitted into the area preserving its aspect ratio and centred (<see cref="Fit"/>), or stretched
/// over it when <see cref="Stretch"/> is set; it follows the view's size on every draw. <see cref="GObject.Color"/> is
/// not used.
/// </summary>
/// <remarks>
/// <see cref="PixelImage"/> is immutable: to show new content assign a new instance to <see cref="Image"/> and redraw
/// the view (for example <see cref="TView.DrawView"/>). The driver uploads each instance once and releases it when no
/// layer refers to it any more. Decoding image files is not part of this library.
/// </remarks>
public class GImage : GObject
{
    /// <summary>Left edge of the area in logical coordinates (see <see cref="GraphConstants"/>).</summary>
    public int Xa { get; set; }
    /// <summary>Top edge of the area in logical coordinates.</summary>
    public int Ya { get; set; }
    /// <summary>Right edge of the area in logical coordinates; default <see cref="GraphConstants.GraphXMax"/>.</summary>
    public int Xb { get; set; } = GraphXMax;
    /// <summary>Bottom edge of the area in logical coordinates; default <see cref="GraphConstants.GraphYMax"/>.</summary>
    public int Yb { get; set; } = GraphYMax;

    /// <summary>Creates an image object covering the whole view.</summary>
    public GImage(PixelImage? image, GraphicsImageFilter filter = GraphicsImageFilter.Nearest) : base(0xFFFFFFFF)
    {
        Image = image;
        Filter = filter;
    }

    /// <summary>The image shown; null draws nothing.</summary>
    public PixelImage? Image { get; set; }

    /// <summary>Sampling filter: <see cref="GraphicsImageFilter.Nearest"/> keeps texels crisp, Linear smooths.</summary>
    public GraphicsImageFilter Filter { get; set; }

    /// <summary>When set the image fills the whole area, ignoring its aspect ratio.</summary>
    public bool Stretch { get; set; }

    /// <summary>Opacity 0..255 multiplied with the image's own alpha; 0 draws nothing.</summary>
    public byte Opacity { get; set; } = 255;

    /// <summary>Moves the area to the given logical corners.</summary>
    public void ChangeArea(int x1, int y1, int x2, int y2)
    {
        Xa = x1; Ya = y1; Xb = x2; Yb = y2;
    }

    /// <summary>
    /// The largest rectangle with the image's aspect ratio that fits <paramref name="area"/>, centred in it (left/top
    /// gets the smaller half of an odd remainder). The scaled side is rounded down and is at least 1; an empty area
    /// or image size gives an empty rectangle at the area's corner.
    /// </summary>
    public static PixelRect Fit(int imageWidth, int imageHeight, PixelRect area)
    {
        if (area.IsEmpty || imageWidth <= 0 || imageHeight <= 0) return new PixelRect(area.X, area.Y, 0, 0);
        long w, h;
        if ((long)imageWidth * area.H <= (long)imageHeight * area.W)
        {
            // Height-limited (or exact): full height, width scaled.
            h = area.H;
            w = Math.Max(1, (long)imageWidth * area.H / imageHeight);
        }
        else
        {
            w = area.W;
            h = Math.Max(1, (long)imageHeight * area.W / imageWidth);
        }
        return new PixelRect(area.X + (int)((area.W - w) / 2), area.Y + (int)((area.H - h) / 2), (int)w, (int)h);
    }

    /// <inheritdoc />
    public override void GraphDraw(GraphicsCanvas canvas, int pixelX, int pixelY, int pixelW, int pixelH)
    {
        if (Image is not { } image || Opacity == 0) return;
        GlobalToPhysical(pixelX, pixelY, pixelW, pixelH, Math.Min(Xa, Xb), Math.Min(Ya, Yb), out int x1, out int y1);
        GlobalToPhysical(pixelX, pixelY, pixelW, pixelH, Math.Max(Xa, Xb), Math.Max(Ya, Yb), out int x2, out int y2);
        var area = new PixelRect(x1, y1, x2 - x1, y2 - y1);
        PixelRect dest = Stretch ? area : Fit(image.Width, image.Height, area);
        canvas.DrawImage(image, dest.X, dest.Y, dest.W, dest.H, Filter, Opacity);
    }
}
