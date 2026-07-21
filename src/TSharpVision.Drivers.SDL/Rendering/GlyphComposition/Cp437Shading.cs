namespace TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

/// <summary>
/// How <see cref="Cp437GlyphGenerator"/> renders the three shading characters
/// <c>B0 ░</c>, <c>B1 ▒</c>, <c>B2 ▓</c>.
/// </summary>
internal enum ShadingMode
{
    /// <summary>
    /// Flat partial coverage: every pixel of the cell carries the same alpha
    /// (25 % / 50 % / 75 %). Seamless for any cell size and any adjacency because there is no
    /// pattern to align, and needs no phase information. Loses the DOS dither texture.
    /// </summary>
    Uniform,

    /// <summary>
    /// Classic DOS-style ordered dither on a 2×2 pixel lattice, sampled in <b>absolute screen
    /// pixel coordinates</b> so the pattern continues unbroken across cell boundaries.
    /// Requires the caller to supply the cell's <see cref="GlyphPhase"/>.
    /// </summary>
    PhasedDither,
}

/// <summary>
/// Position of a cell's top-left pixel within the shading lattice, modulo
/// <see cref="Cp437ShadingLattice.Period"/>.
/// <para>
/// A cell at grid position <c>(column, row)</c> starts at absolute pixel
/// <c>(column * cellWidth, row * cellHeight)</c>. Only that coordinate <i>modulo the lattice
/// period</i> affects the dither, so there are at most <c>Period²</c> = 4 distinct bitmaps per
/// shading character — which is why phase can be part of a cache key without exploding it.
/// </para>
/// </summary>
internal readonly record struct GlyphPhase(int X, int Y)
{
    /// <summary>Phase (0, 0) — also the phase used for every non-shading glyph.</summary>
    public static readonly GlyphPhase Zero = new(0, 0);

    /// <summary>Number of distinct phases per axis.</summary>
    public const int Period = Cp437ShadingLattice.Period;

    /// <summary>Total number of distinct phase combinations (4).</summary>
    public const int Combinations = Period * Period;

    /// <summary>Normalises both components into <c>[0, Period)</c>.</summary>
    public GlyphPhase Normalized() => new(Mod(X), Mod(Y));

    /// <summary>
    /// The phase of the cell at grid position (<paramref name="column"/>,
    /// <paramref name="row"/>) for a <paramref name="cellWidth"/> ×
    /// <paramref name="cellHeight"/> cell.
    /// </summary>
    public static GlyphPhase ForCell(int column, int row, int cellWidth, int cellHeight) =>
        new(Mod(column * cellWidth), Mod(row * cellHeight));

    /// <summary>All four phase combinations, in a stable order.</summary>
    public static IEnumerable<GlyphPhase> AllCombinations()
    {
        for (int y = 0; y < Period; y++)
            for (int x = 0; x < Period; x++)
                yield return new GlyphPhase(x, y);
    }

    private static int Mod(int value)
    {
        int m = value % Period;
        return m < 0 ? m + Period : m;
    }

    public override string ToString() => $"({X},{Y})";
}

/// <summary>
/// The DOS-style dither lattice used by <see cref="ShadingMode.PhasedDither"/>.
/// <para>
/// The lattice is defined on absolute screen pixels <c>(X, Y)</c>, not on cell-local pixels.
/// A cell renders pixel <c>(x, y)</c> by evaluating the lattice at
/// <c>(phase.X + x, phase.Y + y)</c>. Because the lattice period is 2 on both axes, a cell of
/// any width — odd or even — still lands on one of four phases, and two horizontally or
/// vertically adjacent cells always continue the same infinite pattern.
/// </para>
/// <para>
/// Patterns (X, Y are absolute pixel coordinates):
/// </para>
/// <list type="bullet">
/// <item><description><c>B0 ░</c> light — ink where <c>X</c> and <c>Y</c> are both even → 1 of 4 pixels (25 %).</description></item>
/// <item><description><c>B1 ▒</c> medium — ink where <c>X + Y</c> is even → checkerboard (50 %).</description></item>
/// <item><description><c>B2 ▓</c> dark — ink everywhere except where <c>X</c> and <c>Y</c> are both odd → 3 of 4 pixels (75 %), the complement of <c>░</c> shifted by one.</description></item>
/// </list>
/// </summary>
internal static class Cp437ShadingLattice
{
    /// <summary>Lattice period on both axes.</summary>
    public const int Period = 2;

    /// <summary>Alpha written for a lattice pixel that carries ink.</summary>
    public const byte InkAlpha = 255;

    /// <summary>Uniform-mode alpha for <c>B0 ░</c> (25 % of 255, rounded).</summary>
    public const byte UniformLightAlpha = 64;

    /// <summary>Uniform-mode alpha for <c>B1 ▒</c> (50 % of 255, rounded).</summary>
    public const byte UniformMediumAlpha = 128;

    /// <summary>Uniform-mode alpha for <c>B2 ▓</c> (75 % of 255, rounded).</summary>
    public const byte UniformDarkAlpha = 191;

    /// <summary>
    /// Evaluates the lattice for CP437 code <paramref name="code"/> (must be
    /// <c>0xB0</c>, <c>0xB1</c> or <c>0xB2</c>) at absolute pixel (<paramref name="absoluteX"/>,
    /// <paramref name="absoluteY"/>). Returns <c>true</c> when the pixel carries ink.
    /// </summary>
    public static bool HasInk(byte code, int absoluteX, int absoluteY)
    {
        int xOdd = Mod2(absoluteX);
        int yOdd = Mod2(absoluteY);

        return code switch
        {
            0xB0 => xOdd == 0 && yOdd == 0,          // 25 %
            0xB1 => ((xOdd + yOdd) & 1) == 0,        // 50 %
            0xB2 => !(xOdd == 1 && yOdd == 1),       // 75 %
            _    => throw new ArgumentOutOfRangeException(
                        nameof(code), code, "Shading lattice is defined for 0xB0..0xB2 only."),
        };
    }

    /// <summary>Uniform-mode alpha for CP437 code <c>0xB0</c>, <c>0xB1</c> or <c>0xB2</c>.</summary>
    public static byte UniformAlpha(byte code) => code switch
    {
        0xB0 => UniformLightAlpha,
        0xB1 => UniformMediumAlpha,
        0xB2 => UniformDarkAlpha,
        _    => throw new ArgumentOutOfRangeException(
                    nameof(code), code, "Uniform shading is defined for 0xB0..0xB2 only."),
    };

    // Parity. Two's complement makes "& 1" correct for negative values as well.
    private static int Mod2(int v) => v & 1;
}
