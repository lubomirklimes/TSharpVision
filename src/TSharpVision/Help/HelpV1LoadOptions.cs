using TSharpVision.Text;

namespace TSharpVision;

/// <summary>Encoding options for reading legacy single-byte help files.</summary>
public sealed class HelpV1LoadOptions
{
    /// <summary>Single-byte encoding used to decode version 1 help text; defaults to Latin-1.</summary>
    public ILegacyTextEncoding LegacyEncoding { get; set; } = LegacyTextEncodings.Latin1;
}
