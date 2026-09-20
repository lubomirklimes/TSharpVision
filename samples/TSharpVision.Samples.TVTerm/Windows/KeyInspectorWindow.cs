using TSharpVision.Constants;
using TSharpVision.Terminal;

namespace TSharpVision.Samples.TVTerm;

/// <summary>
/// Window that intercepts every key press and pretty-prints the raw event
/// fields (keyCode / charScan / scan / controlKeyState / text) into an embedded
/// read-only TTerminal. Useful for testing keyboard mapping.
/// </summary>
public sealed class KeyInspectorWindow : TWindow
{
    private readonly TTerminal _term;
    private readonly InMemoryTerminalSession _session;

    public KeyInspectorWindow(TRect bounds)
        : base(bounds, "Key Inspector", Views.wnNoNumber)
    {
        flags |= Views.wfClose | Views.wfMove;
        growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);

        int w = bounds.b.x - bounds.a.x;
        int h = bounds.b.y - bounds.a.y;
        _term = new TTerminal(new TRect(1, 1, w - 1, h - 1));
        _term.InputEnabled = false;
        _term.growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
        Insert(_term);

        _session = new InMemoryTerminalSession();
        _term.AttachSession(_session);
        _session.StartAsync().GetAwaiter().GetResult();
        _session.Emit("Key inspector — press keys, close with Alt+F3.\n");
        _session.Emit("keyCode  char  U+      scan  shift  charByte  text\n");
        _session.Emit(new string('-', 55) + "\n");
    }

    public override void HandleEvent(ref TEvent ev)
    {
        if (ev.What == Events.evKeyDown)
        {
            var kd = ev.keyDown;

            // Prefer kd.text (full Unicode) over charScan.charCode (byte, ASCII only).
            // charScan.charCode is 0 for Unicode chars outside Latin-1.
            string text = kd.text ?? string.Empty;
            char displayChar = text.Length == 1 ? text[0]
                : kd.charScan.charCode >= 32 ? (char)kd.charScan.charCode
                : '\0';
            string charCol  = displayChar >= 32 ? displayChar.ToString() : ".";
            string ucodeCol = displayChar != '\0' ? $"{(int)displayChar:X4}" : "----";

            string line = string.Format(
                "{0:X4}     {1,-4}  {2,-6}  {3:X2}    {4:X4}   {5:X2}        {6}\n",
                kd.keyCode,
                charCol,
                ucodeCol,
                kd.charScan.scanCode,
                kd.controlKeyState,
                kd.charScan.charCode,
                text);
            _session.Emit(line);
            ClearEvent(ref ev);
            return;
        }
        base.HandleEvent(ref ev);
    }
}
