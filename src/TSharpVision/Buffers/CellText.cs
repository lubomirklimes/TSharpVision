using TSharpVision.Text;

namespace TSharpVision;

/// <summary>
/// Writes text into rows of <see cref="TScreenChar"/> cells by grapheme cluster and keeps wide clusters whole:
/// a lead is always followed by its trails, and no trail exists without its lead.
/// </summary>
/// <remarks>
/// Invariants, enforced at every write boundary:
/// I1 a trail follows its lead in the same row; I2 overwriting part of a cluster blanks the rest of it (the blank
/// keeps the cell's attribute); I3 a write never starts with an orphan trail nor ends with a lead whose trails were
/// cut off (those cells become blanks).
/// </remarks>
internal static class CellText
{
    /// <summary>True while the legacy one-cell-per-UTF-16-unit writers are in effect.</summary>
    public static bool Legacy => TextLayout.DefaultProfile == TextWidthProfile.Legacy;

    /// <summary>Layout used by cell writers and by measurements that must agree with them.</summary>
    public static TextLayoutOptions Options => TextLayoutOptions.Default with { ZeroWidthAsOne = true };

    /// <summary>Cells that <paramref name="text"/> occupies when written by <see cref="Write"/>.</summary>
    public static int Measure(ReadOnlySpan<char> text) => Legacy ? text.Length : TextLayout.Measure(text, Options);

    /// <summary>
    /// Cells that <c>~hotkey~</c> markup occupies when written by <see cref="TDrawBuffer.moveCStr"/>: the
    /// <c>cstrlen</c> of the controls. Under <see cref="Legacy"/> it counts the UTF-16 units other than '~', as 1.2 did.
    /// </summary>
    public static int HotkeyWidth(ReadOnlySpan<char> text)
    {
        if (!Legacy) return HotkeyText.Measure(text, Options);
        int n = 0;
        foreach (char c in text) if (c != '~') n++;
        return n;
    }

    /// <summary><see cref="HotkeyWidth(ReadOnlySpan{char})"/> of a string; 0 for null.</summary>
    public static int HotkeyWidth(string? text) => text is null ? 0 : HotkeyWidth(text.AsSpan());

    /// <summary>The upper-cased character after the first '~' of <c>~hotkey~</c> markup, or '\0'.</summary>
    public static char Hotkey(string? text) => HotkeyText.GetHotkey(text);

    /// <summary>
    /// UTF-16 units of <paramref name="text"/> that fit into <paramref name="maxCells"/> cells without splitting a
    /// cluster; <paramref name="usedCells"/> receives their width. Under <see cref="Legacy"/> one unit is one cell.
    /// </summary>
    public static int Fit(ReadOnlySpan<char> text, int maxCells, out int usedCells)
    {
        if (maxCells <= 0) { usedCells = 0; return 0; }
        if (Legacy)
        {
            usedCells = Math.Min(text.Length, maxCells);
            return usedCells;
        }
        return TextLayout.Fit(text, maxCells, Options, out usedCells);
    }

    /// <summary>UTF-16 units of <paramref name="text"/> that fit into <paramref name="maxCells"/> cells.</summary>
    public static int Fit(ReadOnlySpan<char> text, int maxCells) => Fit(text, maxCells, out _);

    /// <summary>
    /// <paramref name="text"/> cut to at most <paramref name="maxCells"/> cells at a cluster boundary (under
    /// <see cref="Legacy"/>: its first <paramref name="maxCells"/> UTF-16 units, as <c>Substring</c> did).
    /// </summary>
    public static string Truncate(string text, int maxCells)
    {
        int units = Fit(text, maxCells);
        return units >= text.Length ? text : text.Substring(0, units);
    }

    /// <summary>
    /// UTF-16 offset where text scrolled left by <paramref name="columns"/> cells starts to draw: the first cluster
    /// at or after that column. <paramref name="pad"/> receives the blank cells before it (the visible part of a wide
    /// cluster cut by the scroll edge). Under <see cref="Legacy"/> the offset is <paramref name="columns"/> units.
    /// </summary>
    public static int SkipColumns(ReadOnlySpan<char> text, int columns, out int pad)
    {
        pad = 0;
        if (columns <= 0) return 0;
        if (Legacy) return Math.Min(columns, text.Length);
        int offset = TextLayout.OffsetAtColumn(text, columns, Options, ColumnBias.Ceiling, out int column);
        pad = Math.Max(0, column - columns);
        return offset;
    }

    /// <summary>
    /// Cell column at which the cluster containing UTF-16 <paramref name="offset"/> starts; an offset past the end
    /// adds one cell per missing unit. Under <see cref="Legacy"/> the column is the offset.
    /// </summary>
    public static int ColumnAt(ReadOnlySpan<char> text, int offset)
    {
        if (Legacy || offset <= 0) return offset;
        if (offset >= text.Length) return TextLayout.Measure(text, Options) + (offset - text.Length);
        return TextLayout.ColumnAtOffset(text, offset, Options);
    }

    /// <summary>
    /// Start of the longest tail of <paramref name="text"/> that fits into <paramref name="maxCells"/> cells, at a
    /// cluster boundary. Under <see cref="Legacy"/>: the last <paramref name="maxCells"/> units, moved past a low
    /// surrogate (the 1.2 frame-title rule).
    /// </summary>
    public static int TailStart(ReadOnlySpan<char> text, int maxCells)
    {
        if (maxCells <= 0) return text.Length;
        if (Legacy)
        {
            int start = Math.Max(0, text.Length - maxCells);
            if (start < text.Length && char.IsLowSurrogate(text[start])) start++;
            return start;
        }
        int first = TextLayout.Measure(text, Options) - maxCells;
        if (first <= 0) return 0;
        foreach (TextCluster c in TextLayout.EnumerateClusters(text, Options, 0))
            if (c.Column >= first) return c.Start;
        return text.Length;
    }

    /// <summary>Units of the cluster at <paramref name="index"/> (Legacy: one); guarantees progress when nothing fits.</summary>
    public static int ClusterLength(ReadOnlySpan<char> text, int index) =>
        Legacy || index >= text.Length ? 1 : TextLayout.NextClusterLength(text, index);

    /// <summary>
    /// Writes <paramref name="text"/> from cell <paramref name="indent"/>, using at most <paramref name="maxCells"/>
    /// cells. A cluster that does not fit fills the remaining cells with blanks. Tabs expand to spaces (stops relative
    /// to <paramref name="startColumn"/>), other control characters take one cell each as they are, line breaks take
    /// none, unpaired surrogates draw U+FFFD.
    /// </summary>
    /// <param name="row">Destination row.</param>
    /// <param name="indent">First cell to write.</param>
    /// <param name="text">Text to write.</param>
    /// <param name="attr">Attribute, or null to keep each cell's attribute.</param>
    /// <param name="maxCells">Cells available from <paramref name="indent"/>.</param>
    /// <param name="startColumn">Layout column of the first cell (tab stops).</param>
    /// <returns>Cells written.</returns>
    public static int Write(Span<TScreenChar> row, int indent, ReadOnlySpan<char> text, TColorAttr? attr,
                            int maxCells, int startColumn = 0)
    {
        if (indent < 0 || indent >= row.Length || maxCells <= 0) return 0;
        int limit = (int)Math.Min(row.Length, (long)indent + maxCells);
        int x = indent;
        foreach (TextCluster c in TextLayout.EnumerateClusters(text, Options, startColumn))
        {
            if (x >= limit) break;
            ReadOnlySpan<char> cluster = text.Slice(c.Start, c.Length);
            switch (c.Kind)
            {
                case TextClusterKind.LineBreak:
                    continue;
                case TextClusterKind.Tab:
                    for (int k = 0; k < c.Width && x < limit; k++) Put(row, x++, new TScreenChar(' ', AttrAt(row, x - 1, attr)));
                    continue;
                case TextClusterKind.Control:
                    Put(row, x, new TScreenChar(cluster[0], AttrAt(row, x, attr)));
                    x++;
                    continue;
                case TextClusterKind.Malformed:
                    Put(row, x, new TScreenChar('\uFFFD', AttrAt(row, x, attr)));
                    x++;
                    continue;
            }

            int width = Math.Clamp(c.Width, 1, TScreenChar.MaxCellWidth);
            if (x + width > limit)
            {
                while (x < limit) { Put(row, x, new TScreenChar(' ', AttrAt(row, x, attr))); x++; }
                break;
            }
            TColorAttr a = AttrAt(row, x, attr);
            Put(row, x, TScreenChar.CreateCluster(cluster, width, a));
            for (int k = 1; k < width; k++) Put(row, x + k, TScreenChar.CreateTrail(AttrAt(row, x + k, attr)));
            x += width;
        }
        RepairBoundary(row, indent);
        RepairBoundary(row, x);
        return x - indent;
    }

    /// <summary>Fills <paramref name="count"/> cells with <paramref name="c"/>; a wide character fills cell pairs.</summary>
    public static void Fill(Span<TScreenChar> row, int indent, char c, TColorAttr? attr, int count)
    {
        if (indent < 0 || indent >= row.Length || count <= 0) return;
        int limit = (int)Math.Min(row.Length, (long)indent + count);
        if (char.IsSurrogate(c)) c = '\uFFFD';
        int width = c < '\u0300' ? 1 : TextLayout.Measure([c], Options);
        int x = indent;
        if (width == 2)
        {
            for (; x + 2 <= limit; x += 2)
            {
                Put(row, x, TScreenChar.CreateCluster([c], 2, AttrAt(row, x, attr)));
                Put(row, x + 1, TScreenChar.CreateTrail(AttrAt(row, x + 1, attr)));
            }
            c = ' ';
        }
        for (; x < limit; x++) Put(row, x, new TScreenChar(c, AttrAt(row, x, attr)));
        RepairBoundary(row, indent);
        RepairBoundary(row, limit);
    }

    private static TColorAttr AttrAt(Span<TScreenChar> row, int x, TColorAttr? attr) => attr ?? row[x].Attr;

    private static void Put(Span<TScreenChar> row, int x, TScreenChar cell) => row[x] = cell;

    /// <summary>
    /// Makes the boundary between cells <paramref name="pos"/> - 1 and <paramref name="pos"/> consistent: a cluster
    /// that crosses it must be intact, otherwise its cells on both sides become blanks; trails at
    /// <paramref name="pos"/> without a lead become blanks. Returns the range of cells changed (empty when none).
    /// </summary>
    public static (int First, int End) RepairBoundary(Span<TScreenChar> row, int pos)
    {
        if (pos < 0 || pos >= row.Length) return (pos, pos);
        int first = int.MaxValue, end = int.MinValue;

        // Find the lead whose cluster covers pos - 1, if any.
        int lead = pos - 1;
        while (lead >= 0 && pos - lead <= TScreenChar.MaxCellWidth && row[lead].Kind == TScreenCellKind.Trail) lead--;
        bool crossing = lead >= 0 && pos - lead <= TScreenChar.MaxCellWidth
                        && row[lead].Kind == TScreenCellKind.Lead && lead + row[lead].CellWidth > pos;

        if (crossing)
        {
            int clusterEnd = Math.Min(row.Length, lead + row[lead].CellWidth);
            bool intact = lead + row[lead].CellWidth <= row.Length;
            for (int k = pos; k < clusterEnd && intact; k++)
                if (row[k].Kind != TScreenCellKind.Trail) intact = false;
            if (intact) return (pos, pos);
            for (int k = lead; k < pos; k++) Blank(row, k, ref first, ref end);
        }
        else
        {
            // Trails directly left of pos that no lead covers.
            int covered = lead >= 0 && row[lead].Kind == TScreenCellKind.Lead ? lead + row[lead].CellWidth : lead + 1;
            for (int k = Math.Max(0, covered); k < pos; k++) Blank(row, k, ref first, ref end);
        }

        for (int k = pos; k < row.Length && row[k].Kind == TScreenCellKind.Trail; k++) Blank(row, k, ref first, ref end);
        return first == int.MaxValue ? (pos, pos) : (first, end);
    }

    private static void Blank(Span<TScreenChar> row, int k, ref int first, ref int end)
    {
        row[k] = new TScreenChar(' ', row[k].Attr);
        first = Math.Min(first, k);
        end = Math.Max(end, k + 1);
    }

    /// <summary>
    /// Blanks an orphan trail at the start of <paramref name="cells"/> and a lead whose trails extend past its end:
    /// used after copying a clipped slice of another row (invariant I3).
    /// </summary>
    public static void TrimClippedEdges(Span<TScreenChar> cells)
    {
        for (int k = 0; k < cells.Length && cells[k].Kind == TScreenCellKind.Trail; k++)
            cells[k] = new TScreenChar(' ', cells[k].Attr);
        for (int k = Math.Max(0, cells.Length - TScreenChar.MaxCellWidth); k < cells.Length; k++)
        {
            if (cells[k].Kind != TScreenCellKind.Lead || k + cells[k].CellWidth <= cells.Length) continue;
            for (int j = k; j < cells.Length; j++) cells[j] = new TScreenChar(' ', cells[j].Attr);
            break;
        }
    }
}
