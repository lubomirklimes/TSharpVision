namespace TSharpVision.Terminal;

/// <summary>
/// One cell of the terminal grid: its content, its style and how many columns that content occupies.
/// </summary>
/// <remarks>
/// <para>
/// Content is a grapheme: normally one BMP character, kept inline; a supplementary-plane character or a character with
/// combining marks is kept as a string. An empty cell has no content and draws as a space in its style (erased cells keep
/// the background they were erased with).
/// </para>
/// <para>
/// A wide (East Asian full-width, emoji) character occupies two columns: the left cell holds it with
/// <see cref="Width"/> 2, the right one is a continuation cell with <see cref="Width"/> 0 and no content.
/// </para>
/// </remarks>
public readonly struct TerminalCell : IEquatable<TerminalCell>
{
    private readonly char _char;
    private readonly string? _cluster;

    // 0 = one column, 1 = left half of a wide character, 2 = right half; so default(TerminalCell) is an empty
    // one-column cell in the default style, and a freshly allocated row is blank.
    private readonly byte _kind;

    private TerminalCell(char character, string? cluster, TerminalStyle style, byte width)
    {
        _char = character;
        _cluster = cluster;
        Style = style;
        _kind = width switch { 2 => 1, 0 => 2, _ => 0 };
    }

    /// <summary>An empty cell in <paramref name="style"/>.</summary>
    public static TerminalCell Blank(TerminalStyle style) => new('\0', null, style, 1);

    /// <summary>A cell holding one BMP character.</summary>
    internal static TerminalCell FromChar(char character, TerminalStyle style, byte width)
        => new(character, null, style, width);

    /// <summary>A cell holding a grapheme of more than one UTF-16 unit.</summary>
    internal static TerminalCell FromCluster(string cluster, TerminalStyle style, byte width)
        => cluster.Length == 1 ? new(cluster[0], null, style, width) : new('\0', cluster, style, width);

    /// <summary>The right half of a wide character.</summary>
    internal static TerminalCell Continuation(TerminalStyle style) => new('\0', null, style, 0);

    /// <summary>The cell's text: its grapheme, or an empty string for an empty or continuation cell.</summary>
    public string Text => _cluster ?? (_char == '\0' ? string.Empty : _char.ToString());

    /// <summary>Whether the cell holds no content (empty, erased or a continuation cell).</summary>
    public bool IsEmpty => _cluster is null && _char == '\0';

    /// <summary>Colours and attributes.</summary>
    public TerminalStyle Style { get; }

    /// <summary>Columns this cell's content occupies: 1, 2 for the left half of a wide character, 0 for its right half.</summary>
    public byte Width => _kind switch { 1 => 2, 2 => 0, _ => 1 };

    /// <summary>Whether this is the right half of a wide character.</summary>
    public bool IsContinuation => Width == 0;

    /// <summary>
    /// The single UTF-16 character a one-character-per-cell display shows for this cell: the character itself, a space
    /// for an empty cell, the base character of a combining sequence, or U+FFFD for a supplementary-plane character that
    /// one UTF-16 unit cannot hold.
    /// </summary>
    public char DisplayChar
    {
        get
        {
            if (_cluster is null) return _char == '\0' ? ' ' : _char;
            char first = _cluster[0];
            return char.IsSurrogate(first) ? '�' : first;
        }
    }

    /// <summary>This cell with a combining mark appended to its grapheme.</summary>
    internal TerminalCell WithCombining(string mark)
    {
        string text = Text;
        if (text.Length == 0 || text.Length + mark.Length > MaximumClusterLength) return this;
        return new('\0', text + mark, Style, Width);
    }

    /// <summary>The longest grapheme a cell keeps; further combining marks are dropped, so a flood of them stays bounded.</summary>
    internal const int MaximumClusterLength = 32;

    /// <inheritdoc />
    public bool Equals(TerminalCell other)
        => _char == other._char && string.Equals(_cluster, other._cluster, StringComparison.Ordinal)
           && Style == other.Style && _kind == other._kind;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TerminalCell other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_char, _cluster, Style, _kind);

    /// <summary>Whether both cells are identical.</summary>
    public static bool operator ==(TerminalCell left, TerminalCell right) => left.Equals(right);

    /// <summary>Whether the cells differ.</summary>
    public static bool operator !=(TerminalCell left, TerminalCell right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => IsContinuation ? "<cont>" : IsEmpty ? "<empty>" : Text;
}
