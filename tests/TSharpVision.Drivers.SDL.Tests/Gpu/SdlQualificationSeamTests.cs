using TSharpVision.Drivers.SDL.Gpu;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

public sealed class SdlQualificationSeamTests
{
    [Theory]
    [InlineData(12, 24)]
    [InlineData(24, 48)]
    [InlineData(48, 96)]
    public void CpuAtlasUsesDensityAwareSizingAndRoundTripsSlotPixels(int width, int height)
    {
        using var atlas = new TerminalAtlas(width, height);
        Assert.Equal(TerminalAtlas.ChooseSize(width, height), atlas.Size);
        Assert.Equal((atlas.Size / width) * (atlas.Size / height), atlas.Capacity);
        var alpha = Enumerable.Range(0, width * height).Select(i => (byte)(i % 251)).ToArray();
        var key = GpuGlyphKey.PhaseIndependent('A');
        var uv = atlas.GetOrAllocateSlot(key, _ => alpha);
        Assert.True(uv.u1 > uv.u0);
        Assert.True(atlas.TryGetSlot(key, out int slot));
        Assert.True(atlas.TryGetKey(slot, out var found));
        Assert.Equal(key, found);
        Assert.Equal(alpha, atlas.ReadSlot(slot));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public unsafe void VertexBuildPreservesScaledCursorSourcePhaseAndBufferBounds(int underlineHeight)
    {
        using var atlas = new TerminalAtlas(24, 48);
        var source = new Source();
        var screen = new ScreenBuffer(2, 1);
        screen.SetChar(0, 0, new TScreenChar('A', (TColorAttr)0x17));
        screen.SetChar(1, 0, new TScreenChar('B', (TColorAttr)0x17));
        var backgrounds = stackalloc TerminalGpuPipeline.BgVertex[19];
        var glyphs = stackalloc TerminalGpuPipeline.GlyphVertex[13];
        backgrounds[18].X = 123;
        glyphs[12].X = 456;
        TerminalGpuPipeline.BuildVertices(backgrounds, glyphs, screen, 2, 1, 24, 48,
            0, 0, 1, source, atlas, out int bgCount, out int glyphCount, underlineHeight);
        Assert.Equal(18, bgCount); // Two cell quads and one underline quad.
        Assert.Equal(12, glyphCount);
        Assert.Equal(123f, backgrounds[18].X);
        Assert.Equal(456f, glyphs[12].X);
        Assert.Equal(-1f + 2f * underlineHeight / 48f, backgrounds[6].Y, 5);
        Assert.Equal(2, source.PhaseCalls);
        Assert.All(source.Phases, phase => Assert.Equal(new GlyphPhase(1, 1), phase));
    }

    private sealed class Source : IGpuGlyphSource
    {
        internal int PhaseCalls;
        internal List<GlyphPhase> Phases { get; } = new();
        public GlyphPhase PhaseForCell(int column, int row, int cellWidth, int cellHeight)
        {
            PhaseCalls++;
            return new GlyphPhase(1, 1);
        }
        public GpuGlyphKey CreateKey(char ch, GlyphPhase phase)
        {
            Phases.Add(phase);
            return GpuGlyphKey.PhaseIndependent(ch);
        }
        public byte[] GetAlpha(GpuGlyphKey key) => new byte[24 * 48];
    }
}
