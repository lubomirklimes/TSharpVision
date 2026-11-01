using TSharpVision.Drivers;
using Xunit;

namespace TSharpVision.Tests.Drivers;

/// <summary>
/// The optional <see cref="IPhysicalPaletteDriver"/> / <see cref="IDisplayEffectDriver"/> contract (TV12e):
/// canonical preset ids, and the documented Set* semantics exercised through a minimal in-test driver.
/// </summary>
public sealed class DisplayPresetDriverContractTests
{
    [Fact]
    public void DisplayPresetNames_have_the_canonical_ids()
    {
        Assert.Equal("Default", DisplayPresetNames.Default);
        Assert.Equal("Green", DisplayPresetNames.Green);
        Assert.Equal("Amber", DisplayPresetNames.Amber);
        Assert.Equal("Grayscale", DisplayPresetNames.Grayscale);
        Assert.Equal("Scanlines", DisplayPresetNames.Scanlines);
        Assert.Equal("Glow", DisplayPresetNames.Glow);
        Assert.Equal("Curvature", DisplayPresetNames.Curvature);
        Assert.Equal("ShadowMask", DisplayPresetNames.ShadowMask);
    }

    [Fact]
    public void DisplayPresetNames_no_longer_define_VGA_or_CGA()
    {
        string[] names = typeof(DisplayPresetNames).GetFields().Select(f => f.Name).ToArray();
        Assert.Equal(
            new[] { "Default", "Green", "Amber", "Grayscale", "Scanlines", "Glow", "Curvature", "ShadowMask" },
            names);
        Assert.DoesNotContain("Vga", names);
        Assert.DoesNotContain("Cga", names);
    }

    [Fact]
    public void Capabilities_are_separate_from_IDriver()
    {
        Assert.False(typeof(IDriver).IsAssignableFrom(typeof(IPhysicalPaletteDriver)));
        Assert.False(typeof(IDriver).IsAssignableFrom(typeof(IDisplayEffectDriver)));
        Assert.Empty(typeof(IPhysicalPaletteDriver).GetInterfaces());
        Assert.Empty(typeof(IDisplayEffectDriver).GetInterfaces());
    }

    [Fact]
    public void Fake_driver_is_discovered_by_type_test_and_lists_Default_first()
    {
        object driver = new FakePresetDriver();

        Assert.True(driver is IPhysicalPaletteDriver);
        Assert.True(driver is IDisplayEffectDriver);
        var palette = (IPhysicalPaletteDriver)driver;
        var effect = (IDisplayEffectDriver)driver;
        Assert.Equal(DisplayPresetNames.Default, palette.PhysicalPalettes[0]);
        Assert.Equal(DisplayPresetNames.Default, effect.DisplayEffects[0]);
        Assert.Equal(DisplayPresetNames.Default, palette.PhysicalPalette);
        Assert.Equal(DisplayPresetNames.Default, effect.DisplayEffect);
    }

    [Theory]
    [InlineData(" glow", "Glow")]
    [InlineData("CURVATURE ", "Curvature")]
    [InlineData("shadowmask", "ShadowMask")]
    public void Set_effect_matches_case_insensitively(string requested, string canonical)
    {
        var driver = new FakePresetDriver();
        driver.SetDisplayEffect(requested);
        Assert.Equal(canonical, driver.DisplayEffect);
    }

    [Theory]
    [InlineData("amber", "Amber")]
    [InlineData("  GREEN ", "Green")]
    [InlineData("grayscale", "Grayscale")]
    public void Set_matches_case_insensitively_and_reports_canonical_id(string requested, string canonical)
    {
        var driver = new FakePresetDriver();
        driver.SetPhysicalPalette(requested);
        Assert.Equal(canonical, driver.PhysicalPalette);
    }

    [Fact]
    public void Set_null_throws_ArgumentNullException_and_keeps_selection()
    {
        var driver = new FakePresetDriver();
        driver.SetPhysicalPalette(DisplayPresetNames.Amber);
        driver.SetDisplayEffect(DisplayPresetNames.Scanlines);

        Assert.Throws<ArgumentNullException>(() => driver.SetPhysicalPalette(null!));
        Assert.Throws<ArgumentNullException>(() => driver.SetDisplayEffect(null!));
        Assert.Equal("Amber", driver.PhysicalPalette);
        Assert.Equal("Scanlines", driver.DisplayEffect);
    }

    [Fact]
    public void Set_unknown_throws_ArgumentException_and_keeps_selection()
    {
        var driver = new FakePresetDriver();
        driver.SetPhysicalPalette(DisplayPresetNames.Grayscale);

        var ex = Assert.Throws<ArgumentException>(() => driver.SetPhysicalPalette("Plasma"));
        Assert.IsNotType<ArgumentNullException>(ex);
        Assert.Throws<ArgumentException>(() => driver.SetPhysicalPalette("VGA"));
        Assert.Throws<ArgumentException>(() => driver.SetDisplayEffect("Bloom"));
        Assert.Equal("Grayscale", driver.PhysicalPalette);
        Assert.Equal("Default", driver.DisplayEffect);
    }

    /// <summary>Minimal reference implementation of the documented contract.</summary>
    private sealed class FakePresetDriver : IPhysicalPaletteDriver, IDisplayEffectDriver
    {
        public IReadOnlyList<string> PhysicalPalettes { get; } =
        [
            DisplayPresetNames.Default, DisplayPresetNames.Green, DisplayPresetNames.Amber, DisplayPresetNames.Grayscale,
        ];

        public IReadOnlyList<string> DisplayEffects { get; } =
        [
            DisplayPresetNames.Default, DisplayPresetNames.Scanlines, DisplayPresetNames.Glow,
            DisplayPresetNames.Curvature, DisplayPresetNames.ShadowMask,
        ];

        public string PhysicalPalette { get; private set; } = DisplayPresetNames.Default;

        public string DisplayEffect { get; private set; } = DisplayPresetNames.Default;

        public void SetPhysicalPalette(string palette) => PhysicalPalette = Resolve(PhysicalPalettes, palette, nameof(palette));

        public void SetDisplayEffect(string effect) => DisplayEffect = Resolve(DisplayEffects, effect, nameof(effect));

        private static string Resolve(IReadOnlyList<string> ids, string value, string paramName)
        {
            ArgumentNullException.ThrowIfNull(value, paramName);
            string trimmed = value.Trim();
            foreach (string id in ids)
                if (string.Equals(id, trimmed, StringComparison.OrdinalIgnoreCase))
                    return id;
            throw new ArgumentException($"Unknown id '{trimmed}'.", paramName);
        }
    }
}
