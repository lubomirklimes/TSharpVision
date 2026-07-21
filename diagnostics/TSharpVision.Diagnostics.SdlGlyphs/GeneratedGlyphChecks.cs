using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;
using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Diagnostics.SdlGlyphs;

/// <summary>Outcome of a single machine-readable generated-glyph check.</summary>
/// <param name="Name">Stable identifier, safe to use as a test name.</param>
/// <param name="Passed">Whether the check succeeded.</param>
/// <param name="Detail">Short human-readable explanation; failures list the offending glyphs.</param>
internal readonly record struct GeneratedGlyphCheckResult(string Name, bool Passed, string Detail)
{
    public override string ToString() => $"{(Passed ? "PASS" : "FAIL")}  {Name}  —  {Detail}";
}

/// <summary>
/// Machine-readable correctness checks over <see cref="Cp437GlyphGenerator"/> output.
/// <para>
/// Shared deliberately: the headless diagnostic tool writes these to <c>checks.txt</c> and the
/// unit tests assert on the same methods, so a diagnostic failure and a test failure always mean
/// the same thing. Nothing here touches SDL, a font or a window.
/// </para>
/// </summary>
internal static class GeneratedGlyphChecks
{
    /// <summary>Diagnostic scene: single-line box with an internal cross.</summary>
    internal static readonly string[] SingleBoxScene =
    [
        "┌─┬─┐",
        "│ │ │",
        "├─┼─┤",
        "│ │ │",
        "└─┴─┘",
    ];

    /// <summary>Diagnostic scene: double-line box with an internal cross.</summary>
    internal static readonly string[] DoubleBoxScene =
    [
        "╔═╦═╗",
        "║ ║ ║",
        "╠═╬═╣",
        "║ ║ ║",
        "╚═╩═╝",
    ];

    /// <summary>Diagnostic scene: single verticals crossing double horizontals.</summary>
    internal static readonly string[] MixedSingleVerticalScene =
    [
        "╒═╤═╕",
        "│ │ │",
        "╞═╪═╡",
        "│ │ │",
        "╘═╧═╛",
    ];

    /// <summary>Diagnostic scene: double verticals crossing single horizontals.</summary>
    internal static readonly string[] MixedDoubleVerticalScene =
    [
        "╓─╥─╖",
        "║ ║ ║",
        "╟─╫─╢",
        "║ ║ ║",
        "╙─╨─╜",
    ];

    /// <summary>The 18 CP437 characters that mix single and double line weights.</summary>
    internal static IReadOnlyList<char> MixedBoxCharacters { get; } =
        Cp437GraphicsCharacters.All
            .Where(static e => e.Category == Cp437GraphicsCategory.BoxDrawing &&
                               e.Shape.HorizontalStyle != BoxSideStyle.None &&
                               e.Shape.VerticalStyle   != BoxSideStyle.None &&
                               e.Shape.HorizontalStyle != e.Shape.VerticalStyle)
            .Select(static e => e.Character)
            .ToArray();

    // ─────────────────────────────────────────────────────────────────────────
    // Entry point
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Runs every check against <paramref name="generator"/>.</summary>
    internal static IReadOnlyList<GeneratedGlyphCheckResult> RunAll(Cp437GlyphGenerator generator)
    {
        var results = new List<GeneratedGlyphCheckResult>
        {
            CheckDimensions(generator),
            CheckFullBlockFilled(generator),
            CheckHorizontalHalfBlockPartition(generator),
            CheckVerticalHalfBlockPartition(generator),
            CheckConnectedEdges(generator),
            CheckDisconnectedEdges(generator),
            CheckSceneSeams(generator, "seams.single-box",  SingleBoxScene),
            CheckSceneSeams(generator, "seams.double-box",  DoubleBoxScene),
            CheckSceneSeams(generator, "seams.mixed-single-vertical", MixedSingleVerticalScene),
            CheckSceneSeams(generator, "seams.mixed-double-vertical", MixedDoubleVerticalScene),
            CheckMixedGlyphsGenerate(generator),
            CheckShadingCoverage(generator),
            CheckShadingTiling(generator),
            CheckDeterminism(generator),
        };

        return results;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Individual checks
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Every generated glyph is exactly cellWidth × cellHeight pixels.</summary>
    internal static GeneratedGlyphCheckResult CheckDimensions(Cp437GlyphGenerator g)
    {
        var bad = new List<string>();

        foreach (Cp437GraphicsCharacter e in Cp437GraphicsCharacters.All)
        {
            foreach (GlyphPhase phase in GlyphPhase.AllCombinations())
            {
                if (!g.TryGenerate(e.Character, phase, out AlphaBitmap b))
                {
                    bad.Add($"{e.Code:X2}:not-generated");
                    break;
                }

                if (b.Width != g.CellWidth || b.Height != g.CellHeight)
                {
                    bad.Add($"{e.Code:X2}:{b.Width}x{b.Height}");
                    break;
                }
            }
        }

        return Result("dimensions.all-48",
            bad.Count == 0,
            bad.Count == 0
                ? $"all 48 glyphs are {g.CellWidth}x{g.CellHeight} at all {GlyphPhase.Combinations} phases"
                : "wrong size: " + string.Join(", ", bad));
    }

    /// <summary>█ (DB) fills every pixel of the cell.</summary>
    internal static GeneratedGlyphCheckResult CheckFullBlockFilled(Cp437GlyphGenerator g)
    {
        AlphaBitmap full = g.Generate('█');
        int empty = CountWhere(full, static a => a != 255);

        return Result("blocks.full-block-filled",
            empty == 0,
            empty == 0
                ? $"all {full.Width * full.Height} pixels opaque"
                : $"{empty} pixel(s) not fully opaque");
    }

    /// <summary>▀ (DF) and ▄ (DC) partition the cell exactly: union == █, intersection == ∅.</summary>
    internal static GeneratedGlyphCheckResult CheckHorizontalHalfBlockPartition(Cp437GlyphGenerator g)
        => CheckPartition(g, "blocks.upper-lower-partition", '▀', '▄');

    /// <summary>▌ (DD) and ▐ (DE) partition the cell exactly: union == █, intersection == ∅.</summary>
    internal static GeneratedGlyphCheckResult CheckVerticalHalfBlockPartition(Cp437GlyphGenerator g)
        => CheckPartition(g, "blocks.left-right-partition", '▌', '▐');

    private static GeneratedGlyphCheckResult CheckPartition(
        Cp437GlyphGenerator g, string name, char firstHalf, char secondHalf)
    {
        AlphaBitmap a    = g.Generate(firstHalf);
        AlphaBitmap b    = g.Generate(secondHalf);
        AlphaBitmap full = g.Generate('█');

        int overlap = 0, gap = 0;

        for (int i = 0; i < full.Alpha.Length; i++)
        {
            bool inA = a.Alpha[i] != 0;
            bool inB = b.Alpha[i] != 0;

            if (inA && inB)  overlap++;
            if (!inA && !inB) gap++;
        }

        bool ok = overlap == 0 && gap == 0;

        return Result(name, ok,
            ok
                ? $"'{firstHalf}' ∪ '{secondHalf}' == '█', intersection empty"
                : $"overlap={overlap}px gap={gap}px");
    }

    /// <summary>Every connected side of a box glyph paints the full band at the cell edge.</summary>
    internal static GeneratedGlyphCheckResult CheckConnectedEdges(Cp437GlyphGenerator g)
    {
        var bad = new List<string>();

        foreach (Cp437GraphicsCharacter e in BoxEntries())
        {
            AlphaBitmap b = g.Generate(e.Character);

            foreach ((string side, bool present, bool horizontal, int edge) in Sides(e.Shape, g))
            {
                if (!present) continue;

                bool complete = true;

                foreach ((int start, int thickness) in Bands(e.Shape, horizontal, g))
                {
                    for (int k = 0; k < thickness; k++)
                    {
                        int pos = start + k;
                        // A "horizontal" side (left/right) is bonded through ROW bands;
                        // a "vertical" side (up/down) through COLUMN bands.
                        byte alpha = horizontal ? b[edge, pos] : b[pos, edge];
                        if (alpha == 0) complete = false;
                    }
                }

                if (!complete)
                    bad.Add($"{e.Code:X2}'{e.Character}'.{side}");
            }
        }

        return Result("box.connected-sides-reach-edge",
            bad.Count == 0,
            bad.Count == 0
                ? $"all {BoxEntries().Count()} box glyphs paint every connected side to its cell edge"
                : "incomplete edge: " + string.Join(", ", bad));
    }

    /// <summary>
    /// A side that does not connect leaves its cell edge completely blank.
    /// Skipped for cells so small that the stroke bands themselves touch an edge.
    /// </summary>
    internal static GeneratedGlyphCheckResult CheckDisconnectedEdges(Cp437GlyphGenerator g)
    {
        if (!EdgeTestsMeaningful(g.Geometry))
        {
            return Result("box.disconnected-sides-are-blank", true,
                $"skipped: {g.CellWidth}x{g.CellHeight} cell is too small for the bands to " +
                "stay clear of the edges");
        }

        var bad = new List<string>();

        foreach (Cp437GraphicsCharacter e in BoxEntries())
        {
            AlphaBitmap b = g.Generate(e.Character);

            foreach ((string side, bool present, bool horizontal, int edge) in Sides(e.Shape, g))
            {
                if (present) continue;

                int extent = horizontal ? g.CellHeight : g.CellWidth;
                for (int pos = 0; pos < extent; pos++)
                {
                    byte alpha = horizontal ? b[edge, pos] : b[pos, edge];
                    if (alpha != 0)
                    {
                        bad.Add($"{e.Code:X2}'{e.Character}'.{side}@{pos}");
                        break;
                    }
                }
            }
        }

        return Result("box.disconnected-sides-are-blank",
            bad.Count == 0,
            bad.Count == 0
                ? "no ink leaks onto an unconnected cell edge"
                : "ink on unconnected edge: " + string.Join(", ", bad));
    }

    /// <summary>
    /// Every compatible join in <paramref name="scene"/> is continuous across the cell boundary:
    /// both sides of the seam carry ink at every row/column of the shared band.
    /// </summary>
    internal static GeneratedGlyphCheckResult CheckSceneSeams(
        Cp437GlyphGenerator g, string name, string[] scene)
    {
        var composer = new CellSceneComposer(g);
        AlphaBitmap tiled = composer.ComposeScene(scene, 0, 0, out IReadOnlyCollection<char> missing);

        var broken = new List<string>();

        if (missing.Count > 0)
            broken.Add("unsupported:" + string.Concat(missing));

        int rows = scene.Length;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < scene[r].Length; c++)
            {
                if (!Cp437GraphicsCharacters.TryGetBoxShape(scene[r][c], out BoxGlyphShape here))
                    continue;

                // Horizontal neighbour.
                if (c + 1 < scene[r].Length &&
                    Cp437GraphicsCharacters.TryGetBoxShape(scene[r][c + 1], out BoxGlyphShape east) &&
                    here.Right != BoxSideStyle.None && here.Right == east.Left)
                {
                    int boundaryX = (c + 1) * g.CellWidth;
                    foreach ((int start, int thickness) in Bands(here, horizontal: true, g))
                    {
                        for (int k = 0; k < thickness; k++)
                        {
                            int y = r * g.CellHeight + start + k;
                            if (tiled[boundaryX - 1, y] == 0 || tiled[boundaryX, y] == 0)
                                broken.Add($"h r{r}c{c} '{scene[r][c]}'→'{scene[r][c + 1]}' y={y}");
                        }
                    }
                }

                // Vertical neighbour.
                if (r + 1 < rows && c < scene[r + 1].Length &&
                    Cp437GraphicsCharacters.TryGetBoxShape(scene[r + 1][c], out BoxGlyphShape south) &&
                    here.Down != BoxSideStyle.None && here.Down == south.Up)
                {
                    int boundaryY = (r + 1) * g.CellHeight;
                    foreach ((int start, int thickness) in Bands(here, horizontal: false, g))
                    {
                        for (int k = 0; k < thickness; k++)
                        {
                            int x = c * g.CellWidth + start + k;
                            if (tiled[x, boundaryY - 1] == 0 || tiled[x, boundaryY] == 0)
                                broken.Add($"v r{r}c{c} '{scene[r][c]}'→'{scene[r + 1][c]}' x={x}");
                        }
                    }
                }
            }
        }

        return Result(name,
            broken.Count == 0,
            broken.Count == 0
                ? "all compatible joins continuous"
                : $"{broken.Count} broken: " + string.Join("; ", broken.Take(8)));
    }

    /// <summary>All 18 mixed single/double characters generate a non-empty glyph.</summary>
    internal static GeneratedGlyphCheckResult CheckMixedGlyphsGenerate(Cp437GlyphGenerator g)
    {
        var bad = new List<string>();

        foreach (char ch in MixedBoxCharacters)
        {
            if (!g.TryGenerate(ch, out AlphaBitmap b))
            {
                bad.Add($"'{ch}':missing");
                continue;
            }

            if (CountWhere(b, static a => a != 0) == 0)
                bad.Add($"'{ch}':empty");
        }

        bool countOk = MixedBoxCharacters.Count == 18;

        return Result("box.mixed-characters",
            bad.Count == 0 && countOk,
            bad.Count == 0 && countOk
                ? $"all {MixedBoxCharacters.Count} mixed glyphs generated with ink"
                : $"count={MixedBoxCharacters.Count} problems: " + string.Join(", ", bad));
    }

    /// <summary>
    /// Shading coverage: in Uniform mode every pixel carries exactly the configured alpha; in
    /// PhasedDither mode the ink fraction matches the lattice density within one cell's rounding.
    /// </summary>
    internal static GeneratedGlyphCheckResult CheckShadingCoverage(Cp437GlyphGenerator g)
    {
        var bad = new List<string>();

        foreach (byte code in (byte[])[0xB0, 0xB1, 0xB2])
        {
            Cp437GraphicsCharacter e = Cp437GraphicsCharacters.FromCode(code);

            foreach (GlyphPhase phase in GlyphPhase.AllCombinations())
            {
                AlphaBitmap b = g.Generate(e.Character, phase);

                if (g.Options.ShadingMode == ShadingMode.Uniform)
                {
                    byte expected = Cp437ShadingLattice.UniformAlpha(code);
                    int wrong = CountWhere(b, a => a != expected);
                    if (wrong != 0)
                        bad.Add($"{code:X2}@{phase}:{wrong}px≠{expected}");
                }
                else
                {
                    // Verify against the lattice directly rather than against a target ratio,
                    // which is exact for every cell size.
                    int mismatched = 0;
                    for (int y = 0; y < b.Height; y++)
                        for (int x = 0; x < b.Width; x++)
                        {
                            bool expectInk = Cp437ShadingLattice.HasInk(code, phase.X + x, phase.Y + y);
                            bool hasInk    = b[x, y] != 0;
                            if (expectInk != hasInk) mismatched++;
                        }

                    if (mismatched != 0)
                        bad.Add($"{code:X2}@{phase}:{mismatched}px");
                }
            }
        }

        return Result("shading.coverage",
            bad.Count == 0,
            bad.Count == 0
                ? $"{g.Options.ShadingMode} shading matches its definition at all {GlyphPhase.Combinations} phases"
                : "mismatch: " + string.Join(", ", bad));
    }

    /// <summary>
    /// A tiled field of each shading character reproduces one continuous global pattern:
    /// in PhasedDither the whole field equals the infinite lattice; in Uniform every pixel of the
    /// field carries the same alpha. Either way there is no seam at a cell boundary.
    /// </summary>
    internal static GeneratedGlyphCheckResult CheckShadingTiling(Cp437GlyphGenerator g)
    {
        const int Cols = 8;
        const int Rows = 4;

        var composer = new CellSceneComposer(g);
        var bad      = new List<string>();

        foreach (byte code in (byte[])[0xB0, 0xB1, 0xB2])
        {
            char ch = Cp437GraphicsCharacters.FromCode(code).Character;
            AlphaBitmap field = composer.ComposeFill(ch, Cols, Rows);

            int mismatched = 0;

            for (int y = 0; y < field.Height; y++)
                for (int x = 0; x < field.Width; x++)
                {
                    bool hasInk = field[x, y] != 0;
                    bool expect = g.Options.ShadingMode == ShadingMode.Uniform
                        ? true
                        : Cp437ShadingLattice.HasInk(code, x, y);
                    if (hasInk != expect) mismatched++;
                }

            if (mismatched != 0)
                bad.Add($"{code:X2}:{mismatched}px");
        }

        return Result("shading.tiling-continuity",
            bad.Count == 0,
            bad.Count == 0
                ? $"{Cols}x{Rows} fields of ░ ▒ ▓ reproduce one continuous pattern"
                : "discontinuous: " + string.Join(", ", bad));
    }

    /// <summary>Regenerating every glyph yields byte-identical output.</summary>
    internal static GeneratedGlyphCheckResult CheckDeterminism(Cp437GlyphGenerator g)
    {
        var bad = new List<string>();

        foreach (Cp437GraphicsCharacter e in Cp437GraphicsCharacters.All)
        {
            foreach (GlyphPhase phase in GlyphPhase.AllCombinations())
            {
                AlphaBitmap first  = g.Generate(e.Character, phase);
                AlphaBitmap second = g.Generate(e.Character, phase);

                if (!first.Alpha.AsSpan().SequenceEqual(second.Alpha))
                {
                    bad.Add($"{e.Code:X2}@{phase}");
                    break;
                }
            }
        }

        return Result("determinism.regeneration",
            bad.Count == 0,
            bad.Count == 0
                ? "regeneration is byte-identical for all 48 glyphs at all phases"
                : "non-deterministic: " + string.Join(", ", bad));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The row bands (for left/right sides) or column bands (for up/down sides) that a shape's
    /// strokes occupy, as (start, thickness) pairs.
    /// </summary>
    internal static IEnumerable<(int Start, int Thickness)> Bands(
        BoxGlyphShape shape, bool horizontal, Cp437GlyphGenerator g)
    {
        CellGlyphGeometry geo = g.Geometry;
        BoxSideStyle style = horizontal ? shape.HorizontalStyle : shape.VerticalStyle;

        switch (style)
        {
            case BoxSideStyle.Single:
                yield return (horizontal ? geo.SingleBandY : geo.SingleBandX, geo.SingleStrokeThickness);
                break;

            case BoxSideStyle.Double:
                if (horizontal)
                {
                    yield return (geo.DoubleBandY1, geo.DoubleStrokeThickness);
                    if (geo.DoubleHorizontalSupported)
                        yield return (geo.DoubleBandY2, geo.DoubleStrokeThickness);
                }
                else
                {
                    yield return (geo.DoubleBandX1, geo.DoubleStrokeThickness);
                    if (geo.DoubleVerticalSupported)
                        yield return (geo.DoubleBandX2, geo.DoubleStrokeThickness);
                }
                break;
        }
    }

    /// <summary>
    /// The four sides of a shape as (name, present, isHorizontalSide, edgeCoordinate).
    /// <c>isHorizontalSide</c> is true for left/right (bonded through row bands).
    /// </summary>
    private static IEnumerable<(string Side, bool Present, bool Horizontal, int Edge)> Sides(
        BoxGlyphShape shape, Cp437GlyphGenerator g)
    {
        yield return ("left",  shape.HasLeft,  true,  0);
        yield return ("right", shape.HasRight, true,  g.CellWidth  - 1);
        yield return ("up",    shape.HasUp,    false, 0);
        yield return ("down",  shape.HasDown,  false, g.CellHeight - 1);
    }

    private static IEnumerable<Cp437GraphicsCharacter> BoxEntries() =>
        Cp437GraphicsCharacters.All.Where(static e => e.Category == Cp437GraphicsCategory.BoxDrawing);

    /// <summary>
    /// True when the cell is large enough that no stroke band touches a cell edge, which is
    /// required before "an unconnected side must be blank" can be meaningful.
    /// </summary>
    internal static bool EdgeTestsMeaningful(CellGlyphGeometry geo)
    {
        int ts = geo.SingleStrokeThickness;
        int td = geo.DoubleStrokeThickness;

        bool horizontalOk =
            geo.SingleBandX > 0 && geo.SingleBandX + ts - 1 < geo.CellWidth - 1 &&
            geo.DoubleBandX1 > 0 && geo.DoubleBandX2 + td - 1 < geo.CellWidth - 1;

        bool verticalOk =
            geo.SingleBandY > 0 && geo.SingleBandY + ts - 1 < geo.CellHeight - 1 &&
            geo.DoubleBandY1 > 0 && geo.DoubleBandY2 + td - 1 < geo.CellHeight - 1;

        return horizontalOk && verticalOk;
    }

    private static int CountWhere(AlphaBitmap bitmap, Func<byte, bool> predicate)
    {
        int n = 0;
        foreach (byte a in bitmap.Alpha)
            if (predicate(a)) n++;
        return n;
    }

    private static GeneratedGlyphCheckResult Result(string name, bool passed, string detail) =>
        new(name, passed, detail);
}
