using System.Text;
using TSharpVision.Text;

namespace TSharpVision.Drivers;

/// <summary>
/// Formats one row of <see cref="TScreenChar"/> cells as VT (ANSI) text with the core's cell widths as the
/// authority. Shared by the ANSI terminal driver and the Windows console driver's Windows Terminal tier.
/// </summary>
/// <remarks>
/// <para>A terminal measures text by its own rules, which can differ from the core's (TV13j U0-B: legacy conhost
/// advances one column per UTF-16 unit; a lone regional indicator is one column in Windows Terminal but two in the
/// core). The formatter therefore never trusts the terminal's advance for text whose width is uncertain: after each
/// such cluster it moves the cursor back to the core's column with CUP before the next cell is written.</para>
/// <list type="bullet">
/// <item>A lead writes its whole cluster text once and skips its trails. Before a cluster of two or more cells the
/// cells are erased with ECH, so a terminal that draws the cluster narrower leaves blank cells in the current
/// background rather than stale text. Every lead is followed by a CUP re-anchor.</item>
/// <item>A lead that does not fit (its trails are outside the slice, or it would cross the last screen column)
/// is written as blanks: under DECAWM off a terminal silently drops such a cluster, under DECAWM on it wraps.</item>
/// <item>An orphan trail is written as a blank.</item>
/// <item>Single cells keep their historical output (controls become spaces), with three defensive cases that
/// only arise from raw cell writes: a zero-width character is drawn on a space, a wide BMP character and a raw
/// surrogate pair are followed by a re-anchor, and an unpaired surrogate becomes U+FFFD.</item>
/// </list>
/// <para>East Asian Ambiguous characters in single cells are trusted narrow, as the core's default layout measures
/// them and as conhost and Windows Terminal draw them; re-anchoring after each one would multiply the output of
/// every box-drawing frame.</para>
/// </remarks>
internal static class VtCellFormatter
{
    private const char Replacement = (char)0xFFFD;

    /// <summary>Appends a CUP sequence for the zero-based cell (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static void AppendCursorPosition(StringBuilder sb, int x, int y)
    {
        sb.Append("\x1b[");
        sb.Append(y + 1);
        sb.Append(';');
        sb.Append(x + 1);
        sb.Append('H');
    }

    /// <summary>
    /// Appends the cells of one screen row segment that starts at (<paramref name="x"/>, <paramref name="y"/>),
    /// beginning with a CUP. <paramref name="lastAttr"/> and <paramref name="haveAttr"/> carry the current SGR state
    /// across rows; <paramref name="screenCols"/> (0 = unknown) is the screen width that no cluster may cross.
    /// </summary>
    public static void AppendRow(StringBuilder sb, int x, int y, ReadOnlySpan<TScreenChar> cells, int screenCols,
        Func<TColorAttr, string> sgr, ref TColorAttr lastAttr, ref bool haveAttr)
    {
        AppendCursorPosition(sb, x, y);
        Span<char> scratch = stackalloc char[2];
        bool reanchor = false;
        int i = 0;
        while (i < cells.Length)
        {
            TScreenChar cell = cells[i];
            switch (cell.Kind)
            {
                case TScreenCellKind.Lead:
                {
                    int width = cell.CellWidth;
                    ReadOnlySpan<char> text = cell.GetText(scratch);
                    bool fits = i + width <= cells.Length && (screenCols <= 0 || x + i + width <= screenCols)
                                && TrailsFollow(cells, i, width) && IsPrintable(text);
                    if (!fits)
                    {
                        // Blank every cell the cluster owns inside the slice.
                        int end = Math.Min(cells.Length, i + width);
                        for (int k = i; k < end; k++)
                        {
                            Prepare(sb, x + k, y, ref reanchor);
                            Attr(sb, cells[k].Attr, sgr, ref lastAttr, ref haveAttr);
                            sb.Append(' ');
                        }
                        i = end;
                        continue;
                    }

                    Prepare(sb, x + i, y, ref reanchor);
                    Attr(sb, cell.Attr, sgr, ref lastAttr, ref haveAttr);
                    if (width >= 2)
                    {
                        sb.Append("\x1b[");
                        sb.Append(width);
                        sb.Append('X');
                    }
                    sb.Append(text);
                    reanchor = true;
                    i += width;
                    continue;
                }

                case TScreenCellKind.Trail:
                    // Orphan trail: its lead is not in this slice.
                    Prepare(sb, x + i, y, ref reanchor);
                    Attr(sb, cell.Attr, sgr, ref lastAttr, ref haveAttr);
                    sb.Append(' ');
                    i++;
                    continue;
            }

            char ch = cell.Character;
            Prepare(sb, x + i, y, ref reanchor);
            Attr(sb, cell.Attr, sgr, ref lastAttr, ref haveAttr);
            if (char.IsControl(ch))
            {
                sb.Append(' ');
            }
            else if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < cells.Length && cells[i + 1].Kind == TScreenCellKind.Single
                    && char.IsLowSurrogate(cells[i + 1].Character)
                    && (byte)cells[i + 1].Attr == (byte)cell.Attr)
                {
                    // A raw surrogate pair over two cells: drawn as one character; the terminal decides its width.
                    sb.Append(ch);
                    sb.Append(cells[i + 1].Character);
                    reanchor = true;
                    i += 2;
                    continue;
                }
                sb.Append(Replacement);
            }
            else if (char.IsLowSurrogate(ch))
            {
                sb.Append(Replacement);
            }
            else
            {
                byte props = UnicodeWidthData.Get(ch);
                if ((props & UnicodeWidthData.ZeroWidth) != 0)
                {
                    // A lone mark, joiner or selector would attach to the previous cell; draw it on a space.
                    sb.Append(' ');
                    sb.Append(ch);
                    reanchor = true;
                }
                else
                {
                    sb.Append(ch);
                    if ((props & UnicodeWidthData.EawWide) != 0) reanchor = true;
                }
            }
            i++;
        }
    }

    /// <summary>True when <paramref name="cells"/> holds a lead or trail cell.</summary>
    public static bool HasClusterCells(ReadOnlySpan<TScreenChar> cells)
    {
        foreach (ref readonly TScreenChar cell in cells)
            if (cell.Kind != TScreenCellKind.Single) return true;
        return false;
    }

    private static bool TrailsFollow(ReadOnlySpan<TScreenChar> cells, int lead, int width)
    {
        for (int k = lead + 1; k < lead + width; k++)
            if (cells[k].Kind != TScreenCellKind.Trail) return false;
        return true;
    }

    private static bool IsPrintable(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return false;
        foreach (char c in text)
            if (char.IsControl(c)) return false;
        return true;
    }

    private static void Prepare(StringBuilder sb, int column, int y, ref bool reanchor)
    {
        if (!reanchor) return;
        AppendCursorPosition(sb, column, y);
        reanchor = false;
    }

    private static void Attr(StringBuilder sb, TColorAttr attr, Func<TColorAttr, string> sgr,
        ref TColorAttr lastAttr, ref bool haveAttr)
    {
        if (haveAttr && (byte)attr == (byte)lastAttr) return;
        sb.Append(sgr(attr));
        lastAttr = attr;
        haveAttr = true;
    }
}
