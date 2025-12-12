namespace TSharpVision;

/// <summary>
/// A position within the terminal line buffer: a zero-based index into the
/// retained lines and a zero-based column offset within that line.
/// </summary>
public readonly struct TerminalTextPosition : IEquatable<TerminalTextPosition>
{
    /// <summary>Zero-based index into the terminal's retained line buffer.</summary>
    public int LineIndex { get; }

    /// <summary>Zero-based column within the line (may exceed the line length).</summary>
    public int Column { get; }

    public TerminalTextPosition(int lineIndex, int column)
    {
        LineIndex = lineIndex;
        Column = column;
    }

    /// <summary>
    /// Returns true if this position precedes <paramref name="other"/> in
    /// document order (line-first, column-second).
    /// </summary>
    public bool IsBefore(TerminalTextPosition other)
        => LineIndex < other.LineIndex ||
           (LineIndex == other.LineIndex && Column < other.Column);

    public bool Equals(TerminalTextPosition other)
        => LineIndex == other.LineIndex && Column == other.Column;

    public override bool Equals(object? obj)
        => obj is TerminalTextPosition p && Equals(p);

    public override int GetHashCode() => HashCode.Combine(LineIndex, Column);

    public static bool operator ==(TerminalTextPosition a, TerminalTextPosition b) => a.Equals(b);
    public static bool operator !=(TerminalTextPosition a, TerminalTextPosition b) => !a.Equals(b);

    public override string ToString() => $"({LineIndex},{Column})";
}
