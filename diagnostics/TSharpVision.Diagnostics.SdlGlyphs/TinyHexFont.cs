namespace TSharpVision.Diagnostics.SdlGlyphs;

/// <summary>
/// A 3×5 pixel font covering <c>0-9</c> and <c>A-F</c>, used only to label the
/// <c>cp437-all-b0-df</c> overview.
/// <para>
/// Deliberately built in, not taken from the TTF under test: a font whose graphics glyphs are
/// broken is exactly the case the overview must remain readable for.
/// </para>
/// </summary>
internal static class TinyHexFont
{
    public const int GlyphWidth  = 3;
    public const int GlyphHeight = 5;

    // Each row is 3 bits, MSB = leftmost pixel.
    private static readonly Dictionary<char, byte[]> Glyphs = new()
    {
        ['0'] = [0b111, 0b101, 0b101, 0b101, 0b111],
        ['1'] = [0b010, 0b110, 0b010, 0b010, 0b111],
        ['2'] = [0b111, 0b001, 0b111, 0b100, 0b111],
        ['3'] = [0b111, 0b001, 0b111, 0b001, 0b111],
        ['4'] = [0b101, 0b101, 0b111, 0b001, 0b001],
        ['5'] = [0b111, 0b100, 0b111, 0b001, 0b111],
        ['6'] = [0b111, 0b100, 0b111, 0b101, 0b111],
        ['7'] = [0b111, 0b001, 0b001, 0b001, 0b001],
        ['8'] = [0b111, 0b101, 0b111, 0b101, 0b111],
        ['9'] = [0b111, 0b101, 0b111, 0b001, 0b111],
        ['A'] = [0b111, 0b101, 0b111, 0b101, 0b101],
        ['B'] = [0b110, 0b101, 0b110, 0b101, 0b110],
        ['C'] = [0b111, 0b100, 0b100, 0b100, 0b111],
        ['D'] = [0b110, 0b101, 0b101, 0b101, 0b110],
        ['E'] = [0b111, 0b100, 0b111, 0b100, 0b111],
        ['F'] = [0b111, 0b100, 0b111, 0b100, 0b100],
    };

    /// <summary>Pixel width of <paramref name="text"/> including 1-pixel inter-glyph spacing.</summary>
    public static int MeasureWidth(string text) =>
        text.Length == 0 ? 0 : text.Length * (GlyphWidth + 1) - 1;

    /// <summary>Draws upper-case hex text at (x, y). Unknown characters are skipped.</summary>
    public static void Draw(ArgbBitmap target, int x, int y, string text, uint argb)
    {
        int cursor = x;

        foreach (char ch in text.ToUpperInvariant())
        {
            if (Glyphs.TryGetValue(ch, out byte[]? rows))
            {
                for (int r = 0; r < GlyphHeight; r++)
                    for (int c = 0; c < GlyphWidth; c++)
                        if ((rows[r] & (1 << (GlyphWidth - 1 - c))) != 0)
                            target[cursor + c, y + r] = argb;
            }

            cursor += GlyphWidth + 1;
        }
    }
}
