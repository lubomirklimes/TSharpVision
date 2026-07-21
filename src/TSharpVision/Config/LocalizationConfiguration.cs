namespace TSharpVision.Config;

/// <summary>
/// Typed representation of the <c>[localization]</c> section in a .cfg file.
/// </summary>
public sealed class LocalizationConfiguration
{
    /// <summary>Two-letter language code, e.g. "cs" or "en". Null = built-in English fallback.</summary>
    public string? Language { get; init; }
}
