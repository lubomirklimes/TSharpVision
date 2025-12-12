namespace TSharpVision;

/// <summary>
/// A single terminal screen cell: a character paired with its VGA color attribute.
/// The attribute byte encodes foreground in the low nibble and background in the
/// high nibble, exactly as VGA text mode (e.g. 0x0F = white on black).
/// </summary>
public readonly struct TerminalCell
{
    public char Character { get; }
    public ushort Attr { get; }

    public TerminalCell(char character, ushort attr)
    {
        Character = character;
        Attr = attr;
    }
}
