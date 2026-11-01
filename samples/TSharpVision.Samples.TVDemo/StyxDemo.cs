using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Graphics;
using static TSharpVision.Graphics.GraphConstants;

namespace TSharpVision.Samples.TVDemo;

// ── TStyx ─────────────────────────────────────────────────────────────────
// The flying line of the STYX demo from TVGraph 1.21 (STYX.PAS), ported from the STYX experiment.
// Keeps a history of 16 lines; each tick copies the newest line, moves its end points by the current
// velocities and bounces them off the edges of the logical 10000 x 7500 area. Drawing fades the history:
// the newest line is white, the oldest black.
internal sealed class TStyx : GObject
{
    internal const int Count = 16;

    // Colour index per age: 0 = newest … 15 = oldest (black).
    private static readonly byte[] ColorIdx = { 15, 11, 11, 9, 9, 9, 9, 1, 1, 1, 1, 1, 1, 1, 1, 0 };

    private readonly GLine[] _lines = new GLine[Count];
    private readonly Random _random;
    private int _current;
    private int _vXa, _vYa, _vXb, _vYb;

    public TStyx() : this(Random.Shared) { }

    internal TStyx(Random random) : base(0xFFFFFFFF)
    {
        _random = random;
        for (int i = 0; i < Count; i++)
            _lines[i] = new GLine(GraphXMax / 3, GraphYMax / 3, GraphXMax / 2, GraphYMax / 2, 0xFFFFFFFF);

        _vXa = RandomVel();
        _vYa = RandomVel();
        _vXb = RandomVel();
        _vYb = RandomVel();

        for (int i = 0; i < Count; i++) GraphProd();
    }

    /// <summary>The newest line. Tests.</summary>
    internal GLine Newest => _lines[_current];

    // ±16, ±32, ±48 or ±64, as in STYX.PAS.
    private int RandomVel()
    {
        int v = _random.Next(8) + 1;
        if (v > 4) v = 4 - v;
        return v * 16;
    }

    public override void GraphProd()
    {
        int prev = _current;
        _current = (_current + 1) % Count;

        GLine line = _lines[_current];
        line.ChangeEnds(_lines[prev]);
        line.DeltaEnds(_vXa, _vYa, _vXb, _vYb);

        // Bounce off the edges.
        if (line.Xa <= 0 || line.Xa >= GraphXMax) _vXa = -_vXa;
        if (line.Ya <= 0 || line.Ya >= GraphYMax) _vYa = -_vYa;
        if (line.Xb <= 0 || line.Xb >= GraphXMax) _vXb = -_vXb;
        if (line.Yb <= 0 || line.Yb >= GraphYMax) _vYb = -_vYb;
    }

    public override void GraphDraw(GraphicsCanvas canvas, int px, int py, int pw, int ph)
    {
        for (int count = 0; count < Count; count++)
        {
            int age = (Count - count + _current) % Count;
            _lines[count].Color = CgaPalette[ColorIdx[age]];
            _lines[count].GraphDraw(canvas, px, py, pw, ph);
        }
    }
}

// ── TStyxDemo ──────────────────────────────────────────────────────────────
// A 34 x 12 GWindow titled "S T Y X", as TStyxDemo in the original TVDEMO.PAS.
internal sealed class TStyxDemo : GWindow
{
    public TStyxDemo() : this(new TRect(5, 3, 39, 15)) { }

    internal TStyxDemo(TRect bounds, TStyx? styx = null) : base(bounds, "S T Y X", Views.wnNoNumber)
    {
        Styx = styx ?? new TStyx();
        Insert(Styx);
    }

    internal TStyx Styx { get; }
}

public partial class TVDemoApp
{
    internal const string StyxUnavailableMessage =
        "STYX graphics draws pixels and needs the SDL GPU or SDL display driver.\nThe active driver ({0}) shows text only.";

    // ≡ / STYX graphics. Drivers without IPixelGraphicsDriver (console, terminal, null) get an explanation
    // instead of an empty window; the entry stays in the menu so the demo is discoverable everywhere.
    private void OpenStyx()
    {
        if (DeskTop == null) return;
        if (TDisplay.driver is not IPixelGraphicsDriver)
        {
            MsgBox.MessageBox(DeskTop, string.Format(StyxUnavailableMessage, TDisplay.driver?.GetType().Name ?? "none"),
                MsgBox.mfInformation | MsgBox.mfOKButton);
            return;
        }

        // Successive windows are staggered so each stays visible.
        int offset = _cascade++ % 4 * 2;
        var win = new TStyxDemo(new TRect(5 + offset, 3 + offset / 2, 39 + offset, 15 + offset / 2));
        if (ValidView(win) is { } valid) DeskTop.Insert(valid);
    }
}
