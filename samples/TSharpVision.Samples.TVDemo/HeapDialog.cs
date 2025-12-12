using TSharpVision;
using TSharpVision.Constants;

namespace TSharpVision.Samples.TVDemo;

// ---------------------------------------------------------------------------
// HeapContentView — TView that renders current .NET managed memory.
// Reports GC.GetTotalMemory() as a KB/MB string.
// The memory provider is injectable for smoke tests.
// TVDemoApp.Idle() calls Tick() on this view to refresh the display.
// ---------------------------------------------------------------------------
internal sealed class HeapContentView : TView
{
    public const int ViewW = 28;
    public const int ViewH = 2;

    // Injectable memory provider; defaults to GC.GetTotalMemory(false).
    public Func<long> GetMemory { get; set; } = () => GC.GetTotalMemory(false);

    private string _lastText = string.Empty;

    public HeapContentView(TRect bounds) : base(bounds)
    {
        options |= Views.ofSelectable;
    }

    // Format bytes as "X.XX MB" or "XXX KB".
    // Exported as a static helper for smoke tests.
    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L)
        {
            double mb = bytes / (1024.0 * 1024.0);
            return $"{mb:F2} MB";
        }
        long kb = bytes / 1024;
        return $"{kb} KB";
    }

    public override void Draw()
    {
        var color = (char)GetColor(1);
        string text = $"Managed: {FormatBytes(GetMemory())}";

        var b0 = new TDrawBuffer();
        b0.moveChar(0, ' ', color, size.x);
        b0.moveStr(0, text, color);
        WriteLine(0, 0, size.x, 1, b0);

        var b1 = new TDrawBuffer();
        b1.moveChar(0, ' ', color, size.x);
        b1.moveStr(0, "(GC.GetTotalMemory)", color);
        WriteLine(0, 1, size.x, 1, b1);
    }

    // Called by TVDemoApp.Idle() to refresh if value changed.
    public bool Tick()
    {
        string t = $"Managed: {FormatBytes(GetMemory())}";
        if (t == _lastText) return false;
        _lastText = t;
        DrawView();
        return true;
    }
}

// ---------------------------------------------------------------------------
// HeapDialog — modeless TDialog showing .NET managed memory.
// TVDemoApp keeps a reference and calls Tick() from its Idle() override.
// ---------------------------------------------------------------------------
public sealed class HeapDialog : TDialog
{
    public const int DlgW = HeapContentView.ViewW + 4;   // 32
    public const int DlgH = HeapContentView.ViewH + 4;   // 6

    internal HeapContentView View { get; }

    public HeapDialog(int left = 45, int top = 10)
        : base(new TRect(left, top, left + DlgW, top + DlgH), "Memory")
    {
        View = new HeapContentView(
            new TRect(2, 1, 2 + HeapContentView.ViewW, 1 + HeapContentView.ViewH));
        Insert(View);
    }

    // Allow TVDemoApp to tick the view without holding a separate reference.
    public void Tick() => View.Tick();

    // Convenience for smoke tests.
    public static string FormatBytes(long bytes) => HeapContentView.FormatBytes(bytes);
}
