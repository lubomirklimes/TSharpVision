using System.Collections.Generic;
using TSharpVision.Text;

namespace TSharpVision;

public sealed class EditorEncodingChoice
{
    public EditorEncodingChoice(string key, string fallback, EditorTextEncoding encoding)
    {
        Key = key;
        Fallback = fallback;
        Encoding = encoding;
    }

    public string Key { get; }
    public string Fallback { get; }
    public EditorTextEncoding Encoding { get; }
    public string Label => TSharpVisionIntl.Get(Key, Fallback);
}

public static class EditorEncodingChoices
{
    public static IReadOnlyList<EditorEncodingChoice> BuiltIn { get; } =
        new[]
        {
            new EditorEncodingChoice("Encoding_Auto", "Auto", EditorTextEncoding.Auto),
            new EditorEncodingChoice("Encoding_UTF8", "UTF-8", EditorTextEncoding.Utf8),
            new EditorEncodingChoice("Encoding_Latin1", "Latin-1", EditorTextEncoding.Latin1),
            new EditorEncodingChoice("Encoding_CP437", "CP437", EditorTextEncoding.Legacy(LegacyTextEncodings.Cp437)),
            new EditorEncodingChoice("Encoding_CP852", "CP852", EditorTextEncoding.Legacy(LegacyTextEncodings.Cp852)),
            new EditorEncodingChoice("Encoding_Windows1250", "Windows-1250", EditorTextEncoding.Legacy(LegacyTextEncodings.Windows1250)),
            new EditorEncodingChoice("Encoding_ISO8859_2", "ISO-8859-2", EditorTextEncoding.Legacy(LegacyTextEncodings.Iso8859_2)),
            new EditorEncodingChoice("Encoding_Kamenicky", "Kamenicky / KEYBCS2", EditorTextEncoding.Legacy(LegacyTextEncodings.Kamenicky)),
        };
}
