using System.Runtime.InteropServices;
using TSharpVision.Drivers.SDL.Gpu;
using TSharpVision.Drivers.SDL.Rendering.Fonts;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;
using static TSharpVision.Drivers.SDL.Tests.Unicode.UnicodeTestText;

namespace TSharpVision.Drivers.SDL.Tests.Unicode;

/// <summary>
/// TV13m GPU cluster path without a device: the RGBA cluster atlas (wide slots, keys, exhaustion, eviction, recovery),
/// the cluster vertex build (wide quads, trail skipping, edge clipping, tint vs intrinsic colour, zero allocation) and
/// the glyph atlas eviction that replaced the silent drop.
/// </summary>
public sealed unsafe class ClusterAtlasTests
{
    private const int CellW = 8, CellH = 16;

    /// <summary>Deterministic rasters: colour when the cluster contains an emoji, alpha pattern from the text hash.</summary>
    private sealed class FakeSource : IGpuClusterSource
    {
        public int Calls;
        public readonly List<string> Requests = new();
        public bool Blank;

        public SdlClusterGlyph? GetCluster(ReadOnlySpan<char> cluster, int cells)
        {
            Calls++;
            string text = cluster.ToString();
            Requests.Add(text);
            int w = cells * CellW;
            var rgba = new byte[w * CellH * 4];
            bool color = text.Any(char.IsSurrogate);
            if (!Blank)
                for (int i = 0; i < w * CellH; i++)
                {
                    rgba[i * 4] = color ? (byte)(i * 13) : (byte)255;
                    rgba[i * 4 + 1] = color ? (byte)(i * 7) : (byte)255;
                    rgba[i * 4 + 2] = color ? (byte)(i * 3) : (byte)255;
                    rgba[i * 4 + 3] = (byte)(1 + (i + text.Length) % 255);
                }
            return new SdlClusterGlyph(rgba, w, CellH, cells, color, SdlClusterCoverage.Full, SdlFallbackFontRole.Base, null, false);
        }
    }

    private sealed class GlyphSource : IGpuGlyphSource
    {
        public GpuGlyphKey CreateKey(char ch, GlyphPhase phase) => GpuGlyphKey.PhaseIndependent(ch);
        public byte[] GetAlpha(GpuGlyphKey key) => Enumerable.Repeat((byte)(key.Codepoint & 0xFF), CellW * CellH).ToArray();
    }

    // Unique wide clusters: CJK ideographs from U+4E00.
    private static string Cjk(int i) => S(0x4E00 + i);

    [Fact]
    public void Wide_cluster_slot_spans_its_cells_and_is_rasterised_once()
    {
        var atlas = new ClusterAtlas(CellW, CellH);
        var source = new FakeSource();
        Assert.True(atlas.TryGet(Grinning, 2, source, out ClusterSlot a));
        Assert.True(atlas.TryGet(Grinning, 2, source, out ClusterSlot b));
        Assert.Equal(1, source.Calls);
        Assert.Equal(a, b);
        Assert.Equal(2, a.Cells);
        Assert.True(a.IsColor);
        Assert.Equal(2f * CellW / atlas.Size, a.U1 - a.U0, 5);
        Assert.Equal((float)CellH / atlas.Size, a.V1 - a.V0, 5);

        // Same text in a different width is a different key; a narrow cluster takes one cell.
        Assert.True(atlas.TryGet(Grinning, 1, source, out ClusterSlot narrow));
        Assert.Equal(2, source.Calls);
        Assert.Equal((float)CellW / atlas.Size, narrow.U1 - narrow.U0, 5);
        Assert.NotEqual(a.U0, narrow.U0);
    }

    [Fact]
    public void Blank_raster_takes_no_atlas_space()
    {
        var atlas = new ClusterAtlas(CellW, CellH);
        var source = new FakeSource { Blank = true };
        Assert.True(atlas.TryGet(S(0x200D), 1, source, out ClusterSlot slot));
        Assert.True(slot.Empty);
        source.Blank = false;
        Assert.True(atlas.TryGet(Kan, 2, source, out ClusterSlot kan));
        Assert.Equal(0f, kan.U0);
    }

    [Fact]
    public void Exhausted_atlas_reports_full_then_recovers_after_reset()
    {
        var atlas = new ClusterAtlas(CellW, CellH, size: 64);   // 8 x 4 cell units = 16 two-cell slots
        var source = new FakeSource();
        Assert.Equal(32, atlas.CapacityUnits);
        for (int i = 0; i < 16; i++) Assert.True(atlas.TryGet(Cjk(i), 2, source, out _), $"slot {i}");
        Assert.False(atlas.TryGet(Cjk(16), 2, source, out _));
        Assert.NotEqual(0, atlas.Pixel(0, 0).A);

        atlas.Reset();
        Assert.Equal(1, atlas.Generation);
        Assert.Equal(0, atlas.Count);
        Assert.Equal((0, 0, 0, 0), ((int, int, int, int))atlas.Pixel(0, 0));
        Assert.True(atlas.TryGet(Cjk(16), 2, source, out ClusterSlot recovered));
        Assert.Equal(0f, recovered.U0);
        Assert.Equal(0f, recovered.V0);
    }

    [Fact]
    public void Slot_pixels_are_the_raster()
    {
        var atlas = new ClusterAtlas(CellW, CellH);
        var source = new FakeSource();
        Assert.True(atlas.TryGet(Kan, 2, source, out ClusterSlot slot));
        SdlClusterGlyph glyph = new FakeSource().GetCluster(Kan, 2)!;
        int x0 = (int)Math.Round(slot.U0 * atlas.Size), y0 = (int)Math.Round(slot.V0 * atlas.Size);
        for (int y = 0; y < CellH; y++)
            for (int x = 0; x < 2 * CellW; x++)
            {
                int o = (y * glyph.Width + x) * 4;
                Assert.Equal((glyph.Rgba[o], glyph.Rgba[o + 1], glyph.Rgba[o + 2], glyph.Rgba[o + 3]), atlas.Pixel(x0 + x, y0 + y));
            }
    }

    // ── Vertex build ─────────────────────────────────────────────────────────

    private static readonly uint[] Palette = GpuPhysicalPalette.Default.Argb.ToArray();

    private static TerminalGpuPipeline.GlyphVertex[] BuildClusters(
        ScreenBuffer screen, ClusterAtlas atlas, IGpuClusterSource source, int cx = -1, int cy = -1, ushort cursor = 0)
    {
        var v = new TerminalGpuPipeline.GlyphVertex[screen.Width * screen.Height * 6];
        int n;
        fixed (TerminalGpuPipeline.GlyphVertex* p = v)
            n = TerminalGpuPipeline.BuildClusterVertices(p, v.Length, screen, screen.Width, screen.Height, CellW, CellH,
                cx, cy, cursor, source, atlas, Palette);
        return v[..n];
    }

    private static (float X0, float X1, float U0, float U1) Quad(TerminalGpuPipeline.GlyphVertex[] v, int quad, uint cols)
    {
        float toCol(float x) => (x + 1f) / 2f * cols;
        return (toCol(v[quad * 6].X), toCol(v[quad * 6 + 1].X), v[quad * 6].U, v[quad * 6 + 1].U);
    }

    [Fact]
    public void Sample_one_written_by_the_core_gives_one_quad_per_cluster_spanning_its_cells()
    {
        ScreenBuffer screen = Blank(20, 1);
        Write(screen, 0, 0, Sample1, 0x1E);
        var atlas = new ClusterAtlas(CellW, CellH);
        var source = new FakeSource();
        TerminalGpuPipeline.GlyphVertex[] v = BuildClusters(screen, atlas, source);

        // A + grin(2) + B + thumb(2) + C + flag(2) + D + family(2) + E = 13 cells; 4 leads, trails skipped.
        Assert.Equal(new[] { Grinning, ThumbsUpMedium, FlagCz, Family }, source.Requests);
        Assert.Equal(4 * 6, v.Length);
        int[] starts = { 1, 4, 7, 10 };
        for (int q = 0; q < 4; q++)
        {
            var (x0, x1, _, _) = Quad(v, q, 20);
            Assert.Equal(starts[q], x0, 3);
            Assert.Equal(starts[q] + 2, x1, 3);
        }
        // Colour (fake: surrogate text) clusters are drawn white: intrinsic colours.
        Assert.All(v, vertex => Assert.Equal((255, 255, 255), (vertex.R, vertex.G, vertex.B)));
    }

    [Fact]
    public void Monochrome_cluster_is_tinted_with_the_lead_foreground_and_block_cursor_inverts_it()
    {
        ScreenBuffer screen = Blank(6, 1);
        PutCluster(screen, 1, 0, Kan, 2, 0x4E);
        PutCluster(screen, 3, 0, EAcute, 1, 0x1A);
        var atlas = new ClusterAtlas(CellW, CellH);
        TerminalGpuPipeline.GlyphVertex[] v = BuildClusters(screen, atlas, new FakeSource());
        Assert.Equal(12, v.Length);
        uint yellow = Palette[0xE], green = Palette[0xA], red = Palette[0x4];
        Assert.Equal(((byte)(yellow >> 16), (byte)(yellow >> 8), (byte)yellow), (v[0].R, v[0].G, v[0].B));
        Assert.Equal(((byte)(green >> 16), (byte)(green >> 8), (byte)green), (v[6].R, v[6].G, v[6].B));
        var (x0, x1, _, _) = Quad(v, 1, 6);
        Assert.Equal((3f, 4f), (MathF.Round(x0, 3), MathF.Round(x1, 3)));

        v = BuildClusters(screen, atlas, new FakeSource(), cx: 1, cy: 0, cursor: 100);
        Assert.Equal(((byte)(red >> 16), (byte)(red >> 8), (byte)red), (v[0].R, v[0].G, v[0].B));
    }

    [Fact]
    public void Wide_cluster_cut_at_the_right_edge_or_without_its_trail_is_clipped_not_squeezed()
    {
        ScreenBuffer screen = Blank(4, 2);
        PutCluster(screen, 3, 0, Kan, 2, 0x07, trails: 0);    // last column: no room for the trail
        PutCluster(screen, 0, 1, Ji, 2, 0x07, trails: 0);     // trail overwritten by an ordinary cell
        screen.SetChar(1, 1, new TScreenChar('x', (TColorAttr)0x07));
        var atlas = new ClusterAtlas(CellW, CellH);
        TerminalGpuPipeline.GlyphVertex[] v = BuildClusters(screen, atlas, new FakeSource());
        Assert.Equal(12, v.Length);
        for (int q = 0; q < 2; q++)
        {
            var (x0, x1, u0, u1) = Quad(v, q, 4);
            Assert.Equal(1f, x1 - x0, 3);                          // one cell on screen
            Assert.Equal((float)CellW / atlas.Size, u1 - u0, 5);  // the left half of the two-cell raster
        }
    }

    [Fact]
    public void Orphan_trail_at_the_left_edge_draws_nothing()
    {
        // The core never leaves an orphan trail on screen (a cluster cut at a left edge is blanked); the renderer
        // must still not invent a glyph for one.
        ScreenBuffer screen = Blank(4, 1);
        screen.SetChar(0, 0, TScreenChar.CreateTrail((TColorAttr)0x07));
        PutCluster(screen, 1, 0, Kan, 2, 0x07);
        var source = new FakeSource();
        TerminalGpuPipeline.GlyphVertex[] v = BuildClusters(screen, new ClusterAtlas(CellW, CellH), source);
        Assert.Equal(6, v.Length);
        Assert.Equal(1f, Quad(v, 0, 4).X0, 3);
        Assert.Equal(new[] { Kan }, source.Requests);
    }

    [Fact]
    public void Glyph_pass_skips_leads_only_when_the_cluster_pass_draws_them()
    {
        ScreenBuffer screen = Blank(4, 1);
        screen.SetChar(0, 0, new TScreenChar('A', (TColorAttr)0x07));
        PutCluster(screen, 1, 0, Kan, 2, 0x07);
        using var atlas = new TerminalAtlas(CellW, CellH);
        var bg = new TerminalGpuPipeline.BgVertex[(4 + 1) * 6];
        var glyphs = new TerminalGpuPipeline.GlyphVertex[4 * 6];
        int bgCount, with, without;
        fixed (TerminalGpuPipeline.BgVertex* pb = bg)
        fixed (TerminalGpuPipeline.GlyphVertex* pg = glyphs)
        {
            TerminalGpuPipeline.BuildVertices(pb, pg, screen, 4, 1, CellW, CellH, -1, -1, 0, new GlyphSource(), atlas,
                out bgCount, out without, 2, Palette, skipLeads: true);
            TerminalGpuPipeline.BuildVertices(pb, pg, screen, 4, 1, CellW, CellH, -1, -1, 0, new GlyphSource(), atlas,
                out bgCount, out with, 2, Palette, skipLeads: false);
        }
        Assert.Equal(4 * 6, bgCount);   // every cell, trails included, keeps its background
        Assert.Equal(6, without);       // 'A' only
        Assert.Equal(12, with);         // 'A' and the lead's fallback character (the pre-TV13m behaviour)
    }

    [Fact]
    public void Frame_needing_more_than_the_free_space_evicts_and_draws_everything()
    {
        var atlas = new ClusterAtlas(CellW, CellH, size: 64);   // 16 two-cell slots
        var source = new FakeSource();
        ScreenBuffer first = Blank(24, 1), second = Blank(24, 1);
        for (int i = 0; i < 12; i++) PutCluster(first, (uint)(i * 2), 0, Cjk(i), 2, 0x07);
        for (int i = 0; i < 12; i++) PutCluster(second, (uint)(i * 2), 0, Cjk(100 + i), 2, 0x07);

        Assert.Equal(72, BuildClusters(first, atlas, source).Length);
        Assert.Equal(0, atlas.Generation);
        Assert.Equal(72, BuildClusters(second, atlas, source).Length);   // 12 new > 4 free: evict, rebuild
        Assert.Equal(1, atlas.Generation);
        Assert.Equal(0, atlas.DroppedClusters);
        Assert.Equal(72, BuildClusters(first, atlas, source).Length);
        Assert.Equal(2, atlas.Generation);
    }

    [Fact]
    public void Frame_with_more_clusters_than_the_atlas_holds_counts_the_excess()
    {
        var atlas = new ClusterAtlas(CellW, CellH, size: 64);
        var source = new FakeSource();
        ScreenBuffer screen = Blank(40, 1);
        for (int i = 0; i < 20; i++) PutCluster(screen, (uint)(i * 2), 0, Cjk(i), 2, 0x07);
        Assert.Equal(16 * 6, BuildClusters(screen, atlas, source).Length);
        Assert.Equal(4, atlas.DroppedClusters);
    }

    [Fact]
    public void Warm_cluster_vertex_build_does_not_allocate()
    {
        ScreenBuffer screen = Blank(40, 4);
        for (uint row = 0; row < 4; row++) Write(screen, row, 0, Sample1 + Sample2 + Sample3, (byte)(0x10 + row));
        var atlas = new ClusterAtlas(CellW, CellH);
        var source = new FakeSource();
        var v = new TerminalGpuPipeline.GlyphVertex[40 * 4 * 6];
        fixed (TerminalGpuPipeline.GlyphVertex* p = v)
        {
            TerminalGpuPipeline.BuildClusterVertices(p, v.Length, screen, 40, 4, CellW, CellH, -1, -1, 0, source, atlas, Palette);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++)
                TerminalGpuPipeline.BuildClusterVertices(p, v.Length, screen, 40, 4, CellW, CellH, -1, -1, 0, source, atlas, Palette);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.Equal(7, source.Calls);   // 4 emoji, e+acute and two ideographs, rasterised once each
    }

    // ── Glyph atlas eviction (formerly a silent drop) ────────────────────────

    [Fact]
    public void Full_glyph_atlas_evicts_and_rebuilds_instead_of_dropping()
    {
        const int big = 300;   // 4096 / 300 = 13 -> 169 slots
        using var atlas = new TerminalAtlas(big, big);
        Assert.Equal(169, atlas.Capacity);
        var source = new BigGlyphSource(big);
        ScreenBuffer first = Blank(150, 1), second = Blank(150, 1), huge = Blank(200, 1);
        for (uint i = 0; i < 150; i++)
        {
            first.SetChar(i, 0, new TScreenChar((char)(0x100 + i), (TColorAttr)0x07));
            second.SetChar(i, 0, new TScreenChar((char)(0x300 + i), (TColorAttr)0x07));
        }
        for (uint i = 0; i < 200; i++) huge.SetChar(i, 0, new TScreenChar((char)(0x500 + i), (TColorAttr)0x07));

        Assert.Equal(150 * 6, BuildGlyphs(first, atlas, source, big));
        Assert.Equal(150 * 6, BuildGlyphs(second, atlas, source, big));   // 150 new > 19 free: evict, rebuild
        Assert.Equal(1, atlas.Evictions);
        Assert.Equal(0, atlas.DroppedGlyphs);

        Assert.Equal(169 * 6, BuildGlyphs(huge, atlas, source, big));    // more than the atlas holds: counted
        Assert.Equal(31, atlas.DroppedGlyphs);
    }

    private sealed class BigGlyphSource(int size) : IGpuGlyphSource
    {
        public GpuGlyphKey CreateKey(char ch, GlyphPhase phase) => GpuGlyphKey.PhaseIndependent(ch);
        public byte[] GetAlpha(GpuGlyphKey key) => new byte[size * size];
    }

    private static int BuildGlyphs(ScreenBuffer screen, TerminalAtlas atlas, IGpuGlyphSource source, int cell)
    {
        uint cols = screen.Width;
        var bg = new TerminalGpuPipeline.BgVertex[(cols + 1) * 6];
        var glyphs = new TerminalGpuPipeline.GlyphVertex[cols * 6];
        int glyphCount;
        fixed (TerminalGpuPipeline.BgVertex* pb = bg)
        fixed (TerminalGpuPipeline.GlyphVertex* pg = glyphs)
            TerminalGpuPipeline.BuildVertices(pb, pg, screen, cols, 1, cell, cell, -1, -1, 0, source, atlas,
                out _, out glyphCount, 2, Palette, skipLeads: true);
        return glyphCount;
    }
}
