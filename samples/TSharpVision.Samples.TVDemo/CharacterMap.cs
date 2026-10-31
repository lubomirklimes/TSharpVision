using System.Globalization;
using System.Text;
using TSharpVision.Constants;

namespace TSharpVision.Samples.TVDemo;

// No Unicode name database is bundled: preset names below are block labels only.
internal sealed record CharacterMapRange(string Label, int First, int Last)
{
    public override string ToString() => $"{Label} ({First:X4}-{Last:X4})";
}

internal sealed class CharacterMapModel
{
    internal const int Maximum = 0x10FFFF;
    internal static readonly CharacterMapRange[] Ranges =
    {
        new("Basic Latin", 0, 0x7F), new("Latin-1 Supplement", 0x80, 0xFF),
        new("Box Drawing", 0x2500, 0x257F), new("Block Elements", 0x2580, 0x259F),
        new("Arrows", 0x2190, 0x21FF), new("Geometric Shapes", 0x25A0, 0x25FF),
        new("Miscellaneous Symbols", 0x2600, 0x26FF), new("Dingbats", 0x2700, 0x27BF),
        new("Braille Patterns", 0x2800, 0x28FF)
    };
    internal int Selected { get; private set; } = 0x2500;
    internal int TopRow { get; private set; } = 0x250;
    internal int Rows { get; private set; } = 8;
    internal bool IsScalar => Rune.IsValid(Selected);
    internal string Notation => $"U+{Selected:X4}";
    internal string Source => !IsScalar ? "invalid scalar" : Selected <= 0xFFFF ? $@"\u{Selected:X4}" : $@"\U{Selected:X8}";
    internal string? Character => IsScalar ? new Rune(Selected).ToString() : null;
    internal string Utf8 => Character is string s ? string.Join(" ", Encoding.UTF8.GetBytes(s).Select(b => $"{b:X2}")) : "none (surrogate)";
    // BMP wide ranges from the existing TerminalCharWidth policy.
    // Sample-only subset; ambiguous widths assume narrow, as in that policy.
    private static readonly (int First, int Last)[] WidePreview =
    {
        (0x1100, 0x115F),
        (0x231A, 0x231B),
        (0x2329, 0x232A),
        (0x23E9, 0x23EC),
        (0x23F0, 0x23F0),
        (0x23F3, 0x23F3),
        (0x25FD, 0x25FE),
        (0x2614, 0x2615),
        (0x2648, 0x2653),
        (0x267F, 0x267F),
        (0x2693, 0x2693),
        (0x26A1, 0x26A1),
        (0x26AA, 0x26AB),
        (0x26BD, 0x26BE),
        (0x26C4, 0x26C5),
        (0x26CE, 0x26CE),
        (0x26D4, 0x26D4),
        (0x26EA, 0x26EA),
        (0x26F2, 0x26F3),
        (0x26F5, 0x26F5),
        (0x26FA, 0x26FA),
        (0x26FD, 0x26FD),
        (0x2705, 0x2705),
        (0x270A, 0x270B),
        (0x2728, 0x2728),
        (0x274C, 0x274C),
        (0x274E, 0x274E),
        (0x2753, 0x2755),
        (0x2757, 0x2757),
        (0x2795, 0x2797),
        (0x27B0, 0x27B0),
        (0x27BF, 0x27BF),
        (0x2B1B, 0x2B1C),
        (0x2B50, 0x2B50),
        (0x2B55, 0x2B55),
        (0x2E80, 0x303E),
        (0x3041, 0x33FF),
        (0x3400, 0x4DBF),
        (0x4E00, 0x9FFF),
        (0xA000, 0xA4CF),
        (0xA960, 0xA97F),
        (0xAC00, 0xD7A3),
        (0xF900, 0xFAFF),
        (0xFE10, 0xFE19),
        (0xFE30, 0xFE6F),
        (0xFF00, 0xFF60),
        (0xFFE0, 0xFFE6),
    };
    internal static string Policy(int code)
    {
        if (!Rune.IsValid(code)) return "invalid scalar (surrogate)";
        var category = Rune.GetUnicodeCategory(new Rune(code));
        if (category == UnicodeCategory.Control) return "control: placeholder";
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            return "combining mark: placeholder";
        if (category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            return "format / zero-width: placeholder";
        if (category == UnicodeCategory.OtherNotAssigned) return "unassigned: placeholder";
        if (category == UnicodeCategory.PrivateUse) return "private use: layout unknown";
        if (category == UnicodeCategory.SpaceSeparator) return "space: placeholder";
        // TScreenChar stores char, not Rune. Conservatively preview only the
        // BMP, excluding Hangul jamo, wide symbols, fillers and default emoji.
        // All other ranges remain navigable and copy their exact scalar.
        if (code > 0xFFFF || code is >= 0x1100 and <= 0x11FF || code is 0x3164 or 0xFFA0 || WidePreview.Any(r => code >= r.First && code <= r.Last))
            return "wide / layout unknown: placeholder";
        return "glyph preview; coverage not determined";
    }
    internal static char Glyph(int code) => Policy(code).StartsWith("glyph", StringComparison.Ordinal) ? (char)code : '.';
    internal static bool TryParse(string? text, out int code, out string error)
    {
        string value = (text ?? "").Trim();
        if (value.StartsWith("U+", StringComparison.OrdinalIgnoreCase) || value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        if (value.Length == 0 || !int.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code) || code < 0 || code > Maximum)
        {
            code = 0; error = "Enter hex 0000-10FFFF, e.g. 251C, U+251C or 0x251C."; return false;
        }
        if (!Rune.IsValid(code)) { error = "D800-DFFF are surrogates, not Unicode scalar values."; return false; }
        error = ""; return true;
    }
    internal void Select(int code)
    {
        Selected = Math.Clamp(code, 0, Maximum);
        EnsureVisible();
    }
    internal void Resize(int rows) { Rows = Math.Max(1, rows); EnsureVisible(); }
    private void EnsureVisible()
    {
        int row = Selected / 16;
        if (row < TopRow) TopRow = row;
        if (row >= TopRow + Rows) TopRow = row - Rows + 1;
        TopRow = Math.Clamp(TopRow, 0, Math.Max(0, Maximum / 16 + 1 - Rows));
    }
    internal void Move(int delta)
    {
        int next = Math.Clamp(Selected + delta, 0, Maximum);
        if (next is >= 0xD800 and <= 0xDFFF) next = delta < 0 ? 0xD7FF : 0xE000;
        Select(next);
    }
    internal bool Copy(int kind, IClipboardService clipboard, out string message)
    {
        if (!IsScalar) { message = "Cannot copy an invalid scalar."; return false; }
        string text = kind == 1 ? Notation : kind == 2 ? Source : Character!;
        try
        {
            if (clipboard.IsAvailable && clipboard.SetText(text)) { message = "Copied " + (kind == 0 ? "selected scalar." : "notation."); return true; }
        }
        catch (Exception) { /* Host clipboard may be busy or inaccessible. */ }
        message = "Clipboard unavailable. Use the displayed U+ / C# notation.";
        return false;
    }
}

internal sealed class CharacterMapView : TView
{
    internal CharacterMapModel Model { get; } = new();
    internal string Feedback { get; set; } = string.Empty;
    internal int GridRows => Math.Max(1, size.y - 10);
    internal int CellWidth => size.x >= 56 ? 3 : 2;
    internal CharacterMapView(TRect bounds) : base(bounds)
    {
        options |= Views.ofSelectable;
        growMode = Views.gfGrowHiX | Views.gfGrowHiY;
        eventMask |= Events.evMouseDown;
        Model.Resize(GridRows);
    }
    public override void ChangeBounds(TRect bounds)
    {
        base.ChangeBounds(bounds);
        Model.Resize(GridRows);
        DrawView();
    }
    public override void Draw()
    {
        int rows = GridRows;
        ushort normal = GetColor(6), selected = GetColor(7);
        string[] details =
        {
            $"{Model.Notation}  C#: {Model.Source}  Preview: {CharacterMapModel.Glyph(Model.Selected)}",
            "UTF-8: " + Model.Utf8,
            CharacterMapModel.Policy(Model.Selected),
            Feedback,
            "Arrows:select  PgUp/PgDn or [ ]:page  Home/End:full range",
            "G:Go to  R:Ranges  C:Copy char  N:Copy U+  S:Copy C#",
            "Dots: unsafe cell layout. Font coverage is not determined.",
            "Glyphs depend on driver/font; a box is not proof of support."
        };
        for (int y = 0; y < size.y; y++)
        {
            var buffer = new TDrawBuffer(new TScreenChar[Math.Max(0, size.x)]);
            buffer.moveChar(0, ' ', normal, size.x);
            if (y == 0)
            {
                buffer.moveStr(0, "Row", normal);
                for (int col = 0; col < 16; col++) buffer.moveStr(8 + col * CellWidth, col.ToString("X"), normal);
            }
            else if (y <= rows)
            {
                int start = (Model.TopRow + y - 1) * 16;
                if (start <= CharacterMapModel.Maximum)
                {
                    buffer.moveStr(0, $"{start:X6}", normal);
                    for (int col = 0; col < 16; col++)
                        buffer.moveChar(8 + col * CellWidth, CharacterMapModel.Glyph(start + col), start + col == Model.Selected ? selected : normal, 1);
                }
            }
            else if (y == rows + 1) buffer.moveChar(0, '\u2500', normal, size.x);
            else if (y - rows - 2 < details.Length) buffer.moveStr(0, details[y - rows - 2], normal);
            WriteLine(0, y, size.x, 1, buffer);
        }
    }
    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);
        if (ev.What == Events.evMouseDown)
        {
            var point = MakeLocal(ev.mouse.where);
            if (point.y < 1 || point.y > GridRows || point.x < 8 || point.x >= 8 + 16 * CellWidth) return;
            Model.Select((Model.TopRow + point.y - 1) * 16 + (point.x - 8) / CellWidth);
        }
        else if (ev.What == Events.evKeyboard)
        {
            int delta = ev.keyDown.keyCode switch
            {
                Keys.kbLeft => -1, Keys.kbRight => 1, Keys.kbUp => -16, Keys.kbDown => 16,
                Keys.kbPgUp => -16 * Model.Rows, Keys.kbPgDn => 16 * Model.Rows, _ => 0
            };
            if (delta != 0) Model.Move(delta);
            else if (ev.keyDown.keyCode == Keys.kbHome) Model.Select(0);
            else if (ev.keyDown.keyCode == Keys.kbEnd) Model.Select(CharacterMapModel.Maximum);
            else
            {
                string text = KeyText.PrintableText(ev.keyDown).ToUpperInvariant();
                switch (text)
                {
                    case "[": Model.Move(-16 * Model.Rows); break;
                    case "]": Model.Move(16 * Model.Rows); break;
                    case "G": GoTo(); break;
                    case "R": ChooseRange(); break;
                    case "C": Model.Copy(0, ClipboardService.Current, out string c); Feedback = c; break;
                    case "N": Model.Copy(1, ClipboardService.Current, out string n); Feedback = n; break;
                    case "S": Model.Copy(2, ClipboardService.Current, out string s); Feedback = s; break;
                    default: return;
                }
            }
        }
        else return;
        DrawView();
        ClearEvent(ref ev);
    }
    // Dialog palettes map application roles, so host helpers beside the window
    // on the desktop rather than through the blue window's palette.
    private TGroup? DialogHost => owner?.owner;

    private void GoTo()
    {
        var host = DialogHost;
        if (host == null) return;
        var dialog = MsgBox.BuildInputBox(new TRect(0, 0, 42, 7),
            "Go to code point (hex)", "~C~ode:", Model.Notation, 16);
        dialog.options |= Views.ofCentered;
        TInputLine? input = null;
        dialog.ForEachView(v => { if (v is TInputLine line) input = line; });
        try
        {
            if (host.ExecView(dialog) != Views.cmOK) return;
            if (CharacterMapModel.TryParse(input?.Data, out int code, out string error))
            {
                Model.Select(code); Feedback = "Selected " + Model.Notation;
            }
            else Feedback = error;
        }
        finally { dialog.ShutDown(); }
    }
    private void ChooseRange()
    {
        var host = DialogHost;
        if (host == null) return;
        var dialog = new TDialog(new TRect(0, 0, 52, 9), "Unicode ranges");
        dialog.options |= Views.ofCentered;
        dialog.Insert(new TStaticText(new TRect(2, 1, 50, 2), "Choose a group of code points."));
        dialog.Insert(new TStaticText(new TRect(2, 3, 50, 4), "Group names, not individual character names."));
        var combo = new TComboBox(new TRect(2, 2, 50, 3), CharacterMapModel.Ranges.Select(r => r.ToString()).ToArray());
        dialog.Insert(combo);
        dialog.Insert(new TButton(new TRect(9, 5, 23, 7), "~G~o", Views.cmOK, ButtonConstants.bfDefault));
        dialog.Insert(new TButton(new TRect(27, 5, 41, 7), "~C~ancel", Views.cmCancel, ButtonConstants.bfNormal));
        combo.Select();
        try
        {
            if (host.ExecView(dialog) == Views.cmOK)
            {
                var range = CharacterMapModel.Ranges.FirstOrDefault(r => r.ToString() == combo.Text);
                if (range != null) { Model.Select(range.First); Feedback = "Block: " + range; }
            }
        }
        finally { dialog.ShutDown(); }
    }
}

internal sealed class CharacterMapWindow : TWindow
{
    internal CharacterMapView View { get; }
    internal CharacterMapWindow(TRect bounds) : base(bounds, "Character Map", 0)
    {
        View = new CharacterMapView(new TRect(1, 1, size.x - 1, size.y - 1));
        Insert(View);
        View.Select();
    }
    public override void SizeLimits(ref TPoint min, ref TPoint max)
    {
        base.SizeLimits(ref min, ref max);
        min = new TPoint(60, 16);
    }
}

public partial class TVDemoApp
{
    private void OpenCharacterMap()
    {
        if (DeskTop == null) return;
        var window = new CharacterMapWindow(DemoBounds(78, 23));
        if (ValidView(window) != null) DeskTop.Insert(window);
        else window.ShutDown();
    }
}
