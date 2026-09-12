namespace TSharpVision;

/// <summary>
/// A single terminal screen cell: a character paired with its VGA color attribute.
/// The attribute byte encodes foreground in the low nibble and background in the
/// high nibble, exactly as VGA text mode (e.g. 0x0F = white on black).
/// </summary>
public readonly struct TerminalCell
{
    /// <summary>UTF-16 character stored in this terminal cell.</summary>
    public char Character { get; }
    /// <summary>Packed color attribute whose low byte contains foreground and background VGA color nibbles.</summary>
    public ushort Attr { get; }

    /// <summary>Creates an immutable cell from a UTF-16 character and packed color attribute.</summary>
    public TerminalCell(char character, ushort attr)
    {
        Character = character;
        Attr = attr;
    }
}
