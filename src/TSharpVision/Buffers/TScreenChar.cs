namespace TSharpVision;

/// <summary>
/// One screen cell: a Unicode character plus a <see cref="TColorAttr"/>.
/// Mirrors upstream's packed <c>ushort</c> (low byte char, high byte attr).
/// </summary>
public struct TScreenChar
{
    public TColorAttr Attr;
    public char Character;

    public TScreenChar(char character, TColorAttr attr)
    {
        Character = character;
        Attr = attr;
    }

    /// <summary>
    /// Backwards-compat constructor used by <see cref="ScreenBuffer.Clear"/>.
    /// The console color enums are translated to a TColorAttr by treating the
    /// foreground/background pair as the low/high nibble of the attr byte.
    /// </summary>
    public TScreenChar(char character, System.ConsoleColor foreground, System.ConsoleColor background)
    {
        Character = character;
        Attr = new TColorAttr((byte)((((int)background) & 0x0F) << 4 | (((int)foreground) & 0x0F)));
    }
}
