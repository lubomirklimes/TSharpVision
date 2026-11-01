using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>Pure-table tests for <see cref="GpuPhysicalPalette"/> and <see cref="GpuDisplayEffects"/>.</summary>
public sealed class GpuPhysicalPaletteTests
{
    // Independent copy of the historical TerminalGpuPipeline / SDLGpuRenderer Vga16 table: the classic
    // VGA 16-colour mapping that the Default profile must reproduce byte for byte.
    internal static readonly uint[] LegacyVga16 =
    {
        0xFF000000, 0xFF0000AA, 0xFF00AA00, 0xFF00AAAA,
        0xFFAA0000, 0xFFAA00AA, 0xFFAA5500, 0xFFAAAAAA,
        0xFF555555, 0xFF5555FF, 0xFF55FF55, 0xFF55FFFF,
        0xFFFF5555, 0xFFFF55FF, 0xFFFFFF55, 0xFFFFFFFF,
    };

    public static TheoryData<string> MonoIds => new() { "Green", "Amber", "Grayscale" };

    // Amber draws these as cream highlights (TV12g), outside the plain luma order.
    internal static readonly int[] AmberHighlightIndices = { 4, 12, 14, 15 };

    // Green draws the same indices as pale phosphor-green highlights (TV12i).
    internal static readonly int[] GreenHighlightIndices = { 4, 12, 14, 15 };

    private static bool IsRampEntry(string id, int index) => id switch
    {
        "Amber" => Array.IndexOf(AmberHighlightIndices, index) < 0,
        "Green" => Array.IndexOf(GreenHighlightIndices, index) < 0,
        _       => true,
    };

    private static GpuPhysicalPalette Find(string id)
    {
        Assert.True(GpuPhysicalPalette.TryFind(id, out GpuPhysicalPalette palette));
        return palette;
    }

    private static double Luma(uint argb) =>
        0.299 * ((argb >> 16) & 0xFF) + 0.587 * ((argb >> 8) & 0xFF) + 0.114 * (argb & 0xFF);

    [Fact]
    public void All_lists_the_four_profiles_in_contract_order_with_Default_first()
    {
        Assert.Equal(new[] { "Default", "Green", "Amber", "Grayscale" },
            GpuPhysicalPalette.All.Select(p => p.Id));
        Assert.Same(GpuPhysicalPalette.Default, GpuPhysicalPalette.All[0]);
        Assert.All(GpuPhysicalPalette.All, p => Assert.Equal(16, p.Argb.Count));
        Assert.All(GpuPhysicalPalette.All, p => Assert.Equal(16, p.ArgbSpan.Length));
        Assert.Equal(GpuPhysicalPalette.All.Count, GpuPhysicalPalette.All.Select(p => p.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("Default", "Default")]
    [InlineData("default", "Default")]
    [InlineData("  DEFAULT ", "Default")]
    [InlineData("\tgreen\n", "Green")]
    [InlineData("AMBER", "Amber")]
    [InlineData("grayscale", "Grayscale")]
    public void TryFind_is_case_insensitive_and_trimmed(string input, string expected)
    {
        Assert.True(GpuPhysicalPalette.TryFind(input, out GpuPhysicalPalette palette));
        Assert.Equal(expected, palette.Id);
        Assert.Same(GpuPhysicalPalette.All.Single(p => p.Id == expected), palette);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("EGA")]
    [InlineData("VGA")]
    [InlineData("vga")]
    [InlineData("CGA")]
    [InlineData(" cGa ")]
    [InlineData("Gray")]
    [InlineData("Green Amber")]
    [InlineData("0")]
    [InlineData("Scanlines")]
    public void TryFind_rejects_null_blank_and_unknown(string? input)
    {
        Assert.False(GpuPhysicalPalette.TryFind(input, out GpuPhysicalPalette palette));
        Assert.Same(GpuPhysicalPalette.Default, palette);
    }

    [Fact]
    public void Default_equals_the_legacy_table()
    {
        GpuPhysicalPalette palette = Find("Default");
        Assert.Same(GpuPhysicalPalette.Default, palette);
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(LegacyVga16[i], palette.Argb[i]);
            Assert.Equal(LegacyVga16[i], palette.ArgbSpan[i]);
        }
    }

    [Fact]
    public void Default_matches_the_classic_SDL_renderer_table()
    {
        Assert.Equal(SdlPalette.Vga16, GpuPhysicalPalette.Default.Argb);
        Assert.Equal(LegacyVga16, SdlPalette.Vga16);
    }

    [Fact]
    public void Argb_view_is_read_only()
    {
        Assert.False(GpuPhysicalPalette.Default.Argb is uint[]);
        Assert.Throws<NotSupportedException>(() => ((IList<uint>)GpuPhysicalPalette.Default.Argb)[0] = 1);
    }

    [Fact]
    public void Mono_ramps_keep_their_exact_TV12e_values()
    {
        // Pinned tables (TV12e): removing the VGA/CGA profiles must not move a single ramp entry. Amber's
        // highlight indices 4, 12, 14, 15 were retuned deliberately in TV12g (cream accelerators); its other
        // twelve entries keep their TV12e values. Green's same four indices were retuned in TV12i (pale phosphor
        // green; TV12e values 0xFF0F4B0F, 0xFF1E961E, 0xFF30EE30, 0xFF33FF33); its other twelve are TV12e's.
        Assert.Equal(new uint[]
        {
            0xFF000000, 0xFF0A300A, 0xFF187618, 0xFF1B871B, 0xFF98FF98, 0xFF125C12, 0xFF187718, 0xFF24B424,
            0xFF156915, 0xFF187A18, 0xFF27C127, 0xFF2AD22A, 0xFFA0FFA0, 0xFF21A721, 0xFFA8FFA8, 0xFFC8FFC8,
        }, Find("Green").Argb);
        Assert.Equal(new uint[]
        {
            0xFF000000, 0xFF302100, 0xFF765200, 0xFF875E00, 0xFFFFCE6E, 0xFF5C4000, 0xFF775200, 0xFFB47C00,
            0xFF694900, 0xFF7A5500, 0xFFC18500, 0xFFD29100, 0xFFFFD478, 0xFFA77300, 0xFFFFDA84, 0xFFFFE49C,
        }, Find("Amber").Argb);
        Assert.Equal(new uint[]
        {
            0xFF000000, 0xFF303030, 0xFF767676, 0xFF878787, 0xFF4B4B4B, 0xFF5C5C5C, 0xFF777777, 0xFFB4B4B4,
            0xFF696969, 0xFF7A7A7A, 0xFFC1C1C1, 0xFFD2D2D2, 0xFF969696, 0xFFA7A7A7, 0xFFEEEEEE, 0xFFFFFFFF,
        }, Find("Grayscale").Argb);
    }

    [Theory]
    [MemberData(nameof(MonoIds))]
    public void Mono_ramps_keep_black_opaque_and_follow_Default_luma_order(string id)
    {
        GpuPhysicalPalette mono = Find(id);
        Assert.Equal(0xFF000000u, mono.Argb[0]);
        Assert.All(mono.Argb, argb => Assert.Equal(0xFFu, argb >> 24));

        for (int i = 0; i < 16; i++)
            for (int j = 0; j < 16; j++)
                if (IsRampEntry(id, i) && IsRampEntry(id, j) && Luma(LegacyVga16[i]) < Luma(LegacyVga16[j]))
                    Assert.True(Luma(mono.Argb[i]) <= Luma(mono.Argb[j]),
                        $"{id}: index {i} is darker than {j} in Default but not in the ramp");

        Assert.True(Luma(mono.Argb[7]) > Luma(mono.Argb[8]));
        for (int i = 0; i < 15; i++) Assert.True(Luma(mono.Argb[15]) > Luma(mono.Argb[i]));
        for (int i = 1; i < 16; i++) Assert.NotEqual(0xFF000000u, mono.Argb[i]); // dark blue is not black
    }

    [Theory]
    [InlineData("Green", 0x33, 0xFF, 0x33)]
    [InlineData("Amber", 0xFF, 0xB0, 0x00)]
    [InlineData("Grayscale", 0xFF, 0xFF, 0xFF)]
    public void Mono_ramps_match_the_documented_formula(string id, int pr, int pg, int pb)
    {
        GpuPhysicalPalette mono = Find(id);
        for (int i = 1; i < 16; i++)
        {
            if (!IsRampEntry(id, i)) continue;
            double t = 0.12 + 0.88 * (Luma(LegacyVga16[i]) / 255.0);
            uint r = (uint)Math.Round(pr * t, MidpointRounding.AwayFromZero);
            uint g = (uint)Math.Round(pg * t, MidpointRounding.AwayFromZero);
            uint b = (uint)Math.Round(pb * t, MidpointRounding.AwayFromZero);
            Assert.Equal(0xFF000000u | (r << 16) | (g << 8) | b, mono.Argb[i]);
        }
        if (IsRampEntry(id, 15))
            Assert.Equal(0xFF000000u | ((uint)pr << 16) | ((uint)pg << 8) | (uint)pb, mono.Argb[15]);
    }

    [Fact]
    public void Display_effect_ids_and_parsing()
    {
        Assert.Equal(new[] { "Default", "Scanlines", "Glow", "Curvature", "ShadowMask" }, GpuDisplayEffects.All);
        Assert.Equal("Default", GpuDisplayEffects.Id(GpuDisplayEffect.Default));
        Assert.Equal("Scanlines", GpuDisplayEffects.Id(GpuDisplayEffect.Scanlines));
        Assert.Equal("Glow", GpuDisplayEffects.Id(GpuDisplayEffect.Glow));
        Assert.Equal("Curvature", GpuDisplayEffects.Id(GpuDisplayEffect.Curvature));
        Assert.Equal("ShadowMask", GpuDisplayEffects.Id(GpuDisplayEffect.ShadowMask));
        Assert.True(GpuDisplayEffects.TryParse(" scanLINES ", out GpuDisplayEffect fx));
        Assert.Equal(GpuDisplayEffect.Scanlines, fx);
        Assert.True(GpuDisplayEffects.TryParse("DEFAULT", out fx));
        Assert.Equal(GpuDisplayEffect.Default, fx);
        Assert.True(GpuDisplayEffects.TryParse(" glow", out fx));
        Assert.Equal(GpuDisplayEffect.Glow, fx);
        Assert.True(GpuDisplayEffects.TryParse("CURVATURE ", out fx));
        Assert.Equal(GpuDisplayEffect.Curvature, fx);
        Assert.True(GpuDisplayEffects.TryParse("shadowmask", out fx));
        Assert.Equal(GpuDisplayEffect.ShadowMask, fx);
        foreach (string? bad in new[] { null, "", "  ", "1", "Scanline", "CRT", "VGA", "Shadow Mask", "Glow,Scanlines" })
        {
            Assert.False(GpuDisplayEffects.TryParse(bad, out fx));
            Assert.Equal(GpuDisplayEffect.Default, fx);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuDisplayEffects.Id((GpuDisplayEffect)42));
    }
}
