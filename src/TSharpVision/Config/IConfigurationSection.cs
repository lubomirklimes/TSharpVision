namespace TSharpVision.Config;

/// <summary>
/// Typed configuration section owned by a specific driver or module.
/// Implementations live in their own assembly and are registered at runtime
/// via <see cref="TSharpVision.Drivers.ScreenDriverFactory.RegisterConfigSection{T}"/>.
/// </summary>
public interface IConfigurationSection
{
    /// <summary>INI section name in .cfg files, e.g. "sdl".</summary>
    string SectionName { get; }

    /// <summary>Populates this instance from the raw key/value pairs of the section.</summary>
    void Bind(IReadOnlyDictionary<string, string> rawValues);
}
