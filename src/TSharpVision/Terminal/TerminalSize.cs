namespace TSharpVision;

/// <summary>
/// Immutable terminal cell dimensions reported to <see cref="IResizableTerminalSession"/>.
///
/// <para>
/// <see cref="Columns"/> is the number of drawable text columns in the
/// <see cref="TTerminal"/> client area (i.e. <c>size.x</c>).
/// </para>
/// <para>
/// <see cref="Rows"/> is the full terminal view height in character rows
/// (<c>size.y</c>), regardless of whether input mode is active. Sessions
/// receive the physical cell dimensions, not the reduced output-only height.
/// </para>
/// </summary>
public readonly struct TerminalSize : IEquatable<TerminalSize>
{
    /// <summary>Number of character columns. Always at least 1.</summary>
    public int Columns { get; }

    /// <summary>Number of character rows. Always at least 1.</summary>
    public int Rows { get; }

    /// <summary>
    /// Create a <see cref="TerminalSize"/> with the given dimensions.
    /// Values below 1 are clamped to 1.
    /// </summary>
    public TerminalSize(int columns, int rows)
    {
        Columns = Math.Max(1, columns);
        Rows    = Math.Max(1, rows);
    }

    /// <inheritdoc />
    public bool Equals(TerminalSize other) => Columns == other.Columns && Rows == other.Rows;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TerminalSize other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Columns, Rows);
    /// <summary>Returns true when both column and row counts match.</summary>
    public static bool operator ==(TerminalSize left, TerminalSize right) => left.Equals(right);
    /// <summary>Returns true when either column or row count differs.</summary>
    public static bool operator !=(TerminalSize left, TerminalSize right) => !left.Equals(right);
    /// <inheritdoc />
    public override string ToString() => $"{Columns}x{Rows}";
}
