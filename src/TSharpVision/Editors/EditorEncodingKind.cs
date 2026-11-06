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
    /// <summary>UTF-16 little-endian with a byte-order mark (FF FE).</summary>
    Utf16LeBom,
    /// <summary>UTF-16 big-endian with a byte-order mark (FE FF).</summary>
    Utf16BeBom,
}
