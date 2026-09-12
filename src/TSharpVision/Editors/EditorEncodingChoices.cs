using System.Collections.Generic;
using TSharpVision.Text;

namespace TSharpVision;

/// <summary>A text-encoding choice with a localized UI label.</summary>
public sealed class EditorEncodingChoice
{
    /// <summary>Associates an encoding with a localization key and fallback label.</summary>
    public EditorEncodingChoice(string key, string fallback, EditorTextEncoding encoding)
    {
        Key = key;
        Fallback = fallback;
        Encoding = encoding;
    }

    /// <summary>Localization key used to resolve the displayed label.</summary>
    public string Key { get; }
    /// <summary>Label used when no translation is available.</summary>
    public string Fallback { get; }
    /// <summary>Text-decoding policy selected by this choice.</summary>
    public EditorTextEncoding Encoding { get; }
    /// <summary>Label resolved through the current localization provider on each access.</summary>
    public string Label => TSharpVisionIntl.Get(Key, Fallback);
}

/// <summary>Built-in text-encoding choices offered by editor dialogs.</summary>
public static class EditorEncodingChoices
{
    /// <summary>Ordered encoding choices; index zero is automatic detection.</summary>
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
