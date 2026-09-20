using System;
using TSharpVision.Text;

namespace TSharpVision;

/// <summary>Policy used to decode an opened text file.</summary>
public enum EditorTextEncodingMode
{
    /// <summary>Detect the text encoding from file content.</summary>
    Auto,
    /// <summary>Decode the file as UTF-8.</summary>
    Utf8,
    /// <summary>Decode the file using the associated legacy encoding.</summary>
    Legacy,
}

/// <summary>Immutable file-open encoding policy, optionally carrying a legacy codec.</summary>
public sealed class EditorTextEncoding
{
    private EditorTextEncoding(EditorTextEncodingMode mode, ILegacyTextEncoding? legacyEncoding)
    {
        Mode = mode;
        LegacyEncoding = legacyEncoding;
    }

    /// <summary>Decoding policy applied when opening a file.</summary>
    public EditorTextEncodingMode Mode { get; }
    /// <summary>Codec used in Legacy mode; null for automatic or UTF-8 policies.</summary>
    public ILegacyTextEncoding? LegacyEncoding { get; }

    /// <summary>Shared policy requesting automatic encoding detection.</summary>
    public static EditorTextEncoding Auto { get; } =
        new EditorTextEncoding(EditorTextEncodingMode.Auto, null);

    /// <summary>Shared policy requesting UTF-8 decoding.</summary>
    public static EditorTextEncoding Utf8 { get; } =
        new EditorTextEncoding(EditorTextEncodingMode.Utf8, null);

    /// <summary>Shared policy requesting Latin-1 decoding.</summary>
    public static EditorTextEncoding Latin1 { get; } =
        Legacy(LegacyTextEncodings.Latin1);

    /// <summary>Creates a policy using the supplied non-null single-byte codec.</summary>
    public static EditorTextEncoding Legacy(ILegacyTextEncoding encoding)
    {
        if (encoding == null) throw new ArgumentNullException(nameof(encoding));
        return new EditorTextEncoding(EditorTextEncodingMode.Legacy, encoding);
    }
}
