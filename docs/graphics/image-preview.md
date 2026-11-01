# Showing an image in a window

`TSharpVision.Graphics` can show an already-decoded bitmap inside a normal Turbo Vision window on drivers that draw
pixels. This page walks through a small image preview: building a `PixelImage` from decoded pixels, showing it fitted
to the window with nearest or linear sampling, replacing it, closing, and what happens on a text-only driver.

## Support

Pixel graphics need a driver implementing `TSharpVision.Drivers.IPixelGraphicsDriver`: `SDLGpuDriver` and `SDLDriver`
(package `TSharpVision.Drivers.SDL`). The Console and Terminal drivers and `NullDriver` do not implement it. Check the
capability before opening a preview (see [fallback](#drivers-without-pixel-graphics)); a `GView` on such a driver shows
its `UnavailableText` in text cells instead of drawing.

Reference `TSharpVision.Graphics` and `TSharpVision.Drivers.SDL` with the same version as the other TSharpVision
packages. `TSharpVision.Graphics` depends only on `TSharpVision`; neither package brings in the other.

## What is out of scope

The library contains no decoder. Reading PNG, JPEG, GIF or any other file format, EXIF orientation, colour profiles,
animation frames and premultiplied-alpha sources are the application's job. Decode with the library of your choice and
hand over RGBA8888 pixels with straight (non-premultiplied) alpha, rows top to bottom. If your decoder produces BGRA,
swap the R and B bytes; if it produces premultiplied alpha, divide the colour channels by alpha first.

## Building a `PixelImage`

`PixelImage` copies the pixels you pass, so your buffer can be reused or returned to a pool right away. A `stride`
argument accepts padded rows. Both sides must be 1 to `PixelImage.MaxDimension` (16 384) and the image at most
`PixelImage.MaxBytes`; larger images throw `ArgumentOutOfRangeException`, so scale them down while decoding.

```csharp
using TSharpVision.Drivers;

// rgba: width * height * 4 bytes from your decoder, R, G, B, A per pixel, straight alpha.
static PixelImage ToPixelImage(int width, int height, byte[] rgba, int stride = 0) =>
    new PixelImage(width, height, rgba, stride);
```

The image is immutable. A driver uploads each instance once and keeps it while a layer refers to it, so redrawing an
unchanged image costs nothing extra. To show different content, create a new instance.

For demos and tests without any file, `TestPatterns.Checkerboard`, `TestPatterns.Gradient` and
`TestPatterns.ColorBars` generate images.

## The preview window

`GImage` is a display-list object that draws a `PixelImage` into a logical area of its `GView` (the whole view by
default). It keeps the aspect ratio and centres the image (`GImage.Fit`) unless `Stretch` is set. It recomputes this
on every draw, so the image follows the window when the user resizes or zooms it; `GWindow` already resizes its view
with the frame.

```csharp
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Graphics;

public sealed class ImagePreviewWindow : GWindow
{
    private readonly GImage _image;

    public ImagePreviewWindow(TRect bounds, string title, PixelImage image)
        : base(bounds, title, Views.wnNoNumber)
    {
        View.BackgroundAttr = (byte)0x08; // dark gray around the letterbox
        _image = new GImage(image, GraphicsImageFilter.Linear);
        Insert(_image);
    }

    // Nearest keeps texels crisp (pixel art, enlarged screenshots); Linear smooths scaled photos.
    public void SetFilter(GraphicsImageFilter filter)
    {
        _image.Filter = filter;
        View.DrawView();
    }

    // Show different content: a new immutable PixelImage, then redraw. The old instance is released by the
    // driver once no layer refers to it.
    public void ShowImage(PixelImage image)
    {
        _image.Image = image;
        View.DrawView();
    }
}
```

`View.DrawView()` redraws the view and resubmits its layer. The view also keeps its clip region in step with
windows, menus and dialogs moving over it before every presented frame; the application calls nothing for that.

## Opening it, and drivers without pixel graphics

Open the preview only when the driver can draw it, and explain otherwise, as the TVDemo graphics windows do:

```csharp
public static void OpenPreview(TProgram app, string title, int width, int height, byte[] rgba)
{
    if (app.DeskTop is not { } desk) return;
    if (TDisplay.driver is not IPixelGraphicsDriver)
    {
        MsgBox.MessageBox(desk,
            "Image preview needs a pixel graphics driver (SDL GPU or SDL).\nThe active driver shows text only.",
            MsgBox.mfInformation | MsgBox.mfOKButton);
        return;
    }

    var win = new ImagePreviewWindow(new TRect(2, 1, 62, 21), title, new PixelImage(width, height, rgba));
    if (app.ValidView(win) is { } valid) desk.Insert(valid);
}
```

The capability can also be missing behind a driver decorator that does not forward `IPixelGraphicsDriver`.

## Closing and disposal

Closing the window (`Close`, Alt+F3, the close icon) shuts the `GView` down: it stops its animation timer and removes
its driver layer, so no pixels outlive the window. Hiding the window removes the layer until it is shown again. Nothing
needs disposing on the `PixelImage` itself; it is ordinary managed memory, and the driver frees its texture when the
last layer using the image is gone.

## See also

- `GraphicsCanvas` (shapes, `DrawImage`) and `GView` XML documentation for the exact pixel rules.
- TVDemo `≡ > Graphics gallery` shows the same bitmap with nearest and linear sampling side by side, stretched
  images with opacity, and canvas shapes; `≡ > Live chart` shows animated plots.
