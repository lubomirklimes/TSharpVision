using TSharpVision.Text;

namespace TSharpVision;

public sealed class HelpV1CompileOptions
{
    public ILegacyTextEncoding LegacyEncoding { get; set; } = LegacyTextEncodings.Latin1;
}
