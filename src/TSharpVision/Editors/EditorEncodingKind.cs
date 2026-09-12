namespace TSharpVision;

/// <summary>Encoding actually recognized or used for an editor file.</summary>
public enum EditorEncodingKind
{
    /// <summary>UTF-8 without a byte-order mark.</summary>
    Utf8,
    /// <summary>UTF-8 with a byte-order mark.</summary>
    Utf8Bom,
    /// <summary>One-byte Latin-1 character mapping.</summary>
    Latin1,
    /// <summary>A configured legacy single-byte encoding.</summary>
    Legacy,
}
