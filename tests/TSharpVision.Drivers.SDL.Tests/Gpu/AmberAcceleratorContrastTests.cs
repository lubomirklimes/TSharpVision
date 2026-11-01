using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// TV12g: the Amber physical palette keeps the logical Color palette's accelerators visible. The attribute pairs
/// are the ones TVDemo actually composes (pinned by TSharpVision.Tests: TVDemoAcceleratorAttributeTests): menu bar,
/// menus and status line draw accelerators as 0x74 (red on light gray) beside 0x70 text, selected rows 0x24 beside
/// 0x20, dialog labels 0x7E beside 0x70, buttons 0x2E beside 0x20/0x2B/0x2F, disabled entries 0x78 and 0x28.
/// Contrast is the WCAG ratio of sRGB relative luminance.
/// </summary>
public sealed class AmberAcceleratorContrastTests
{
    private static readonly GpuPhysicalPalette Amber = GpuPhysicalPalette.All.Single(p => p.Id == "Amber");

    // The TV12e Amber table: the plain luma ramp, before the highlight entries were retuned.
    private static readonly uint[] PlainRamp = BuildPlainRamp();

    private static uint[] BuildPlainRamp()
    {
        var t = new uint[16];
        t[0] = 0xFF000000;
        for (int i = 1; i < 16; i++)
        {
            double y = 0.12 + 0.88 * (Luma601(GpuPhysicalPaletteTests.LegacyVga16[i]));
            uint r = (uint)Math.Round(0xFF * y, MidpointRounding.AwayFromZero);
            uint g = (uint)Math.Round(0xB0 * y, MidpointRounding.AwayFromZero);
            t[i] = 0xFF000000u | (r << 16) | (g << 8);
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

    private static double Hue(uint argb)
    {
        double r = ((argb >> 16) & 0xFF) / 255.0, g = ((argb >> 8) & 0xFF) / 255.0, b = (argb & 0xFF) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        if (max == min) return double.NaN;
        double h = max == r ? (g - b) / (max - min) : max == g ? 2 + (b - r) / (max - min) : 4 + (r - g) / (max - min);
        return (h * 60 + 360) % 360;
    }

    [Theory]
    [InlineData(0x74, 0x70)] // menu bar / menu / status line accelerator beside normal text
    [InlineData(0x24, 0x20)] // the same on the selected row
    public void MenuAcceleratorsStandOutFromTheTextBesideThem(int accelerator, int text)
    {
        double before = Fg(PlainRamp, accelerator, text), after = Fg(Amber.Argb, accelerator, text);
        Assert.True(before < 2, $"precondition: TV12e ratio was {before:F2}");
        Assert.True(after >= 7, $"0x{accelerator:X2} vs 0x{text:X2}: {after:F2}");
        Assert.True(Luminance(Amber.Argb[accelerator & 0x0F]) > Luminance(Amber.Argb[accelerator >> 4]),
            "the accelerator is luminous: brighter than its background, black text is darker");
    }

    [Fact]
    public void AcceleratorsStayLegibleOnLightAndDarkBars()
    {
        Assert.True(OnBg(Amber.Argb, 0x74) >= 2.4, $"0x74 {OnBg(Amber.Argb, 0x74):F2}");  // light menu bar
        Assert.True(OnBg(Amber.Argb, 0x24) >= 4.5, $"0x24 {OnBg(Amber.Argb, 0x24):F2}");  // darker selected bar
    }

    [Theory]
    [InlineData(0x7E, 0x70)] // dialog label shortcut
    [InlineData(0x2E, 0x20)] // button shortcut, normal button
    [InlineData(0x2E, 0x2B)] // button shortcut, default button
    [InlineData(0x2E, 0x2F)] // button shortcut, focused button
    public void DialogMnemonicsDoNotGetWorse(int mnemonic, int text)
    {
        Assert.True(Fg(Amber.Argb, mnemonic, text) >= Fg(PlainRamp, mnemonic, text) - 0.25,
            $"0x{mnemonic:X2} vs 0x{text:X2}: {Fg(PlainRamp, mnemonic, text):F2} -> {Fg(Amber.Argb, mnemonic, text):F2}");
        Assert.True(OnBg(Amber.Argb, mnemonic) >= OnBg(PlainRamp, mnemonic));
    }

    [Theory]
    [InlineData(0x70)] [InlineData(0x20)] [InlineData(0x78)] [InlineData(0x28)] [InlineData(0x2B)] [InlineData(0x17)]
    public void TextBackgroundsAndDisabledEntriesAreUnchanged(int attr)
    {
        Assert.Equal(PlainRamp[attr & 0x0F], Amber.Argb[attr & 0x0F]);
        Assert.Equal(PlainRamp[attr >> 4], Amber.Argb[attr >> 4]);
    }

    [Fact]
    public void DisabledEntriesStayDistinctFromTextAndAccelerators()
    {
        uint disabled = Amber.Argb[8], text = Amber.Argb[0], accelerator = Amber.Argb[4];
        Assert.True(Luminance(text) < Luminance(disabled) && Luminance(disabled) < Luminance(accelerator));
        Assert.True(Contrast(disabled, accelerator) >= 4);
    }

    [Fact]
    public void HighlightsAreCreamInTheAmberHueOrderedAndNeverWhite()
    {
        double rampHue = Hue(PlainRamp[7]);
        int[] highlights = GpuPhysicalPaletteTests.AmberHighlightIndices;
        foreach (int i in highlights)
        {
            uint c = Amber.Argb[i];
            Assert.InRange(Hue(c), rampHue - 4, rampHue + 4);
            Assert.NotEqual(0xFFFFFFFFu, c);
            Assert.True((c & 0xFF) < 0xC0, $"index {i} is cream, not near-white: 0x{c:X8}");
            Assert.Equal(0xFFu, c >> 24);
        }
        for (int k = 1; k < highlights.Length; k++)
            Assert.True(Luminance(Amber.Argb[highlights[k - 1]]) < Luminance(Amber.Argb[highlights[k]]));
        for (int i = 0; i < 15; i++) Assert.True(Luminance(Amber.Argb[15]) > Luminance(Amber.Argb[i]));
    }

    [Fact]
    public void BlackWhiteAndMonochromePairsDoNotGetWorse()
    {
        // Every fg/bg pair the BlackWhite and Monochrome application palettes use.
        foreach (string table in new[] { TProgram.cpBlackWhite, TProgram.cpMonochrome })
            foreach (int attr in table.Select(c => (int)c).Where(a => a != 0 && (a & 0x0F) != a >> 4).Distinct())
                Assert.True(OnBg(Amber.Argb, attr) >= OnBg(PlainRamp, attr) - 1e-9,
                    $"0x{attr:X2}: {OnBg(PlainRamp, attr):F2} -> {OnBg(Amber.Argb, attr):F2}");
    }

    [Fact]
    public void AmberAcceleratorReachesTheVertexStream()
    {
        // The table is what BuildVertices uses for the cell colour (GpuPaletteVertexTests covers the plumbing).
        Assert.Equal(0xFFFFCE6Eu, Amber.ArgbSpan[0x74 & 0x0F]);
        Assert.Equal(0xFFB47C00u, Amber.ArgbSpan[0x74 >> 4]);
    }
}
