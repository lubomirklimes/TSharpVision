using System.Reflection;

namespace TSharpVision.Config;

/// <summary>
/// Loads <see cref="TSharpVisionConfiguration"/> from .cfg files.
///
/// Priority (lowest → highest):
///   1. User config  — <c>~/.tsvision.cfg</c> (all platforms)
///   2. App config   — <c>{AppContext.BaseDirectory}/{AssemblyName}.cfg</c>
///   3. Environment variables — applied by each driver at runtime
///
/// If neither file exists a default (all-null) configuration is returned.
/// A configuration assigned in code to <see cref="Drivers.ScreenDriverFactory.Configuration"/>
/// replaces the loaded one as-is; it is not merged with the files.
/// </summary>
public static class TSharpVisionConfigurationLoader
{
    /// <summary>
    /// Resolves the per-application config file path (next to the binary).
    /// Returns <c>null</c> if the entry assembly name cannot be determined.
    /// </summary>
    public static string? ResolveConfigPath()
    {
        string? assemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
        if (string.IsNullOrEmpty(assemblyName))
            return null;

        return Path.Combine(AppContext.BaseDirectory, assemblyName + ".cfg");
    }

    /// <summary>
    /// Resolves the user-level config file path.
    /// Returns <c>~/.tsvision.cfg</c> on all platforms:
    ///   Windows → <c>%USERPROFILE%\.tsvision.cfg</c>
    ///   Linux/macOS → <c>$HOME/.tsvision.cfg</c>
    /// </summary>
    public static string ResolveUserConfigPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".tsvision.cfg");
    }

    /// <summary>
    /// Loads configuration from both the user config and the app config,
    /// merging them with app-config values taking precedence.
    /// Returns defaults when no files exist.
    /// </summary>
    public static TSharpVisionConfiguration Load()
    {
        // Layer 1: user-level config (lowest priority)
        string userPath = ResolveUserConfigPath();
        var cfg = File.Exists(userPath)
            ? LoadFromPath(userPath)
            : new TSharpVisionConfiguration();

        // Layer 2: app config overrides user config
        string? appPath = ResolveConfigPath();
        if (appPath != null && File.Exists(appPath))
            cfg = Merge(cfg, LoadFromPath(appPath));

        return cfg;
    }

    /// <summary>
    /// Loads configuration from an explicit file path.
    /// Returns defaults when the file does not exist or cannot be read.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The file sets <c>[graphics] fontFit</c> to a value that names no <see cref="FontFitMode"/>.
    /// </exception>
    public static TSharpVisionConfiguration LoadFromPath(string path)
    {
        if (!File.Exists(path))
            return new TSharpVisionConfiguration();

        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> rawSections;
        try
        {
            string text = File.ReadAllText(path);
            var ini = IniConfigurationReader.Parse(text);
            rawSections = ini.GetAllSections();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: failed to read config '{path}': {ex.Message}");
            return new TSharpVisionConfiguration();
        }

        var driverRaw   = rawSections.GetValueOrDefault("driver");
        var graphicsRaw = rawSections.GetValueOrDefault("graphics");
        var locRaw      = rawSections.GetValueOrDefault("localization");

        // An invalid fontFit is an error, not a warning: an explicit choice is never replaced by the default.
        return new TSharpVisionConfiguration
        {
            Driver = new DriverConfiguration
            {
                Name = driverRaw?.GetValueOrDefault("name"),
            },
            Graphics = new GraphicsDriverConfiguration
            {
                FontName = graphicsRaw?.GetValueOrDefault("fontName"),
                FontSize = ParseNullablePositiveInt(graphicsRaw?.GetValueOrDefault("fontSize")),
                ExplicitFontFit = ParseFontFit(graphicsRaw?.GetValueOrDefault("fontFit"), path),
            },
            Localization = new LocalizationConfiguration
            {
                Language = locRaw?.GetValueOrDefault("language"),
            },
            RawSections = rawSections,
        };
    }

    /// <summary>
    /// Merges two configurations. Non-null values in <paramref name="override"/>
    /// win over values in <paramref name="base"/>.
    /// Raw sections are merged per-key within each section.
    /// </summary>
    internal static TSharpVisionConfiguration Merge(
        TSharpVisionConfiguration @base,
        TSharpVisionConfiguration @override) =>
        new TSharpVisionConfiguration
        {
            Driver = new DriverConfiguration
            {
                Name = @override.Driver.Name ?? @base.Driver.Name,
            },
            Graphics = new GraphicsDriverConfiguration
            {
                FontName = @override.Graphics.FontName ?? @base.Graphics.FontName,
                FontSize = @override.Graphics.FontSize ?? @base.Graphics.FontSize,
                ExplicitFontFit = @override.Graphics.ExplicitFontFit ?? @base.Graphics.ExplicitFontFit,
            },
            Localization = new LocalizationConfiguration
            {
                Language = @override.Localization.Language ?? @base.Localization.Language,
            },
            RawSections = MergeRawSections(@base.RawSections, @override.RawSections),
        };

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> MergeRawSections(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> @base,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> @override)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in @base)
            result[kvp.Key] = kvp.Value;

        foreach (var (sectionName, overrideSection) in @override)
        {
            if (result.TryGetValue(sectionName, out var baseSection))
            {
                var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in baseSection)
                    merged[kvp.Key] = kvp.Value;
                foreach (var kvp in overrideSection)
                    merged[kvp.Key] = kvp.Value;
                result[sectionName] = merged;
            }
            else
            {
                result[sectionName] = overrideSection;
            }
        }

        return result;
    }

    private static int? ParseNullablePositiveInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (int.TryParse(value, out int parsed) && parsed > 0)
            return parsed;

        Console.Error.WriteLine(
            $"Warning: ignoring invalid [graphics] fontSize value '{value}'.");
        return null;
    }

    /// <summary>
    /// Parses a <c>[graphics] fontFit</c> value. Missing or blank means "not set" (null), like
    /// <c>fontSize</c>; mode names match case-insensitively with surrounding whitespace ignored.
    /// </summary>
    /// <exception cref="InvalidOperationException">The value names no <see cref="FontFitMode"/>.</exception>
    internal static FontFitMode? ParseFontFit(string? value, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string trimmed = value.Trim();
        if (string.Equals(trimmed, nameof(FontFitMode.Condense), StringComparison.OrdinalIgnoreCase))
            return FontFitMode.Condense;
        if (string.Equals(trimmed, nameof(FontFitMode.Uniform), StringComparison.OrdinalIgnoreCase))
            return FontFitMode.Uniform;

        string where = path is null ? "" : $" in '{path}'";
        throw new InvalidOperationException(
            $"Invalid [graphics] fontFit value '{trimmed}'{where}. Supported values: Condense, Uniform.");
    }
}
