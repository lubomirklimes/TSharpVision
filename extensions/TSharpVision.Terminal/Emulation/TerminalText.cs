using System.Text;

namespace TSharpVision.Terminal;

/// <summary>A logical line of the terminal buffer: one row, or several joined because each wrapped into the next.</summary>
internal readonly record struct TerminalLine(int FirstRow, int RowCount);

/// <summary>Text of the terminal buffer, for copying, searching and the plain-text views of <see cref="TTerminal"/>.</summary>
internal static class TerminalText
{
    /// <summary>The text of a row: each cell's grapheme, empty cells as spaces, trailing spaces removed.</summary>
    public static string RowText(ReadOnlySpan<TerminalCell> cells)
    {
        var text = new StringBuilder(cells.Length);
        AppendCells(text, cells, 0, cells.Length, null, 0);
        return TrimEnd(text);
    }

    /// <summary>The logical lines of <paramref name="emulator"/>'s buffer rows (scrollback, then screen).</summary>
    public static List<TerminalLine> Lines(TerminalEmulator emulator)
    {
        int rows = emulator.BufferRowCount;
        var lines = new List<TerminalLine>(rows);
        int first = 0;
        for (int row = 0; row < rows; row++)
        {
            emulator.GetBufferRow(row, out bool wrapped);
            if (wrapped && row < rows - 1) continue;
            lines.Add(new TerminalLine(first, row - first + 1));
            first = row + 1;
        }

        return lines;
    }

    /// <summary>
    /// The text of cells [<paramref name="startColumn"/>, <paramref name="endColumn"/>) of a logical line, where a
    /// column counts cells from the start of the line across its wrapped rows. Trailing spaces are removed. When
    /// <paramref name="columns"/> is given, it receives the line column of each character of the result.
    /// </summary>
    public static string LineText(TerminalEmulator emulator, TerminalLine line, int startColumn = 0,
        int endColumn = int.MaxValue, List<int>? columns = null)
    {
        var text = new StringBuilder();
        int offset = 0;
        for (int i = 0; i < line.RowCount; i++)
        {
            ReadOnlySpan<TerminalCell> cells = emulator.GetBufferRow(line.FirstRow + i, out _);
            int from = Math.Max(0, startColumn - offset);
            int to = Math.Min(cells.Length, endColumn - offset);
            if (from < to) AppendCells(text, cells, from, to, columns, offset);
            offset += cells.Length;
            if (offset >= endColumn) break;
        }

        string result = TrimEnd(text);
        if (columns is not null && columns.Count > result.Length) columns.RemoveRange(result.Length, columns.Count - result.Length);
        return result;
    }

    /// <summary>The line that holds buffer row <paramref name="row"/>, and the column offset of that row within it.</summary>
    public static int LineOfRow(List<TerminalLine> lines, int row, TerminalEmulator emulator, out int columnOffset)
    {
        columnOffset = 0;
        int low = 0, high = lines.Count - 1;
        while (low <= high)
        {
            int middle = (low + high) >> 1;
            TerminalLine line = lines[middle];
            if (row < line.FirstRow) high = middle - 1;
            else if (row >= line.FirstRow + line.RowCount) low = middle + 1;
            else
            {
                for (int r = line.FirstRow; r < row; r++) columnOffset += emulator.GetBufferRow(r, out _).Length;
                return middle;
            }
        }

        return -1;
    }

    private static void AppendCells(StringBuilder text, ReadOnlySpan<TerminalCell> cells, int from, int to,
        List<int>? columns, int offset)
    {
        for (int column = from; column < to; column++)
        {
            TerminalCell cell = cells[column];
            if (cell.IsContinuation) continue;
            string grapheme = cell.IsEmpty ? " " : cell.Text;
            text.Append(grapheme);
            if (columns is not null)
                for (int i = 0; i < grapheme.Length; i++) columns.Add(offset + column);
        }
    }

    private static string TrimEnd(StringBuilder text)
    {
        int length = text.Length;
        while (length > 0 && text[length - 1] == ' ') length--;
        return text.ToString(0, length);
    }
}
