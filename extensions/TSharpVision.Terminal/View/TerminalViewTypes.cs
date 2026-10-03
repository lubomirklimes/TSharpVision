namespace TSharpVision.Terminal;

/// <summary>How <see cref="TTerminal"/> treats the keyboard.</summary>
public enum TerminalInputMode
{
    /// <summary>
    /// Output only: keys scroll the buffer (Up, Down, PageUp, PageDown, Home, End); Ctrl+C copies a selection.
    /// </summary>
    None,

    /// <summary>
    /// Local command line: the view keeps a prompt row with line editing and history below the output, and Enter
    /// raises <see cref="TTerminal.CommandSubmitted"/>.
    /// </summary>
    Command,

    /// <summary>
    /// A real terminal: every key is encoded as the program expects it (<see cref="TerminalInputEncoder"/>) and sent to
    /// the attached session, which echoes, edits and prompts itself.
    /// </summary>
    RawSession,
}

/// <summary>
/// A position in the text of a <see cref="TTerminal"/>: a logical line of its buffer (scrollback, then screen; a line
/// that wrapped counts once) and a cell column within that line.
/// </summary>
public readonly struct TerminalTextPosition : IEquatable<TerminalTextPosition>, IComparable<TerminalTextPosition>
{
    /// <summary>A position; nothing is clamped.</summary>
    public TerminalTextPosition(int lineIndex, int column)
    {
        LineIndex = lineIndex;
        Column = column;
    }

    /// <summary>Zero-based logical line.</summary>
    public int LineIndex { get; }

    /// <summary>Zero-based cell column within the logical line (a wide character spans two).</summary>
    public int Column { get; }

    /// <summary>Whether this position comes before <paramref name="other"/>.</summary>
    public bool IsBefore(TerminalTextPosition other) => CompareTo(other) < 0;

    /// <inheritdoc />
    public int CompareTo(TerminalTextPosition other)
        => LineIndex != other.LineIndex ? LineIndex.CompareTo(other.LineIndex) : Column.CompareTo(other.Column);

    /// <inheritdoc />
    public bool Equals(TerminalTextPosition other) => LineIndex == other.LineIndex && Column == other.Column;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TerminalTextPosition other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(LineIndex, Column);

    /// <summary>Whether both positions are the same.</summary>
    public static bool operator ==(TerminalTextPosition left, TerminalTextPosition right) => left.Equals(right);

    /// <summary>Whether the positions differ.</summary>
    public static bool operator !=(TerminalTextPosition left, TerminalTextPosition right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => $"({LineIndex},{Column})";
}

/// <summary>A command entered on the prompt row of a <see cref="TTerminal"/> in <see cref="TerminalInputMode.Command"/>.</summary>
public sealed class TerminalCommandEventArgs : EventArgs
{
    /// <summary>The command text, without prompt or newline.</summary>
    public TerminalCommandEventArgs(string command) => Command = command;

    /// <summary>The command text, without prompt or newline.</summary>
    public string Command { get; }
}
