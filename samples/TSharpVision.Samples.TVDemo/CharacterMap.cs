using System.Globalization;
using System.Text;
using TSharpVision.Constants;
using TSharpVision.Text;

namespace TSharpVision.Samples.TVDemo;

// No Unicode name database is bundled: preset names below are block labels only.
internal sealed record CharacterMapRange(string Label, int First, int Last)
{
    public override string ToString() => $"{Label} ({First:X4}-{Last:X4})";
}

/// <summary>One line of the reference corpus: what it shows and the text drawn.</summary>
internal sealed record CharacterMapSample(string Label, string Text);

internal sealed class CharacterMapModel
{
    internal const int Maximum = 0x10FFFF;
    internal static readonly CharacterMapRange[] Ranges =
    {
        new("Basic Latin", 0, 0x7F), new("Latin-1 Supplement", 0x80, 0xFF),
        new("Box Drawing", 0x2500, 0x257F), new("Block Elements", 0x2580, 0x259F),
        new("Arrows", 0x2190, 0x21FF), new("Geometric Shapes", 0x25A0, 0x25FF),
        new("Miscellaneous Symbols", 0x2600, 0x26FF), new("Dingbats", 0x2700, 0x27BF),
        new("Braille Patterns", 0x2800, 0x28FF),
        new("CJK Unified Ideographs", 0x4E00, 0x9FFF),
        new("Musical Symbols", 0x1D100, 0x1D1FF),
        new("Emoticons", 0x1F600, 0x1F64F)
    };

    // A compact acceptance corpus: each line's expected column count comes from TextLayout, the same
    // measurement the framework writes cells with; how the glyphs look depends on the driver and fonts.
    internal static readonly CharacterMapSample[] Samples =
    {
        new("Czech", "P\u0159\u00EDli\u0161 \u017Elu\u0165ou\u010Dk\u00FD k\u016F\u0148"),
        new("Greek/Cyr", "\u03A9 \u0416"),
        new("Combining", "e\u0301"),
        new("CJK", "\u6F22\u5B57"),
        new("Suppl.", "\U0001D11E"),
        new("Emoji", "\U0001F600 \U0001F44D\U0001F3FD \U0001F468\u200D\U0001F469\u200D\U0001F467 \U0001F469\U0001F3FD\u200D\U0001F4BB"),
        new("Flag", "\U0001F1E8\U0001F1FF"),
        new("Hearts", "\u2764 \u2764\uFE0F \u2764\uFE0E"),
        new("Mixed", "A\U0001F600B\U0001F44D\U0001F3FDC\U0001F1E8\U0001F1FFD\U0001F468\u200D\U0001F469\u200D\U0001F467E")
    };

    internal int Selected { get; private set; } = 0x2500;
    internal int TopRow { get; private set; } = 0x250;
    internal int Rows { get; private set; } = 8;
    internal bool IsScalar => Rune.IsValid(Selected);
    // U+ notation with at least four hex digits: U+0041, U+1F600, U+10FFFF.
    internal string Notation => $"U+{Selected:X4}";
    internal string Source => !IsScalar ? "invalid scalar" : Selected <= 0xFFFF ? $@"\u{Selected:X4}" : $@"\U{Selected:X8}";
    internal string? Character => IsScalar ? new Rune(Selected).ToString() : null;
    internal string Utf8 => Character is string s ? string.Join(" ", Encoding.UTF8.GetBytes(s).Select(b => $"{b:X2}")) : "none (surrogate)";
    internal string Utf16 => Character is string s ? string.Join(" ", s.Select(c => $"{(int)c:X4}")) : "none (surrogate)";

    /// <summary>Screen columns of a scalar drawn on its own, from TextLayout (Unicode 16.0); 0 for an invalid scalar.</summary>
    internal static int Columns(int code) =>
        Rune.IsValid(code) ? TextLayout.Measure(new Rune(code).ToString(), TextLayoutOptions.Default) : 0;

    private static bool Legacy => TextLayout.DefaultProfile == TextWidthProfile.Legacy;
    private static readonly TextLayoutOptions UnicodeLayout = TextLayoutOptions.Default with { Profile = TextWidthProfile.Unicode };

    /// <summary>Why a code point is drawn as itself, on a dotted circle, or as a placeholder.</summary>
    internal static string Policy(int code)
    {
        if (!Rune.IsValid(code)) return "invalid scalar (surrogate): placeholder";
        var category = Rune.GetUnicodeCategory(new Rune(code));
        if (category == UnicodeCategory.Control) return "control: placeholder";
        // Variation selectors and the Hangul fillers are default-ignorable: alone they draw nothing.
        if (code is >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF or 0x115F or 0x1160 or 0x3164 or 0xFFA0)
            return "format / zero-width: placeholder";
        if (category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            return "format / zero-width: placeholder";
        if (category == UnicodeCategory.OtherNotAssigned) return "unassigned: placeholder";
        if (category == UnicodeCategory.SpaceSeparator) return "space: placeholder";
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            return Legacy ? "combining mark: placeholder (legacy cell width)" : "combining mark: shown on a dotted circle";
        if (Legacy && (code > 0xFFFF || TextLayout.Measure(new Rune(code).ToString(), UnicodeLayout) != 1))
            return "wide or supplementary: placeholder (legacy cell width)";
        if (category == UnicodeCategory.PrivateUse) return "private use: glyph depends on the font";
        return "glyph shown; font coverage not determined";
    }

    /// <summary>
    /// The text drawn for a code point in the grid: the scalar itself (a whole cluster, wide where TextLayout says
    /// so), a combining mark on U+25CC, or "." where nothing visible or measurable would be drawn.
    /// </summary>
    internal static string Preview(int code)
    {
        string policy = Policy(code);
        if (policy.StartsWith("combining mark: shown", StringComparison.Ordinal)) return "\u25CC" + new Rune(code);
        return policy.StartsWith("glyph", StringComparison.Ordinal) || policy.StartsWith("private", StringComparison.Ordinal)
            ? new Rune(code).ToString()
            : ".";
    }

    /// <summary>Columns the grid preview of a code point occupies.</summary>
    internal static int PreviewColumns(int code) => TextLayout.Measure(Preview(code), TextLayoutOptions.Default);

    /// <summary>Scalars of a text in U+ notation, separated by spaces.</summary>
    internal static string Scalars(string text) => string.Join(" ", text.EnumerateRunes().Select(r => $"U+{r.Value:X4}"));

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
    /// <summary>Restores a saved grid position (after a search).</summary>
    internal void Restore(int selected, int topRow)
    {
        Selected = Math.Clamp(selected, 0, Maximum);
        TopRow = topRow;
        EnsureVisible();
    }
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
    // First column of sample text and rulers on the reference page.
    internal const int SampleColumn = 11;
    // Columns of the filtered list: code, glyph, name.
    internal const int ListGlyphColumn = 9;
    internal const int ListNameColumn = 12;

    internal CharacterMapModel Model { get; } = new();
    internal string Feedback { get; set; } = string.Empty;
    internal bool ShowSamples { get; set; }

    // ---- Filter (F / A) -------------------------------------------------------------------------------------
    /// <summary>The active search, or null while the left panel shows the whole grid.</summary>
    internal CharacterSearchResult? Filter { get; private set; }
    internal int ResultIndex { get; private set; }
    internal int ResultTop { get; private set; }
    /// <summary>The last query, offered again by Find.</summary>
    internal string LastQuery { get; private set; } = string.Empty;
    private (int Selected, int TopRow) _beforeSearch;

    // ---- Geometry ---------------------------------------------------------------------------------------------
    internal int CellWidth => size.x >= 110 ? 4 : size.x >= 76 ? 3 : 2;
    /// <summary>Width of the left panel: the 16-column grid (and the filtered list in the same columns).</summary>
    internal int LeftWidth => 8 + 16 * CellWidth;
    /// <summary>First column of the right panel; a vertical line separates the panels.</summary>
    internal int RightColumn => LeftWidth + 1;
    internal int RightWidth => Math.Max(0, size.x - RightColumn);
    /// <summary>Grid or list rows: every row under the header.</summary>
    internal int GridRows => Math.Max(1, size.y - 1);

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
        KeepResultVisible();
        DrawView();
    }

    /// <summary>Runs <paramref name="query"/>: matches switch the left panel to the list, none keep the current view.</summary>
    internal void ApplySearch(string? query)
    {
        string trimmed = (query ?? string.Empty).Trim();
        LastQuery = trimmed;
        if (trimmed.Length == 0) { ShowAll(); Feedback = "Empty query: showing all."; return; }
        CharacterSearchResult result = UnicodeCharacterNames.Shared.Search(trimmed);
        if (result.Count == 0) { Feedback = $"No matches for '{trimmed}'."; return; }
        if (Filter is null) _beforeSearch = (Model.Selected, Model.TopRow);
        Filter = result;
        ResultIndex = 0; ResultTop = 0;
        Model.Select(result.Codes[0]);
        Feedback = result.Count == 1 ? "1 match." : $"{result.Count} matches.";
    }

    /// <summary>Clears the filter and returns to the grid at the code point selected before the search.</summary>
    internal void ShowAll()
    {
        if (Filter is null) return;
        Filter = null;
        Model.Restore(_beforeSearch.Selected, _beforeSearch.TopRow);
        Feedback = "Showing all code points.";
    }

    private void SelectResult(int index)
    {
        if (Filter is null) return;
        ResultIndex = Math.Clamp(index, 0, Filter.Count - 1);
        KeepResultVisible();
        Model.Select(Filter.Codes[ResultIndex]);
    }

    private void KeepResultVisible()
    {
        if (Filter is null) return;
        int rows = GridRows;
        if (ResultIndex < ResultTop) ResultTop = ResultIndex;
        if (ResultIndex >= ResultTop + rows) ResultTop = ResultIndex - rows + 1;
        ResultTop = Math.Clamp(ResultTop, 0, Math.Max(0, Filter.Count - rows));
    }

    /// <summary>
    /// The right panel's lines: metadata first (code and cells, name, block, category, feedback, UTF-8/16, aliases,
    /// CLDR name, each wrapped and added only while it fits), then every keyboard shortcut. The shortcuts use two
    /// columns from 40 columns, one per line when the height allows, and a compact two-per-line form otherwise, so
    /// all of them stay visible at the minimum size.
    /// </summary>
    internal List<string> RightPanelLines()
    {
        int width = RightWidth, rows = size.y;
        var db = UnicodeCharacterNames.Shared;
        int code = Model.Selected;
        (string Key, string Action)[] keys =
        [
            ("F", "Find"), ("A", "Show all"), ("G", "Go to code"), ("R", "Ranges"), ("U", "Samples page"),
            ("C", "Copy char"), ("N", "Copy U+"), ("S", "Copy C#"), ("Arrows", "Select"), ("PgUp/PgDn", "Page"),
            ("Esc", "Back/close"),
        ];
        var shortcuts = new List<string>();
        if (width >= 40)
        {
            int keyRows = (keys.Length + 1) / 2, half = width / 2;
            for (int i = 0; i < keyRows; i++)
            {
                string line = KeyLine(keys[i]);
                if (i + keyRows < keys.Length) line = line.PadRight(half) + KeyLine(keys[i + keyRows]);
                shortcuts.Add(line);
            }
        }
        else if (rows - keys.Length >= 9)
        {
            foreach (var key in keys) shortcuts.Add(KeyLine(key));
        }
        else
        {
            shortcuts.AddRange(["F Find   A All", "G Go to  R Range", "U Sample C Copy", "N Copy U+ S C#",
                                "Arrows/PgUp/PgDn", "Esc Back/close"]);
        }

        int room = rows - shortcuts.Count;
        int cells = CharacterMapModel.Columns(code);
        var meta = new List<string> { $"{Model.Notation}  {cells} cell{(cells == 1 ? "" : "s")}" };
        meta.AddRange(Wrap(db.Label(code), width, room >= 10 ? 3 : 2));
        string[] aliases = db.GetAliases(code);
        string?[] optional =
        [
            db.GetBlock(code) is string block ? "Block: " + block : null,
            Rune.IsValid(code) ? "Category: " + CategoryLabel(Rune.GetUnicodeCategory(new Rune(code)), width >= 30) : null,
            "UTF-8 " + Model.Utf8,
            "UTF-16 " + Model.Utf16,
            Feedback.Length > 0 ? Feedback : null,
            aliases.Length > 0 ? "Alias: " + string.Join("; ", aliases) : null,
            db.GetCldrName(code) is string cldr ? "CLDR: " + cldr : null,
        ];
        foreach (string? item in optional)
        {
            if (item is null) continue;
            List<string> lines = Wrap(item, width, 2).ToList();
            if (meta.Count + lines.Count > room) continue;
            meta.AddRange(lines);
        }
        if (meta.Count < room) meta.Add(string.Empty);
        meta.AddRange(shortcuts);
        return meta;
    }

    // "F  Find", "Arrows Select": single letters in a three-cell column, longer keys followed by one space.
    private static string KeyLine((string Key, string Action) k) => (k.Key.Length == 1 ? k.Key.PadRight(3) : k.Key + " ") + k.Action;

    // "So OtherSymbol", or only "So" when the panel is narrow (one line at the minimum size).
    private static string CategoryLabel(UnicodeCategory category, bool withName)
    {
        string code = category switch
        {
            UnicodeCategory.UppercaseLetter => "Lu", UnicodeCategory.LowercaseLetter => "Ll",
            UnicodeCategory.TitlecaseLetter => "Lt", UnicodeCategory.ModifierLetter => "Lm",
            UnicodeCategory.OtherLetter => "Lo", UnicodeCategory.NonSpacingMark => "Mn",
            UnicodeCategory.SpacingCombiningMark => "Mc", UnicodeCategory.EnclosingMark => "Me",
            UnicodeCategory.DecimalDigitNumber => "Nd", UnicodeCategory.LetterNumber => "Nl",
            UnicodeCategory.OtherNumber => "No", UnicodeCategory.SpaceSeparator => "Zs",
            UnicodeCategory.LineSeparator => "Zl", UnicodeCategory.ParagraphSeparator => "Zp",
            UnicodeCategory.Control => "Cc", UnicodeCategory.Format => "Cf", UnicodeCategory.Surrogate => "Cs",
            UnicodeCategory.PrivateUse => "Co", UnicodeCategory.ConnectorPunctuation => "Pc",
            UnicodeCategory.DashPunctuation => "Pd", UnicodeCategory.OpenPunctuation => "Ps",
            UnicodeCategory.ClosePunctuation => "Pe", UnicodeCategory.InitialQuotePunctuation => "Pi",
            UnicodeCategory.FinalQuotePunctuation => "Pf", UnicodeCategory.OtherPunctuation => "Po",
            UnicodeCategory.MathSymbol => "Sm", UnicodeCategory.CurrencySymbol => "Sc",
            UnicodeCategory.ModifierSymbol => "Sk", UnicodeCategory.OtherSymbol => "So",
            _ => "Cn",
        };
        return withName ? $"{code} {category}" : code;
    }

    // Word-wraps by cells into at most maxLines lines; text that does not fit ends with "..." on the last line, and a
    // word longer than the width is cut at a cluster boundary.
    internal static IEnumerable<string> Wrap(string text, int width, int maxLines)
    {
        if (width <= 0 || maxLines <= 0) yield break;
        var options = TextLayoutOptions.Default;
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (string word in text.Split(' '))
        {
            string candidate = line.Length == 0 ? word : line + " " + word;
            if (TextLayout.Measure(candidate, options) <= width) { line.Clear().Append(candidate); continue; }
            if (line.Length > 0) lines.Add(line.ToString());
            line.Clear().Append(word);
        }
        if (line.Length > 0) lines.Add(line.ToString());
        for (int i = 0; i < lines.Count && i < maxLines; i++)
        {
            string current = lines[i];
            bool truncated = i == maxLines - 1 && lines.Count > maxLines;
            if (truncated || TextLayout.Measure(current, options) > width)
            {
                int keep = TextLayout.Fit(current, Math.Max(0, width - 3), options, out _);
                current = current[..keep].TrimEnd() + "...";
            }
            yield return current;
        }
    }
    public override void Draw()
    {
        if (ShowSamples) { DrawSamples(); return; }
        int rows = GridRows;
        ushort normal = GetColor(6), selected = GetColor(7);
        List<string> right = RightPanelLines();
        for (int y = 0; y < size.y; y++)
        {
            var buffer = new TDrawBuffer(new TScreenChar[Math.Max(0, size.x)]);
            buffer.moveChar(0, ' ', normal, size.x);
            if (Filter is null) DrawGridRow(ref buffer, y, rows, normal, selected);
            else DrawListRow(ref buffer, y, normal, selected);
            if (LeftWidth < size.x) buffer.moveChar(LeftWidth, '\u2502', normal, 1);
            if (y < right.Count && RightWidth > 0) buffer.moveStr(RightColumn, right[y], normal, RightWidth, 0);
            WriteLine(0, y, size.x, 1, buffer);
        }
    }

    private void DrawGridRow(ref TDrawBuffer buffer, int y, int rows, ushort normal, ushort selected)
    {
        if (y == 0)
        {
            buffer.moveStr(0, "Row", normal);
            for (int col = 0; col < 16; col++) buffer.moveStr(8 + col * CellWidth, col.ToString("X"), normal);
            return;
        }
        if (y > rows) return;
        int start = (Model.TopRow + y - 1) * 16;
        if (start > CharacterMapModel.Maximum) return;
        buffer.moveStr(0, $"{start:X6}", normal);
        // Each code point is written as text, so a wide or supplementary glyph becomes a lead cell with its
        // trail; the grid step (2 or 3 cells) leaves room for two columns.
        for (int col = 0; col < 16; col++)
        {
            int code = start + col;
            if (code > CharacterMapModel.Maximum) break;
            buffer.moveStr(8 + col * CellWidth, CharacterMapModel.Preview(code), code == Model.Selected ? selected : normal);
        }
    }

    // One match per row in fixed cell columns: U+ code, glyph (up to two cells), name; the selected row is highlighted
    // across the whole left panel. moveStr pads and never splits a cluster, so wide glyphs cannot shift the name.
    private void DrawListRow(ref TDrawBuffer buffer, int y, ushort normal, ushort selected)
    {
        if (Filter is null) return;
        if (y == 0)
        {
            string header = $"{Filter.Count} match{(Filter.Count == 1 ? "" : "es")}: {Filter.Query}  (A: all)";
            buffer.moveStr(0, header, normal, LeftWidth, 0);
            return;
        }
        int index = ResultTop + y - 1;
        if (index >= Filter.Count) return;
        int code = Filter.Codes[index];
        ushort attr = index == ResultIndex ? selected : normal;
        buffer.moveStr(0, $"U+{code:X4}", attr, ListGlyphColumn, 0);
        buffer.moveStr(ListGlyphColumn, CharacterMapModel.Preview(code), attr, ListNameColumn - ListGlyphColumn, 0);
        buffer.moveStr(ListNameColumn, UnicodeCharacterNames.Shared.Label(code), attr, LeftWidth - ListNameColumn, 0);
    }
    // The reference page: each sample is followed by '|' at the column TextLayout expects, so text that a driver
    // draws wider or narrower than its cells shows up against the marker and the ruler. The last rows put a wide
    // character at the view's right edge (fits / cut) and across its left edge, through the framework's clipping.
    private void DrawSamples()
    {
        ushort normal = GetColor(6), selected = GetColor(7);
        int line = 0;
        void Row(string text)
        {
            var buffer = new TDrawBuffer(new TScreenChar[Math.Max(1, size.x)]);
            buffer.moveChar(0, ' ', normal, size.x);
            buffer.moveStr(0, text, normal);
            WriteLine(0, line++, size.x, 1, buffer);
        }

        Row("Unicode samples (U: back). '|' = expected end column.");
        var ruler = new StringBuilder(new string(' ', SampleColumn));
        for (int c = 0; SampleColumn + c < size.x; c++) ruler.Append((char)('0' + c % 10));
        Row(ruler.ToString());
        foreach (CharacterMapSample sample in CharacterMapModel.Samples)
        {
            int width = TextLayout.Measure(sample.Text, TextLayoutOptions.Default);
            var buffer = new TDrawBuffer(new TScreenChar[Math.Max(1, size.x)]);
            buffer.moveChar(0, ' ', normal, size.x);
            buffer.moveStr(0, sample.Label, normal, SampleColumn - 1, 0);
            buffer.moveStr(SampleColumn, sample.Text, selected);
            buffer.moveStr(SampleColumn + width, $"| {width} cell{(width == 1 ? "" : "s")}  {CharacterMapModel.Scalars(sample.Text)}", normal);
            WriteLine(0, line++, size.x, 1, buffer);
        }

        // Right edge: the last two cells hold a whole wide character; one cell further, the framework blanks it.
        int right = Math.Max(SampleColumn, size.x - 2);
        var edge = new TDrawBuffer(new TScreenChar[Math.Max(1, size.x + 2)]);
        edge.moveChar(0, ' ', normal, size.x + 2);
        edge.moveStr(0, "Right fit", normal);
        edge.moveStr(right, "\u6F22", selected);
        WriteLine(0, line++, size.x, 1, edge);
        edge.moveChar(0, ' ', normal, size.x + 2);
        edge.moveStr(0, "Right cut (blank)", normal);
        edge.moveStr(right + 1, "\u6F22", selected);
        WriteLine(0, line++, size.x + 2, 1, edge);

        // Left edge: written from column -1, so the first ideograph loses its left half and must show as a blank.
        var left = new TDrawBuffer(new TScreenChar[Math.Max(1, size.x + 1)]);
        left.moveChar(0, ' ', normal, size.x + 1);
        left.moveStr(0, "\u6F22\u5B57", selected);
        left.moveStr(5, "<- left cut: blank, then \u5B57", normal);
        WriteLine(-1, line++, size.x + 1, 1, left);

        Row("Cells come from TextLayout (Unicode " + TextLayout.UnicodeVersion + ", " + TextLayout.DefaultProfile + ").");
        Row("Missing glyphs are font coverage, not width errors.");
        while (line < size.y) Row(string.Empty);
    }

    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);
        if (ev.What == Events.evMouseDown)
        {
            if (ShowSamples) return;
            var point = MakeLocal(ev.mouse.where);
            if (Filter is not null)
            {
                if (point.y < 1 || point.x >= LeftWidth || ResultTop + point.y - 1 >= Filter.Count) return;
                SelectResult(ResultTop + point.y - 1);
            }
            else
            {
                if (point.y < 1 || point.y > GridRows || point.x < 8 || point.x >= 8 + 16 * CellWidth) return;
                Model.Select((Model.TopRow + point.y - 1) * 16 + (point.x - 8) / CellWidth);
            }
        }
        else if (ev.What == Events.evKeyboard && ev.keyDown.keyCode == Keys.kbEsc)
        {
            // Esc steps back: samples page -> browser, filtered list -> all code points, grid -> close the window.
            if (ShowSamples) ShowSamples = false;
            else if (Filter is not null) ShowAll();
            else
            {
                ClearEvent(ref ev);
                Message(owner, Events.evCommand, Views.cmClose, owner);
                return;
            }
        }
        else if (ev.What == Events.evKeyboard && Filter is not null && !ShowSamples && ListKey(ev.keyDown.keyCode))
        {
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
                    case "F": Find(); break;
                    case "A": ShowSamples = false; ShowAll(); break;
                    // Go to and Ranges jump inside the whole grid, so they end a search first.
                    case "G": GoTo(); break;
                    case "R": ChooseRange(); break;
                    case "U": ShowSamples = !ShowSamples; break;
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
    // Up/Down, PgUp/PgDn and Home/End move through the filtered list; true when handled.
    private bool ListKey(ushort keyCode)
    {
        int page = Math.Max(1, GridRows);
        switch (keyCode)
        {
            case Keys.kbUp: SelectResult(ResultIndex - 1); return true;
            case Keys.kbDown: SelectResult(ResultIndex + 1); return true;
            case Keys.kbPgUp: SelectResult(ResultIndex - page); return true;
            case Keys.kbPgDn: SelectResult(ResultIndex + page); return true;
            case Keys.kbHome: SelectResult(0); return true;
            case Keys.kbEnd: SelectResult(int.MaxValue); return true;
            case Keys.kbLeft or Keys.kbRight: return true;
            default: return false;
        }
    }

    // Dialog palettes map application roles, so host helpers beside the window
    // on the desktop rather than through the blue window's palette.
    private TGroup? DialogHost => owner?.owner;

    private void Find()
    {
        var host = DialogHost;
        if (host == null) return;
        var dialog = MsgBox.BuildInputBox(new TRect(0, 0, 52, 7),
            "Find character", "~F~ind:", LastQuery, 64);
        dialog.options |= Views.ofCentered;
        TInputLine? input = null;
        dialog.ForEachView(v => { if (v is TInputLine line) input = line; });
        try
        {
            // Cancel keeps the current view, filter and query.
            if (host.ExecView(dialog) == Views.cmOK) ApplySearch(input?.Data);
        }
        finally { dialog.ShutDown(); }
    }

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
                Filter = null;
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
                if (range != null) { Filter = null; Model.Select(range.First); Feedback = "Block: " + range; }
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
