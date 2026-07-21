namespace TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

/// <summary>
/// Which kind of generated glyph a CP437 <c>B0</c>–<c>DF</c> code is.
/// </summary>
internal enum Cp437GraphicsCategory
{
    /// <summary>Shading / pattern fill: <c>B0 ░</c>, <c>B1 ▒</c>, <c>B2 ▓</c>.</summary>
    Shading,

    /// <summary>Box drawing, single / double / mixed: <c>B3</c>–<c>DA</c>.</summary>
    BoxDrawing,

    /// <summary>Block element: <c>DB █</c>, <c>DC ▄</c>, <c>DD ▌</c>, <c>DE ▐</c>, <c>DF ▀</c>.</summary>
    Block,
}

/// <summary>
/// One entry of the CP437 <c>B0</c>–<c>DF</c> graphics table.
/// </summary>
/// <param name="Code">CP437 byte value (<c>0xB0</c>–<c>0xDF</c>).</param>
/// <param name="Character">Unicode character TSharpVision stores in <c>TScreenChar</c>.</param>
/// <param name="Category">Which generator branch produces this glyph.</param>
/// <param name="Shape">Box shape; meaningless unless <see cref="Category"/> is BoxDrawing.</param>
/// <param name="Name">Short human-readable description, used by the diagnostic report.</param>
internal readonly record struct Cp437GraphicsCharacter(
    byte                  Code,
    char                  Character,
    Cp437GraphicsCategory Category,
    BoxGlyphShape         Shape,
    string                Name);

/// <summary>
/// The complete, explicit CP437 <c>0xB0</c>–<c>0xDF</c> table used by
/// <see cref="Cp437GlyphGenerator"/>.
/// <para>
/// TSharpVision stores screen characters as Unicode (see <c>TSharpVisionGlyphs</c>); the CP437
/// byte is recorded here only because that is how the range is specified. The character mapping
/// was verified against <c>System.Text.Encoding.GetEncoding(437)</c>.
/// </para>
/// <para>
/// The table is written out literally rather than derived from Unicode names, so that it can be
/// read and reviewed directly. All 48 codes are present exactly once.
/// </para>
/// </summary>
internal static class Cp437GraphicsCharacters
{
    /// <summary>First CP437 code covered by the generator.</summary>
    public const byte FirstCode = 0xB0;

    /// <summary>Last CP437 code covered by the generator.</summary>
    public const byte LastCode = 0xDF;

    /// <summary>Number of codes covered: 48.</summary>
    public const int Count = LastCode - FirstCode + 1;

    private const BoxSideStyle N = BoxSideStyle.None;
    private const BoxSideStyle S = BoxSideStyle.Single;
    private const BoxSideStyle D = BoxSideStyle.Double;

    private static readonly Cp437GraphicsCharacter[] _entries = BuildEntries();
    private static readonly Dictionary<char, Cp437GraphicsCharacter> _byChar =
        _entries.ToDictionary(static e => e.Character);

    /// <summary>All 48 entries, in ascending CP437 code order.</summary>
    public static IReadOnlyList<Cp437GraphicsCharacter> All => _entries;

    /// <summary>All 48 characters, in ascending CP437 code order.</summary>
    public static IReadOnlyList<char> AllCharacters { get; } =
        _entries.Select(static e => e.Character).ToArray();

    /// <summary>True when <paramref name="ch"/> is one of the 48 generated characters.</summary>
    public static bool Contains(char ch) => _byChar.ContainsKey(ch);

    /// <summary>Looks up the table entry for <paramref name="ch"/>.</summary>
    public static bool TryGet(char ch, out Cp437GraphicsCharacter entry) =>
        _byChar.TryGetValue(ch, out entry);

    /// <summary>Returns the entry for a CP437 code in <c>B0</c>–<c>DF</c>.</summary>
    public static Cp437GraphicsCharacter FromCode(byte code)
    {
        if (code < FirstCode || code > LastCode)
            throw new ArgumentOutOfRangeException(
                nameof(code), code, $"CP437 code must be in 0x{FirstCode:X2}..0x{LastCode:X2}.");

        return _entries[code - FirstCode];
    }

    /// <summary>
    /// Returns the per-side box shape for a box-drawing character in <c>B3</c>–<c>DA</c>.
    /// Returns <c>false</c> for shading, block elements and anything outside the range.
    /// </summary>
    public static bool TryGetBoxShape(char ch, out BoxGlyphShape shape)
    {
        if (_byChar.TryGetValue(ch, out Cp437GraphicsCharacter entry) &&
            entry.Category == Cp437GraphicsCategory.BoxDrawing)
        {
            shape = entry.Shape;
            return true;
        }

        shape = default;
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The table.  Columns:  CP437  Unicode  char  category / (Left, Right, Up, Down)
    // ─────────────────────────────────────────────────────────────────────────────

    private static Cp437GraphicsCharacter[] BuildEntries()
    {
        var entries = new List<Cp437GraphicsCharacter>(Count);

        // ── Shading (B0–B2) ──────────────────────────────────────────────────────
        Shade(0xB0, '░', "light shade");            // U+2591
        Shade(0xB1, '▒', "medium shade");           // U+2592
        Shade(0xB2, '▓', "dark shade");             // U+2593

        // ── Box drawing (B3–DA) ──────────────────────────────────────────────────
        //                       L  R  U  D
        Box(0xB3, '│', N, N, S, S, "light vertical");                       // U+2502
        Box(0xB4, '┤', S, N, S, S, "light vertical and left");              // U+2524
        Box(0xB5, '╡', D, N, S, S, "vertical single and left double");      // U+2561
        Box(0xB6, '╢', S, N, D, D, "vertical double and left single");      // U+2562
        Box(0xB7, '╖', S, N, N, D, "down double and left single");          // U+2556
        Box(0xB8, '╕', D, N, N, S, "down single and left double");          // U+2555
        Box(0xB9, '╣', D, N, D, D, "double vertical and left");             // U+2563
        Box(0xBA, '║', N, N, D, D, "double vertical");                      // U+2551
        Box(0xBB, '╗', D, N, N, D, "double down and left");                 // U+2557
        Box(0xBC, '╝', D, N, D, N, "double up and left");                   // U+255D
        Box(0xBD, '╜', S, N, D, N, "up double and left single");            // U+255C
        Box(0xBE, '╛', D, N, S, N, "up single and left double");            // U+255B
        Box(0xBF, '┐', S, N, N, S, "light down and left");                  // U+2510
        Box(0xC0, '└', N, S, S, N, "light up and right");                   // U+2514
        Box(0xC1, '┴', S, S, S, N, "light up and horizontal");              // U+2534
        Box(0xC2, '┬', S, S, N, S, "light down and horizontal");            // U+252C
        Box(0xC3, '├', N, S, S, S, "light vertical and right");             // U+251C
        Box(0xC4, '─', S, S, N, N, "light horizontal");                     // U+2500
        Box(0xC5, '┼', S, S, S, S, "light vertical and horizontal");        // U+253C
        Box(0xC6, '╞', N, D, S, S, "vertical single and right double");     // U+255E
        Box(0xC7, '╟', N, S, D, D, "vertical double and right single");     // U+255F
        Box(0xC8, '╚', N, D, D, N, "double up and right");                  // U+255A
        Box(0xC9, '╔', N, D, N, D, "double down and right");                // U+2554
        Box(0xCA, '╩', D, D, D, N, "double up and horizontal");             // U+2569
        Box(0xCB, '╦', D, D, N, D, "double down and horizontal");           // U+2566
        Box(0xCC, '╠', N, D, D, D, "double vertical and right");            // U+2560
        Box(0xCD, '═', D, D, N, N, "double horizontal");                    // U+2550
        Box(0xCE, '╬', D, D, D, D, "double vertical and horizontal");       // U+256C
        Box(0xCF, '╧', D, D, S, N, "up single and horizontal double");      // U+2567
        Box(0xD0, '╨', S, S, D, N, "up double and horizontal single");      // U+2568
        Box(0xD1, '╤', D, D, N, S, "down single and horizontal double");    // U+2564
        Box(0xD2, '╥', S, S, N, D, "down double and horizontal single");    // U+2565
        Box(0xD3, '╙', N, S, D, N, "up double and right single");           // U+2559
        Box(0xD4, '╘', N, D, S, N, "up single and right double");           // U+2558
        Box(0xD5, '╒', N, D, N, S, "down single and right double");         // U+2552
        Box(0xD6, '╓', N, S, N, D, "down double and right single");         // U+2553
        Box(0xD7, '╫', S, S, D, D, "vertical double and horizontal single");// U+256B
        Box(0xD8, '╪', D, D, S, S, "vertical single and horizontal double");// U+256A
        Box(0xD9, '┘', S, N, S, N, "light up and left");                    // U+2518
        Box(0xDA, '┌', N, S, N, S, "light down and right");                 // U+250C

        // ── Block elements (DB–DF) ───────────────────────────────────────────────
        Block(0xDB, '█', "full block");             // U+2588
        Block(0xDC, '▄', "lower half block");       // U+2584
        Block(0xDD, '▌', "left half block");        // U+258C
        Block(0xDE, '▐', "right half block");       // U+2590
        Block(0xDF, '▀', "upper half block");       // U+2580

        Cp437GraphicsCharacter[] result = entries.ToArray();

        if (result.Length != Count)
            throw new InvalidOperationException(
                $"CP437 graphics table must contain {Count} entries; found {result.Length}.");

        for (int i = 0; i < result.Length; i++)
        {
            if (result[i].Code != FirstCode + i)
                throw new InvalidOperationException(
                    $"CP437 graphics table is out of order at index {i} " +
                    $"(expected 0x{FirstCode + i:X2}, found 0x{result[i].Code:X2}).");
        }

        return result;

        void Shade(byte code, char ch, string name) =>
            entries.Add(new Cp437GraphicsCharacter(
                code, ch, Cp437GraphicsCategory.Shading, default, name));

        void Block(byte code, char ch, string name) =>
            entries.Add(new Cp437GraphicsCharacter(
                code, ch, Cp437GraphicsCategory.Block, default, name));

        void Box(byte code, char ch,
                 BoxSideStyle left, BoxSideStyle right, BoxSideStyle up, BoxSideStyle down,
                 string name)
        {
            var shape = new BoxGlyphShape(left, right, up, down);
            shape.Validate(ch);
            entries.Add(new Cp437GraphicsCharacter(
                code, ch, Cp437GraphicsCategory.BoxDrawing, shape, name));
        }
    }
}
