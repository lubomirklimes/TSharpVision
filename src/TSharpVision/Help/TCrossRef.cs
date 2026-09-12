namespace TSharpVision;

/// <summary>A link from a span of topic text to another help context.</summary>
public sealed class TCrossRef
{
    /// <summary>Destination help context identifier for this link.</summary>
    public int @ref;
    /// <summary>Offset into the concatenated topic text, measured in UTF-16 code units in memory.</summary>
    public int offset;
    /// <summary>Length of the linked text in UTF-16 code units, limited to 255.</summary>
    public byte length;
}
