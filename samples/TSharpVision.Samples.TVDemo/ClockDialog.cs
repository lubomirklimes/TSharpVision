using TSharpVision;
using TSharpVision.Constants;

namespace TSharpVision.Samples.TVDemo;

// ---------------------------------------------------------------------------
// ClockContentView — TView that renders the current local time.
// The time provider is injectable for smoke tests.
// TVDemoApp.Idle() calls DrawView() on this view to tick the display.
// ---------------------------------------------------------------------------
internal sealed class ClockContentView : TView
{
    public const int ViewW = 22;
    public const int ViewH = 2;

    // Injectable time provider; defaults to DateTime.Now.
    public Func<DateTime> GetNow { get; set; } = () => DateTime.Now;

    private string _lastTime = string.Empty;

    // Static text role: classic gray (0x70) through ClockDialog in Color, the dialog palette otherwise.
    private static readonly TPalette _palette = new TPalette("\x06", 1);
    public override TPalette GetPalette() => _palette;

    public ClockContentView(TRect bounds) : base(bounds)
    {
        options |= Views.ofSelectable;
    }

    // Format the time as "HH:mm:ss  ddd dd MMM yyyy".
    // This is the string that appears in Draw(); exported for smoke tests.
    public string FormatTime(DateTime dt) =>
        $"{dt:HH:mm:ss}  {dt:ddd dd MMM yyyy}";

    public override void Draw()
    {
        var color = (char)GetColor(1);
        string t = FormatTime(GetNow());

        // Row 0: time line
        var b0 = new TDrawBuffer();
        b0.moveChar(0, ' ', color, size.x);
        b0.moveStr(0, t, color);
        WriteLine(0, 0, size.x, 1, b0);

        // Row 1: subtle label
        var b1 = new TDrawBuffer();
        b1.moveChar(0, ' ', color, size.x);
        b1.moveStr(0, "(Local time)", color);
        WriteLine(0, 1, size.x, 1, b1);
    }

    // Called by TVDemoApp.Idle() to refresh if time changed.
    public bool Tick()
    {
        string t = FormatTime(GetNow());
        if (t == _lastTime) return false;
        _lastTime = t;
        DrawView();
        return true;
    }
}

// ---------------------------------------------------------------------------
// ClockDialog — modeless TDialog that shows the current time.
// TVDemoApp keeps a reference to the embedded ClockContentView
// and calls Tick() from its Idle() override.
// ---------------------------------------------------------------------------
public sealed class ClockDialog : TDialog
{
    public override byte MapColor(int index) =>
        ShowcasePalette.IsClassic(this) ? DemoAppearance.GrayWindow(index) : base.MapColor(index);
    public const int DlgW = ClockContentView.ViewW + 4;   // 26
    public const int DlgH = ClockContentView.ViewH + 4;   // 6

    internal ClockContentView View { get; }

    public ClockDialog(int left = 50, int top = 3)
        : base(new TRect(left, top, left + DlgW, top + DlgH), "Clock")
    {
        View = new ClockContentView(
            new TRect(2, 1, 2 + ClockContentView.ViewW, 1 + ClockContentView.ViewH));
        Insert(View);
        // No Close button needed — the frame close icon handles it.
    }

    // Allow TVDemoApp to tick the view without holding a separate reference.
    public void Tick() => View.Tick();

    // Allow smoke tests to check formatting.
    public string FormatTime(DateTime dt) => View.FormatTime(dt);
}

// ---------------------------------------------------------------------------
// ShowcasePalette — palette family lookup shared by the TVDemo accessories and games (Clock, Calendar,
// Calculator, Puzzle, ASCII chart, Tetris). Their classic literal colors apply in the Color family only; in
// BlackWhite and Monochrome they resolve logical roles through the application palette instead. The family is
// read at draw time, so TProgram.SetAppPalette (whose Redraw re-runs every Draw) updates an open view at once.
// ---------------------------------------------------------------------------
internal static class ShowcasePalette
{
    // Family of the program that owns the view; Color while the view is not inside a program.
    public static TProgram.AP Family(TView view)
    {
        for (TView? v = view; v != null; v = v.owner)
            if (v is TProgram program) return program.AppPalette;
        return TProgram.AP.Color;
    }

    public static bool IsClassic(TView view) => Family(view) == TProgram.AP.Color;
}
