namespace TSharpVision.TableView;

/// <summary>
/// A read-only table that a <see cref="TTableView"/> presents: columns, an optional row count, and rows read in
/// bounded blocks.
/// </summary>
/// <remarks>
/// <para>
/// The contract is the smallest one a paged grid needs. It says nothing about files, databases or plugins, so a file
/// format, a database query, a stream or a synthetic generator can all implement it. Row positions are 64-bit and the
/// view never asks for more than one block of rows at a time, so a source is never required to materialise its rows.
/// </para>
/// <para>
/// <b>Row count.</b> <see cref="RowCount"/> distinguishes three situations explicitly:
/// </para>
/// <list type="bullet">
/// <item><c>0</c> — a known empty table; the view reads nothing and shows the headers.</item>
/// <item>a positive value — a known total; the view reads no further than it.</item>
/// <item><see langword="null"/> — the total is not known. The view reads block by block as the person navigates,
/// never scans for the total, and learns where the table ends from <see cref="TableRowBlock.ReachedEnd"/>.</item>
/// </list>
/// <para>
/// <b>Display, not data.</b> Cells are text with a <see cref="TableCellRole"/>: the source decides how a value is
/// written, the view only lays it out, clips it and colours it by role. Typed values, identities and anything else the
/// owner needs later travel in <see cref="TableRow.Tag"/>, which the view never inspects.
/// </para>
/// <para>
/// <b>Threading.</b> <see cref="ReadRowsAsync"/> is called from a thread-pool thread, never from the view's drawing
/// code, and may be called again before an earlier call has completed. <see cref="Columns"/> and
/// <see cref="RowCount"/> are read on the event-loop thread and must not block.
/// </para>
/// <para>
/// <b>Ownership.</b> The view does not own its source and never disposes it. Reads still outstanding when the source
/// is replaced or the view shuts down are cancelled through their token, and their results are ignored.
/// </para>
/// </remarks>
public interface ITableDataSource
{
    /// <summary>Gets the columns, in display order. Read once when the source is attached; must not change afterwards.</summary>
    IReadOnlyList<TableColumn> Columns { get; }

    /// <summary>Gets the number of rows, or <see langword="null"/> while the total is not known.</summary>
    long? RowCount { get; }

    /// <summary>Reads at most <paramref name="count"/> rows starting at the zero-based position <paramref name="start"/>.</summary>
    /// <param name="start">Zero-based row position, never negative.</param>
    /// <param name="count">The most rows wanted; never more than one block of the view.</param>
    /// <param name="cancellationToken">Cancelled when the view no longer needs the rows.</param>
    /// <returns>
    /// The rows from <paramref name="start"/>. Fewer than <paramref name="count"/> rows with
    /// <see cref="TableRowBlock.ReachedEnd"/> means the table ends there; fewer without it means the missing rows could
    /// not be supplied, and the view shows them as unavailable rather than as the end.
    /// </returns>
    ValueTask<TableRowBlock> ReadRowsAsync(long start, int count, CancellationToken cancellationToken);
}

/// <summary>How a column's cells are aligned within the column.</summary>
public enum TableAlignment
{
    /// <summary>Text starts at the left edge of the column.</summary>
    Left = 0,

    /// <summary>Text ends at the right edge of the column — the usual choice for numbers.</summary>
    Right = 1,
}

/// <summary>One column as the view shows it: a header and layout hints.</summary>
public sealed class TableColumn
{
    /// <summary>Describes a column.</summary>
    /// <param name="title">The header text; null is shown as empty.</param>
    /// <param name="preferredWidth">A preferred width in cells, or null to size from the header and the first rows.</param>
    /// <param name="alignment">How cells are aligned.</param>
    public TableColumn(string? title, int? preferredWidth = null, TableAlignment alignment = TableAlignment.Left)
    {
        Title = title ?? string.Empty;
        PreferredWidth = preferredWidth;
        Alignment = alignment;
    }

    /// <summary>Gets the header text.</summary>
    public string Title { get; }

    /// <summary>Gets the preferred width in cells, or null. The view clamps it to its own minimum and maximum.</summary>
    public int? PreferredWidth { get; }

    /// <summary>Gets how cells are aligned.</summary>
    public TableAlignment Alignment { get; }
}

/// <summary>
/// What a cell's text is, so the view can present it by meaning — and so that meaning never rests on colour alone:
/// the source's text already says it (<c>NULL</c>, <c>&lt;binary, 16 bytes&gt;</c>, <c>!12a</c>).
/// </summary>
public enum TableCellRole
{
    /// <summary>An ordinary value.</summary>
    Normal = 0,

    /// <summary>An absent value (SQL NULL), distinct from an empty string, zero or false.</summary>
    Null = 1,

    /// <summary>A placeholder standing for a value that is not shown as text — binary data, an uninterpreted value.</summary>
    Special = 2,

    /// <summary>A value that is invalid, doubtful or could not be read.</summary>
    Error = 3,
}

/// <summary>One cell as the view shows it.</summary>
public readonly struct TableCell
{
    private readonly string? _text;

    /// <summary>Describes a cell.</summary>
    /// <param name="text">The text to show; null is shown as empty. The view clips it to the column.</param>
    /// <param name="role">What the text is.</param>
    public TableCell(string? text, TableCellRole role = TableCellRole.Normal)
    {
        _text = text;
        Role = role;
    }

    /// <summary>Gets the text to show; never null (also for a default cell).</summary>
    public string Text => _text ?? string.Empty;

    /// <summary>Gets what the text is.</summary>
    public TableCellRole Role { get; }
}

/// <summary>
/// What a whole row is, so the view can present it by meaning. As with cells, meaning never rests on colour alone:
/// the row's <see cref="TableRow.Label"/> says it too (for example a trailing <c>*</c>).
/// </summary>
public enum TableRowRole
{
    /// <summary>An ordinary row.</summary>
    Normal = 0,

    /// <summary>A row that is present but not live — kept, marked, never hidden (for example a soft-deleted record).</summary>
    Muted = 1,

    /// <summary>A row the source has doubts about as a whole.</summary>
    Warning = 2,
}

/// <summary>One row as the view shows it.</summary>
public sealed class TableRow
{
    /// <summary>Describes a row.</summary>
    /// <param name="cells">One cell per column, in column order. Missing cells are shown blank; extra cells are ignored.</param>
    /// <param name="tag">Anything the owner wants back for this row (typed values, an identity). Never inspected by the view.</param>
    /// <param name="role">What the row is.</param>
    /// <param name="label">
    /// Text for the row header (<see cref="TTableView.RowHeaderWidth"/>), such as a record number and a state mark; null
    /// for none. It identifies the row for people and is never derived by the view from the row's position.
    /// </param>
    public TableRow(IReadOnlyList<TableCell> cells, object? tag = null, TableRowRole role = TableRowRole.Normal, string? label = null)
    {
        Cells = cells ?? throw new ArgumentNullException(nameof(cells));
        Tag = tag;
        Role = role;
        Label = label;
    }

    /// <summary>Gets the cells, in column order.</summary>
    public IReadOnlyList<TableCell> Cells { get; }

    /// <summary>Gets the owner's payload for this row.</summary>
    public object? Tag { get; }

    /// <summary>Gets what the row is.</summary>
    public TableRowRole Role { get; }

    /// <summary>Gets the row header text, or null.</summary>
    public string? Label { get; }
}

/// <summary>
/// Where a <see cref="TTableView"/> is: the current cell and the scroll position. An owner keeps one to put the view
/// back exactly where it was (<see cref="TTableView.SetDataSource(ITableDataSource?, TableViewPosition)"/>).
/// </summary>
public readonly struct TableViewPosition
{
    /// <summary>Describes a position.</summary>
    /// <param name="row">The current row.</param>
    /// <param name="column">The current column.</param>
    /// <param name="topRow">The first row on screen.</param>
    /// <param name="leftColumn">The first column on screen.</param>
    /// <param name="columnWidths">
    /// The column widths laid out, so a restored view lays the columns out as before instead of sampling them again from
    /// other rows; null to size them afresh. Ignored when it does not match the table's column count.
    /// </param>
    public TableViewPosition(long row, int column, long topRow, int leftColumn, IReadOnlyList<int>? columnWidths = null)
    {
        Row = row;
        Column = column;
        TopRow = topRow;
        LeftColumn = leftColumn;
        ColumnWidths = columnWidths?.ToArray();
    }

    /// <summary>Gets the current row.</summary>
    public long Row { get; }

    /// <summary>Gets the current column.</summary>
    public int Column { get; }

    /// <summary>Gets the first row on screen.</summary>
    public long TopRow { get; }

    /// <summary>Gets the first column on screen.</summary>
    public int LeftColumn { get; }

    /// <summary>Gets the column widths laid out, or null.</summary>
    public IReadOnlyList<int>? ColumnWidths { get; }
}

/// <summary>A block of consecutive rows, as one read produced it.</summary>
public sealed class TableRowBlock
{
    /// <summary>Describes a block.</summary>
    /// <param name="start">The position of the first row; must equal the position that was asked for.</param>
    /// <param name="rows">The rows, in order.</param>
    /// <param name="reachedEnd">Whether no rows follow the last one in <paramref name="rows"/>.</param>
    public TableRowBlock(long start, IReadOnlyList<TableRow> rows, bool reachedEnd)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        Start = start;
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        ReachedEnd = reachedEnd;
    }

    /// <summary>Gets the position of the first row.</summary>
    public long Start { get; }

    /// <summary>Gets the rows, in order.</summary>
    public IReadOnlyList<TableRow> Rows { get; }

    /// <summary>Gets whether no rows follow the last one in <see cref="Rows"/>.</summary>
    public bool ReachedEnd { get; }
}
