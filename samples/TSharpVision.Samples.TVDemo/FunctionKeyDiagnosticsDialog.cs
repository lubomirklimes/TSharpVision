using System.Text.Json;
using TSharpVision.Constants;
using TSharpVision.Drivers;

namespace TSharpVision.Samples.TVDemo;

/// <summary>A matrix column: the modifiers reported on the function-key event itself.</summary>
internal enum FunctionKeyColumn { Plain, Shift, Ctrl, Alt, Cmd }

/// <summary>
/// Records which F1-F10 combinations the application actually received. Nothing is inferred
/// from what the platform could send: a cell is marked only by a key-down event carrying
/// exactly that combination.
/// </summary>
internal sealed class FunctionKeyDiagnosticsState
{
    public const int Rows = 10;
    public const int Columns = 5;
    /// <summary>The modifier bits of the public event model: Shift, Ctrl, Alt and Meta.</summary>
    public const uint ModifierMask = Keys.kbShift | Keys.kbCtrlShift | Keys.kbAltShift | Keys.kbMetaShift;

    /// <summary>What a user calls <see cref="Keys.kbMetaShift"/>: Cmd on a Mac keyboard, Meta elsewhere.</summary>
    public static string MetaName(bool macOS) => macOS ? "Cmd" : "Meta";
    public static readonly string Meta = MetaName(OperatingSystem.IsMacOS());
    public static readonly string[] ColumnTitles = { "Plain", "Shift", "Ctrl", "Alt/Opt", Meta };

    // One table per modifier, F1-F12, in the order the drivers choose a key code: the code
    // carries the highest-precedence modifier, controlKeyState carries the whole combination.
    private static readonly ushort[][] Tables =
    {
        new[] { Keys.kbF1, Keys.kbF2, Keys.kbF3, Keys.kbF4, Keys.kbF5, Keys.kbF6,
                Keys.kbF7, Keys.kbF8, Keys.kbF9, Keys.kbF10, Keys.kbF11, Keys.kbF12 },
        new[] { Keys.kbShiftF1, Keys.kbShiftF2, Keys.kbShiftF3, Keys.kbShiftF4, Keys.kbShiftF5, Keys.kbShiftF6,
                Keys.kbShiftF7, Keys.kbShiftF8, Keys.kbShiftF9, Keys.kbShiftF10, Keys.kbShiftF11, Keys.kbShiftF12 },
        new[] { Keys.kbCtrlF1, Keys.kbCtrlF2, Keys.kbCtrlF3, Keys.kbCtrlF4, Keys.kbCtrlF5, Keys.kbCtrlF6,
                Keys.kbCtrlF7, Keys.kbCtrlF8, Keys.kbCtrlF9, Keys.kbCtrlF10, Keys.kbCtrlF11, Keys.kbCtrlF12 },
        new[] { Keys.kbAltF1, Keys.kbAltF2, Keys.kbAltF3, Keys.kbAltF4, Keys.kbAltF5, Keys.kbAltF6,
                Keys.kbAltF7, Keys.kbAltF8, Keys.kbAltF9, Keys.kbAltF10, Keys.kbAltF11, Keys.kbAltF12 },
    };
    private static readonly uint[] TableModifiers = { 0, Keys.kbShift, Keys.kbCtrlShift, Keys.kbAltShift };

    private readonly bool[,] _marks = new bool[Rows, Columns];
    private readonly List<string> _other = new();

    /// <summary>Function-key combinations outside the matrix (several modifiers, F11, F12), oldest first.</summary>
    public IReadOnlyList<string> Other => _other;
    public TEvent LastEvent { get; private set; }
    public bool HasEvent { get; private set; }
    public int EventCount { get; private set; }

    public const int Cells = Rows * Columns;

    public bool IsMarked(int number, FunctionKeyColumn column) => _marks[number - 1, (int)column];

    public int MarkedCount
    {
        get
        {
            int count = 0;
            foreach (bool mark in _marks) if (mark) count++;
            return count;
        }
    }

    /// <summary>Recognizes every key code of F1-F12, with the modifier the code itself names.</summary>
    public static bool TryGetFunctionKey(ushort keyCode, out int number, out uint implied)
    {
        for (int table = 0; table < Tables.Length; table++)
        {
            int index = Array.IndexOf(Tables[table], keyCode);
            if (index < 0) continue;
            number = index + 1;
            implied = TableModifiers[table];
            return true;
        }
        number = 0;
        implied = 0;
        return false;
    }

    /// <summary>The Shift, Ctrl, Alt and Meta bits the event carries, plus the one its own key code names.</summary>
    public static uint EventModifiers(in TEvent ev)
    {
        uint modifiers = ev.keyDown.controlKeyState & ModifierMask;
        if (ev.What != Events.evModifierChanged && TryGetFunctionKey(ev.keyDown.keyCode, out _, out uint implied))
            modifiers |= implied;
        return modifiers;
    }

    /// <summary>The matrix column of an exact single-modifier (or no-modifier) combination; null for anything else.</summary>
    public static FunctionKeyColumn? Classify(uint modifiers) => (modifiers & ModifierMask) switch
    {
        0 => FunctionKeyColumn.Plain,
        Keys.kbShift => FunctionKeyColumn.Shift,
        Keys.kbCtrlShift => FunctionKeyColumn.Ctrl,
        Keys.kbAltShift => FunctionKeyColumn.Alt,
        Keys.kbMetaShift => FunctionKeyColumn.Cmd,
        _ => null,
    };

    public static string ModifierText(uint modifiers) => ModifierText(modifiers, Meta);

    public static string ModifierText(uint modifiers, string meta)
    {
        var parts = new List<string>(4);
        if ((modifiers & Keys.kbShift) != 0) parts.Add("Shift");
        if ((modifiers & Keys.kbCtrlShift) != 0) parts.Add("Ctrl");
        if ((modifiers & Keys.kbAltShift) != 0) parts.Add("Alt");
        if ((modifiers & Keys.kbMetaShift) != 0) parts.Add(meta);
        return parts.Count == 0 ? "none" : string.Join("+", parts);
    }

    /// <summary>The key alone, without modifiers: "F5", "Tab", "A".</summary>
    public static string KeyText(in TEvent ev)
    {
        if (ev.What == Events.evModifierChanged) return "(modifier only)";
        ushort code = ev.keyDown.keyCode;
        if (TryGetFunctionKey(code, out int number, out _)) return $"F{number}";
        string? name = KeyboardDiagnosticsState.MapKey(code);
        if (name != null) return name;
        return code == 0 ? "(text only)" : $"0x{code:X4}";
    }

    public static string CombinationText(in TEvent ev)
    {
        uint modifiers = EventModifiers(ev);
        return modifiers == 0 ? KeyText(ev) : $"{ModifierText(modifiers)}+{KeyText(ev)}";
    }

    public void Record(in TEvent ev)
    {
        LastEvent = ev;
        HasEvent = true;
        EventCount++;
        // Only a press is a received combination; a release repeats the identity of its press.
        if (ev.What != Events.evKeyDown || !TryGetFunctionKey(ev.keyDown.keyCode, out int number, out _)) return;
        FunctionKeyColumn? column = number <= Rows ? Classify(EventModifiers(ev)) : null;
        if (column is { } exact) _marks[number - 1, (int)exact] = true;
        else
        {
            string combination = CombinationText(ev);
            if (!_other.Contains(combination)) _other.Add(combination);
        }
    }

    /// <summary>Clears what this diagnostic recorded. Driver and application state are not touched.</summary>
    public void Reset()
    {
        Array.Clear(_marks);
        _other.Clear();
        LastEvent = default;
        HasEvent = false;
        EventCount = 0;
    }
}

internal sealed class FunctionKeyDiagnosticsView : TView
{
    internal enum PaletteRole : ushort { Text = 1, Heading, Empty, Marked }
    internal readonly record struct Segment(int X, string Text, PaletteRole Role);

    internal const int ViewWidth = 76;
    internal const int ViewHeight = 17;
    internal const int MatrixX = 2;
    internal const int LabelWidth = 6;
    internal const int CellWidth = 8;
    internal const int HeaderY = 1;
    internal const int FirstRowY = 3;
    internal const int DividerX = MatrixX + LabelWidth + FunctionKeyDiagnosticsState.Columns * CellWidth;
    internal const int DetailsX = DividerX + 2;
    internal const int DetailLabelWidth = 11;
    internal const int OtherY = 14;
    internal const int LegendY = 16;
    private const int TextWidth = ViewWidth - 2 * MatrixX;

    private static readonly TPalette Palette = new("\x06\x09\x10\x14", 4);
    private readonly Func<IDriver?> _driver;

    public FunctionKeyDiagnosticsState Model { get; } = new();

    public FunctionKeyDiagnosticsView(Func<IDriver?>? driver = null)
        : base(new TRect(1, 1, 1 + ViewWidth, 1 + ViewHeight))
    {
        _driver = driver ?? (() => TDisplay.driver);
    }

    public override TPalette GetPalette() => Palette;

    internal static int ColumnX(FunctionKeyColumn column) => MatrixX + LabelWidth + (int)column * CellWidth;

    internal static string CellText(bool marked) => marked ? $"[{TSharpVisionGlyphs.CheckBoxChecked}]" : "[ ]";

    /// <summary>A short, stable name for the running backend, read from the driver instance itself.</summary>
    internal static string DriverName(IDriver? driver) => driver?.GetType().Name switch
    {
        null => "none",
        "AnsiTerminalDriver" => "Terminal",
        "SDLDriver" => "SDL",
        "SDLGpuDriver" => "SDL GPU",
        "Win32ConsoleDriver" => "Console",
        "NullDriver" => "Null",
        var name => name,
    };

    /// <summary>The terminal program, when a text driver runs inside one that names itself.</summary>
    internal static string HostName(IDriver? driver)
    {
        if (driver == null) return "-";
        if (driver.SupportsGraphics) return "native window";
        string? program = Environment.GetEnvironmentVariable("TERM_PROGRAM");
        return string.IsNullOrEmpty(program) ? "unknown" : program;
    }

    internal static string EventName(ushort what) => what switch
    {
        Events.evKeyDown => "KeyDown",
        Events.evKeyUp => "KeyUp",
        Events.evModifierChanged => "ModifierChanged",
        _ => $"0x{what:X4}",
    };

    internal string? DetailLine(int y)
    {
        static string YesNo(bool value) => value ? "Yes" : "No";
        IDriver? driver = _driver();
        KeyboardCapabilities capabilities = driver?.KeyboardCapabilities ?? KeyboardCapabilities.None;
        TEvent last = Model.LastEvent;
        KeyDownEvent key = last.keyDown;
        bool has = Model.HasEvent;
        (string Label, string Value)? line = y switch
        {
            1 => ("Driver:", DriverName(driver)),
            2 => ("Host:", HostName(driver)),
            3 => ("KeyUp:", YesNo(capabilities.HasFlag(KeyboardCapabilities.KeyReleaseEvents))),
            4 => ("Mod keys:", YesNo(capabilities.HasFlag(KeyboardCapabilities.StandaloneModifierTransitions))),
            5 => ($"{FunctionKeyDiagnosticsState.Meta} key:", YesNo(capabilities.HasFlag(KeyboardCapabilities.StandaloneMetaTransitions))),
            6 => ("Event:", has ? EventName(last.What) : "-"),
            7 => ("Last key:", has ? FunctionKeyDiagnosticsState.KeyText(last) : "-"),
            8 => ("Modifiers:", has ? FunctionKeyDiagnosticsState.ModifierText(FunctionKeyDiagnosticsState.EventModifiers(last)) : "-"),
            9 => ("Key code:", has ? $"0x{key.keyCode:X4}" : "-"),
            10 => ("Scan:", has ? $"0x{key.charScan.scanCode:X2} raw 0x{key.raw_scanCode:X2}" : "-"),
            11 => ("State:", has ? $"0x{key.controlKeyState:X8}" : "-"),
            12 => ("Text:", has ? JsonSerializer.Serialize(key.text ?? string.Empty) : "-"),
            13 => ("Events:", Model.EventCount.ToString()),
            _ => null,
        };
        if (line is not { } value) return null;
        string text = value.Label.PadRight(DetailLabelWidth) + value.Value;
        return text.Length <= ViewWidth - DetailsX ? text : text[..(ViewWidth - DetailsX)];
    }

    /// <summary>Everything outside the matrix on two lines; the oldest entries give way to the newest.</summary>
    internal string[] OtherLines()
    {
        const string prefix = "Other: ", indent = "       ";
        IReadOnlyList<string> entries = Model.Other;
        if (entries.Count == 0) return new[] { prefix + "none (several modifiers, F11 and F12 appear here)", string.Empty };
        for (int dropped = 0; dropped < entries.Count; dropped++)
        {
            var lines = new List<string> { dropped == 0 ? prefix : $"{prefix}(+{dropped} earlier) " };
            for (int i = dropped; i < entries.Count; i++)
            {
                string piece = entries[i] + (i < entries.Count - 1 ? "," : string.Empty);
                string line = lines[^1];
                string candidate = line.EndsWith(' ') ? line + piece : line + " " + piece;
                if (candidate.Length <= TextWidth) lines[^1] = candidate;
                else lines.Add(indent + piece);
            }
            if (lines.Count <= 2) return new[] { lines[0], lines.Count > 1 ? lines[1] : string.Empty };
        }
        return new[] { prefix + entries[^1], string.Empty };
    }

    internal string LegendLine() =>
        $"[{TSharpVisionGlyphs.CheckBoxChecked}] received  [ ] not received" +
        $"   {Model.MarkedCount}/{FunctionKeyDiagnosticsState.Cells}";

    internal IEnumerable<Segment> Segments(int y)
    {
        if (y == 0)
            yield return new(MatrixX, $"Press F1-F10 alone or with Shift, Ctrl, Alt/Option or {FunctionKeyDiagnosticsState.Meta}.", PaletteRole.Text);
        if (y == HeaderY)
        {
            yield return new(MatrixX, "Key", PaletteRole.Heading);
            foreach (FunctionKeyColumn column in Enum.GetValues<FunctionKeyColumn>())
            {
                string title = FunctionKeyDiagnosticsState.ColumnTitles[(int)column];
                yield return new(ColumnX(column) + (CellWidth - title.Length) / 2, title, PaletteRole.Heading);
            }
        }
        if (y == HeaderY + 1)
            yield return new(MatrixX, new string(TSharpVisionGlyphs.FrameHorizontal, DividerX - MatrixX - 1), PaletteRole.Text);
        int number = y - FirstRowY + 1;
        if (number is >= 1 and <= FunctionKeyDiagnosticsState.Rows)
        {
            yield return new(MatrixX, $"F{number}", PaletteRole.Heading);
            foreach (FunctionKeyColumn column in Enum.GetValues<FunctionKeyColumn>())
            {
                bool marked = Model.IsMarked(number, column);
                PaletteRole role = marked ? PaletteRole.Marked : PaletteRole.Empty;
                // The whole cell is painted, so a marked cell is a solid block as well as an [X].
                yield return new(ColumnX(column) + 1, $"  {CellText(marked)}  ", role);
            }
        }
        if (y >= HeaderY && y <= FirstRowY + FunctionKeyDiagnosticsState.Rows)
            yield return new(DividerX, TSharpVisionGlyphs.FrameVertical.ToString(), PaletteRole.Text);
        string? detail = DetailLine(y);
        if (detail != null) yield return new(DetailsX, detail, PaletteRole.Text);
        if (y is OtherY or OtherY + 1)
            yield return new(MatrixX, OtherLines()[y - OtherY], PaletteRole.Text);
        if (y == LegendY) yield return new(MatrixX, LegendLine(), PaletteRole.Text);
    }

    /// <summary>The row's characters as drawn, for layout checks.</summary>
    internal string RowText(int y)
    {
        var row = new char[ViewWidth];
        Array.Fill(row, ' ');
        foreach (var segment in Segments(y))
            for (int i = 0; i < segment.Text.Length && segment.X + i < ViewWidth; i++)
                row[segment.X + i] = segment.Text[i];
        return new string(row);
    }

    public override void Draw()
    {
        var normal = (char)GetColor((ushort)PaletteRole.Text);
        for (int y = 0; y < size.y; y++)
        {
            var buffer = new TDrawBuffer();
            buffer.moveChar(0, ' ', normal, size.x);
            foreach (var segment in Segments(y))
            {
                int length = Math.Min(segment.Text.Length, size.x - segment.X);
                if (length > 0)
                    buffer.moveStr(segment.X, segment.Text[..length], (char)GetColor((ushort)segment.Role));
            }
            WriteLine(0, (short)y, size.x, 1, buffer);
        }
    }
}

/// <summary>
/// Modal, like the Keyboard diagnostic: while it runs the application stops turning status-line
/// shortcuts (F1 Help, F10 Menu, Alt+F3, F5 ...) into commands, so every function-key
/// combination reaches this dialog first. Closing it restores normal behaviour.
/// </summary>
internal sealed class FunctionKeyDiagnosticsDialog : TDialog
{
    internal const int DialogWidth = FunctionKeyDiagnosticsView.ViewWidth + 2;
    internal const int DialogHeight = FunctionKeyDiagnosticsView.ViewHeight + 5;
    internal FunctionKeyDiagnosticsView View { get; }
    internal TButton ResetButton { get; }
    internal TButton CloseButton { get; }

    public FunctionKeyDiagnosticsDialog(Func<IDriver?>? driver = null)
        : base(new TRect(0, 0, DialogWidth, DialogHeight), "Function Key Diagnostics")
    {
        eventMask |= Events.evKeyUp | Events.evModifierChanged;
        View = new FunctionKeyDiagnosticsView(driver);
        Insert(View);
        int buttonY = DialogHeight - 4;
        ResetButton = new TButton(new TRect(0, buttonY, 12, buttonY + 2), "~R~eset", TVDemoCmd.cmFunctionKeysReset, ButtonConstants.bfNormal);
        CloseButton = new TButton(new TRect(14, buttonY, 26, buttonY + 2), "~C~lose", Views.cmCancel, ButtonConstants.bfNormal);
        Insert(ResetButton);
        Insert(CloseButton);
        DemoButtonLayout.CenterRows(this);
        ResetButton.Select();
    }

    /// <summary>
    /// Keys that operate the dialog's own buttons. Everything else, Esc and Enter included, is
    /// only recorded: an undecoded escape sequence must not close the dialog or press a button.
    /// </summary>
    internal static bool ReachesControls(in TEvent ev) =>
        ev.What == Events.evKeyDown && ev.keyDown.keyCode switch
        {
            Keys.kbTab or Keys.kbShiftTab or Keys.kbAltR or Keys.kbAltC => true,
            Keys.kbSpace => (ev.keyDown.controlKeyState & (Keys.kbCtrlShift | Keys.kbAltShift | Keys.kbMetaShift)) == 0,
            _ => false,
        };

    public override void HandleEvent(ref TEvent ev)
    {
        if (ev.What is Events.evKeyDown or Events.evKeyUp or Events.evModifierChanged)
        {
            View.Model.Record(ev);
            View.DrawView();
            if (!ReachesControls(ev))
            {
                ClearEvent(ref ev);
                return;
            }
        }
        else if (ev.What == Events.evCommand && ev.message.command == TVDemoCmd.cmFunctionKeysReset)
        {
            View.Model.Reset();
            View.DrawView();
            ClearEvent(ref ev);
            return;
        }
        base.HandleEvent(ref ev);
    }
}

public partial class TVDemoApp
{
    private void OpenFunctionKeyDiagnostics()
    {
        var dialog = new FunctionKeyDiagnosticsDialog();
        if (ValidView(dialog) == null) return;
        dialog.MoveTo(Math.Max(0, (DeskTop!.size.x - dialog.size.x) / 2), Math.Max(0, (DeskTop.size.y - dialog.size.y) / 2));
        // The capture the Keyboard diagnostic uses: no status-line shortcuts, raw key identities.
        _keyboardCapture = true;
        try { DeskTop.ExecView(dialog); }
        finally { _keyboardCapture = false; dialog.ShutDown(); }
    }
}
