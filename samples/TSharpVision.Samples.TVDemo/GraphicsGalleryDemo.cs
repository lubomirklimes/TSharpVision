using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Graphics;
using static TSharpVision.Graphics.GraphConstants;

namespace TSharpVision.Samples.TVDemo;

// ── TGalleryScene ──────────────────────────────────────────────────────────
// The vector part of the graphics gallery: the view is split into 3 x 2 panels; the top row shows the canvas
// shapes (lines, rectangles, polylines, ellipses), translucent overlaps and clipping at the view's edge. The
// bottom row only gets thin frames here; the images inside them are GImage objects (TGraphicsGalleryDemo).
internal sealed class TGalleryScene : GObject
{
    internal const uint Frame = 0x555555FF;
    private readonly TPoint[] _points = new TPoint[8];

    public TGalleryScene() : base(0xFFFFFFFF) { }

    // Pixel rectangle of panel (column, row) inside the view, with a 2-pixel gap.
    internal static PixelRect Panel(int column, int row, int px, int py, int pw, int ph)
    {
        int x0 = px + pw * column / 3, x1 = px + pw * (column + 1) / 3;
        int y0 = py + ph * row / 2, y1 = py + ph * (row + 1) / 2;
        return new PixelRect(x0 + 2, y0 + 2, Math.Max(0, x1 - x0 - 4), Math.Max(0, y1 - y0 - 4));
    }

    public override void GraphDraw(GraphicsCanvas canvas, int px, int py, int pw, int ph)
    {
        for (int row = 0; row < 2; row++)
            for (int column = 0; column < 3; column++)
            {
                PixelRect p = Panel(column, row, px, py, pw, ph);
                canvas.DrawRect(p.X, p.Y, p.W, p.H, Frame);
            }
        DrawShapes(canvas, Panel(0, 0, px, py, pw, ph));
        DrawTransparency(canvas, Panel(1, 0, px, py, pw, ph));
        DrawClipping(canvas, Panel(2, 0, px, py, pw, ph), px, py, pw, ph);
    }

    // Lines fanning out of a corner, a rectangle, a filled ellipse with its outline and a closed polyline.
    private void DrawShapes(GraphicsCanvas canvas, PixelRect p)
    {
        if (p.W < 8 || p.H < 8) return;
        int x = p.X + 2, y = p.Y + 2, w = p.W - 4, h = p.H - 4;
        for (int i = 0; i <= 4; i++)
            canvas.DrawLine(x, y + h - 1, x + w * i / 8, y, CgaPalette[9 + i]);
        canvas.DrawRect(x + w / 2, y + 1, w / 2 - 1, h / 2 - 1, CgaPalette[14]);
        int ex = x + w / 2 + 2, ey = y + h / 2 + 1, ew = w / 2 - 3, eh = h / 2 - 2;
        canvas.FillEllipse(ex, ey, ew, eh, CgaPalette[1]);
        canvas.DrawEllipse(ex, ey, ew, eh, CgaPalette[15]);
        _points[0] = new TPoint(x + 2, y + h - 2);
        _points[1] = new TPoint(x + w / 4, y + h / 2);
        _points[2] = new TPoint(x + w / 2 - 2, y + h - 2);
        canvas.DrawPolyline(_points.AsSpan(0, 3), CgaPalette[10], closed: true);
    }

    // Red, green and blue discs at half alpha overlap; a translucent bar crosses them; a translucent zigzag shows the
    // stronger joint pixels of a polyline (each joint is drawn by both of its segments).
    private void DrawTransparency(GraphicsCanvas canvas, PixelRect p)
    {
        if (p.W < 8 || p.H < 8) return;
        int d = Math.Min(p.W * 2 / 3, p.H * 2 / 3);
        int cx = p.X + (p.W - d) / 2, cy = p.Y + 2;
        canvas.FillEllipse(cx - d / 4, cy, d, d, 0xFF000080);
        canvas.FillEllipse(cx + d / 4, cy, d, d, 0x00FF0080);
        canvas.FillEllipse(cx, cy + d / 3, d, d, 0x0000FF80);
        canvas.FillRect(p.X + 1, p.Y + p.H / 2 - 2, p.W - 2, 4, 0xFFFFFF60);
        int n = _points.Length;
        for (int i = 0; i < n; i++)
            _points[i] = new TPoint(p.X + 2 + (p.W - 5) * i / (n - 1), p.Y + p.H - 3 - (i % 2) * (p.H / 4));
        canvas.DrawPolyline(_points, 0xFFFF5580);
    }

    // Shapes that run past the view: a disc centred on the view's top-right corner and a rectangle a billion
    // pixels wide. The driver clips them to the visible part of the view (and cuts out windows in front).
    private static void DrawClipping(GraphicsCanvas canvas, PixelRect p, int px, int py, int pw, int ph)
    {
        if (p.W < 8 || p.H < 8) return;
        int r = Math.Min(p.W, p.H) * 3 / 4;
        canvas.FillEllipse(px + pw - r, py - r, 2 * r, 2 * r, 0xFF880080);
        canvas.DrawEllipse(px + pw - r, py - r, 2 * r, 2 * r, CgaPalette[14]);
        canvas.DrawRect(p.X + 4, p.Y + p.H / 2, 1_000_000_000, p.H / 3, CgaPalette[11]);
        canvas.DrawLine(p.X + 4, p.Y + p.H - 3, px + pw + 1000, p.Y + p.H - 3, CgaPalette[12]);
    }
}

// ── TGraphicsGalleryDemo ───────────────────────────────────────────────────
// Window with the gallery scene, plus a generated colour-bar image shown with nearest and linear filtering side by
// side, and a stretched gradient under a half-transparent checkerboard. A caption row under the view names the panels.
internal sealed class TGraphicsGalleryDemo : GWindow
{
    internal const string Caption = " Shapes | alpha | clipping   nearest | linear | stretch";

    public TGraphicsGalleryDemo() : this(new TRect(3, 2, 63, 20)) { }

    internal TGraphicsGalleryDemo(TRect bounds) : base(bounds, "Graphics gallery", Views.wnNoNumber)
    {
        // Leave the last client row for the caption.
        TRect r = View.GetBounds();
        r.b.y--;
        View.Locate(r);
        var caption = new TStaticText(new TRect(r.a.x, r.b.y, r.b.x, r.b.y + 1), Caption)
        {
            growMode = (byte)(Views.gfGrowLoY | Views.gfGrowHiX | Views.gfGrowHiY),
        };
        Insert(caption);

        PixelImage bars = TestPatterns.ColorBars(16, 8);
        Scene = new TGalleryScene();
        Nearest = ImageInPanel(bars, GraphicsImageFilter.Nearest, 0);
        Linear = ImageInPanel(bars, GraphicsImageFilter.Linear, 1);
        Gradient = ImageInPanel(TestPatterns.Gradient(64, 4, 0x0000AAFF, 0xFFAA00FF), GraphicsImageFilter.Linear, 2);
        Gradient.Stretch = true;
        Checker = ImageInPanel(TestPatterns.Checkerboard(8, 8, 1, 0xFFFFFFFF, 0x00000000), GraphicsImageFilter.Nearest, 2);
        Checker.Stretch = true;
        Checker.Opacity = 0x80;

        Insert(Scene);
        Insert(Nearest);
        Insert(Linear);
        Insert(Gradient);
        Insert(Checker);
    }

    internal TGalleryScene Scene { get; }
    internal GImage Nearest { get; }
    internal GImage Linear { get; }
    internal GImage Gradient { get; }
    internal GImage Checker { get; }

    // A GImage in bottom panel `column`, inset a little from the panel frame (logical coordinates).
    private static GImage ImageInPanel(PixelImage image, GraphicsImageFilter filter, int column)
    {
        var obj = new GImage(image, filter);
        int mx = GraphXMax / 60, my = GraphYMax / 30;
        obj.ChangeArea(GraphXMax * column / 3 + mx, GraphYMax / 2 + my, GraphXMax * (column + 1) / 3 - mx, GraphYMax - my);
        return obj;
    }
}

public partial class TVDemoApp
{
    internal const string GraphicsUnavailableMessage =
        "{0} draws pixels and needs the SDL GPU or SDL display driver.\nThe active driver ({1}) shows text only.";

    // ≡ / Graphics gallery. Like STYX: text drivers get an explanation instead of an empty window.
    private void OpenGraphicsGallery()
    {
        if (!PixelGraphicsAvailable("The graphics gallery")) return;
        int offset = _cascade++ % 4 * 2;
        var win = new TGraphicsGalleryDemo(new TRect(3 + offset, 2 + offset / 2, 63 + offset, 20 + offset / 2));
        if (ValidView(win) is { } valid) DeskTop!.Insert(valid);
    }

    private bool PixelGraphicsAvailable(string what)
    {
        if (DeskTop == null) return false;
        if (TDisplay.driver is IPixelGraphicsDriver) return true;
        MsgBox.MessageBox(DeskTop, string.Format(GraphicsUnavailableMessage, what, TDisplay.driver?.GetType().Name ?? "none"),
            MsgBox.mfInformation | MsgBox.mfOKButton);
        return false;
    }
}
