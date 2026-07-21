// Glyph classification and texture-cache keying for SDLRenderer. Both are pure — no SDL handle,
// font or window — so they are exercised directly rather than through a constructed renderer.
using TSharpVision.Diagnostics.SdlGlyphs;
using TSharpVision.Drivers.SDL.Renderer;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;
using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Drivers.SDL.Tests;

public sealed class SdlGlyphRenderPolicyTests
{
    private static Cp437GlyphGenerator Generator(int cellWidth = 11, int cellHeight = 20) =>
        new(cellWidth, cellHeight);

    // ── Classification ─────────────────────────────────────────────────────────

    [Fact]
    public void AllCp437GraphicsCharacters_AreGenerated()
    {
        foreach (char ch in Cp437GraphicsCharacters.AllCharacters)
            Assert.Equal(SdlGlyphRenderClass.Generated, SdlGlyphRenderPolicy.Classify(ch));
    }

    [Fact]
    public void SpaceAndNul_AreSpace()
    {
        Assert.Equal(SdlGlyphRenderClass.Space, SdlGlyphRenderPolicy.Classify(' '));
        Assert.Equal(SdlGlyphRenderClass.Space, SdlGlyphRenderPolicy.Classify('\0'));
    }

    [Theory]
    [InlineData('A')]
    [InlineData('z')]
    [InlineData('0')]
    [InlineData('■')]
    [InlineData('▲')]
    public void TextAndOutOfRangeGlyphs_AreNatural(char ch)
    {
        Assert.False(Cp437GlyphGenerator.IsGenerated(ch));
        Assert.Equal(SdlGlyphRenderClass.Natural, SdlGlyphRenderPolicy.Classify(ch));
    }

    [Fact]
    public void BoxDrawingOutsideB0Df_UsesTheFontCellPath()
    {
        // U+2571 has no CP437 code, so it still comes from the font.
        Assert.False(Cp437GlyphGenerator.IsGenerated('╱'));
        Assert.Equal(SdlGlyphRenderClass.FontCell, SdlGlyphRenderPolicy.Classify('╱'));
    }

    // ── Phase ──────────────────────────────────────────────────────────────────

    [Fact]
    public void OddCellWidth_AlternatesPhaseAcrossColumns()
    {
        Assert.Equal(new GlyphPhase(0, 0), GlyphPhase.ForCell(0, 0, 11, 20));
        Assert.Equal(new GlyphPhase(1, 0), GlyphPhase.ForCell(1, 0, 11, 20));
        Assert.Equal(new GlyphPhase(0, 0), GlyphPhase.ForCell(2, 0, 11, 20));
    }

    [Fact]
    public void OddCellHeight_AlternatesPhaseAcrossRows()
    {
        Assert.Equal(new GlyphPhase(0, 0), GlyphPhase.ForCell(0, 0, 12, 23));
        Assert.Equal(new GlyphPhase(0, 1), GlyphPhase.ForCell(0, 1, 12, 23));
        Assert.Equal(new GlyphPhase(0, 0), GlyphPhase.ForCell(0, 2, 12, 23));
    }

    [Fact]
    public void EvenCellSize_KeepsPhaseConstant()
    {
        for (int col = 0; col < 4; col++)
            Assert.Equal(new GlyphPhase(0, 0), GlyphPhase.ForCell(col, 0, 12, 24));
    }

    // ── Texture cache key ──────────────────────────────────────────────────────

    [Fact]
    public void ShadingCharacters_GetOneKeyPerPhase()
    {
        Cp437GlyphGenerator gen = Generator();

        foreach (char ch in (char[])['░', '▒', '▓'])
        {
            var keys = new HashSet<SdlGlyphTextureKey>();
            foreach (GlyphPhase phase in GlyphPhase.AllCombinations())
                keys.Add(SdlGlyphTextureKey.Create(ch, 0xFFFFFFFFu, phase, gen));

            Assert.Equal(GlyphPhase.Combinations, keys.Count);
        }
    }

    [Theory]
    [InlineData('█')]
    [InlineData('┼')]
    [InlineData('╬')]
    [InlineData('A')]
    public void PhaseIndependentGlyphs_GetOneKey(char ch)
    {
        Cp437GlyphGenerator gen = Generator();

        var keys = new HashSet<SdlGlyphTextureKey>();
        foreach (GlyphPhase phase in GlyphPhase.AllCombinations())
            keys.Add(SdlGlyphTextureKey.Create(ch, 0xFFFFFFFFu, phase, gen));

        Assert.Single(keys);
    }

    [Fact]
    public void EquivalentPhases_ShareAKey()
    {
        Cp437GlyphGenerator gen = Generator();

        Assert.Equal(SdlGlyphTextureKey.Create('░', 0xFFFFFFFFu, new GlyphPhase(0, 0), gen),
                     SdlGlyphTextureKey.Create('░', 0xFFFFFFFFu, new GlyphPhase(2, 4), gen));
    }

    [Fact]
    public void CodepointAndColourDistinguishKeys()
    {
        Cp437GlyphGenerator gen = Generator();

        SdlGlyphTextureKey a = SdlGlyphTextureKey.Create('A', 0xFFFFFFFFu, GlyphPhase.Zero, gen);
        SdlGlyphTextureKey b = SdlGlyphTextureKey.Create('B', 0xFFFFFFFFu, GlyphPhase.Zero, gen);
        SdlGlyphTextureKey c = SdlGlyphTextureKey.Create('A', 0xFFFF0000u, GlyphPhase.Zero, gen);

        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void UniformShading_NeedsNoPhaseVariants()
    {
        var gen = new Cp437GlyphGenerator(11, 20, Cp437GlyphGeneratorOptions.UniformShading);

        var keys = new HashSet<SdlGlyphTextureKey>();
        foreach (GlyphPhase phase in GlyphPhase.AllCombinations())
            keys.Add(SdlGlyphTextureKey.Create('░', 0xFFFFFFFFu, phase, gen));

        Assert.Single(keys);
    }

    // ── Generated bitmap size guard ────────────────────────────────────────────

    [Fact]
    public void SizeGuard_RejectsAWrongSizedBitmap()
    {
        var wrong = AlphaBitmap.CreateEmpty(10, 19);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            GeneratedGlyphValidator.EnsureExpectedSize(wrong, 11, 20, '░'));

        Assert.Contains("10x19", ex.Message);
        Assert.Contains("11x20", ex.Message);
    }

    [Fact]
    public void SizeGuard_AcceptsEveryRealGeneratedGlyph()
    {
        foreach ((int w, int h) in (ValueTuple<int, int>[])[(11, 20), (12, 23), (24, 43), (1, 1)])
        {
            var gen = new Cp437GlyphGenerator(w, h);
            foreach (char ch in Cp437GraphicsCharacters.AllCharacters)
                GeneratedGlyphValidator.EnsureExpectedSize(gen.Generate(ch), w, h, ch);
        }
    }
}
