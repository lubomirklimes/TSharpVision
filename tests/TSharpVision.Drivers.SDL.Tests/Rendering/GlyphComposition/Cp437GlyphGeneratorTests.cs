using TSharpVision.Diagnostics.SdlGlyphs;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;
using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Drivers.SDL.Tests.Rendering.GlyphComposition;

public class Cp437GlyphGeneratorTests
{
    /// <summary>Cell sizes covering even/odd width and height, plus real measured fonts.</summary>
    public static TheoryData<int, int> Cells => new()
    {
        { 11, 20 },  // Consolas 20 pt          (odd  w, even h)
        { 12, 23 },  // Courier New 20 pt       (even w, odd  h)
        { 12, 20 },  // Lucida Console 20 pt    (even w, even h)
        {  9, 15 },  // odd on both axes
        {  8, 16 },  // classic VGA
        { 16, 32 },  // large: 3-pixel strokes
    };

    private static Cp437GlyphGenerator Make(int w, int h, ShadingMode mode = ShadingMode.PhasedDither) =>
        new(w, h, mode == ShadingMode.Uniform
            ? Cp437GlyphGeneratorOptions.UniformShading
            : Cp437GlyphGeneratorOptions.Default);

    // ── Dimensions ────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Cells))]
    public void EveryGeneratedGlyph_HasExactlyCellDimensions(int w, int h)
    {
        Cp437GlyphGenerator g = Make(w, h);

        foreach (char ch in Cp437GraphicsCharacters.AllCharacters)
        {
            foreach (GlyphPhase phase in GlyphPhase.AllCombinations())
            {
                Assert.True(g.TryGenerate(ch, phase, out AlphaBitmap b), $"'{ch}' not generated");
                Assert.Equal(w, b.Width);
                Assert.Equal(h, b.Height);
            }
        }
    }

    [Fact]
    public void IsGenerated_CoversTheWholeRangeAndNothingElse()
    {
        Assert.Equal(48, Cp437GraphicsCharacters.AllCharacters.Count(Cp437GlyphGenerator.IsGenerated));

        Assert.False(Cp437GlyphGenerator.IsGenerated('A'));
        Assert.False(Cp437GlyphGenerator.IsGenerated(' '));
        Assert.False(Cp437GlyphGenerator.IsGenerated('■'));  // CP437 FE, outside B0-DF
        Assert.False(Cp437GlyphGenerator.IsGenerated('▲'));  // CP437 1E, outside B0-DF
    }

    [Fact]
    public void TryGenerate_ReturnsFalseForUngeneratedCharacters()
    {
        Cp437GlyphGenerator g = Make(11, 20);

        Assert.False(g.TryGenerate('A', out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => g.Generate('A'));
    }

    // ── Blocks ────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Cells))]
    public void FullBlock_FillsEveryPixel(int w, int h)
    {
        AlphaBitmap full = Make(w, h).Generate('█');

        Assert.All(full.Alpha, a => Assert.Equal(255, a));
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void UpperAndLowerHalfBlocks_PartitionTheCellExactly(int w, int h)
    {
        AssertPartition(Make(w, h), '▀', '▄');
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void LeftAndRightHalfBlocks_PartitionTheCellExactly(int w, int h)
    {
        AssertPartition(Make(w, h), '▌', '▐');
    }

    private static void AssertPartition(Cp437GlyphGenerator g, char first, char second)
    {
        AlphaBitmap a    = g.Generate(first);
        AlphaBitmap b    = g.Generate(second);
        AlphaBitmap full = g.Generate('█');

        for (int i = 0; i < full.Alpha.Length; i++)
        {
            bool inA = a.Alpha[i] != 0;
            bool inB = b.Alpha[i] != 0;

            Assert.False(inA && inB, $"'{first}' and '{second}' overlap at index {i}");
            Assert.True(inA || inB,  $"'{first}' and '{second}' leave a gap at index {i}");
        }
    }

    [Fact]
    public void HalfBlockSplit_PutsTheExtraPixelOnTheUpperAndLeftHalf()
    {
        // 11 x 15: odd on both axes.
        Cp437GlyphGenerator g = Make(11, 15);

        AlphaBitmap upper = g.Generate('▀');
        AlphaBitmap left  = g.Generate('▌');

        int upperRows = Enumerable.Range(0, 15).Count(y => upper[0, y] != 0);
        int leftCols  = Enumerable.Range(0, 11).Count(x => left[x, 0] != 0);

        Assert.Equal(8, upperRows);   // (15 + 1) / 2
        Assert.Equal(6, leftCols);    // (11 + 1) / 2
    }

    // ── Box drawing ───────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Cells))]
    public void ConnectedSides_ReachTheirCellEdge(int w, int h)
    {
        Cp437GlyphGenerator g = Make(w, h);

        GeneratedGlyphCheckResult r = GeneratedGlyphChecks.CheckConnectedEdges(g);

        Assert.True(r.Passed, r.Detail);
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void DisconnectedSides_LeaveTheirCellEdgeBlank(int w, int h)
    {
        Cp437GlyphGenerator g = Make(w, h);

        GeneratedGlyphCheckResult r = GeneratedGlyphChecks.CheckDisconnectedEdges(g);

        Assert.True(r.Passed, r.Detail);
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void SingleLineScene_HasNoBrokenSeams(int w, int h)
    {
        AssertScene(w, h, "single", GeneratedGlyphChecks.SingleBoxScene);
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void DoubleLineScene_HasNoBrokenSeams(int w, int h)
    {
        AssertScene(w, h, "double", GeneratedGlyphChecks.DoubleBoxScene);
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void MixedSingleVerticalScene_HasNoBrokenSeams(int w, int h)
    {
        AssertScene(w, h, "mixed-single-vertical", GeneratedGlyphChecks.MixedSingleVerticalScene);
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void MixedDoubleVerticalScene_HasNoBrokenSeams(int w, int h)
    {
        AssertScene(w, h, "mixed-double-vertical", GeneratedGlyphChecks.MixedDoubleVerticalScene);
    }

    private static void AssertScene(int w, int h, string name, string[] scene)
    {
        GeneratedGlyphCheckResult r =
            GeneratedGlyphChecks.CheckSceneSeams(Make(w, h), name, scene);

        Assert.True(r.Passed, r.Detail);
    }

    [Fact]
    public void MixedCharacters_AreEighteenAndAllProduceInk()
    {
        Assert.Equal(18, GeneratedGlyphChecks.MixedBoxCharacters.Count);

        Cp437GlyphGenerator g = Make(11, 20);

        foreach (char ch in GeneratedGlyphChecks.MixedBoxCharacters)
        {
            AlphaBitmap b = g.Generate(ch);
            Assert.Contains(b.Alpha, static a => a != 0);
        }
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void EveryMixedCharacter_ConnectsOnAllOfItsDeclaredSides(int w, int h)
    {
        Cp437GlyphGenerator g = Make(w, h);

        foreach (char ch in GeneratedGlyphChecks.MixedBoxCharacters)
        {
            Assert.True(Cp437GraphicsCharacters.TryGetBoxShape(ch, out BoxGlyphShape shape));
            AlphaBitmap b = g.Generate(ch);

            AssertSideReachesEdge(b, shape, ch, g);
        }
    }

    private static void AssertSideReachesEdge(
        AlphaBitmap b, BoxGlyphShape shape, char ch, Cp437GlyphGenerator g)
    {
        foreach ((int start, int thickness) in GeneratedGlyphChecks.Bands(shape, horizontal: true, g))
        {
            for (int k = 0; k < thickness; k++)
            {
                if (shape.HasLeft)
                    Assert.True(b[0, start + k] != 0, $"'{ch}' left edge blank at row {start + k}");
                if (shape.HasRight)
                    Assert.True(b[g.CellWidth - 1, start + k] != 0, $"'{ch}' right edge blank at row {start + k}");
            }
        }

        foreach ((int start, int thickness) in GeneratedGlyphChecks.Bands(shape, horizontal: false, g))
        {
            for (int k = 0; k < thickness; k++)
            {
                if (shape.HasUp)
                    Assert.True(b[start + k, 0] != 0, $"'{ch}' top edge blank at column {start + k}");
                if (shape.HasDown)
                    Assert.True(b[start + k, g.CellHeight - 1] != 0, $"'{ch}' bottom edge blank at column {start + k}");
            }
        }
    }

    /// <summary>
    /// The classic CP437 look: a double crossing opens its channels, so ╬ is four corner pieces
    /// with an unpainted centre rather than a solid waffle.
    /// </summary>
    [Fact]
    public void DoubleCross_OpensItsChannels()
    {
        Cp437GlyphGenerator g = Make(11, 20);
        CellGlyphGeometry   c = g.Geometry;
        AlphaBitmap         b = g.Generate('╬');

        int td = c.DoubleStrokeThickness;

        // The upper rail must be interrupted between the two vertical rails.
        for (int x = c.DoubleBandX1 + td; x <= c.DoubleBandX2 - 1; x++)
            Assert.Equal(0, b[x, c.DoubleBandY1]);

        // ...but must still reach both cell edges.
        Assert.NotEqual(0, b[0, c.DoubleBandY1]);
        Assert.NotEqual(0, b[c.CellWidth - 1, c.DoubleBandY1]);
    }

    /// <summary>A single line crossing a double line is not interrupted, and does not break it.</summary>
    [Fact]
    public void MixedCross_KeepsBothLinesContinuous()
    {
        Cp437GlyphGenerator g = Make(11, 20);
        CellGlyphGeometry   c = g.Geometry;
        AlphaBitmap         b = g.Generate('╪');   // vertical single, horizontal double

        for (int x = 0; x < c.CellWidth; x++)
        {
            Assert.NotEqual(0, b[x, c.DoubleBandY1]);
            Assert.NotEqual(0, b[x, c.DoubleBandY2]);
        }

        for (int y = 0; y < c.CellHeight; y++)
            Assert.NotEqual(0, b[c.SingleBandX, y]);
    }

    // ── Shading ───────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Cells))]
    public void UniformShading_UsesTheConfiguredAlphaInEveryPixel(int w, int h)
    {
        Cp437GlyphGenerator g = Make(w, h, ShadingMode.Uniform);

        Assert.All(g.Generate('░').Alpha, a => Assert.Equal(Cp437ShadingLattice.UniformLightAlpha,  a));
        Assert.All(g.Generate('▒').Alpha, a => Assert.Equal(Cp437ShadingLattice.UniformMediumAlpha, a));
        Assert.All(g.Generate('▓').Alpha, a => Assert.Equal(Cp437ShadingLattice.UniformDarkAlpha,   a));
    }

    [Fact]
    public void UniformShading_DoesNotDependOnPhase()
    {
        Cp437GlyphGenerator g = Make(11, 20, ShadingMode.Uniform);

        AlphaBitmap zero = g.Generate('▒', new GlyphPhase(0, 0));
        AlphaBitmap one  = g.Generate('▒', new GlyphPhase(1, 1));

        Assert.Equal(zero.Alpha, one.Alpha);
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void PhasedDither_MatchesTheGlobalLatticeAtEveryPhase(int w, int h)
    {
        Cp437GlyphGenerator g = Make(w, h);

        foreach (byte code in (byte[])[0xB0, 0xB1, 0xB2])
        {
            char ch = Cp437GraphicsCharacters.FromCode(code).Character;

            foreach (GlyphPhase phase in GlyphPhase.AllCombinations())
            {
                AlphaBitmap b = g.Generate(ch, phase);

                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        bool expected = Cp437ShadingLattice.HasInk(code, phase.X + x, phase.Y + y);
                        Assert.Equal(expected, b[x, y] != 0);
                    }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void PhasedDither_TilesSeamlesslyInBothDirections(int w, int h)
    {
        var generator = Make(w, h);
        var composer  = new CellSceneComposer(generator);

        foreach (byte code in (byte[])[0xB0, 0xB1, 0xB2])
        {
            char ch = Cp437GraphicsCharacters.FromCode(code).Character;

            // 4x3 cells is enough to cross several boundaries on both axes.
            AlphaBitmap field = composer.ComposeFill(ch, 4, 3);

            for (int y = 0; y < field.Height; y++)
                for (int x = 0; x < field.Width; x++)
                {
                    bool expected = Cp437ShadingLattice.HasInk(code, x, y);
                    Assert.True(expected == (field[x, y] != 0),
                        $"'{ch}' breaks the lattice at ({x},{y}) for a {w}x{h} cell");
                }
        }
    }

    [Fact]
    public void GlyphPhase_ForCell_UsesTheCellOriginModuloTheLatticePeriod()
    {
        // Odd cell width: successive columns alternate phase.
        Assert.Equal(new GlyphPhase(0, 0), GlyphPhase.ForCell(0, 0, 11, 20));
        Assert.Equal(new GlyphPhase(1, 0), GlyphPhase.ForCell(1, 0, 11, 20));
        Assert.Equal(new GlyphPhase(0, 0), GlyphPhase.ForCell(2, 0, 11, 20));

        // Even cell width: every column has the same phase.
        Assert.Equal(new GlyphPhase(0, 0), GlyphPhase.ForCell(1, 0, 12, 20));

        // Odd cell height alternates rows.
        Assert.Equal(new GlyphPhase(0, 1), GlyphPhase.ForCell(0, 1, 12, 23));

        Assert.Equal(4, GlyphPhase.AllCombinations().Count());
    }

    [Fact]
    public void RequiresPhase_IsTrueOnlyForShadingInDitherMode()
    {
        Cp437GlyphGenerator dither  = Make(11, 20);
        Cp437GlyphGenerator uniform = Make(11, 20, ShadingMode.Uniform);

        Assert.True(dither.RequiresPhase('░'));
        Assert.False(dither.RequiresPhase('█'));
        Assert.False(dither.RequiresPhase('┼'));
        Assert.False(uniform.RequiresPhase('░'));
    }

    // ── Determinism and font independence ─────────────────────────────────────

    [Theory]
    [MemberData(nameof(Cells))]
    public void Regeneration_IsByteIdentical(int w, int h)
    {
        Cp437GlyphGenerator g = Make(w, h);

        foreach (char ch in Cp437GraphicsCharacters.AllCharacters)
            foreach (GlyphPhase phase in GlyphPhase.AllCombinations())
                Assert.Equal(g.Generate(ch, phase).Alpha, g.Generate(ch, phase).Alpha);
    }

    /// <summary>
    /// Two different fonts that happen to produce the same cell size must produce identical
    /// glyphs — the whole point of deriving geometry from the cell instead of the face.
    /// </summary>
    [Fact]
    public void TwoGeneratorsWithTheSameCellSize_ProduceIdenticalGlyphs()
    {
        Cp437GlyphGenerator a = Make(12, 20);
        Cp437GlyphGenerator b = Make(12, 20);

        foreach (char ch in Cp437GraphicsCharacters.AllCharacters)
            foreach (GlyphPhase phase in GlyphPhase.AllCombinations())
                Assert.Equal(a.Generate(ch, phase).Alpha, b.Generate(ch, phase).Alpha);
    }

    // ── Degenerate geometry ───────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 40)]
    [InlineData(40, 1)]
    [InlineData(3, 3)]
    [InlineData(40, 40)]
    public void DegenerateCellSizes_GenerateAll48GlyphsWithoutThrowing(int w, int h)
    {
        var generator = new Cp437GlyphGenerator(w, h);

        foreach (char ch in Cp437GraphicsCharacters.AllCharacters)
        {
            Assert.True(generator.TryGenerate(ch, out AlphaBitmap b), $"'{ch}' at {w}x{h}");
            Assert.Equal(w, b.Width);
            Assert.Equal(h, b.Height);
        }
    }

    [Fact]
    public void TinyCells_StillReachTheirConnectedEdges()
    {
        // A 1x1 cell is the extreme degenerate case: every stroke collapses onto the one pixel.
        var g = new Cp437GlyphGenerator(1, 1);

        Assert.Equal(255, g.Generate('█')[0, 0]);
        Assert.Equal(255, g.Generate('┼')[0, 0]);
        Assert.Equal(255, g.Generate('╬')[0, 0]);
    }

    // ── Cache ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Cache_ReturnsTheSameInstanceAndKeepsPhasesApart()
    {
        var cache = new CellGlyphBitmapCache(11, 20);

        Assert.True(cache.TryGet('░', new GlyphPhase(0, 0), out AlphaBitmap a0));
        Assert.True(cache.TryGet('░', new GlyphPhase(0, 0), out AlphaBitmap a1));
        Assert.Same(a0, a1);

        Assert.True(cache.TryGet('░', new GlyphPhase(1, 0), out AlphaBitmap b0));
        Assert.NotSame(a0, b0);
        Assert.NotEqual(a0.Alpha, b0.Alpha);

        // A glyph that cannot depend on phase collapses to one entry.
        Assert.True(cache.TryGet('█', new GlyphPhase(0, 0), out AlphaBitmap f0));
        Assert.True(cache.TryGet('█', new GlyphPhase(1, 1), out AlphaBitmap f1));
        Assert.Same(f0, f1);

        Assert.False(cache.TryGet('A', out _));
    }

    // ── Shared check suite ────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Cells))]
    public void SharedCheckSuite_PassesForBothShadingModes(int w, int h)
    {
        foreach (ShadingMode mode in (ShadingMode[])[ShadingMode.Uniform, ShadingMode.PhasedDither])
        {
            foreach (GeneratedGlyphCheckResult r in GeneratedGlyphChecks.RunAll(Make(w, h, mode)))
                Assert.True(r.Passed, $"{mode} {w}x{h}: {r}");
        }
    }
}
