namespace TSharpVision.Terminal;

/// <summary>
/// Maps a cell's terminal colours to the 16-colour VGA attribute TSharpVision drivers draw — the one place where colour
/// intent is approximated. The emulator keeps what the application asked for.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>Default colours: light grey on black; bold default text is white.</description></item>
///   <item><description>ANSI 0–15 (SGR 30–37, 90–97, 40–47, 100–107, and 38/48;5;0–15): through a 16-entry table, by
///   default the standard ANSI→VGA order (red is VGA 4, blue VGA 1, …). Bold makes 0–7 bright.</description></item>
///   <item><description>256-colour 16–255 and 24-bit colours: the nearest of the 16 VGA colours by squared RGB
///   distance, the lowest index winning a tie — deterministic, and computed once per index.</description></item>
///   <item><description>Inverse swaps foreground and background, concealed text takes the background as foreground, and
///   reverse video (DECSCNM) swaps the whole screen. Italic, underline, strikethrough, dim and blink have no VGA
///   attribute and are not shown.</description></item>
/// </list>
/// </remarks>
internal sealed class TerminalColorMapper
{
    /// <summary>VGA foreground for default-coloured text.</summary>
    public const byte DefaultForeground = 0x07;

    /// <summary>VGA background for the default background.</summary>
    public const byte DefaultBackground = 0x00;

    // ANSI colour n (0 black, 1 red, 2 green, 3 yellow, 4 blue, 5 magenta, 6 cyan, 7 white) → VGA index.
    private static readonly byte[] AnsiToVga = { 0, 4, 2, 6, 1, 5, 3, 7, 8, 12, 10, 14, 9, 13, 11, 15 };

    private static readonly (byte R, byte G, byte B)[] VgaPalette =
    {
        (0, 0, 0), (0, 0, 170), (0, 170, 0), (0, 170, 170), (170, 0, 0), (170, 0, 170), (170, 85, 0), (170, 170, 170),
        (85, 85, 85), (85, 85, 255), (85, 255, 85), (85, 255, 255), (255, 85, 85), (255, 85, 255), (255, 255, 85),
        (255, 255, 255),
    };

    private static readonly byte[] IndexedToVga = BuildIndexedTable();

    private readonly byte[] _ansi = (byte[])AnsiToVga.Clone();

    /// <summary>
    /// Replaces the table for ANSI colours 0–15: <paramref name="map16"/>[n] is the VGA colour (0–15) for ANSI colour
    /// n. Fewer than 16 entries leave the table unchanged.
    /// </summary>
    public void ApplyColorMap(byte[] map16)
    {
        if (map16 is null || map16.Length < 16) return;
        for (int i = 0; i < 16; i++) _ansi[i] = (byte)(map16[i] & 0x0F);
    }

    /// <summary>The packed VGA attribute (background in the high nibble) for a cell in <paramref name="style"/>.</summary>
    public byte ToAttribute(TerminalStyle style, bool reverseVideo)
    {
        bool bold = (style.Attributes & TerminalCellAttributes.Bold) != 0;
        byte fg = style.Foreground.Kind switch
        {
            TerminalColorKind.Default => bold ? (byte)0x0F : DefaultForeground,
            TerminalColorKind.Indexed => Indexed(style.Foreground.Index, bold),
            _ => Nearest(style.Foreground.Red, style.Foreground.Green, style.Foreground.Blue),
        };
        byte bg = style.Background.Kind switch
        {
            TerminalColorKind.Default => DefaultBackground,
            TerminalColorKind.Indexed => Indexed(style.Background.Index, false),
            _ => Nearest(style.Background.Red, style.Background.Green, style.Background.Blue),
        };

        if ((style.Attributes & TerminalCellAttributes.Inverse) != 0 ^ reverseVideo) (fg, bg) = (bg, fg);
        if ((style.Attributes & TerminalCellAttributes.Hidden) != 0) fg = bg;
        return (byte)((bg << 4) | fg);
    }

    private byte Indexed(byte index, bool bold)
    {
        if (index < 16) return _ansi[bold && index < 8 ? index + 8 : index];
        return IndexedToVga[index];
    }

    /// <summary>The nearest VGA colour to an RGB value.</summary>
    public static byte Nearest(byte red, byte green, byte blue)
    {
        int best = 0, bestDistance = int.MaxValue;
        for (int i = 0; i < VgaPalette.Length; i++)
        {
            int dr = red - VgaPalette[i].R, dg = green - VgaPalette[i].G, db = blue - VgaPalette[i].B;
            int distance = dr * dr + dg * dg + db * db;
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return (byte)best;
    }

    private static byte[] BuildIndexedTable()
    {
        var table = new byte[256];
        byte[] levels = { 0, 95, 135, 175, 215, 255 };
        for (int i = 0; i < 16; i++) table[i] = AnsiToVga[i];
        for (int i = 16; i < 232; i++)
        {
            int n = i - 16;
            table[i] = Nearest(levels[n / 36], levels[n / 6 % 6], levels[n % 6]);
        }

        for (int i = 232; i < 256; i++)
        {
            byte grey = (byte)(8 + 10 * (i - 232));
            table[i] = Nearest(grey, grey, grey);
        }

        return table;
    }
}
