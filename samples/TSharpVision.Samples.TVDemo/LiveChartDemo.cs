using TSharpVision.Constants;
using TSharpVision.Graphics;
using static TSharpVision.Graphics.GraphConstants;

namespace TSharpVision.Samples.TVDemo;

// ── TLiveChart ─────────────────────────────────────────────────────────────
// Three animated plots in one view: a sine wave and a random walk scrolling right to left (polylines over a ring
// buffer of samples), and a row of translucent bars at the bottom pulsing with phase-shifted sines. Each GraphProd
// adds one sample; drawing reuses one point buffer, so a frame allocates nothing.
internal sealed class TLiveChart : GObject
{
    internal const int Samples = 96, Bars = 12;
    internal const uint GridColor = 0x303030FF, SineColor = 0x55FFFFFF, NoiseColor = 0xFFFF55FF, BarColor = 0x00AA0090;

    private readonly double[] _sine = new double[Samples];
    private readonly double[] _noise = new double[Samples];
    private readonly TPoint[] _points = new TPoint[Samples];
    private readonly Random _random;
    private int _newest; // ring index of the newest sample
    private double _walk;

    public TLiveChart() : this(Random.Shared) { }

    internal TLiveChart(Random random) : base(0xFFFFFFFF)
    {
        _random = random;
        for (int i = 0; i < Samples; i++) GraphProd();
    }

    /// <summary>Number of GraphProd calls so far. Tests.</summary>
    internal long Ticks { get; private set; }

    public override void GraphProd()
    {
        Ticks++;
        _newest = (_newest + 1) % Samples;
        _sine[_newest] = Math.Sin(Ticks * 0.15);
        _walk = Math.Clamp(_walk + (_random.NextDouble() - 0.5) * 0.3, -1, 1);
        _noise[_newest] = _walk;
    }

    // Height (0..1) of bar i at the current tick.
    internal double BarHeight(int i) => 0.5 + 0.45 * Math.Sin(Ticks * 0.07 + i * 0.6);

    public override void GraphDraw(GraphicsCanvas canvas, int px, int py, int pw, int ph)
    {
        int x = px + 2, y = py + 2, w = pw - 4, h = ph - 4;
        if (w < 8 || h < 8) return;

        // Frame and grid: plots in the top two thirds, bars in the bottom third.
        int plotH = h * 2 / 3;
        canvas.DrawRect(x, y, w, h, CgaPalette[8]);
        for (int i = 1; i < 4; i++) canvas.DrawLine(x + 1, y + plotH * i / 4, x + w - 2, y + plotH * i / 4, GridColor);
        canvas.DrawLine(x + 1, y + plotH, x + w - 2, y + plotH, CgaPalette[8]);

        int barTop = y + plotH + 2, barH = h - plotH - 4;
        for (int i = 0; i < Bars; i++)
        {
            int bx0 = x + 2 + (w - 4) * i / Bars, bx1 = x + 2 + (w - 4) * (i + 1) / Bars;
            int bh = (int)(barH * BarHeight(i));
            canvas.FillRect(bx0 + 1, barTop + barH - bh, bx1 - bx0 - 2, bh, BarColor);
        }

        Plot(canvas, _noise, NoiseColor, x + 1, y + 1, w - 2, plotH - 2);
        Plot(canvas, _sine, SineColor, x + 1, y + 1, w - 2, plotH - 2);
    }

    // Oldest sample at the left, newest at the right; value -1..1 maps to the bottom..top of the area.
    private void Plot(GraphicsCanvas canvas, double[] values, uint color, int x, int y, int w, int h)
    {
        for (int k = 0; k < Samples; k++)
        {
            double v = values[(_newest + 1 + k) % Samples];
            _points[k] = new TPoint(x + (w - 1) * k / (Samples - 1), y + (int)Math.Round((1 - v) * (h - 1) / 2));
        }
        canvas.DrawPolyline(_points, color);
    }
}

// ── TLiveChartDemo ─────────────────────────────────────────────────────────
internal sealed class TLiveChartDemo : GWindow
{
    public TLiveChartDemo() : this(new TRect(8, 4, 58, 18)) { }

    internal TLiveChartDemo(TRect bounds, TLiveChart? chart = null) : base(bounds, "Live chart", Views.wnNoNumber)
    {
        Chart = chart ?? new TLiveChart();
        Insert(Chart);
    }

    internal TLiveChart Chart { get; }
}

public partial class TVDemoApp
{
    // ≡ / Live chart. Like STYX: text drivers get an explanation instead of an empty window.
    private void OpenLiveChart()
    {
        if (!PixelGraphicsAvailable("The live chart")) return;
        int offset = _cascade++ % 4 * 2;
        var win = new TLiveChartDemo(new TRect(8 + offset, 4 + offset / 2, 58 + offset, 18 + offset / 2));
        if (ValidView(win) is { } valid) DeskTop!.Insert(valid);
    }
}
