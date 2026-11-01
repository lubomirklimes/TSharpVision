using System.Reflection;
using TSharpVision.Drivers.SDL.Gpu;
using TSharpVision.Drivers.SDL.Rendering.GlyphComposition;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// TV12i: the Green physical palette keeps the logical Color palette's accelerators visible, as Amber does since
/// TV12g. The attribute pairs are the ones TVDemo actually composes (pinned by TSharpVision.Tests:
/// TVDemoAcceleratorAttributeTests): 0x74 beside 0x70 (menu bar, menus, status line), 0x24 beside 0x20 (selected row),
/// 0x7E beside 0x70 (dialog label), 0x2E beside 0x20/0x2B/0x2F (buttons), disabled 0x78 and 0x28.
/// Contrast is the WCAG ratio of sRGB relative luminance (artifacts/qualification/tv12i/green_eval.py).
/// </summary>
public sealed class GreenAcceleratorContrastTests
{
    private static readonly GpuPhysicalPalette Green = GpuPhysicalPalette.All.Single(p => p.Id == "Green");

    // The TV12e Green table: the plain luma ramp of #33FF33, before the highlight entries were retuned.
    private static readonly uint[] PlainRamp = BuildPlainRamp();

    private static uint[] BuildPlainRamp()
    {
        var t = new uint[16];
        t[0] = 0xFF000000;
        for (int i = 1; i < 16; i++)
        {
            double y = 0.12 + 0.88 * Luma601(GpuPhysicalPaletteTests.LegacyVga16[i]);
            uint rb = (uint)Math.Round(0x33 * y, MidpointRounding.AwayFromZero);
            uint g = (uint)Math.Round(0xFF * y, MidpointRounding.AwayFromZero);
            t[i] = 0xFF000000u | (rb << 16) | (g << 8) | rb;
        }
        return t;
    }

    private static double Luma601(uint argb) =>
        (0.299 * ((argb >> 16) & 0xFF) + 0.587 * ((argb >> 8) & 0xFF) + 0.114 * (argb & 0xFF)) / 255.0;

    private static double Linear(uint channel)
    {
        double v = channel / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(uint argb) =>
        0.2126 * Linear((argb >> 16) & 0xFF) + 0.7152 * Linear((argb >> 8) & 0xFF) + 0.0722 * Linear(argb & 0xFF);

    private static double Contrast(uint a, uint b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Fg(IReadOnlyList<uint> p, int attr, int other) => Contrast(p[attr & 0x0F], p[other & 0x0F]);
    private static double OnBg(IReadOnlyList<uint> p, int attr) => Contrast(p[attr & 0x0F], p[attr >> 4]);

    [Fact]
    public void The_plain_ramp_oracle_matches_the_TV12e_Green_pin()
    {
        Assert.Equal(new uint[]
        {
            0xFF000000, 0xFF0A300A, 0xFF187618, 0xFF1B871B, 0xFF0F4B0F, 0xFF125C12, 0xFF187718, 0xFF24B424,
            0xFF156915, 0xFF187A18, 0xFF27C127, 0xFF2AD22A, 0xFF1E961E, 0xFF21A721, 0xFF30EE30, 0xFF33FF33,
        }, PlainRamp);
    }

    [Theory]
    [InlineData(0x74, 0x70)] // menu bar / menu / status line accelerator beside normal text
    [InlineData(0x24, 0x20)] // the same on the selected row
    public void MenuAcceleratorsStandOutFromTheTextBesideThem(int accelerator, int text)
    {
        double before = Fg(PlainRamp, accelerator, text), after = Fg(Green.Argb, accelerator, text);
        Assert.True(before < 2.1, $"precondition: TV12e ratio was {before:F2}");
        Assert.True(after >= 7, $"0x{accelerator:X2} vs 0x{text:X2}: {after:F2}");
        Assert.True(Luminance(Green.Argb[accelerator & 0x0F]) > Luminance(Green.Argb[accelerator >> 4]),
            "the accelerator is luminous: brighter than its background, black text is darker");
    }

    [Fact]
    public void AcceleratorsStayLegibleOnLightAndSelectedBars()
    {
        // Green's light bar (#24B424, L 0.331) is brighter than Amber's, so the light-on-light ceiling is lower:
        // documented threshold 2.2 on the light bar (actual 2.24), 4.5 on the selected bar (actual 4.69).
        Assert.True(OnBg(Green.Argb, 0x74) >= 2.2, $"0x74 {OnBg(Green.Argb, 0x74):F2}");
        Assert.True(OnBg(Green.Argb, 0x24) >= 4.5, $"0x24 {OnBg(Green.Argb, 0x24):F2}");
        Assert.True(OnBg(Green.Argb, 0x24) > OnBg(PlainRamp, 0x24)); // 1.79 before
    }

    [Theory]
    [InlineData(0x7E, 0x70)] // dialog label shortcut
    [InlineData(0x2E, 0x20)] // button shortcut, normal button
    [InlineData(0x2E, 0x2B)] // button shortcut, default button
    [InlineData(0x2E, 0x2F)] // button shortcut, focused button (1.16 -> 1.06; Default VGA yellow vs white is 1.07)
    public void DialogMnemonicsDoNotGetWorse(int mnemonic, int text)
    {
        Assert.True(Fg(Green.Argb, mnemonic, text) >= Fg(PlainRamp, mnemonic, text) - 0.25,
            $"0x{mnemonic:X2} vs 0x{text:X2}: {Fg(PlainRamp, mnemonic, text):F2} -> {Fg(Green.Argb, mnemonic, text):F2}");
        Assert.True(OnBg(Green.Argb, mnemonic) >= OnBg(PlainRamp, mnemonic));
    }

    [Theory]
    [InlineData(0x70)] [InlineData(0x20)] [InlineData(0x78)] [InlineData(0x28)] [InlineData(0x2B)] [InlineData(0x17)]
    [InlineData(0x30)] [InlineData(0x71)] [InlineData(0x72)]
    public void TextBackgroundsAndDisabledEntriesAreUnchanged(int attr)
    {
        Assert.Equal(PlainRamp[attr & 0x0F], Green.Argb[attr & 0x0F]);
        Assert.Equal(PlainRamp[attr >> 4], Green.Argb[attr >> 4]);
    }

    [Fact]
    public void OnlyTheFourHighlightEntriesChanged()
    {
        for (int i = 0; i < 16; i++)
        {
            if (GpuPhysicalPaletteTests.GreenHighlightIndices.Contains(i)) Assert.NotEqual(PlainRamp[i], Green.Argb[i]);
            else Assert.Equal(PlainRamp[i], Green.Argb[i]);
        }
    }

    [Fact]
    public void DisabledEntriesStayDistinctFromTextAndAccelerators()
    {
        uint disabled = Green.Argb[8], text = Green.Argb[0], accelerator = Green.Argb[4];
        Assert.True(Luminance(text) < Luminance(disabled) && Luminance(disabled) < Luminance(accelerator));
        Assert.True(Contrast(disabled, accelerator) >= 5); // 5.58 (TV12e 1.51: disabled looked like an accelerator)
    }

    [Fact]
    public void HighlightsArePalePhosphorGreenOrderedAndNeverWhite()
    {
        int[] highlights = GpuPhysicalPaletteTests.GreenHighlightIndices;
        foreach (int i in highlights)
        {
            uint c = Green.Argb[i];
            uint r = (c >> 16) & 0xFF, g = (c >> 8) & 0xFF, b = c & 0xFF;
            Assert.Equal(0xFFu, c >> 24);
            Assert.Equal(0xFFu, g);
            Assert.Equal(r, b);                  // equal red and blue: hue exactly the ramp's 120°
            Assert.True(r > 0x33, $"index {i} is paler than the phosphor: 0x{c:X8}");
            Assert.True(r <= 0xC8, $"index {i} is pale green, not near-white: 0x{c:X8}");
            Assert.NotEqual(0xFFFFFFFFu, c);
        }
        for (int k = 1; k < highlights.Length; k++)
            Assert.True(Luminance(Green.Argb[highlights[k - 1]]) < Luminance(Green.Argb[highlights[k]]));
        for (int i = 0; i < 15; i++) Assert.True(Luminance(Green.Argb[15]) > Luminance(Green.Argb[i]));
        // Every ramp entry also has r == b, so the whole table is one hue.
        foreach (uint c in Green.Argb) Assert.Equal((c >> 16) & 0xFF, c & 0xFF);
    }

    [Fact]
    public void BlackWhiteAndMonochromePairsDoNotGetWorse()
    {
        foreach (string table in new[] { TProgram.cpBlackWhite, TProgram.cpMonochrome })
            foreach (int attr in table.Select(c => (int)c).Where(a => a != 0 && (a & 0x0F) != a >> 4).Distinct())
                Assert.True(OnBg(Green.Argb, attr) >= OnBg(PlainRamp, attr) - 1e-9,
                    $"0x{attr:X2}: {OnBg(PlainRamp, attr):F2} -> {OnBg(Green.Argb, attr):F2}");
    }

    [Fact]
    public void ColorPaletteGetsNoNewWeakPairs()
    {
        // Pre-existing weak pairs (< 1.5:1) through the Green ramp: 0x28, 0x38, 0x7A. None may be added; the only
        // pair that gets lower is the menu accelerator on its light bar (3.75 -> 2.24, see above).
        int[] attrs = TProgram.cpColor.Select(c => (int)c).Where(a => a != 0 && (a & 0x0F) != a >> 4).Distinct().ToArray();
        int[] weakBefore = attrs.Where(a => OnBg(PlainRamp, a) < 1.5).OrderBy(a => a).ToArray();
        int[] weakAfter = attrs.Where(a => OnBg(Green.Argb, a) < 1.5).OrderBy(a => a).ToArray();
        Assert.Equal(weakBefore, weakAfter);
        Assert.Equal(attrs.Min(a => OnBg(PlainRamp, a)), attrs.Min(a => OnBg(Green.Argb, a)), 9);
        foreach (int a in attrs.Where(a => a != 0x74))
            Assert.True(OnBg(Green.Argb, a) >= OnBg(PlainRamp, a) - 1e-9,
                $"0x{a:X2}: {OnBg(PlainRamp, a):F2} -> {OnBg(Green.Argb, a):F2}");
    }

    [Fact]
    public void GreenAcceleratorReachesTheVertexStream()
    {
        Assert.Equal(0xFF98FF98u, Green.ArgbSpan[0x74 & 0x0F]);
        Assert.Equal(0xFF24B424u, Green.ArgbSpan[0x74 >> 4]);
        Assert.Equal(0xFFA8FFA8u, Green.ArgbSpan[0x2E & 0x0F]);
        Assert.Equal(0xFF187618u, Green.ArgbSpan[0x2E >> 4]);
    }

    // ─── CPU-compositing fallback: Green → Default → Green, effects ─────────

    private const uint Cols = 4, Rows = 2;

    private static ScreenBuffer AcceleratorScreen()
    {
        // Generated full blocks (U+2588) composite as solid fg, spaces as solid bg: menu, selected row, label, button.
        var screen = new ScreenBuffer((ushort)Cols, (ushort)Rows);
        (char ch, byte attr)[] cells =
        {
            ('█', 0x74), ('█', 0x70), (' ', 0x70), ('█', 0x24),
            ('█', 0x7E), ('█', 0x2E), ('█', 0x2F), (' ', 0x17),
        };
        for (int i = 0; i < cells.Length; i++)
            screen.SetChar((uint)(i % Cols), (uint)(i / Cols), new TScreenChar(cells[i].ch, (TColorAttr)cells[i].attr));
        return screen;
    }

    private static SDLGpuRenderer HeadlessRenderer()
    {
        var renderer = new SDLGpuRenderer(IntPtr.Zero, new SDLGpuOptions());
        typeof(SDLGpuRenderer).GetField("_generatedGlyphs", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(renderer, new CellGlyphBitmapCache(renderer.CellWidth, renderer.CellHeight));
        return renderer;
    }

    private static (byte, byte, byte) Rgb(uint argb) => ((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static (byte, byte, byte) CellCentre(byte[] px, SDLGpuRenderer r, int col, int row)
    {
        int x = col * r.CellWidth + r.CellWidth / 2, y = row * r.CellHeight + r.CellHeight / 2;
        int o = (y * (int)Cols * r.CellWidth + x) * 4;
        return (px[o], px[o + 1], px[o + 2]);
    }

    private static byte[] Effect(byte[] src, SDLGpuRenderer r, Func<byte[], byte[], int, int, bool> apply)
    {
        var dst = new byte[src.Length];
        apply(src, dst, (int)Cols * r.CellWidth, (int)Rows * r.CellHeight);
        return dst;
    }

    private static byte[] WithGlow(byte[] src, SDLGpuRenderer r) =>
        Effect(src, r, (s, d, w, h) => { GpuGlow.ApplyReference(s, d, w, h, 1f); return true; });

    private static byte[] WithShadowMask(byte[] src, SDLGpuRenderer r) =>
        Effect(src, r, (s, d, w, h) => { GpuShadowMask.ApplyReference(s, d, w, h, 1f); return true; });

    [Fact]
    public void Green_to_Default_to_Green_restores_Default_byte_identically()
    {
        ScreenBuffer screen = AcceleratorScreen();
        using var fresh = HeadlessRenderer();
        byte[] baseline = fresh.CompositeForTesting(screen, Cols, Rows).ToArray();
        Assert.Same(GpuPhysicalPalette.Default, fresh.PhysicalPalette);

        using var switched = HeadlessRenderer();
        switched.PhysicalPalette = Green;
        byte[] green1 = switched.CompositeForTesting(screen, Cols, Rows).ToArray();
        switched.PhysicalPalette = GpuPhysicalPalette.Default;
        byte[] restored = switched.CompositeForTesting(screen, Cols, Rows).ToArray();
        switched.PhysicalPalette = Green;
        byte[] green2 = switched.CompositeForTesting(screen, Cols, Rows).ToArray();

        Assert.Equal(baseline, restored);
        Assert.Equal(green1, green2);
        Assert.NotEqual(baseline, green1);

        // Independent oracle: the Default menu accelerator is classic VGA red, the Green one pale phosphor green.
        Assert.Equal(Rgb(GpuPhysicalPaletteTests.LegacyVga16[4]), CellCentre(baseline, fresh, 0, 0));
        Assert.Equal(Rgb(0xFF98FF98), CellCentre(green1, switched, 0, 0));
        Assert.Equal(Rgb(0xFF24B424), CellCentre(green1, switched, 2, 0)); // light bar unchanged
        Assert.Equal(Rgb(0xFF000000), CellCentre(green1, switched, 1, 0)); // black text unchanged
        Assert.Equal(Rgb(0xFFA8FFA8), CellCentre(green1, switched, 1, 1)); // button shortcut
        Assert.Equal(Rgb(0xFFC8FFC8), CellCentre(green1, switched, 2, 1)); // focused caption

        // The Default composite feeds the effect reference functions unchanged after the round trip.
        Assert.Equal(WithGlow(baseline, fresh), WithGlow(restored, switched));
        Assert.Equal(WithShadowMask(baseline, fresh), WithShadowMask(restored, switched));
    }

    [Fact]
    public void Green_highlights_stay_single_hue_under_Glow_and_ShadowMask()
    {
        ScreenBuffer screen = AcceleratorScreen();
        using var renderer = HeadlessRenderer();
        renderer.PhysicalPalette = Green;
        byte[] px = renderer.CompositeForTesting(screen, Cols, Rows).ToArray();
        foreach (byte[] image in new[] { px, WithGlow(px, renderer), WithShadowMask(px, renderer) })
            for (int o = 0; o < image.Length; o += 4)
                Assert.Equal(image[o], image[o + 2]); // r == b everywhere: still the ramp's single green hue
        // Glow only brightens; the highlight stays brighter than its light bar.
        byte[] glow = WithGlow(px, renderer);
        Assert.True(CellCentre(glow, renderer, 0, 0).Item1 > CellCentre(glow, renderer, 2, 0).Item1);
    }
}
