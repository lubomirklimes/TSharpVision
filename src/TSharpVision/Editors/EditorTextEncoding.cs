using System;
using TSharpVision.Text;

namespace TSharpVision;

public enum EditorTextEncodingMode
{
    Auto,
    Utf8,
    Legacy,
}

public sealed class EditorTextEncoding
{
    private EditorTextEncoding(EditorTextEncodingMode mode, ILegacyTextEncoding legacyEncoding)
    {
        Mode = mode;
        LegacyEncoding = legacyEncoding;
    }

    public EditorTextEncodingMode Mode { get; }
    public ILegacyTextEncoding LegacyEncoding { get; }

    public static EditorTextEncoding Auto { get; } =
        new EditorTextEncoding(EditorTextEncodingMode.Auto, null);

    public static EditorTextEncoding Utf8 { get; } =
        new EditorTextEncoding(EditorTextEncodingMode.Utf8, null);

    public static EditorTextEncoding Latin1 { get; } =
        Legacy(LegacyTextEncodings.Latin1);

    public static EditorTextEncoding Legacy(ILegacyTextEncoding encoding)
    {
        if (encoding == null) throw new ArgumentNullException(nameof(encoding));
        return new EditorTextEncoding(EditorTextEncodingMode.Legacy, encoding);
    }
}
