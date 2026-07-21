using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Diagnostics.SdlGlyphs;

/// <summary>
/// Character-level layouts for the diagnostic BMPs. Everything here is described as cells so the
/// scenes can be reviewed as text; <see cref="SceneRenderer"/> turns them into pixels.
/// </summary>
internal static class Scenes
{
    // VGA palette indices used throughout.
    private const byte Black     = 0;
    private const byte Blue      = 1;
    private const byte Cyan      = 3;
    private const byte LightGray = 7;
    private const byte DarkGray  = 8;
    private const byte White     = 15;

    private const char Light  = '░';   // B0
    private const char Medium = '▒';   // B1
    private const char Dark   = '▓';   // B2
    private const char Full   = '█';   // DB
    private const char Lower  = '▄';   // DC
    private const char Left   = '▌';   // DD
    private const char Right  = '▐';   // DE
    private const char Upper  = '▀';   // DF

    // ─────────────────────────────────────────────────────────────────────────
    // cp437-shading
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 2×2 patches and 8×4 fields of ░ ▒ ▓, plus a density comparison band.
    /// Any seam at a cell boundary shows up as a bright or dark line inside a field.
    /// </summary>
    public static TextGrid Shading()
    {
        var g = new TextGrid(26, 9, White, Black);

        // 2x2 patches — the minimum case from the brief.
        Patch(0,  0, Light);
        Patch(3,  0, Medium);
        Patch(6,  0, Dark);

        // Larger fields where a repeating seam becomes obvious.
        Field(0,  3, Light);
        Field(9,  3, Medium);
        Field(18, 3, Dark);

        // Density comparison: ░ ▒ ▓ side by side.
        g.Repeat(0,  8, Light,  6, White, Black);
        g.Repeat(6,  8, Medium, 6, White, Black);
        g.Repeat(12, 8, Dark,   6, White, Black);

        return g;

        void Patch(int c, int r, char ch) => g.FillRect(c, r, 2, 2, ch, White, Black);
        void Field(int c, int r, char ch) => g.FillRect(c, r, 8, 4, ch, White, Black);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // cp437-boxes
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Single, double and both mixed grids; nested boxes at zero and two-cell inset; a shared
    /// wall; a single box touching a double box; and a strip of every box character B3–DA.
    /// </summary>
    public static TextGrid Boxes()
    {
        var g = new TextGrid(24, 22, White, Black);

        g.PutBlock(0,  0, GeneratedGlyphChecks.SingleBoxScene,             White, Black);
        g.PutBlock(6,  0, GeneratedGlyphChecks.DoubleBoxScene,             White, Black);
        g.PutBlock(12, 0, GeneratedGlyphChecks.MixedSingleVerticalScene,   White, Black);
        g.PutBlock(18, 0, GeneratedGlyphChecks.MixedDoubleVerticalScene,   White, Black);

        // Nested: a single box immediately inside a double box (no margin).
        g.PutBlock(0, 6, new[]
        {
            "╔═══════╗",
            "║┌─────┐║",
            "║│     │║",
            "║├─────┤║",
            "║│     │║",
            "║└─────┘║",
            "╚═══════╝",
        }, White, Black);

        // Nested with a two-cell margin.
        g.PutBlock(11, 6, new[]
        {
            "╔═════════╗",
            "║         ║",
            "║ ┌─────┐ ║",
            "║ │     │ ║",
            "║ └─────┘ ║",
            "║         ║",
            "╚═════════╝",
        }, White, Black);

        // Two boxes sharing a wall.
        g.PutBlock(0, 15, new[]
        {
            "┌───┬───┐",
            "│   │   │",
            "└───┴───┘",
        }, White, Black);

        // A single box directly beside a double box.
        g.PutBlock(11, 15, new[]
        {
            "┌───┐╔═══╗",
            "│   │║   ║",
            "└───┘╚═══╝",
        }, White, Black);

        // Every box character B3..DA, in CP437 order.
        char[] boxChars = Cp437GraphicsCharacters.All
            .Where(static e => e.Category == Cp437GraphicsCategory.BoxDrawing)
            .Select(static e => e.Character)
            .ToArray();

        for (int i = 0; i < boxChars.Length; i++)
            g.Put(i % 20, 19 + i / 20, boxChars[i], White, Black);

        return g;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // cp437-blocks
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Single / run / rectangle full blocks, the two half-block pairs, alternating half blocks,
    /// and the seven tetrominoes — the exact shapes the Tetris sample draws with █.
    /// </summary>
    public static TextGrid Blocks()
    {
        var g = new TextGrid(36, 18, White, Black);

        g.Put(0, 0, Full, White, Black);                      // one block
        g.Repeat(2, 0, Full, 8, White, Black);                // horizontal run

        for (int r = 0; r < 6; r++)                           // vertical run
            g.Put(12, r, Full, White, Black);

        g.FillRect(14, 0, 6, 4, Full, White, Black);          // solid rectangle

        g.Repeat(0, 7, Upper, 8, White, Black);               // ▀ over ▄ must form a solid band
        g.Repeat(0, 8, Lower, 8, White, Black);

        for (int r = 7; r <= 8; r++)                          // ▌ beside ▐ must form a solid column
        {
            g.Put(10, r, Left,  White, Black);
            g.Put(11, r, Right, White, Black);
        }

        for (int c = 0; c < 12; c++)                          // alternating half blocks
        {
            g.Put(14 + c, 7, (c % 2 == 0) ? Upper : Lower, White, Black);
            g.Put(14 + c, 8, (c % 2 == 0) ? Left  : Right, White, Black);
        }

        string[][] tetrominoes =
        [
            ["████"],                    // I
            ["██", "██"],                // O
            ["███", " █ "],              // T
            [" ██", "██ "],              // S
            ["██ ", " ██"],              // Z
            ["█  ", "███"],              // J
            ["  █", "███"],              // L
        ];

        byte[] colors = [White, 14, 11, 10, 12, 9, 13];

        int col = 0;
        for (int t = 0; t < tetrominoes.Length; t++)
        {
            g.PutBlock(col, 11, tetrominoes[t], colors[t], Black);
            col += tetrominoes[t].Max(static row => row.Length) + 1;
        }

        // Density ramp next to a full block: █ ▓ ▒ ░
        g.Repeat(0,  15, Full,   4, White, Black);
        g.Repeat(4,  15, Dark,   4, White, Black);
        g.Repeat(8,  15, Medium, 4, White, Black);
        g.Repeat(12, 15, Light,  4, White, Black);

        return g;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // cp437-button-shadow
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two Turbo Vision buttons drawn exactly as <c>TButton.DrawState</c> draws them
    /// (<c>▄</c> on the first body row's right edge, <c>█</c> on later rows, and a bottom row of
    /// two spaces followed by <c>▀</c>), once on a plain background and once over the <c>░</c>
    /// desktop fill.
    /// </summary>
    public static TextGrid ButtonShadow()
    {
        var g = new TextGrid(24, 16, LightGray, Black);

        // Plain background.
        DrawButton(g, col: 2, row: 1, width: 12, height: 2, backdrop: Black);
        DrawButton(g, col: 2, row: 5, width: 12, height: 3, backdrop: Black);

        // Over the desktop pattern.
        g.FillRect(0, 9, 24, 7, Light, LightGray, Blue);
        DrawButton(g, col: 2, row: 10, width: 12, height: 3, backdrop: Blue);

        return g;
    }

    /// <summary>
    /// Faithful transcription of <c>TButton.DrawState(down: false)</c>. Colours are
    /// representative (black on cyan body, dark grey shadow) because the real values come from
    /// the running palette; the characters and their positions are exact.
    /// </summary>
    private static void DrawButton(TextGrid g, int col, int row, int width, int height, byte backdrop)
    {
        const byte ButtonFg = Black;
        const byte ButtonBg = Cyan;
        const byte ShadowFg = DarkGray;

        int s = width - 1;

        for (int y = 0; y <= height - 2; y++)
        {
            // b.moveChar(0, ' ', cButton, size.x)
            g.Repeat(col, row + y, ' ', width, ButtonFg, ButtonBg);

            // b.putAttribute(0, cShadow)
            g.Put(col, row + y, ' ', ShadowFg, backdrop);

            // b.putAttribute(s, cShadow); b.putChar(s, y == 0 ? shadows[0] : shadows[1])
            g.Put(col + s, row + y, y == 0 ? Lower : Full, ShadowFg, backdrop);
        }

        // Title, centred on row size.y / 2 - 1.
        int titleRow = height / 2 - 1;
        const string title = "OK";
        int titleCol = col + 1 + (s - title.Length - 1) / 2;
        g.PutString(Math.Max(col + 1, titleCol), row + titleRow, title, ButtonFg, ButtonBg);

        // b.moveChar(0, ' ', cShadow, 2); b.moveChar(2, ch, cShadow, s - 1)
        g.Repeat(col,     row + height - 1, ' ',   2,     ShadowFg, backdrop);
        g.Repeat(col + 2, row + height - 1, Upper, s - 1, ShadowFg, backdrop);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // cp437-scrollbars
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Vertical and horizontal scrollbars drawn exactly as <c>TScrollBar.DrawPos</c> draws them,
    /// at three thumb positions, plus a window whose frame meets both bars.
    /// <para>
    /// Of the five characters only the track <c>▒</c> (B1) and the bright fill <c>░</c> (B0) are
    /// generated; <c>▲ ▼ ◄ ► ■</c> are outside B0–DF and still come from the font. The
    /// "generated only" render of this same scene shows exactly which cells those are.
    /// </para>
    /// </summary>
    public static TextGrid Scrollbars()
    {
        var g = new TextGrid(24, 28, LightGray, Black);

        // Three vertical bars, thumb near the start / middle / end.
        VerticalBar(g, col: 1, row: 0, length: 12, thumbIndex: 1);
        VerticalBar(g, col: 4, row: 0, length: 12, thumbIndex: 6);
        VerticalBar(g, col: 7, row: 0, length: 12, thumbIndex: 10);

        // An "empty range" bar: TScrollBar uses the ▒ track when maxVal == minVal.
        VerticalBar(g, col: 10, row: 0, length: 12, thumbIndex: -1);

        // Three horizontal bars.
        HorizontalBar(g, col: 0, row: 13, length: 20, thumbIndex: 1);
        HorizontalBar(g, col: 0, row: 15, length: 20, thumbIndex: 9);
        HorizontalBar(g, col: 0, row: 17, length: 20, thumbIndex: 18);
        HorizontalBar(g, col: 0, row: 19, length: 20, thumbIndex: -1);

        // A window frame with both bars attached, to expose the frame/track junction.
        const int wx = 0, wy = 21, ww = 20, wh = 7;

        g.Put(wx, wy, '┌', White, Blue);
        g.Repeat(wx + 1, wy, '─', ww - 2, White, Blue);
        g.Put(wx + ww - 1, wy, '┐', White, Blue);

        for (int r = 1; r < wh - 1; r++)
        {
            g.Put(wx, wy + r, '│', White, Blue);
            g.Repeat(wx + 1, wy + r, ' ', ww - 2, LightGray, Black);
            g.Put(wx + ww - 1, wy + r, '│', White, Blue);
        }

        g.Put(wx, wy + wh - 1, '└', White, Blue);
        g.Repeat(wx + 1, wy + wh - 1, '─', ww - 2, White, Blue);
        g.Put(wx + ww - 1, wy + wh - 1, '┘', White, Blue);

        VerticalBar(g, col: wx + ww - 1, row: wy + 1, length: wh - 2, thumbIndex: 2);
        HorizontalBar(g, col: wx + 1, row: wy + wh - 1, length: ww - 2, thumbIndex: 5);

        return g;
    }

    // TScrollBar.DrawPos: chars[0] at index 0, chars[4] (bright ░) filling 1..size-2,
    // chars[3] (■) at the thumb, chars[1] at index size-1. When maxVal == minVal the whole
    // track uses chars[2] (▒) instead.
    private const byte ScrollArrowFg = Black;
    private const byte ScrollTrackFg = Blue;
    private const byte ScrollThumbFg = Black;
    private const byte ScrollBg      = Cyan;

    private static void VerticalBar(TextGrid g, int col, int row, int length, int thumbIndex)
    {
        int s = length - 1;

        g.Put(col, row, '▲', ScrollArrowFg, ScrollBg);

        char fill = thumbIndex < 0 ? Medium : Light;
        for (int i = 1; i < s; i++)
            g.Put(col, row + i, fill, ScrollTrackFg, ScrollBg);

        if (thumbIndex >= 0)
            g.Put(col, row + thumbIndex, '■', ScrollThumbFg, ScrollBg);

        g.Put(col, row + s, '▼', ScrollArrowFg, ScrollBg);
    }

    private static void HorizontalBar(TextGrid g, int col, int row, int length, int thumbIndex)
    {
        int s = length - 1;

        g.Put(col, row, '◄', ScrollArrowFg, ScrollBg);

        char fill = thumbIndex < 0 ? Medium : Light;
        for (int i = 1; i < s; i++)
            g.Put(col + i, row, fill, ScrollTrackFg, ScrollBg);

        if (thumbIndex >= 0)
            g.Put(col + thumbIndex, row, '■', ScrollThumbFg, ScrollBg);

        g.Put(col + s, row, '►', ScrollArrowFg, ScrollBg);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // cp437-all-b0-df (tiled, unlabelled)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>All 48 glyphs on a tight 8×6 grid, for automatic adjacency analysis.</summary>
    public static TextGrid AllGraphicsTiled()
    {
        var g = new TextGrid(8, 6, White, Black);

        IReadOnlyList<char> all = Cp437GraphicsCharacters.AllCharacters;
        for (int i = 0; i < all.Count; i++)
            g.Put(i % 8, i / 8, all[i], White, Black);

        return g;
    }
}
