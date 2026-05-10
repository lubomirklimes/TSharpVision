using TSharpVision.Text;

namespace TSharpVision;

public sealed class HelpV1LoadOptions
{
    public ILegacyTextEncoding LegacyEncoding { get; set; } = LegacyTextEncodings.Latin1;
}
