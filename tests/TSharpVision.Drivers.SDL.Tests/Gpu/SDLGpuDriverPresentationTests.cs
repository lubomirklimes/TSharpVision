using TSharpVision.Config;
using TSharpVision.Drivers.SDL.Gpu;

namespace TSharpVision.Drivers.SDL.Tests.Gpu;

/// <summary>
/// The SDL GPU driver's physical palette and display effect capabilities, without SDL: discovery,
/// runtime selection rules and <c>[graphics] palette/effect</c> seeding.
/// </summary>
public sealed class SDLGpuDriverPresentationTests
{
    [Fact]
    public void GpuDriverExposesBothCapabilities()
    {
        using var driver = new SDLGpuDriver();
        Assert.IsAssignableFrom<IPhysicalPaletteDriver>(driver);
        Assert.IsAssignableFrom<IDisplayEffectDriver>(driver);
    }

    [Fact]
    public void ListsTheBuiltInPresetsWithDefaultFirstAndStartsAtDefault()
    {
        using var driver = new SDLGpuDriver();
        Assert.Equal(new[] { "Default", "Green", "Amber", "Grayscale" }, driver.PhysicalPalettes);
        Assert.Equal(new[] { "Default", "Scanlines", "Glow", "Curvature", "ShadowMask" }, driver.DisplayEffects);
        Assert.Equal(DisplayPresetNames.Default, driver.PhysicalPalette);
        Assert.Equal(DisplayPresetNames.Default, driver.DisplayEffect);
    }

    [Theory]
    [InlineData("amber", "Amber")]
    [InlineData("  GREEN ", "Green")]
    [InlineData("grayscale", "Grayscale")]
    [InlineData("default", "Default")]
    public void SetPhysicalPaletteMatchesCaseInsensitivelyAndReportsCanonicalId(string requested, string expected)
    {
        using var driver = new SDLGpuDriver();
        driver.SetPhysicalPalette(requested);
        Assert.Equal(expected, driver.PhysicalPalette);
    }

    [Fact]
    public void SetDisplayEffectMatchesCaseInsensitivelyAndCanReturnToDefault()
    {
        using var driver = new SDLGpuDriver();
        driver.SetDisplayEffect(" scanlines");
        Assert.Equal("Scanlines", driver.DisplayEffect);
        driver.SetDisplayEffect("DEFAULT");
        Assert.Equal("Default", driver.DisplayEffect);
    }

    [Theory]
    [InlineData("Glow", "Glow")]
    [InlineData(" curvature ", "Curvature")]
    [InlineData("SHADOWMASK", "ShadowMask")]
    [InlineData("scanlines", "Scanlines")]
    public void EverySupportedEffectCanBeSelectedOneAtATime(string requested, string expected)
    {
        using var driver = new SDLGpuDriver();
        driver.SetDisplayEffect("Scanlines");
        driver.SetDisplayEffect(requested);
        Assert.Equal(expected, driver.DisplayEffect);
        driver.SetDisplayEffect(DisplayPresetNames.Default);
        Assert.Equal("Default", driver.DisplayEffect);
    }

    [Fact]
    public void CorePresetNameConstantsAreTheDriverIds()
    {
        using var driver = new SDLGpuDriver();
        Assert.Equal(new[]
        {
            DisplayPresetNames.Default, DisplayPresetNames.Green, DisplayPresetNames.Amber, DisplayPresetNames.Grayscale,
        }, driver.PhysicalPalettes);
        Assert.Equal(new[]
        {
            DisplayPresetNames.Default, DisplayPresetNames.Scanlines, DisplayPresetNames.Glow,
            DisplayPresetNames.Curvature, DisplayPresetNames.ShadowMask,
        }, driver.DisplayEffects);
    }

    [Theory]
    [InlineData("Custom")]
    [InlineData("Pure Black & White")]
    [InlineData("")]
    [InlineData("MDA")]
    [InlineData("VGA")]
    [InlineData("vga")]
    [InlineData("CGA")]
    [InlineData(" cga ")]
    public void UnknownPaletteThrowsAndKeepsTheActiveOne(string requested)
    {
        using var driver = new SDLGpuDriver();
        driver.SetPhysicalPalette("Green");
        Assert.Throws<ArgumentException>(() => driver.SetPhysicalPalette(requested));
        Assert.Equal("Green", driver.PhysicalPalette);
    }

    [Theory]
    [InlineData("Bloom")]
    [InlineData("Shadow Mask")]
    [InlineData("Glow,Scanlines")]
    [InlineData("VGA")]
    [InlineData(" ")]
    public void UnknownEffectThrowsAndKeepsTheActiveOne(string requested)
    {
        using var driver = new SDLGpuDriver();
        driver.SetDisplayEffect("Scanlines");
        Assert.Throws<ArgumentException>(() => driver.SetDisplayEffect(requested));
        Assert.Equal("Scanlines", driver.DisplayEffect);
    }

    [Fact]
    public void NullSelectionsThrowArgumentNull()
    {
        using var driver = new SDLGpuDriver();
        Assert.Throws<ArgumentNullException>(() => driver.SetPhysicalPalette(null!));
        Assert.Throws<ArgumentNullException>(() => driver.SetDisplayEffect(null!));
        Assert.Equal("Default", driver.PhysicalPalette);
        Assert.Equal("Default", driver.DisplayEffect);
    }

    [Fact]
    public void AbsentConfigurationKeepsDefaults()
    {
        using var driver = new SDLGpuDriver();
        driver.ApplyConfiguredPresentation(null);
        driver.ApplyConfiguredPresentation(new GraphicsDriverConfiguration());
        Assert.Equal("Default", driver.PhysicalPalette);
        Assert.Equal("Default", driver.DisplayEffect);
    }

    [Fact]
    public void ConfigurationSeedsPaletteAndEffect()
    {
        using var driver = new SDLGpuDriver();
        driver.ApplyConfiguredPresentation(new GraphicsDriverConfiguration { Palette = "green", Effect = "Scanlines" });
        Assert.Equal("Green", driver.PhysicalPalette);
        Assert.Equal("Scanlines", driver.DisplayEffect);
    }

    [Fact]
    public void UnknownConfiguredIdsWarnAndKeepDefault()
    {
        using var driver = new SDLGpuDriver();
        var previous = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            driver.ApplyConfiguredPresentation(new GraphicsDriverConfiguration { Palette = "Custom", Effect = "Bloom" });
        }
        finally { Console.SetError(previous); }

        Assert.Equal("Default", driver.PhysicalPalette);
        Assert.Equal("Default", driver.DisplayEffect);
        Assert.Contains("Unknown [graphics] palette 'Custom'", captured.ToString());
        Assert.Contains("Unknown [graphics] effect 'Bloom'", captured.ToString());
    }

    [Theory]
    [InlineData("VGA")]
    [InlineData("CGA")]
    [InlineData(" vga ")]
    public void RemovedVgaAndCgaPalettesInConfigurationWarnAndUseDefault(string configured)
    {
        using var driver = new SDLGpuDriver();
        var previous = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            // Must not throw: a config written for TV12e (VGA/CGA) still starts.
            driver.ApplyConfiguredPresentation(new GraphicsDriverConfiguration { Palette = configured, Effect = "Scanlines" });
        }
        finally { Console.SetError(previous); }

        string log = captured.ToString();
        Assert.Equal("Default", driver.PhysicalPalette);
        Assert.Equal("Scanlines", driver.DisplayEffect);
        Assert.Contains($"Unknown [graphics] palette '{configured}'", log);
        Assert.Contains("using Default", log);
        Assert.DoesNotContain("Unknown [graphics] effect", log);
    }

    [Theory]
    [InlineData("Glow", "Glow")]
    [InlineData("curvature", "Curvature")]
    [InlineData(" ShadowMask ", "ShadowMask")]
    [InlineData("Scanlines", "Scanlines")]
    public void ConfiguredEffectsParse(string configured, string expected)
    {
        using var driver = new SDLGpuDriver();
        var previous = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        try
        {
            driver.ApplyConfiguredPresentation(new GraphicsDriverConfiguration { Effect = configured });
        }
        finally { Console.SetError(previous); }

        Assert.Equal(expected, driver.DisplayEffect);
        Assert.Equal("Default", driver.PhysicalPalette);
        Assert.DoesNotContain("Unknown [graphics]", captured.ToString());
    }

    [Fact]
    public void ApplicationSelectionBeforeInitializeWinsOverConfiguration()
    {
        using var driver = new SDLGpuDriver();
        driver.SetPhysicalPalette("Amber");
        driver.ApplyConfiguredPresentation(new GraphicsDriverConfiguration { Palette = "Green", Effect = "Scanlines" });
        Assert.Equal("Amber", driver.PhysicalPalette);
        Assert.Equal("Scanlines", driver.DisplayEffect);
    }

    [Fact]
    public void ConfigurationIsReadOnceSoResumeKeepsTheRunningChoice()
    {
        using var driver = new SDLGpuDriver();
        var cfg = new GraphicsDriverConfiguration { Palette = "Green" };
        driver.ApplyConfiguredPresentation(cfg);
        driver.SetPhysicalPalette("Default");
        driver.ApplyConfiguredPresentation(cfg);
        Assert.Equal("Default", driver.PhysicalPalette);
    }

    [Fact]
    public void UnsupportedSdlAndNullDriversDoNotExposeTheCapabilities()
    {
        // The classic SDL renderer driver and NullDriver must stay without the optional capability.
        Assert.False(typeof(IPhysicalPaletteDriver).IsAssignableFrom(typeof(SDLDriver)));
        Assert.False(typeof(IDisplayEffectDriver).IsAssignableFrom(typeof(SDLDriver)));
        Assert.False(typeof(IPhysicalPaletteDriver).IsAssignableFrom(typeof(NullDriver)));
        Assert.False(typeof(IDisplayEffectDriver).IsAssignableFrom(typeof(NullDriver)));
    }
}
