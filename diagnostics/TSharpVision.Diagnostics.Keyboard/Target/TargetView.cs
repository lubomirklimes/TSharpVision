using TSharpVision.Constants;
using TSharpVision.Diagnostics.Keyboard.Controller;
using TSharpVision.Diagnostics.Keyboard.Protocol;

namespace TSharpVision.Diagnostics.Keyboard.Target;

/// <summary>The target's whole user interface: what is being asked for and the last event received.</summary>
internal sealed class TargetView : TView
{
    private static readonly TPalette Palette = new("\x01", 1);

    public string Driver { get; set; } = string.Empty;
    public string Profile { get; set; } = string.Empty;
    public string Step { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string Expected { get; set; } = string.Empty;
    public string Status { get; set; } = "Waiting for controller...";
    public RecordedEvent? Last { get; set; }

    public TargetView(TRect bounds) : base(bounds)
    {
        growMode = Views.gfGrowHiX | Views.gfGrowHiY;
    }

    public override TPalette GetPalette() => Palette;

    internal IReadOnlyList<string> Lines()
    {
        var lines = new List<string>
        {
            "Keyboard Diagnostic Target",
            string.Empty,
            $"Driver:   {Driver}",
            $"Profile:  {Profile}",
            $"Step:     {Step}",
            $"Action:   {Caption}",
            $"Expected: {Expected}",
            string.Empty,
            "Last event:",
        };
        if (Last is { } ev)
        {
            lines.Add($"  {ev.What}");
            lines.Add($"  Key code: 0x{ev.KeyCode:X4} ({KeyNames.Describe(ev.KeyCode)})");
            lines.Add($"  Text:     {KeyNames.Quote(ev.Text)}");
            lines.Add($"  Raw scan: 0x{ev.RawScan:X2}");
            lines.Add($"  State:    0x{ev.State:X8}");
        }
        else
            lines.Add("  (none)");
        lines.Add(string.Empty);
        lines.Add(Status);
        return lines;
    }

    public override void Draw()
    {
        var color = (char)GetColor(1);
        IReadOnlyList<string> lines = Lines();
        for (int y = 0; y < size.y; y++)
        {
            var buffer = new TDrawBuffer();
            buffer.moveChar(0, ' ', color, size.x);
            if (y >= 1 && y - 1 < lines.Count)
            {
                string line = lines[y - 1];
                buffer.moveStr(2, line[..Math.Min(line.Length, Math.Max(0, size.x - 2))], color);
            }
            WriteLine(0, (short)y, size.x, 1, buffer);
        }
    }
}
