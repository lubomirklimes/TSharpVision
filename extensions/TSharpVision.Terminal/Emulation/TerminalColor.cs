namespace TSharpVision.Terminal;

/// <summary>How a <see cref="TerminalColor"/> is expressed.</summary>
public enum TerminalColorKind : byte
{
    /// <summary>The terminal's default foreground or background.</summary>
    Default,

    /// <summary>An entry of the 256-colour palette: 0–15 are the ANSI colours, 16–231 the 6×6×6 cube, 232–255 greys.</summary>
    Indexed,

    /// <summary>A 24-bit true colour.</summary>
    Rgb,
}

/// <summary>
/// A colour as the terminal application asked for it: default, a 256-colour palette index or 24-bit RGB. It keeps the
/// application's intent; approximating it to what a display driver can show happens only when a cell is drawn.
/// </summary>
public readonly struct TerminalColor : IEquatable<TerminalColor>
{
    private const uint KindShift = 24;
    private readonly uint _value;

    private TerminalColor(uint value) => _value = value;

    /// <summary>The default colour.</summary>
    public static TerminalColor Default => default;

    /// <summary>A 256-colour palette entry.</summary>
    public static TerminalColor FromIndex(byte index) => new(((uint)TerminalColorKind.Indexed << (int)KindShift) | index);

    /// <summary>A 24-bit colour.</summary>
    public static TerminalColor FromRgb(byte red, byte green, byte blue)
        => new(((uint)TerminalColorKind.Rgb << (int)KindShift) | ((uint)red << 16) | ((uint)green << 8) | blue);

    /// <summary>How the colour is expressed.</summary>
    public TerminalColorKind Kind => (TerminalColorKind)(_value >> (int)KindShift);

    /// <summary>The palette index of an <see cref="TerminalColorKind.Indexed"/> colour; 0 otherwise.</summary>
    public byte Index => Kind == TerminalColorKind.Indexed ? (byte)_value : (byte)0;

    /// <summary>The red component of an <see cref="TerminalColorKind.Rgb"/> colour; 0 otherwise.</summary>
    public byte Red => Kind == TerminalColorKind.Rgb ? (byte)(_value >> 16) : (byte)0;

    /// <summary>The green component of an <see cref="TerminalColorKind.Rgb"/> colour; 0 otherwise.</summary>
    public byte Green => Kind == TerminalColorKind.Rgb ? (byte)(_value >> 8) : (byte)0;

    /// <summary>The blue component of an <see cref="TerminalColorKind.Rgb"/> colour; 0 otherwise.</summary>
    public byte Blue => Kind == TerminalColorKind.Rgb ? (byte)_value : (byte)0;

    /// <summary>Whether this is the default colour.</summary>
    public bool IsDefault => _value == 0;

    /// <inheritdoc />
    public bool Equals(TerminalColor other) => _value == other._value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TerminalColor other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (int)_value;

    /// <summary>Whether both colours are the same.</summary>
    public static bool operator ==(TerminalColor left, TerminalColor right) => left.Equals(right);

    /// <summary>Whether the colours differ.</summary>
    public static bool operator !=(TerminalColor left, TerminalColor right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        TerminalColorKind.Indexed => $"#{Index}",
        TerminalColorKind.Rgb => $"rgb({Red},{Green},{Blue})",
        _ => "default",
    };
}

/// <summary>Character attributes of a terminal cell, as set by SGR.</summary>
[Flags]
public enum TerminalCellAttributes : byte
{
    /// <summary>No attribute.</summary>
    None = 0,

    /// <summary>Bold or increased intensity (SGR 1).</summary>
    Bold = 1,

    /// <summary>Faint or decreased intensity (SGR 2).</summary>
    Dim = 2,

    /// <summary>Italic (SGR 3).</summary>
    Italic = 4,

    /// <summary>Underlined, any style (SGR 4, 21).</summary>
    Underline = 8,

    /// <summary>Blinking (SGR 5, 6).</summary>
    Blink = 16,

    /// <summary>Foreground and background swapped (SGR 7).</summary>
    Inverse = 32,

    /// <summary>Concealed (SGR 8).</summary>
    Hidden = 64,

    /// <summary>Crossed out (SGR 9).</summary>
    Strikethrough = 128,
}

/// <summary>The graphic rendition a cell is written with: colours and attributes.</summary>
public readonly record struct TerminalStyle(TerminalColor Foreground, TerminalColor Background, TerminalCellAttributes Attributes)
{
    /// <summary>Default colours, no attributes.</summary>
    public static TerminalStyle Default => default;

    /// <summary>
    /// The style erased cells get: the current background (background colour erase, as xterm does), default foreground,
    /// no attributes.
    /// </summary>
    internal TerminalStyle ForErase => new(TerminalColor.Default, Background, TerminalCellAttributes.None);
}
