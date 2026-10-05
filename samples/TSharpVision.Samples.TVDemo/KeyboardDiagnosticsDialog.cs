using System.Reflection;
using System.Text.Json;
using TSharpVision.Constants;
using TSharpVision.Drivers;

namespace TSharpVision.Samples.TVDemo;

// All identities come from the public Keys table, including its historical aliases.
internal sealed class KeyboardDiagnosticsState
{
    private static readonly Dictionary<ushort, string> Names = BuildNames();
    public HashSet<string> Pressed { get; } = new();
    public uint Modifiers { get; private set; }
    public TEvent LastEvent { get; private set; }
    public bool HasEvent { get; private set; }
    public KeyboardCapabilities Capabilities { get; private set; }
    public bool Releases => Capabilities.HasFlag(KeyboardCapabilities.KeyReleaseEvents);
    public bool DistinctKeypad => Capabilities.HasFlag(KeyboardCapabilities.DistinctNumericKeypad);
    // A keypad press that carried no text of its own; a text commit may follow it.
    private bool _keypadAwaitsText;
    public bool LastIsKeypadText { get; private set; }

    public KeyboardDiagnosticsState(KeyboardCapabilities capabilities = KeyboardCapabilities.None)
    {
        Capabilities = capabilities;
    }

    private static Dictionary<ushort, string> BuildNames()
    {
        var names = new Dictionary<ushort, string>();
        foreach (var field in typeof(Keys).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(ushort)) continue;
            string name = field.Name[2..];
            foreach (string prefix in new[] { "CtrlShift", "Shift", "Ctrl", "Alt" })
                if (name.StartsWith(prefix, StringComparison.Ordinal)) { name = name[prefix.Length..]; break; }
            if (name is "NoKey" or "PrtSc") continue;
            name = name switch { "Minus" => "-", "Equal" => "=", "GrayMinus" => "KP-", "GrayPlus" => "KP+", _ => name };
            if (name.StartsWith("Keypad", StringComparison.Ordinal)) name = "KP" + name[6..];
            names.TryAdd((ushort)field.GetRawConstantValue()!, name);
        }
        return names;
    }

    public static string? MapKey(ushort code)
    {
        if (Names.TryGetValue(code, out string? name)) return name;
        // Printable identity is separate from driver-supplied Unicode text.
        char ch = (char)code;
        if (ch < ' ' || ch > '~') return null;
        const string shifted = "~!@#$%^&*()_+{}|:\"<>?";
        const string plain = "`1234567890-=[]\\;',./";
        int index = shifted.IndexOf(ch);
        return (index >= 0 ? plain[index] : char.ToUpperInvariant(ch)).ToString() switch
        {
            " " => "Space", var label => label
        };
    }

    public static bool IsKeypad(string? key) =>
        key != null && (key == "NumLock" || key.StartsWith("KP", StringComparison.Ordinal));

    public bool ModifierActive(uint mask) => (Modifiers & mask) != 0;

    public void Record(TEvent ev, KeyboardCapabilities capabilities)
    {
        if (Capabilities != capabilities) Pressed.Clear();
        Capabilities = capabilities;
        Modifiers = ev.keyDown.controlKeyState;
        LastEvent = ev;
        HasEvent = true;
        if (ev.What == Events.evModifierChanged) return;
        string? key = MapKey(ev.keyDown.keyCode);
        bool keypadAwaitedText = _keypadAwaitsText;
        _keypadAwaitsText = false;
        LastIsKeypadText = false;
        if (ev.What == Events.evKeyDown)
        {
            bool keypad = IsKeypad(key);
            bool hasText = ev.keyDown.text.Length != 0;
            _keypadAwaitsText = keypad && !hasText;
            // Some drivers commit a keypad key's text as a second event. That commit has
            // no release of its own and is not a press of the main-keyboard key.
            if (Releases && keypadAwaitedText && hasText && !keypad) { LastIsKeypadText = true; return; }
            if (!Releases) Pressed.Clear();
            if (key != null) Pressed.Add(key);
        }
        else if (ev.What == Events.evKeyUp && Releases && key != null) Pressed.Remove(key);
    }
}

internal sealed class KeyboardDiagnosticsView : TView
{
    internal enum KeyGroup { Function, Typing, Navigation, Keypad, Lock }
    internal enum PaletteRole : ushort { Details = 1, NormalKey, HeldKey, LastKey, ActiveModifier, InactiveModifier }
    internal readonly record struct KeyCell(int X, int Y, int Width, string Label,
        uint Modifier = 0, KeyGroup Group = KeyGroup.Typing, string? Caption = null, int Height = 1);

    // Main block, then the navigation cluster, then the optional numeric keypad.
    internal const int MainWidth = 61;
    internal const int NavigationX = MainWidth + 2;
    internal const int BaseWidth = NavigationX + 15;
    internal const int KeypadX = BaseWidth + 2;
    internal const int KeypadWidth = KeypadX + 19;
    internal const int Rows = 17;

    internal IReadOnlyList<KeyCell> Cells { get; private set; }
    internal bool ShowsKeypad { get; private set; }
    public KeyboardDiagnosticsState Model { get; }

    internal static int WidthFor(KeyboardCapabilities capabilities) =>
        capabilities.HasFlag(KeyboardCapabilities.DistinctNumericKeypad) ? KeypadWidth : BaseWidth;

    public KeyboardDiagnosticsView(KeyboardCapabilities capabilities)
        : base(new TRect(1, 1, 1 + WidthFor(capabilities), 1 + Rows))
    {
        Model = new KeyboardDiagnosticsState(capabilities);
        ShowsKeypad = Model.DistinctKeypad;
        Cells = CreateCells(ShowsKeypad);
    }

    // Local roles map through the dialog's standard palette, never raw attributes.
    private static readonly TPalette Palette = new("\x06\x10\x14\x16\x14\x10", 6);
    public override TPalette GetPalette() => Palette;

    /// <summary>Follows the model's capability; returns true when the keypad appeared or disappeared.</summary>
    internal bool SyncLayout()
    {
        if (ShowsKeypad == Model.DistinctKeypad) return false;
        ShowsKeypad = Model.DistinctKeypad;
        Cells = CreateCells(ShowsKeypad);
        // The owner resizes around this view afterwards, so its current size is no limit.
        SetBounds(new TRect(origin.x, origin.y, origin.x + WidthFor(Model.Capabilities), origin.y + Rows));
        return true;
    }

    internal static IReadOnlyList<KeyCell> CreateCells(bool keypad)
    {
        var cells = new List<KeyCell> { new(0, 0, 5, "Esc", Group: KeyGroup.Function) };
        // F1-F4, F5-F8 and F9-F12 are separate blocks, as on a PC keyboard.
        for (int i = 0; i < 12; i++)
            cells.Add(new(8 + i * 4 + i / 4 * 2, 0, 3, $"F{i + 1}", Group: KeyGroup.Function));
        void Row(int y, int x, string characters)
        {
            foreach (char ch in characters) { cells.Add(new(x, y, 3, ch.ToString())); x += 4; }
        }
        Row(2, 0, "`1234567890-=");
        cells.Add(new(52, 2, 9, "Back", Caption: "Backspace"));
        cells.Add(new(0, 4, 5, "Tab")); Row(4, 6, "QWERTYUIOP[]");
        cells.Add(new(54, 4, 7, "\\"));
        // Caps is a lock indicator, not an invented physical key identity.
        cells.Add(new(0, 6, 6, "Caps", Keys.kbCapsState, KeyGroup.Lock));
        Row(6, 7, "ASDFGHJKL;'"); cells.Add(new(51, 6, 10, "Enter"));
        // The event model reports logical modifiers, so both sides light together.
        cells.Add(new(0, 8, 8, "Shift", Keys.kbShift)); Row(8, 9, "ZXCVBNM,./");
        cells.Add(new(49, 8, 12, "Shift", Keys.kbShift));
        cells.Add(new(0, 10, 6, "Ctrl", Keys.kbCtrlShift));
        cells.Add(new(7, 10, 5, "Alt", Keys.kbAltShift));
        cells.Add(new(13, 10, 33, "Space"));
        cells.Add(new(47, 10, 5, "Alt", Keys.kbAltShift));
        cells.Add(new(53, 10, 8, "Ctrl", Keys.kbCtrlShift));

        string[][] navigation =
        {
            new[] { "Ins", "Home", "PgUp" }, new[] { "Del", "End", "PgDn" },
            Array.Empty<string>(), new[] { "", "Up", "" }, new[] { "Left", "Down", "Right" }
        };
        for (int row = 0; row < navigation.Length; row++)
            for (int col = 0; col < navigation[row].Length; col++)
                if (navigation[row][col].Length != 0)
                    cells.Add(new(NavigationX + col * 5, 2 + row * 2, col == 2 ? 5 : 4,
                        navigation[row][col], Group: KeyGroup.Navigation));

        cells.Add(new(7, 12, 6, "Caps", Keys.kbCapsState, KeyGroup.Lock));
        cells.Add(new(14, 12, 5, "Num", Keys.kbNumState, KeyGroup.Lock));
        cells.Add(new(20, 12, 8, "Scroll", Keys.kbScrollState, KeyGroup.Lock));
        if (!keypad) return cells;

        // Captions are separate from logical identities. The tall + and Enter keys
        // span the rows of their two neighbours and carry their caption once.
        (string Label, string Caption, int Width, int Height)[][] numpad =
        {
            new[] { ("NumLock", "Num", 4, 1), ("KPDivide", "/", 4, 1), ("KPMultiply", "*", 4, 1), ("KP-", "-", 4, 1) },
            new[] { ("KP7", "7", 4, 1), ("KP8", "8", 4, 1), ("KP9", "9", 4, 1), ("KP+", "+", 4, 3) },
            new[] { ("KP4", "4", 4, 1), ("KP5", "5", 4, 1), ("KP6", "6", 4, 1) },
            new[] { ("KP1", "1", 4, 1), ("KP2", "2", 4, 1), ("KP3", "3", 4, 1), ("KPEnter", "Ent", 4, 3) },
            new[] { ("KP0", "0", 9, 1), ("KPDecimal", ".", 4, 1) }
        };
        for (int row = 0; row < numpad.Length; row++)
        {
            int x = KeypadX;
            foreach (var (label, caption, width, height) in numpad[row])
            {
                cells.Add(new(x, 2 + row * 2, width, label, Group: KeyGroup.Keypad, Caption: caption, Height: height));
                x += width + 1;
            }
        }
        return cells;
    }

    internal PaletteRole CellRole(KeyCell cell)
    {
        if (cell.Modifier != 0)
            return Model.ModifierActive(cell.Modifier) ? PaletteRole.ActiveModifier : PaletteRole.InactiveModifier;
        if (Model.Pressed.Contains(cell.Label))
            return Model.Releases ? PaletteRole.HeldKey : PaletteRole.LastKey;
        if (Model.HasEvent && Model.LastEvent.What != Events.evModifierChanged && !Model.LastIsKeypadText &&
            KeyboardDiagnosticsState.MapKey(Model.LastEvent.keyDown.keyCode) == cell.Label)
            return PaletteRole.LastKey;
        return PaletteRole.NormalKey;
    }

    internal string? DetailLine(int y)
    {
        static string YesNo(bool value) => value ? "Yes" : "No";
        KeyDownEvent key = Model.LastEvent.keyDown;
        return y switch
        {
            12 => "Locks:",
            14 when Model.HasEvent => $"Event: {EventName(Model.LastEvent.What)}  Key: {KeyboardDiagnosticsState.MapKey(key.keyCode) ?? "unmapped"}  Text: {JsonSerializer.Serialize(key.text)}",
            14 => "Press keys to inspect input; Esc, Tab and Enter are captured.",
            15 when Model.HasEvent => $"Key code: 0x{key.keyCode:X4}  Legacy scan: 0x{key.charScan.scanCode:X2}  Raw scan: 0x{key.raw_scanCode:X2}  State: 0x{key.controlKeyState:X8}",
            16 => $"KeyUp: {(Model.Releases ? "Yes (held highlight)" : "No (last press highlight)")}  Modifier transitions: {YesNo(Model.Capabilities.HasFlag(KeyboardCapabilities.StandaloneModifierTransitions))}  Distinct keypad: {YesNo(Model.DistinctKeypad)}",
            _ => null
        };
    }

    public override void Draw()
    {
        var normal = (char)GetColor((ushort)PaletteRole.Details);
        for (int y = 0; y < size.y; y++)
        {
            var buffer = new TDrawBuffer();
            buffer.moveChar(0, ' ', normal, size.x);
            foreach (var cell in Cells.Where(c => y >= c.Y && y < c.Y + c.Height))
            {
                char color = (char)GetColor((ushort)CellRole(cell));
                string label = cell.Caption ?? cell.Label;
                buffer.moveChar(cell.X, ' ', color, cell.Width);
                // A tall key is one block with its caption on the middle row only.
                if (y == cell.Y + cell.Height / 2)
                    buffer.moveStr(cell.X + (cell.Width - label.Length) / 2, label, color);
            }
            string? line = DetailLine(y);
            if (line != null) buffer.moveStr(0, line[..Math.Min(line.Length, size.x)], normal);
            WriteLine(0, (short)y, size.x, 1, buffer);
        }
    }

    private static string EventName(ushort what) => what switch
    {
        Events.evKeyDown => "KeyDown", Events.evKeyUp => "KeyUp", _ => "ModifierChanged"
    };
}
internal sealed class KeyboardDiagnosticsDialog : TDialog
{
    private const int Height = 22;
    private readonly Func<KeyboardCapabilities> _capabilities;
    internal KeyboardDiagnosticsView View { get; }

    private static KeyboardCapabilities DriverCapabilities() =>
        TDisplay.driver?.KeyboardCapabilities ?? KeyboardCapabilities.None;
    private static int WidthFor(KeyboardCapabilities capabilities) => KeyboardDiagnosticsView.WidthFor(capabilities) + 2;

    // The capability, never the driver type, decides whether the keypad is drawn.
    public KeyboardDiagnosticsDialog(Func<KeyboardCapabilities>? capabilities = null)
        : base(new TRect(0, 0, WidthFor((capabilities ?? DriverCapabilities)()), Height), "Keyboard Diagnostics")
    {
        _capabilities = capabilities ?? DriverCapabilities;
        eventMask |= Events.evKeyUp | Events.evModifierChanged;
        View = new KeyboardDiagnosticsView(_capabilities());
        Insert(View);
        // Bottom-right corner; it follows the right edge when the keypad appears or disappears.
        Insert(new TButton(new TRect(size.x - 12, Height - 3, size.x - 2, Height - 1), "Close", Views.cmCancel, ButtonConstants.bfNormal)
        {
            growMode = Views.gfGrowLoX | Views.gfGrowHiX
        });
    }

    internal void Center()
    {
        if (owner == null) return;
        MoveTo(Math.Max(0, (owner.size.x - size.x) / 2), Math.Max(0, (owner.size.y - size.y) / 2));
    }

    public override void HandleEvent(ref TEvent ev)
    {
        if (ev.What is Events.evKeyDown or Events.evKeyUp or Events.evModifierChanged)
        {
            View.Model.Record(ev, _capabilities());
            // Terminal protocol negotiation can change the capability while the dialog is open.
            if (View.SyncLayout())
            {
                GrowTo(WidthFor(View.Model.Capabilities), Height);
                Center();
                DrawView();
            }
            else View.DrawView();
            ClearEvent(ref ev);
            return;
        }
        base.HandleEvent(ref ev);
    }
}

public partial class TVDemoApp
{
    private bool _keyboardCapture;
    public override void GetEvent(ref TEvent ev)
    {
        if (!_keyboardCapture) { base.GetEvent(ref ev); return; }
        // TProgram.GetEvent otherwise converts status shortcuts before modal dispatch.
        var statusLine = StatusLine;
        try { StatusLine = null; base.GetEvent(ref ev); }
        finally { StatusLine = statusLine; }
    }
    private void OpenKeyboardDlg()
    {
        var dialog = new KeyboardDiagnosticsDialog();
        if (ValidView(dialog) == null) return;
        dialog.MoveTo(Math.Max(0, (DeskTop!.size.x - dialog.size.x) / 2), Math.Max(0, (DeskTop.size.y - dialog.size.y) / 2));
        _keyboardCapture = true;
        try { DeskTop.ExecView(dialog); }
        finally { _keyboardCapture = false; dialog.ShutDown(); }
    }
}
