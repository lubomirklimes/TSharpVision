// [graphics] fontFit: how the typed GraphicsDriverConfiguration.FontFit (TW-5A.4) reaches both SDL
// drivers, and the pure Uniform sizing rules (TW-5A.3). Parsing and layering of the .cfg value are
// core tests (TSharpVision.Tests FontFitConfigurationTests). No window or font is opened here; the
// rasteriser and the real renderers are covered by the native-pixel tests (FontFitTests).
using System.Text;
using TSharpVision.Config;
using TSharpVision.Drivers.SDL.Gpu;
using TSharpVision.Drivers.SDL.Rendering.Fonts;

namespace TSharpVision.Drivers.SDL.Tests;

/// <summary>
/// Both SDL drivers take the mode from <see cref="ScreenDriverFactory.Configuration"/>'s typed
/// <see cref="GraphicsDriverConfiguration.FontFit"/>, whether it was loaded from a real cfg file or
/// set in code. The injected first native operation throws, so no window opens.
/// </summary>
[Collection("SDL initialization")]
public sealed class SdlFontFitDriverTests
{
    private sealed class Sentinel : Exception;

    [Theory]
    [InlineData(false, "", FontFitMode.Condense)]
    [InlineData(false, "fontFit=Condense", FontFitMode.Condense)]
    [InlineData(false, "fontFit=Uniform", FontFitMode.Uniform)]
    [InlineData(true, "", FontFitMode.Condense)]
    [InlineData(true, "fontFit=Condense", FontFitMode.Condense)]
    [InlineData(true, "fontFit=uniform", FontFitMode.Uniform)]
    public void Both_drivers_take_the_mode_loaded_from_a_cfg_file(bool gpu, string fitLine, FontFitMode expected)
    {
        using var cfg = new TempCfg($"[graphics]\nfontName=consola.ttf\nfontSize=20\n{fitLine}\n");
        Assert.Equal(expected, InitializeWith(gpu, TSharpVisionConfigurationLoader.LoadFromPath(cfg.Path)));
    }

    [Theory]
    [InlineData(false, FontFitMode.Condense)]
    [InlineData(false, FontFitMode.Uniform)]
    [InlineData(true, FontFitMode.Condense)]
    [InlineData(true, FontFitMode.Uniform)]
    public void Both_drivers_take_a_mode_set_in_code_without_any_cfg_file(bool gpu, FontFitMode mode)
    {
        var configuration = new TSharpVisionConfiguration
        {
            Graphics = new GraphicsDriverConfiguration { FontName = "consola.ttf", FontSize = 20, FontFit = mode },
        };
        Assert.Equal(mode, InitializeWith(gpu, configuration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void No_configuration_at_all_is_Condense(bool gpu) =>
        Assert.Equal(FontFitMode.Condense, InitializeWith(gpu, null));

    [Theory]
    [InlineData(false, FontFitMode.Condense, "Uniform")]
    [InlineData(false, FontFitMode.Uniform, "Condense")]
    [InlineData(true, FontFitMode.Condense, "Uniform")]
    [InlineData(true, FontFitMode.Uniform, "Bogus")]
    public void Neither_driver_reads_the_raw_graphics_section(bool gpu, FontFitMode typed, string raw)
    {
        // A raw [graphics] fontFit that disagrees with the typed value (even an invalid one) is ignored.
        var configuration = new TSharpVisionConfiguration
        {
            Graphics = new GraphicsDriverConfiguration { FontFit = typed },
            RawSections = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["graphics"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["fontFit"] = raw },
            },
        };
        Assert.Equal(typed, InitializeWith(gpu, configuration));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_new_initialization_uses_the_new_configuration(bool gpu)
    {
        using var driver = gpu ? (IDisposable)new SDLGpuDriver() : new SDLDriver();
        WithConfiguration(new TSharpVisionConfiguration { Graphics = new() { FontFit = FontFitMode.Uniform } }, () =>
            Assert.Throws<InvalidOperationException>(() => Initialize(driver, () => throw new Sentinel())));
        Assert.Equal(FontFitMode.Uniform, FontFit(driver));
        WithConfiguration(new TSharpVisionConfiguration { Graphics = new() { FontFit = FontFitMode.Condense } }, () =>
            Assert.Throws<InvalidOperationException>(() => Initialize(driver, () => throw new Sentinel())));
        Assert.Equal(FontFitMode.Condense, FontFit(driver));
    }

    private static FontFitMode InitializeWith(bool gpu, TSharpVisionConfiguration? configuration)
    {
        FontFitMode resolved = default;
        WithConfiguration(configuration, () =>
        {
            using var driver = gpu ? (IDisposable)new SDLGpuDriver() : new SDLDriver();
            var error = Assert.Throws<InvalidOperationException>(() => Initialize(driver, () => throw new Sentinel()));
            Assert.IsType<Sentinel>(error.InnerException);
            resolved = FontFit(driver);
        });
        return resolved;
    }

    private static void WithConfiguration(TSharpVisionConfiguration? configuration, Action body)
    {
        string? noSdl = Environment.GetEnvironmentVariable("TSharpVision_NO_SDL");
        TSharpVisionConfiguration? previous = ScreenDriverFactory.Configuration;
        Environment.SetEnvironmentVariable("TSharpVision_NO_SDL", null);
        try
        {
            ScreenDriverFactory.Configuration = configuration;
            body();
        }
        finally
        {
            ScreenDriverFactory.Configuration = previous;
            Environment.SetEnvironmentVariable("TSharpVision_NO_SDL", noSdl);
        }
    }

    private static void Initialize(IDisposable driver, Action action)
    {
        if (driver is SDLGpuDriver gpu) gpu.Initialize(action);
        else ((SDLDriver)driver).Initialize(action);
    }

    private static FontFitMode FontFit(IDisposable driver) =>
        driver is SDLGpuDriver gpu ? gpu.FontFit : ((SDLDriver)driver).FontFit;

    private sealed class TempCfg : IDisposable
    {
        public TempCfg(string text)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tsv-fontfit-{Guid.NewGuid():N}.cfg");
            File.WriteAllText(Path, text);
        }

        public string Path { get; }

        public void Dispose() { try { File.Delete(Path); } catch (IOException) { } }
    }
}

/// <summary>Pure Uniform sizing rules (<see cref="SdlFontCellPlacement"/>).</summary>
public sealed class SdlUniformFitPolicyTests
{
    [Fact]
    public void Sizes_start_at_the_configured_size_and_go_down_in_whole_points_to_half()
    {
        Assert.Equal([20f, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10], SdlFontCellPlacement.UniformSizes(20).ToArray());
        Assert.Equal([9f, 8, 7, 6, 5], SdlFontCellPlacement.UniformSizes(9).ToArray());
        Assert.All(SdlFontCellPlacement.UniformSizes(14), s => Assert.True(s <= 14, "never enlarged"));
    }

    [Fact]
    public void Text_fits_when_its_widest_ink_and_its_extent_around_one_baseline_fit_the_cell()
    {
        Assert.True(SdlFontCellPlacement.UniformFits(16, 4, 11, 11, 20));   // Consolas 18 pt in 11x20
        Assert.False(SdlFontCellPlacement.UniformFits(16, 4, 12, 11, 20));  // Æ one column too wide (19 pt)
        Assert.False(SdlFontCellPlacement.UniformFits(18, 4, 11, 11, 20));  // Ů 18 rows high (20 pt)
    }

    [Fact]
    public void The_baseline_centres_the_line_box_inside_the_band_the_ink_needs()
    {
        // Consolas 18 pt in 11x20: line box 18 (ascent 14) centred → 15, but ĥ needs 16 rows above.
        Assert.Equal(16, SdlFontCellPlacement.UniformTextBaseline(20, 14, 18, 16, 4));
        // JetBrains Mono 20 pt: the line box fills the cell, so the font keeps its own baseline.
        Assert.Equal(21, SdlFontCellPlacement.UniformTextBaseline(27, 21, 27, 20, 5));
        // Cascadia Mono 17 pt in 12x24: line box 20 (ascent 16) centred → 18, inside 17..19.
        Assert.Equal(18, SdlFontCellPlacement.UniformTextBaseline(24, 16, 20, 17, 5));
        // Nothing fits: the ink above the baseline is kept, the bottom gives way.
        Assert.Equal(12, SdlFontCellPlacement.UniformTextBaseline(14, 10, 14, 12, 4));
    }

    [Fact]
    public void The_repertoire_is_ascii_latin1_letters_and_latin_extended_a_without_compatibility_characters()
    {
        string r = SdlFontCellPlacement.UniformRepertoire;
        Assert.Equal(94 + 62 + 128 - 6, r.Length);
        Assert.Equal(r.Length, r.Distinct().Count());
        Assert.All("AZaz09gjpqy_|@{}ŘÍŠŽŤČĎŮÁÉÓÚÝĚŇÜříšžťčďůáéóúýěňüĥĺľÅÆÇß", c => Assert.Contains(c, r));
        Assert.All("×÷  €►─█", c => Assert.DoesNotContain(c, r));
        Assert.All(SdlFontCellPlacement.UniformExcluded, c => Assert.DoesNotContain(c, r));
    }

    [Fact]
    public void The_excluded_characters_are_exactly_the_compatibility_characters_of_those_ranges()
    {
        var compatibility = new StringBuilder();
        for (char c = 'À'; c <= 'ſ'; c++)
        {
            string s = c.ToString();
            if (char.IsLetter(c) && s.Normalize(NormalizationForm.FormKD) != s.Normalize(NormalizationForm.FormD))
                compatibility.Append(c);
        }
        Assert.Equal(SdlFontCellPlacement.UniformExcluded, compatibility.ToString());
    }

    [Fact]
    public void Every_Czech_letter_lower_and_upper_case_decides_the_common_size()
    {
        // TW-5A.4 regression: the owner-approved repertoire measures the whole Czech alphabet,
        // including carons, acutes, the ring (the tallest accents) and the widest letters.
        const string upper = "AÁBCČDĎEÉĚFGHIÍJKLMNŇOÓPQRŘSŠTŤUÚŮVWXYÝZŽ";
        string lower = upper.ToLowerInvariant();
        Assert.Equal(41, upper.Length);
        Assert.Equal("aábcčdďeéěfghiíjklmnňoópqrřsštťuúůvwxyýzž", lower);
        string r = SdlFontCellPlacement.UniformRepertoire;
        Assert.All(upper + lower, c => Assert.Contains(c, r));
        Assert.All("ŮůĎďŤťŘřÁÉÍÓÚÝŽŠČĚŇgjpqyWMwm", c => Assert.Contains(c, r));
    }

    [Fact]
    public void Rare_compatibility_letters_such_as_l_middle_dot_stay_outside_the_repertoire()
    {
        Assert.Contains('ŀ', SdlFontCellPlacement.UniformExcluded);
        Assert.DoesNotContain('ŀ', SdlFontCellPlacement.UniformRepertoire);
        Assert.Equal("ĲĳĿŀŉſ", SdlFontCellPlacement.UniformExcluded);
    }
}

/// <summary>Checks the compiled shader resource declaration without creating a GPU device.</summary>
public sealed class SdlShaderSamplerContractTests
{
    [Fact]
    public void Only_the_glyph_fragment_shader_declares_one_sampler()
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        Type type = typeof(SDLGpuDriver).Assembly.GetType("TSharpVision.Drivers.SDL.Gpu.TerminalGpuPipeline")!;
        var loader = type.GetMethod("LoadShaderFromSpirv", flags)!;
        var parameter = loader.GetParameters()[2];
        Assert.Equal(typeof(uint), parameter.ParameterType);
        Assert.Equal(0u, parameter.DefaultValue);

        var initialize = type.GetConstructors(flags).Single();
        var instructions = ReadInstructions(initialize);
        var samplers = new Dictionary<string, int>();
        for (int i = 3; i < instructions.Count; i++)
        {
            if (instructions[i].Operand is not System.Reflection.MethodBase called || called != loader) continue;
            string resource = Assert.IsType<string>(instructions[i - 3].Operand);
            var op = instructions[i - 1].Op;
            int count = op == System.Reflection.Emit.OpCodes.Ldc_I4_1 ? 1
                : op == System.Reflection.Emit.OpCodes.Ldc_I4_0 ? 0
                : throw new InvalidOperationException($"Unexpected sampler argument {op}");
            samplers.Add(resource, count);
        }
        Assert.Equal(4, samplers.Count);
        Assert.Equal(1, samplers.Single(x => x.Key.EndsWith("GlyphFrag.spv", StringComparison.Ordinal)).Value);
        Assert.All(samplers.Where(x => !x.Key.EndsWith("GlyphFrag.spv", StringComparison.Ordinal)), x => Assert.Equal(0, x.Value));
        Assert.Contains(ReadInstructions(loader), x => x.Operand is System.Reflection.FieldInfo f && f.Name == "NumSamplers");
    }

    private static List<(System.Reflection.Emit.OpCode Op, object? Operand)> ReadInstructions(System.Reflection.MethodBase method)
    {
        var codes = typeof(System.Reflection.Emit.OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(System.Reflection.Emit.OpCode))
            .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!).ToDictionary(x => unchecked((ushort)x.Value));
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        var result = new List<(System.Reflection.Emit.OpCode, object?)>();
        for (int i = 0; i < il.Length;)
        {
            ushort value = il[i++];
            if (value == 0xFE) value = (ushort)(0xFE00 | il[i++]);
            var op = codes[value];
            object? operand = null;
            int size = op.OperandType switch
            {
                System.Reflection.Emit.OperandType.InlineNone => 0,
                System.Reflection.Emit.OperandType.ShortInlineBrTarget or System.Reflection.Emit.OperandType.ShortInlineI or System.Reflection.Emit.OperandType.ShortInlineVar => 1,
                System.Reflection.Emit.OperandType.InlineVar => 2,
                System.Reflection.Emit.OperandType.InlineI8 or System.Reflection.Emit.OperandType.InlineR => 8,
                System.Reflection.Emit.OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, i) * 4,
                _ => 4,
            };
            if (op.OperandType == System.Reflection.Emit.OperandType.InlineMethod)
                operand = method.Module.ResolveMethod(BitConverter.ToInt32(il, i));
            else if (op.OperandType == System.Reflection.Emit.OperandType.InlineField)
                operand = method.Module.ResolveField(BitConverter.ToInt32(il, i));
            else if (op.OperandType == System.Reflection.Emit.OperandType.InlineString)
                operand = method.Module.ResolveString(BitConverter.ToInt32(il, i));
            result.Add((op, operand));
            i += size;
        }
        return result;
    }
}
