// Tests for the SDL_GPU generated-glyph integration.
//
// Everything here is headless: the GPU renderer itself needs a device and a window, so the pure
// pieces are exercised directly —
//   GpuGlyphKey             the phase-aware atlas/cache key
//   IGpuGlyphSource         the seam the pipeline and atlas talk through
//   GeneratedGlyphValidator the size contract the atlas enforces
//   GpuAlphaBlend           the CPU-fallback blend arithmetic
//
// A FakeGlyphSource stands in for SDLGpuRenderer where the key-normalisation contract needs
// exercising end to end; it reproduces the renderer's CreateKey logic against the real generator.
using TSharpVision.Drivers.SDL.Gpu;
using TSharpVision.Drivers.SDL.Renderer;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;
using TSharpVision.Drivers.SDL.Rendering.PixelAnalysis;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

public sealed class SdlGpuGeneratedGlyphTests
{
    private const int CellW = 11;   // Consolas 20pt — odd width
    private const int CellH = 20;

    /// <summary>
    /// Mirrors <c>SDLGpuRenderer.CreateKey</c> / <c>GetAlpha</c> without needing a GPU device.
    /// The logic under test is the normalisation rule and the generated-first precedence.
    /// </summary>
    private sealed class FakeGlyphSource : IGpuGlyphSource
    {
        private readonly CellGlyphBitmapCache _generated;
        private readonly int _cellWidth;
        private readonly int _cellHeight;

        public FakeGlyphSource(int cellWidth = CellW, int cellHeight = CellH,
                               ShadingMode shading = ShadingMode.PhasedDither)
        {
            _cellWidth  = cellWidth;
            _cellHeight = cellHeight;
            _generated  = new CellGlyphBitmapCache(new Cp437GlyphGenerator(
                cellWidth, cellHeight,
                shading == ShadingMode.Uniform
                    ? Cp437GlyphGeneratorOptions.UniformShading
                    : Cp437GlyphGeneratorOptions.Default));
        }

        public GpuGlyphKey CreateKey(char ch, GlyphPhase phase)
        {
            if (!_generated.Generator.RequiresPhase(ch))
                return GpuGlyphKey.PhaseIndependent(ch);

            GlyphPhase n = phase.Normalized();
            return new GpuGlyphKey((uint)ch, n.X, n.Y);
        }

        public byte[]? GetAlpha(GpuGlyphKey key)
        {
            char ch = key.Character;

            // Stands in for the font path: only B0-DF is generated.
            if (!_generated.TryGet(ch, key.Phase, out AlphaBitmap bitmap))
                return null;

            GeneratedGlyphValidator.EnsureExpectedSize(bitmap, _cellWidth, _cellHeight, ch);
            return bitmap.Alpha;
        }
    }

    // ── Generated classification (shared with SDLRenderer) ─────────────────────

    [Theory]
    [InlineData('A')]
    [InlineData('■')]  // CP437 FE — outside B0-DF
    [InlineData('▲')]  // CP437 1E — outside B0-DF
    [InlineData('►')]  // CP437 10 — outside B0-DF
    public void OutOfRangeCharacters_AreNotGenerated(char ch)
    {
        Assert.False(Cp437GlyphGenerator.IsGenerated(ch));
    }

    // ── Phase ──────────────────────────────────────────────────────────────────

    [Fact]
    public void OddCellWidth_AlternatesPhaseX()
    {
        Assert.Equal(new GlyphPhase(0, 0), GlyphPhase.ForCell(0, 0, 11, 20));
        Assert.Equal(new GlyphPhase(1, 0), GlyphPhase.ForCell(1, 0, 11, 20));
        Assert.Equal(new GlyphPhase(0, 0), GlyphPhase.ForCell(2, 0, 11, 20));
    }

    [Fact]
    public void OddCellHeight_AlternatesPhaseY()
    {
        Assert.Equal(new GlyphPhase(0, 0), GlyphPhase.ForCell(0, 0, 12, 23));
        Assert.Equal(new GlyphPhase(0, 1), GlyphPhase.ForCell(0, 1, 12, 23));
        Assert.Equal(new GlyphPhase(0, 0), GlyphPhase.ForCell(0, 2, 12, 23));
    }

    [Fact]
    public void EvenCellDimensions_HoldPhaseConstant()
    {
        for (int col = 0; col < 5; col++)
            for (int row = 0; row < 5; row++)
                Assert.Equal(new GlyphPhase(0, 0), GlyphPhase.ForCell(col, row, 12, 24));
    }

    // ── GpuGlyphKey / GPU alpha cache key ────────────────────────────────

    [Fact]
    public void ShadingCharacters_ProduceFourDistinctKeys_UnderPhasedDither()
    {
        var source = new FakeGlyphSource();

        foreach (char ch in (char[])['░', '▒', '▓'])
        {
            var keys = new HashSet<GpuGlyphKey>();
            foreach (GlyphPhase phase in GlyphPhase.AllCombinations())
                keys.Add(source.CreateKey(ch, phase));

            Assert.Equal(4, keys.Count);
        }
    }

    [Theory]
    [InlineData('█')]   // block
    [InlineData('▀')]   // half block
    [InlineData('┼')]   // single box drawing
    [InlineData('╬')]   // double box drawing
    [InlineData('╪')]   // mixed box drawing
    public void GeneratedNonShadingCharacters_CollapseToOneKey(char ch)
    {
        var source = new FakeGlyphSource();

        var keys = new HashSet<GpuGlyphKey>();
        foreach (GlyphPhase phase in GlyphPhase.AllCombinations())
            keys.Add(source.CreateKey(ch, phase));

        Assert.Single(keys);
        Assert.Equal(GpuGlyphKey.PhaseIndependent(ch), keys.Single());
    }

    [Theory]
    [InlineData('A')]
    [InlineData('■')]
    [InlineData('▲')]
    public void OrdinaryFontCharacters_CollapseToOneKey(char ch)
    {
        var source = new FakeGlyphSource();

        var keys = new HashSet<GpuGlyphKey>();
        foreach (GlyphPhase phase in GlyphPhase.AllCombinations())
            keys.Add(source.CreateKey(ch, phase));

        Assert.Single(keys);
    }

    [Fact]
    public void UniformShading_AlsoCollapsesToOneKey()
    {
        var source = new FakeGlyphSource(shading: ShadingMode.Uniform);

        var keys = new HashSet<GpuGlyphKey>();
        foreach (GlyphPhase phase in GlyphPhase.AllCombinations())
            keys.Add(source.CreateKey('░', phase));

        Assert.Single(keys);
    }

    /// <summary>
    /// How many atlas slots a full screen of generated glyphs actually needs. The number of
    /// reachable phases depends on cell parity, not just on the lattice: an even cell dimension
    /// makes that axis' phase constant across the whole grid.
    /// </summary>
    [Theory]
    [InlineData(11, 20, 2)]   // odd width, even height  -> phases (0,0) (1,0)
    [InlineData(12, 23, 2)]   // even width, odd height  -> phases (0,0) (0,1)
    [InlineData(12, 24, 1)]   // both even               -> phase  (0,0) only
    [InlineData(11, 23, 4)]   // both odd                -> all four phases: the worst case
    public void WholeScreenOfGeneratedGlyphs_AddsOnlyShadingPhaseVariants(
        int cellW, int cellH, int expectedPhasesPerShadingChar)
    {
        var source = new FakeGlyphSource(cellW, cellH);
        var keys   = new HashSet<GpuGlyphKey>();

        foreach (char ch in Cp437GraphicsCharacters.AllCharacters)
            for (int col = 0; col < 8; col++)
                for (int row = 0; row < 8; row++)
                    keys.Add(source.CreateKey(ch, GlyphPhase.ForCell(col, row, cellW, cellH)));

        // 45 phase-independent glyphs, plus 3 shading characters x their reachable phases.
        Assert.Equal(45 + 3 * expectedPhasesPerShadingChar, keys.Count);

        // And never more than the documented ceiling of 12 extra slots.
        Assert.True(keys.Count <= 45 + 3 * GlyphPhase.Combinations);
    }

    [Fact]
    public void PhaseIsNormalised_SoEquivalentPhasesShareASlot()
    {
        var source = new FakeGlyphSource();

        Assert.Equal(source.CreateKey('░', new GlyphPhase(0, 0)),
                     source.CreateKey('░', new GlyphPhase(2, 4)));   // same phase mod 2
    }

    // ── Generated bitmap identity across back-ends ─────────────────────────────

    /// <summary>
    /// The bytes the GPU path receives must be byte-identical to what the same generator produces
    /// for SDLRenderer and for the diagnostics tool — one generator, one result.
    /// </summary>
    [Theory]
    [InlineData(11, 20)]   // Consolas 20pt
    [InlineData(12, 23)]   // Courier New 20pt
    [InlineData(12, 24)]   // Cascadia Mono 20pt
    [InlineData(24, 43)]   // Consolas 43pt — the 2px stroke run
    public void GpuAlphaIsByteIdenticalToTheReferenceGenerator(int cellW, int cellH)
    {
        var source    = new FakeGlyphSource(cellW, cellH);
        var reference = new Cp437GlyphGenerator(cellW, cellH, Cp437GlyphGeneratorOptions.Default);

        foreach (char ch in Cp437GraphicsCharacters.AllCharacters)
        {
            foreach (GlyphPhase phase in GlyphPhase.AllCombinations())
            {
                GpuGlyphKey key = source.CreateKey(ch, phase);
                byte[]? gpuAlpha = source.GetAlpha(key);

                Assert.NotNull(gpuAlpha);
                Assert.Equal(cellW * cellH, gpuAlpha!.Length);

                // Reference uses the key's (normalised) phase, exactly as the atlas would.
                AlphaBitmap expected = reference.Generate(ch, key.Phase);
                Assert.Equal(expected.Alpha, gpuAlpha);
            }
        }
    }

    // ── Atlas size contract ────────────────────────────────────────────────────

    [Fact]
    public void AtlasValidator_AcceptsExactlyCellSizedAlpha()
    {
        var alpha = new byte[CellW * CellH];

        GeneratedGlyphValidator.EnsureExpectedLength(alpha, CellW, CellH, '░');
        // No exception — the call completing is the assertion.
    }

    [Fact]
    public void AtlasValidator_RejectsTooShortAlpha()
    {
        var tooShort = new byte[CellW * CellH - 1];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            GeneratedGlyphValidator.EnsureExpectedLength(tooShort, CellW, CellH, '░'));

        Assert.Contains($"{CellW * CellH - 1} byte", ex.Message);
        Assert.Contains($"{CellW}x{CellH}", ex.Message);
        Assert.Contains("░", ex.Message);
        Assert.Contains("U+2591", ex.Message);
    }

    [Fact]
    public void AtlasValidator_RejectsTooLongAlpha()
    {
        var tooLong = new byte[CellW * CellH + 7];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            GeneratedGlyphValidator.EnsureExpectedLength(tooLong, CellW, CellH, '█'));

        Assert.Contains($"{CellW * CellH + 7} byte", ex.Message);
        Assert.Contains($"expected {CellW * CellH}", ex.Message);
        Assert.Contains("█", ex.Message);
    }

    [Fact]
    public void AtlasValidator_NeverFiresForRealGeneratorOutput()
    {
        foreach ((int w, int h) in (ValueTuple<int, int>[])[(11, 20), (12, 23), (24, 43), (1, 1)])
        {
            var gen = new Cp437GlyphGenerator(w, h);
            foreach (char ch in Cp437GraphicsCharacters.AllCharacters)
                GeneratedGlyphValidator.EnsureExpectedLength(gen.Generate(ch).Alpha, w, h, ch);
        }
    }

    // ── CPU-fallback blend arithmetic ──────────────────────────────────────────

    [Theory]
    [InlineData(0, 200, 0)]      // fully transparent — destination survives
    [InlineData(255, 200, 0)]
    [InlineData(0, 0, 0)]
    public void Blend_WithZeroAlpha_LeavesDestinationUnchanged(int src, int dst, int alpha)
    {
        Assert.Equal((byte)dst, GpuAlphaBlend.Blend((byte)src, (byte)dst, (byte)alpha));
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(255, 0)]
    [InlineData(137, 42)]
    public void Blend_WithFullAlpha_ReplacesDestinationExactly(int src, int dst)
    {
        Assert.Equal((byte)src, GpuAlphaBlend.Blend((byte)src, (byte)dst, 255));
    }

    [Fact]
    public void Blend_AtHalfAlpha_IsTheRoundedMidpoint()
    {
        // 255 over 0 at alpha 128 -> 255*128/255 = 128 exactly.
        Assert.Equal(128, GpuAlphaBlend.Blend(255, 0, 128));

        // Symmetric case: 0 over 255 at alpha 128 -> 127.
        Assert.Equal(127, GpuAlphaBlend.Blend(0, 255, 128));
    }

    /// <summary>
    /// The specific defect this replaced: <c>&gt;&gt; 8</c> divides by 256, so white-on-black at
    /// full coverage produced 254 instead of 255 and every partial blend was biased dark.
    /// </summary>
    [Fact]
    public void Blend_HasNoDivideBy256Bias()
    {
        for (int alpha = 0; alpha <= 255; alpha++)
        {
            byte correct = GpuAlphaBlend.Blend(255, 0, (byte)alpha);
            byte biased  = (byte)((255 * alpha + 0 * (255 - alpha)) >> 8);

            Assert.True(correct >= biased,
                $"alpha={alpha}: corrected blend {correct} should never be darker than the old {biased}");
        }

        // The endpoint the old code got outright wrong.
        Assert.Equal(255, GpuAlphaBlend.Blend(255, 0, 255));
        Assert.Equal(254, (byte)((255 * 255 + 0) >> 8));
    }

    [Fact]
    public void Blend_IsExactAcrossTheWholeDomain_ForRepresentativeValues()
    {
        foreach (byte src in (byte[])[0, 1, 127, 128, 254, 255])
        {
            foreach (byte dst in (byte[])[0, 1, 127, 128, 254, 255])
            {
                for (int a = 0; a <= 255; a += 17)
                {
                    byte actual   = GpuAlphaBlend.Blend(src, dst, (byte)a);
                    int  expected = (int)Math.Round((src * a + dst * (255.0 - a)) / 255.0,
                                                    MidpointRounding.AwayFromZero);

                    Assert.True(Math.Abs(actual - expected) <= 1,
                        $"src={src} dst={dst} a={a}: got {actual}, expected ~{expected}");
                }
            }
        }
    }
}
