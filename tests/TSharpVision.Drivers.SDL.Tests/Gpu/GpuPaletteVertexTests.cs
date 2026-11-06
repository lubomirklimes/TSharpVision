using System.Reflection;
using System.Runtime.InteropServices;
using TSharpVision.Drivers.SDL.Gpu;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// Physical palette plumbing through <see cref="TerminalGpuPipeline.BuildVertices(TerminalGpuPipeline.BgVertex*, TerminalGpuPipeline.GlyphVertex*, ScreenBuffer, uint, uint, int, int, int, int, ushort, IGpuGlyphSource, TerminalAtlas, out int, out int, int)"/>
/// and the CPU-compositing fallback. No GPU device: the vertex build writes into ordinary memory
/// and the fallback composites into a managed pixel buffer.
/// </summary>
public sealed unsafe class GpuPaletteVertexTests
{
    private const int CellW = 8, CellH = 16;
    private const uint Cols = 4, Rows = 2;

    private static ScreenBuffer MixedScreen()
    {
        var screen = new ScreenBuffer((ushort)Cols, (ushort)Rows);
        (char ch, byte attr)[] cells =
        {
            (' ', 0x17), ('A', 0x4E), ('█', 0x6F), ('\0', 0x70),
            ('x', 0x1E), (' ', 0x06), ('B', 0x5A), ('C', 0x89),
        };
        for (int i = 0; i < cells.Length; i++)
            screen.SetChar((uint)(i % Cols), (uint)(i / Cols), new TScreenChar(cells[i].ch, (TColorAttr)cells[i].attr));
        return screen;
    }

    public static TheoryData<int, int, ushort> Cursors => new()
    {
        { -1, -1, 0 },  // no cursor
        { 1, 0, 100 },  // block cursor on a glyph cell
        { 1, 1, 100 },  // block cursor on a space
        { 2, 1, 1 },    // underline cursor on a glyph cell
        { 0, 1, 50 },   // underline cursor (type < 100)
        { 3, 0, 120 },  // block cursor on a '\0' cell
    };

    private sealed class Source : IGpuGlyphSource
    {
        public GlyphPhase PhaseForCell(int column, int row, int cellWidth, int cellHeight) => new(column & 1, row & 1);
        public GpuGlyphKey CreateKey(char ch, GlyphPhase phase) => GpuGlyphKey.PhaseIndependent(ch);
        public byte[] GetAlpha(GpuGlyphKey key) =>
            Enumerable.Range(0, CellW * CellH).Select(i => (byte)((i * 7 + key.Character) & 0xFF)).ToArray();
    }

    private sealed record Streams(TerminalGpuPipeline.BgVertex[] Bg, TerminalGpuPipeline.GlyphVertex[] Glyph)
    {
        public byte[] Bytes() =>
            MemoryMarshal.AsBytes(Bg.AsSpan()).ToArray().Concat(MemoryMarshal.AsBytes(Glyph.AsSpan()).ToArray()).ToArray();
    }

    // palette == null: the original (no-palette) overload.
    private static Streams Build(ScreenBuffer screen, int cx, int cy, ushort type, uint[]? palette)
    {
        using var atlas = new TerminalAtlas(CellW, CellH);
        var bg = new TerminalGpuPipeline.BgVertex[(Cols * Rows + 1) * 6];
        var glyph = new TerminalGpuPipeline.GlyphVertex[Cols * Rows * 6];
        int bgCount, glyphCount;
        fixed (TerminalGpuPipeline.BgVertex* pb = bg)
        fixed (TerminalGpuPipeline.GlyphVertex* pg = glyph)
        {
            if (palette == null)
                TerminalGpuPipeline.BuildVertices(pb, pg, screen, Cols, Rows, CellW, CellH,
                    cx, cy, type, new Source(), atlas, out bgCount, out glyphCount, 3);
            else
                TerminalGpuPipeline.BuildVertices(pb, pg, screen, Cols, Rows, CellW, CellH,
                    cx, cy, type, new Source(), atlas, out bgCount, out glyphCount, 3, palette);
        }
        return new Streams(bg[..bgCount], glyph[..glyphCount]);
    }

    private static (byte, byte, byte) Rgb(uint argb) =>
        ((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    // Independent oracle of the colour sequence: per cell one bg quad (fg if block cursor),
    // an underline quad in fg for an underline cursor, one glyph quad for a non-space cell.
    private static (List<(byte, byte, byte)> Bg, List<(byte, byte, byte)> Glyph) ExpectedColors(
        ScreenBuffer screen, int cx, int cy, ushort type, IReadOnlyList<uint> table)
    {
        var bg = new List<(byte, byte, byte)>();
        var glyph = new List<(byte, byte, byte)>();
        for (uint row = 0; row < Rows; row++)
            for (uint col = 0; col < Cols; col++)
            {
                TScreenChar cell = screen.GetChar(col, row);
                byte attr = (byte)(cell.Attr & 0xFF);
                uint fg = table[attr & 0x0F], back = table[(attr >> 4) & 0x0F];
                bool cursor = type != 0 && col == cx && row == cy;
                if (cursor && type >= 100) (fg, back) = (back, fg);
                bg.Add(Rgb(back));
                if (cursor && type < 100) bg.Add(Rgb(fg));
                if (cell.Character is not (' ' or '\0')) glyph.Add(Rgb(fg));
            }
        return (bg, glyph);
    }

    private static void AssertColors(Streams streams, (List<(byte, byte, byte)> Bg, List<(byte, byte, byte)> Glyph) expected)
    {
        Assert.Equal(expected.Bg.Count * 6, streams.Bg.Length);
        Assert.Equal(expected.Glyph.Count * 6, streams.Glyph.Length);
        for (int i = 0; i < streams.Bg.Length; i++)
        {
            var v = streams.Bg[i];
            Assert.Equal(expected.Bg[i / 6], (v.R, v.G, v.B));
            Assert.Equal(255, v.A);
        }
        for (int i = 0; i < streams.Glyph.Length; i++)
        {
            var v = streams.Glyph[i];
            Assert.Equal(expected.Glyph[i / 6], (v.R, v.G, v.B));
            Assert.Equal(255, v.A);
        }
    }

    [Theory]
    [MemberData(nameof(Cursors))]
    public void Default_palette_vertex_streams_are_byte_identical_to_the_no_palette_build(int cx, int cy, ushort type)
    {
        ScreenBuffer screen = MixedScreen();
        Streams legacy = Build(screen, cx, cy, type, null);
        Streams explicitDefault = Build(screen, cx, cy, type, GpuPhysicalPalette.Default.Argb.ToArray());
        Streams legacyTable = Build(screen, cx, cy, type, GpuPhysicalPaletteTests.LegacyVga16.ToArray());

        Assert.Equal(legacy.Bytes(), explicitDefault.Bytes());
        Assert.Equal(legacy.Bytes(), legacyTable.Bytes());
        // And the no-palette build still produces the historical (classic VGA) Default colours.
        AssertColors(legacy, ExpectedColors(screen, cx, cy, type, GpuPhysicalPaletteTests.LegacyVga16));
    }

    [Theory]
    [MemberData(nameof(Cursors))]
    public void Non_default_palettes_substitute_colours_only(int cx, int cy, ushort type)
    {
        ScreenBuffer screen = MixedScreen();
        Streams legacy = Build(screen, cx, cy, type, null);
        foreach (GpuPhysicalPalette palette in GpuPhysicalPalette.All.Skip(1))
        {
            Streams streams = Build(screen, cx, cy, type, palette.Argb.ToArray());
            AssertColors(streams, ExpectedColors(screen, cx, cy, type, palette.Argb));

            // Geometry and atlas UVs are untouched.
            Assert.Equal(legacy.Bg.Select(v => (v.X, v.Y)), streams.Bg.Select(v => (v.X, v.Y)));
            Assert.Equal(legacy.Glyph.Select(v => (v.X, v.Y, v.U, v.V)), streams.Glyph.Select(v => (v.X, v.Y, v.U, v.V)));
        }
    }

    [Fact]
    public void Block_cursor_swaps_the_profile_colours()
    {
        ScreenBuffer screen = MixedScreen();
        GpuPhysicalPalette green = GpuPhysicalPalette.All.Single(p => p.Id == "Green");
        // Cell (1,0) is 'A' with attr 0x4E: fg 14, bg 4. The block cursor draws bg = fg[14], glyph = bg[4].
        Streams streams = Build(screen, 1, 0, 100, green.Argb.ToArray());
        var cellBg = streams.Bg[6];
        var cellGlyph = streams.Glyph[0];
        Assert.Equal(Rgb(green.Argb[14]), (cellBg.R, cellBg.G, cellBg.B));
        Assert.Equal(Rgb(green.Argb[4]), (cellGlyph.R, cellGlyph.G, cellGlyph.B));
    }

    [Fact]
    public void Default_keeps_the_classic_VGA_brown_at_index_6()
    {
        ScreenBuffer screen = MixedScreen();
        Streams legacy = Build(screen, -1, -1, 0, null);
        Streams explicitDefault = Build(screen, -1, -1, 0, GpuPhysicalPalette.Default.Argb.ToArray());
        // Cell (2,0) (attr 0x6F) has index 6 as a drawn colour (its bg): brown, not the raw RGBI dark yellow.
        var a = legacy.Bg[2 * 6];
        var b = explicitDefault.Bg[2 * 6];
        Assert.Equal(((byte)0xAA, (byte)0x55, (byte)0x00), (a.R, a.G, a.B));
        Assert.Equal((a.R, a.G, a.B), (b.R, b.G, b.B));
        Assert.Equal(MemoryMarshal.AsBytes(legacy.Glyph.AsSpan()).ToArray(), MemoryMarshal.AsBytes(explicitDefault.Glyph.AsSpan()).ToArray());
    }

    [Fact]
    public void Short_palette_is_rejected()
    {
        ScreenBuffer screen = MixedScreen();
        Assert.Throws<ArgumentException>(() => Build(screen, -1, -1, 0, new uint[15]));
    }

    // ─── CPU-compositing fallback ────────────────────────────────────────────

    private static SDLGpuRenderer HeadlessRenderer()
    {
        // No font and no device: install the managed CP437 generator so generated glyphs (U+2588)
        // composite. Font glyphs ('A', 'x', ...) have no rasteriser and are skipped, as in the
        // production fallback when a glyph is unavailable.
        var renderer = new SDLGpuRenderer(IntPtr.Zero, new SDLGpuOptions());
        typeof(SDLGpuRenderer).GetField("_generatedGlyphs", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(renderer, new CellGlyphBitmapCache(renderer.CellWidth, renderer.CellHeight));
        return renderer;
    }

    private static (byte, byte, byte) PixelAt(byte[] px, SDLGpuRenderer r, int x, int y)
    {
        int o = (y * (int)Cols * r.CellWidth + x) * 4;
        return (px[o], px[o + 1], px[o + 2]);
    }

    [Theory]
    [MemberData(nameof(Cursors))]
    public void Cpu_fallback_is_identical_under_Default(int cx, int cy, ushort type)
    {
        ScreenBuffer screen = MixedScreen();
        using var fresh = HeadlessRenderer();
        fresh.SetCursor(cx, cy, type);
        byte[] baseline = fresh.CompositeForTesting(screen, Cols, Rows).ToArray();
        Assert.Same(GpuPhysicalPalette.Default, fresh.PhysicalPalette);
        Assert.Equal(GpuDisplayEffect.Default, fresh.DisplayEffect);

        using var switched = HeadlessRenderer();
        switched.SetCursor(cx, cy, type);
        switched.PhysicalPalette = GpuPhysicalPalette.All.Single(p => p.Id == "Amber");
        switched.CompositeForTesting(screen, Cols, Rows);
        switched.PhysicalPalette = GpuPhysicalPalette.Default;
        switched.DisplayEffect = GpuDisplayEffect.Default;
        Assert.Equal(baseline, switched.CompositeForTesting(screen, Cols, Rows));
        switched.PhysicalPalette = GpuPhysicalPalette.All.Single(p => p.Id == "Grayscale");
        switched.CompositeForTesting(screen, Cols, Rows);
        switched.PhysicalPalette = GpuPhysicalPalette.Default;
        Assert.Equal(baseline, switched.CompositeForTesting(screen, Cols, Rows));

        // Independent oracle: a non-cursor space cell (0,0) attr 0x17 is legacy blue throughout.
        if (!(cx == 0 && cy == 0))
            Assert.Equal(Rgb(GpuPhysicalPaletteTests.LegacyVga16[1]), PixelAt(baseline, fresh, 3, 5));
        // Generated full block at (2,0) attr 0x6F: white glyph over brown; its centre is fg.
        if (!(cx == 2 && cy == 0))
            Assert.Equal(Rgb(GpuPhysicalPaletteTests.LegacyVga16[15]),
                PixelAt(baseline, fresh, 2 * fresh.CellWidth + fresh.CellWidth / 2, fresh.CellHeight / 2));
    }

    [Fact]
    public void Cpu_fallback_uses_the_active_palette_including_cursor_inversion()
    {
        ScreenBuffer screen = MixedScreen();
        using var renderer = HeadlessRenderer();
        GpuPhysicalPalette defaultPalette = GpuPhysicalPalette.Default;
        GpuPhysicalPalette green = GpuPhysicalPalette.All.Single(p => p.Id == "Green");
        int w = renderer.CellWidth, h = renderer.CellHeight;

        renderer.SetCursor(-1, -1, 0);
        renderer.PhysicalPalette = defaultPalette;
        byte[] px = renderer.CompositeForTesting(screen, Cols, Rows);
        Assert.Equal(Rgb(defaultPalette.Argb[0]), PixelAt(px, renderer, 1 * w + 1, 1 * h + 1)); // (1,1) attr 0x06 space: bg black

        // Block cursor on the space at (1,1), attr 0x06: inverted, so the whole cell is fg = index 6.
        renderer.SetCursor(1, 1, 100);
        px = renderer.CompositeForTesting(screen, Cols, Rows);
        Assert.Equal(((byte)0xAA, (byte)0x55, (byte)0x00), PixelAt(px, renderer, 1 * w + 1, 1 * h + 1));
        renderer.PhysicalPalette = green;
        px = renderer.CompositeForTesting(screen, Cols, Rows);
        Assert.Equal(Rgb(green.Argb[6]), PixelAt(px, renderer, 1 * w + 1, 1 * h + 1));
        Assert.Equal(Rgb(green.Argb[1]), PixelAt(px, renderer, 1, 1)); // (0,0) attr 0x17 bg
    }

    [Fact]
    public void Renderer_rejects_a_null_palette()
    {
        using var renderer = HeadlessRenderer();
        Assert.Throws<ArgumentNullException>(() => renderer.PhysicalPalette = null!);
        Assert.Same(GpuPhysicalPalette.Default, renderer.PhysicalPalette);
        renderer.DisplayEffect = GpuDisplayEffect.Scanlines;
        Assert.Equal(GpuDisplayEffect.Scanlines, renderer.DisplayEffect);
    }
}
