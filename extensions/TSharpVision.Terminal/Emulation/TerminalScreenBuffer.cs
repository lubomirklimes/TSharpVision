namespace TSharpVision.Terminal;

/// <summary>One row of the grid, or of scrollback.</summary>
internal sealed class TerminalRow
{
    public TerminalRow(int width) => Cells = new TerminalCell[width];

    private TerminalRow(TerminalCell[] cells, bool wrapped)
    {
        Cells = cells;
        Wrapped = wrapped;
    }

    public TerminalCell[] Cells { get; private set; }

    /// <summary>The text of this row continues on the next one: the cursor wrapped at its right margin.</summary>
    public bool Wrapped { get; set; }

    public int Width => Cells.Length;

    public void Fill(int from, int to, TerminalCell cell)
    {
        from = Math.Max(0, from);
        to = Math.Min(Cells.Length, to);
        if (from < to) Array.Fill(Cells, cell, from, to - from);
    }

    public void Clear(TerminalStyle style)
    {
        Array.Fill(Cells, TerminalCell.Blank(style));
        Wrapped = false;
    }

    public void Resize(int width)
    {
        if (width == Cells.Length) return;
        TerminalCell[] cells = Cells;
        Array.Resize(ref cells, width);
        Cells = cells;
        // A wide character whose right half was cut off is no longer drawable.
        if (width > 0 && Cells[width - 1].Width == 2) Cells[width - 1] = TerminalCell.Blank(Cells[width - 1].Style);
    }

    /// <summary>Whether nothing was written on this row: no content and no background set by an erase.</summary>
    public bool IsBlank()
    {
        foreach (TerminalCell cell in Cells)
            if (!cell.IsEmpty || !cell.Style.Background.IsDefault || cell.Style.Attributes != TerminalCellAttributes.None) return false;
        return true;
    }

    /// <summary>
    /// A copy for scrollback. A row that does not wrap is trimmed of its trailing default blanks, which is most of a
    /// typical row; a wrapped row keeps its full width so column offsets within its logical line stay exact.
    /// </summary>
    public TerminalRow ToScrollback()
    {
        int length = Cells.Length;
        if (!Wrapped)
            while (length > 0 && Cells[length - 1].IsEmpty && Cells[length - 1].Style == TerminalStyle.Default) length--;
        var copy = new TerminalCell[length];
        Array.Copy(Cells, copy, length);
        return new TerminalRow(copy, Wrapped);
    }

    /// <summary>This scrollback row as a screen row of <paramref name="width"/> columns.</summary>
    public TerminalRow ToScreen(int width)
    {
        var row = new TerminalRow((TerminalCell[])Cells.Clone(), Wrapped);
        row.Resize(width);
        return row;
    }
}

/// <summary>The bounded history of rows scrolled off the top of the primary screen, oldest first.</summary>
internal sealed class TerminalScrollback
{
    private TerminalRow[] _items = Array.Empty<TerminalRow>();
    private int _start;

    public TerminalScrollback(int capacity) => Capacity = Math.Max(0, capacity);

    /// <summary>The most rows kept; the oldest is dropped when a row is added beyond it.</summary>
    public int Capacity { get; private set; }

    public int Count { get; private set; }

    /// <summary>Rows ever added, including those since dropped: lets a viewer keep its place while output arrives.</summary>
    public long TotalAdded { get; private set; }

    public TerminalRow this[int index] => _items[(_start + index) % _items.Length];

    public void Add(TerminalRow row)
    {
        TotalAdded++;
        if (Capacity == 0) return;
        if (Count == _items.Length && _items.Length < Capacity) Grow(Math.Min(Capacity, Math.Max(64, _items.Length * 2)));

        if (Count < _items.Length)
        {
            _items[(_start + Count) % _items.Length] = row;
            Count++;
        }
        else
        {
            _items[_start] = row;
            _start = (_start + 1) % _items.Length;
        }
    }

    /// <summary>Takes the newest row back, for a screen that grows taller.</summary>
    public TerminalRow? TakeNewest()
    {
        if (Count == 0) return null;
        int index = (_start + Count - 1) % _items.Length;
        TerminalRow row = _items[index];
        _items[index] = null!;
        Count--;
        return row;
    }

    public void Clear()
    {
        _items = Array.Empty<TerminalRow>();
        _start = 0;
        Count = 0;
    }

    public void SetCapacity(int capacity)
    {
        capacity = Math.Max(0, capacity);
        if (capacity == Capacity) return;
        var kept = new List<TerminalRow>(Math.Min(Count, capacity));
        for (int i = Math.Max(0, Count - capacity); i < Count; i++) kept.Add(this[i]);
        Capacity = capacity;
        _items = kept.Count == 0 ? Array.Empty<TerminalRow>() : kept.ToArray();
        _start = 0;
        Count = kept.Count;
        if (Count > 0 && Count < Capacity) Grow(Math.Min(Capacity, Math.Max(64, Count)));
    }

    private void Grow(int size)
    {
        var items = new TerminalRow[size];
        for (int i = 0; i < Count; i++) items[i] = this[i];
        _items = items;
        _start = 0;
    }
}

/// <summary>The cursor state DECSC saves and DECRC restores.</summary>
internal readonly record struct TerminalSavedCursor(
    int Row, int Column, TerminalStyle Style, bool PendingWrap, bool OriginMode, TerminalCharsets Charsets);

/// <summary>G0/G1 designations and which of them is invoked into GL.</summary>
internal readonly record struct TerminalCharsets(bool G0Graphics, bool G1Graphics, bool ShiftOut)
{
    /// <summary>Whether the invoked set is DEC Special Graphics (line drawing).</summary>
    public bool GraphicsActive => ShiftOut ? G1Graphics : G0Graphics;
}

/// <summary>
/// One screen of the terminal — the primary or the alternate one — with its cursor, margins, tab stops and, for the
/// primary screen, scrollback. Positions are zero-based and always inside the grid.
/// </summary>
internal sealed class TerminalScreenBuffer
{
    private TerminalRow[] _rows;
    private bool[] _tabStops;

    public TerminalScreenBuffer(int width, int height, TerminalScrollback? scrollback)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Scrollback = scrollback;
        _rows = new TerminalRow[Height];
        for (int i = 0; i < Height; i++) _rows[i] = new TerminalRow(Width);
        _tabStops = DefaultTabStops(Width, null);
        ScrollBottom = Height - 1;
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Scrollback of the primary screen; null for the alternate screen, which never keeps history.</summary>
    public TerminalScrollback? Scrollback { get; }

    public int CursorRow { get; private set; }

    public int CursorColumn { get; private set; }

    /// <summary>A character was printed in the last column and the next one wraps first (xterm's "wrapnext").</summary>
    public bool PendingWrap { get; private set; }

    public TerminalStyle Style { get; set; }

    public int ScrollTop { get; private set; }

    public int ScrollBottom { get; private set; }

    public TerminalSavedCursor? Saved { get; set; }

    public TerminalRow this[int row] => _rows[row];

    // ── printing ─────────────────────────────────────────────────────────────

    /// <summary>Writes one character of <paramref name="width"/> columns at the cursor and advances it.</summary>
    public void Print(char character, string? cluster, int width, bool autoWrap, bool insertMode)
    {
        if (width > Width) width = 1;

        if (PendingWrap)
        {
            PendingWrap = false;
            if (autoWrap)
            {
                _rows[CursorRow].Wrapped = true;
                CursorColumn = 0;
                Index();
            }
        }

        if (width == 2 && CursorColumn == Width - 1)
        {
            if (autoWrap)
            {
                // The wide character does not fit: the last column stays empty and it goes to the next row.
                ClearWideAround(_rows[CursorRow], CursorColumn, 1);
                _rows[CursorRow].Cells[CursorColumn] = TerminalCell.Blank(Style.ForErase);
                _rows[CursorRow].Wrapped = true;
                CursorColumn = 0;
                Index();
            }
            else
            {
                CursorColumn = Width - 2;
            }
        }

        TerminalRow row = _rows[CursorRow];
        if (insertMode) InsertCellsInRow(row, CursorColumn, width, Style.ForErase);
        ClearWideAround(row, CursorColumn, width);

        row.Cells[CursorColumn] = cluster is null
            ? TerminalCell.FromChar(character, Style, (byte)width)
            : TerminalCell.FromCluster(cluster, Style, (byte)width);
        if (width == 2) row.Cells[CursorColumn + 1] = TerminalCell.Continuation(Style);

        CursorColumn += width;
        if (CursorColumn >= Width)
        {
            CursorColumn = Width - 1;
            PendingWrap = autoWrap;
        }
    }

    /// <summary>Adds a zero-width combining mark to the character before the cursor.</summary>
    public void AppendCombining(string mark)
    {
        int column = PendingWrap ? CursorColumn : CursorColumn - 1;
        if (column < 0) return;
        TerminalRow row = _rows[CursorRow];
        if (row.Cells[column].IsContinuation && column > 0) column--;
        TerminalCell cell = row.Cells[column];
        if (cell.IsEmpty) return;
        row.Cells[column] = cell.WithCombining(mark);
    }

    // ── cursor ───────────────────────────────────────────────────────────────

    public void CarriageReturn()
    {
        CursorColumn = 0;
        PendingWrap = false;
    }

    /// <summary>Moves down one row, scrolling the region up at its bottom margin (IND, and LF).</summary>
    public void Index()
    {
        PendingWrap = false;
        if (CursorRow == ScrollBottom) ScrollUp(1);
        else if (CursorRow < Height - 1) CursorRow++;
    }

    /// <summary>Moves up one row, scrolling the region down at its top margin (RI).</summary>
    public void ReverseIndex()
    {
        PendingWrap = false;
        if (CursorRow == ScrollTop) ScrollDown(1);
        else if (CursorRow > 0) CursorRow--;
    }

    public void Backspace()
    {
        PendingWrap = false;
        if (CursorColumn > 0) CursorColumn--;
    }

    public void SetCursor(int row, int column)
    {
        CursorRow = Math.Clamp(row, 0, Height - 1);
        CursorColumn = Math.Clamp(column, 0, Width - 1);
        PendingWrap = false;
    }

    /// <summary>CUU: up, stopping at the top margin when the cursor is inside the region.</summary>
    public void MoveUp(int count)
    {
        int limit = CursorRow >= ScrollTop ? ScrollTop : 0;
        SetCursor(Math.Max(limit, CursorRow - Math.Max(1, count)), CursorColumn);
    }

    /// <summary>CUD: down, stopping at the bottom margin when the cursor is inside the region.</summary>
    public void MoveDown(int count)
    {
        int limit = CursorRow <= ScrollBottom ? ScrollBottom : Height - 1;
        SetCursor(Math.Min(limit, CursorRow + Math.Max(1, count)), CursorColumn);
    }

    public void MoveRight(int count) => SetCursor(CursorRow, CursorColumn + Math.Max(1, count));

    public void MoveLeft(int count) => SetCursor(CursorRow, CursorColumn - Math.Max(1, count));

    // ── tabs ─────────────────────────────────────────────────────────────────

    public void TabForward(int count)
    {
        PendingWrap = false;
        for (int n = Math.Max(1, count); n > 0 && CursorColumn < Width - 1; n--)
        {
            int column = CursorColumn + 1;
            while (column < Width - 1 && !_tabStops[column]) column++;
            CursorColumn = column;
        }
    }

    public void TabBackward(int count)
    {
        PendingWrap = false;
        for (int n = Math.Max(1, count); n > 0 && CursorColumn > 0; n--)
        {
            int column = CursorColumn - 1;
            while (column > 0 && !_tabStops[column]) column--;
            CursorColumn = column;
        }
    }

    public void SetTabStop() => _tabStops[CursorColumn] = true;

    /// <summary>TBC: 0 clears the stop at the cursor, 3 clears all.</summary>
    public void ClearTabStops(int mode)
    {
        if (mode == 0) _tabStops[CursorColumn] = false;
        else if (mode == 3) Array.Clear(_tabStops);
    }

    private static bool[] DefaultTabStops(int width, bool[]? previous)
    {
        var stops = new bool[width];
        int kept = previous is null ? 0 : Math.Min(width, previous.Length);
        if (previous is not null) Array.Copy(previous, stops, kept);
        for (int column = Math.Max(kept, 1); column < width; column++) stops[column] = column % 8 == 0;
        return stops;
    }

    // ── erasing ──────────────────────────────────────────────────────────────

    /// <summary>EL: 0 cursor to end, 1 start to cursor, 2 whole row.</summary>
    public void EraseInLine(int mode)
    {
        PendingWrap = false;
        TerminalRow row = _rows[CursorRow];
        TerminalCell blank = TerminalCell.Blank(Style.ForErase);
        switch (mode)
        {
            case 0:
                ClearWideAround(row, CursorColumn, Width - CursorColumn);
                row.Fill(CursorColumn, Width, blank);
                row.Wrapped = false;
                break;
            case 1:
                ClearWideAround(row, 0, CursorColumn + 1);
                row.Fill(0, CursorColumn + 1, blank);
                break;
            case 2:
                row.Clear(Style.ForErase);
                break;
        }
    }

    /// <summary>ED: 0 cursor to end of screen, 1 start of screen to cursor, 2 whole screen, 3 scrollback.</summary>
    public void EraseInDisplay(int mode)
    {
        PendingWrap = false;
        switch (mode)
        {
            case 0:
                EraseInLine(0);
                for (int r = CursorRow + 1; r < Height; r++) _rows[r].Clear(Style.ForErase);
                break;
            case 1:
                for (int r = 0; r < CursorRow; r++) _rows[r].Clear(Style.ForErase);
                EraseInLine(1);
                break;
            case 2:
                for (int r = 0; r < Height; r++) _rows[r].Clear(Style.ForErase);
                break;
            case 3:
                Scrollback?.Clear();
                break;
        }
    }

    /// <summary>ECH: blanks <paramref name="count"/> cells from the cursor without moving anything.</summary>
    public void EraseCharacters(int count)
    {
        PendingWrap = false;
        count = Math.Min(Math.Max(1, count), Width - CursorColumn);
        TerminalRow row = _rows[CursorRow];
        ClearWideAround(row, CursorColumn, count);
        row.Fill(CursorColumn, CursorColumn + count, TerminalCell.Blank(Style.ForErase));
    }

    // ── inserting and deleting ───────────────────────────────────────────────

    /// <summary>ICH: shifts the rest of the row right; cells pushed past the margin are lost.</summary>
    public void InsertCharacters(int count)
    {
        PendingWrap = false;
        InsertCellsInRow(_rows[CursorRow], CursorColumn, Math.Max(1, count), Style.ForErase);
    }

    /// <summary>DCH: shifts the rest of the row left and blanks the vacated cells at the right margin.</summary>
    public void DeleteCharacters(int count)
    {
        PendingWrap = false;
        TerminalRow row = _rows[CursorRow];
        int column = CursorColumn;
        count = Math.Min(Math.Max(1, count), Width - column);
        ClearWideAround(row, column, count);
        Array.Copy(row.Cells, column + count, row.Cells, column, Width - column - count);
        row.Fill(Width - count, Width, TerminalCell.Blank(Style.ForErase));
    }

    private static void InsertCellsInRow(TerminalRow row, int column, int count, TerminalStyle eraseStyle)
    {
        int width = row.Width;
        count = Math.Min(count, width - column);
        if (row.Cells[column].IsContinuation && column > 0) row.Cells[column - 1] = TerminalCell.Blank(row.Cells[column - 1].Style);
        Array.Copy(row.Cells, column, row.Cells, column + count, width - column - count);
        row.Fill(column, column + count, TerminalCell.Blank(eraseStyle));
        // A wide character pushed half past the margin loses its right half.
        if (row.Cells[width - 1].Width == 2) row.Cells[width - 1] = TerminalCell.Blank(row.Cells[width - 1].Style);
        // A continuation now directly after the inserted blanks has lost its left half.
        int after = column + count;
        if (after < width && row.Cells[after].IsContinuation) row.Cells[after] = TerminalCell.Blank(row.Cells[after].Style);
    }

    /// <summary>IL: inserts blank rows at the cursor, within the scroll region; the cursor goes to the left margin.</summary>
    public void InsertLines(int count)
    {
        if (CursorRow < ScrollTop || CursorRow > ScrollBottom) return;
        ShiftRowsDown(CursorRow, ScrollBottom, Math.Max(1, count));
        CarriageReturn();
    }

    /// <summary>DL: deletes rows at the cursor, within the scroll region; the cursor goes to the left margin.</summary>
    public void DeleteLines(int count)
    {
        if (CursorRow < ScrollTop || CursorRow > ScrollBottom) return;
        ShiftRowsUp(CursorRow, ScrollBottom, Math.Max(1, count), toScrollback: false);
        CarriageReturn();
    }

    // ── scrolling ────────────────────────────────────────────────────────────

    /// <summary>
    /// Scrolls the region up. Rows leaving the top of the primary screen with the top margin at its first row go to
    /// scrollback, as xterm keeps them; rows leaving an inner region are gone.
    /// </summary>
    public void ScrollUp(int count) => ShiftRowsUp(ScrollTop, ScrollBottom, Math.Max(1, count), toScrollback: ScrollTop == 0);

    public void ScrollDown(int count) => ShiftRowsDown(ScrollTop, ScrollBottom, Math.Max(1, count));

    private void ShiftRowsUp(int top, int bottom, int count, bool toScrollback)
    {
        int height = bottom - top + 1;
        count = Math.Min(count, height);
        var vacated = new TerminalRow[count];
        for (int i = 0; i < count; i++)
        {
            TerminalRow leaving = _rows[top + i];
            if (toScrollback && Scrollback is not null)
            {
                Scrollback.Add(leaving.ToScrollback());
                leaving = new TerminalRow(Width);
            }

            leaving.Clear(Style.ForErase);
            vacated[i] = leaving;
        }

        Array.Copy(_rows, top + count, _rows, top, height - count);
        Array.Copy(vacated, 0, _rows, bottom - count + 1, count);
    }

    private void ShiftRowsDown(int top, int bottom, int count)
    {
        int height = bottom - top + 1;
        count = Math.Min(count, height);
        var vacated = new TerminalRow[count];
        for (int i = 0; i < count; i++)
        {
            vacated[i] = _rows[bottom - i];
            vacated[i].Clear(Style.ForErase);
        }

        Array.Copy(_rows, top, _rows, top + count, height - count);
        Array.Copy(vacated, 0, _rows, top, count);
    }

    /// <summary>DECSTBM with zero-based inclusive margins; an invalid region means the whole screen.</summary>
    public void SetScrollRegion(int top, int bottom)
    {
        if (top < 0 || bottom >= Height || top >= bottom)
        {
            top = 0;
            bottom = Height - 1;
        }

        ScrollTop = top;
        ScrollBottom = bottom;
    }

    // ── whole screen ─────────────────────────────────────────────────────────

    public void Reset()
    {
        foreach (TerminalRow row in _rows) row.Clear(TerminalStyle.Default);
        Style = TerminalStyle.Default;
        CursorRow = CursorColumn = 0;
        PendingWrap = false;
        ScrollTop = 0;
        ScrollBottom = Height - 1;
        _tabStops = DefaultTabStops(Width, null);
        Saved = null;
    }

    /// <summary>DECALN: fills the screen with 'E'.</summary>
    public void FillWithE()
    {
        foreach (TerminalRow row in _rows) row.Fill(0, Width, TerminalCell.FromChar('E', TerminalStyle.Default, 1));
        SetScrollRegion(0, Height - 1);
        SetCursor(0, 0);
    }

    public void ClearAll(TerminalStyle style)
    {
        foreach (TerminalRow row in _rows) row.Clear(style);
    }

    public void Restore(TerminalSavedCursor saved)
    {
        SetCursor(saved.Row, saved.Column);
        Style = saved.Style;
        PendingWrap = saved.PendingWrap && CursorColumn == Width - 1;
    }

    /// <summary>
    /// Changes the size. Nothing is reflowed. Losing rows keeps the cursor's row on screen: blank rows below the
    /// cursor go first, then rows from the top, which the primary screen keeps in scrollback. Gaining rows takes them
    /// back from scrollback first. The margins reset to the whole screen; tab stops are kept where they still fit.
    /// </summary>
    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height) return;

        var rows = new List<TerminalRow>(_rows);
        int cursorRow = CursorRow;

        if (height < rows.Count)
        {
            int excess = rows.Count - height;
            for (int r = rows.Count - 1; r > cursorRow && excess > 0 && rows[r].IsBlank(); r--)
            {
                rows.RemoveAt(r);
                excess--;
            }

            for (; excess > 0; excess--)
            {
                if (Scrollback is not null) Scrollback.Add(rows[0].ToScrollback());
                rows.RemoveAt(0);
                cursorRow--;
            }
        }
        else if (height > rows.Count)
        {
            int missing = height - rows.Count;
            while (missing > 0 && Scrollback?.TakeNewest() is { } back)
            {
                rows.Insert(0, back.ToScreen(width));
                cursorRow++;
                missing--;
            }

            for (; missing > 0; missing--) rows.Add(new TerminalRow(width));
        }

        foreach (TerminalRow row in rows) row.Resize(width);

        _rows = rows.ToArray();
        _tabStops = DefaultTabStops(width, _tabStops);
        Width = width;
        Height = height;
        ScrollTop = 0;
        ScrollBottom = height - 1;
        CursorRow = Math.Clamp(cursorRow, 0, height - 1);
        CursorColumn = Math.Clamp(CursorColumn, 0, width - 1);
        PendingWrap = false;
        if (Saved is { } saved)
            Saved = saved with { Row = Math.Clamp(saved.Row, 0, height - 1), Column = Math.Clamp(saved.Column, 0, width - 1) };
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Before cells [column, column + count) are overwritten: a wide character that is only partly inside the range
    /// would be left with a lone half, so its other half is blanked.
    /// </summary>
    private static void ClearWideAround(TerminalRow row, int column, int count)
    {
        if (count <= 0) return;
        int last = Math.Min(row.Width - 1, column + count - 1);
        if (row.Cells[column].IsContinuation && column > 0)
            row.Cells[column - 1] = TerminalCell.Blank(row.Cells[column - 1].Style);
        if (row.Cells[last].Width == 2 && last + 1 < row.Width)
            row.Cells[last + 1] = TerminalCell.Blank(row.Cells[last + 1].Style);
    }
}
