namespace TSharpVision.Config;

/// <summary>
/// Runtime configuration for TSharpVision loaded from .cfg files.
///
/// Core sections (<see cref="Driver"/>, <see cref="Graphics"/>, <see cref="Localization"/>)
/// are typed and owned by the core assembly.
///
/// Driver-specific sections (e.g. <c>[sdl]</c>) are exposed as raw key/value pairs
/// via <see cref="RawSections"/> and bound to typed objects through
/// <see cref="TSharpVision.Drivers.ScreenDriverFactory.RegisterConfigSection{T}"/>.
/// </summary>
public sealed class TSharpVisionConfiguration
{
    /// <summary>Typed <c>[driver]</c> section — used by core to select the screen driver.</summary>
    public DriverConfiguration Driver { get; init; } = new();

    /// <summary>Typed <c>[graphics]</c> section — font settings shared by all graphical drivers.</summary>
    public GraphicsDriverConfiguration Graphics { get; init; } = new();

    /// <summary>Typed <c>[localization]</c> section.</summary>
    public LocalizationConfiguration Localization { get; init; } = new();

    /// <summary>
    /// All raw sections parsed from the .cfg file (case-insensitive section and key names).
    /// Driver assemblies use this to bind their own <see cref="IConfigurationSection"/> instances.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> RawSections { get; init; }
        = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the raw key/value pairs for <paramref name="section"/>, or null if absent.</summary>
    public IReadOnlyDictionary<string, string>? GetRawSection(string section)
        => RawSections.TryGetValue(section, out var s) ? s : null;
}
