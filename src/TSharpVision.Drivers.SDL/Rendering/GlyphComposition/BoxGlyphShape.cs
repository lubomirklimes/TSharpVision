namespace TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

/// <summary>
/// Line weight of one side of a box-drawing glyph.
/// </summary>
/// <remarks>
/// Per-side rather than per-glyph, because 18 CP437 characters mix single and double sides
/// (<c>B5 B6 B7 B8 BD BE C6 C7 CF D0 D1 D2 D3 D4 D5 D6 D7 D8</c>).
/// </remarks>
internal enum BoxSideStyle
{
    /// <summary>No line leaves the cell on this side.</summary>
    None = 0,

    /// <summary>A single line leaves the cell on this side.</summary>
    Single = 1,

    /// <summary>A double line (two parallel rails) leaves the cell on this side.</summary>
    Double = 2,
}

/// <summary>
/// Per-side description of a box-drawing glyph: which sides connect and with what weight.
/// </summary>
/// <remarks>
/// Within CP437 <c>B3</c>–<c>DA</c> the left/right pair always shares one style, as does the
/// up/down pair (a character never mixes a single left with a double right). The generator
/// relies on that; <see cref="Validate"/> enforces it for the table.
/// </remarks>
internal readonly record struct BoxGlyphShape(
    BoxSideStyle Left,
    BoxSideStyle Right,
    BoxSideStyle Up,
    BoxSideStyle Down)
{
    public bool HasLeft  => Left  != BoxSideStyle.None;
    public bool HasRight => Right != BoxSideStyle.None;
    public bool HasUp    => Up    != BoxSideStyle.None;
    public bool HasDown  => Down  != BoxSideStyle.None;

    /// <summary>Style shared by the left/right pair, or <see cref="BoxSideStyle.None"/>.</summary>
    public BoxSideStyle HorizontalStyle =>
        Left != BoxSideStyle.None ? Left : Right;

    /// <summary>Style shared by the up/down pair, or <see cref="BoxSideStyle.None"/>.</summary>
    public BoxSideStyle VerticalStyle =>
        Up != BoxSideStyle.None ? Up : Down;

    /// <summary>
    /// Throws when the left/right or up/down pair disagrees about line weight, which the
    /// generator does not model. Used by the mapping-table tests.
    /// </summary>
    public void Validate(char ch)
    {
        if (HasLeft && HasRight && Left != Right)
            throw new InvalidOperationException(
                $"'{ch}' (U+{(int)ch:X4}) has mismatched Left/Right styles ({Left}/{Right}).");

        if (HasUp && HasDown && Up != Down)
            throw new InvalidOperationException(
                $"'{ch}' (U+{(int)ch:X4}) has mismatched Up/Down styles ({Up}/{Down}).");
    }
}
