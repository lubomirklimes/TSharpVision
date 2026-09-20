namespace TSharpVision;

/// One entry in a <see cref="TResourceCollection"/>: the key and the
/// (offset, length) at which the value can be found in the surrounding
/// <see cref="TResourceFile"/>.
public sealed class TResourceItem
{
    /// <summary>Payload byte offset relative to the resource container's BasePos.</summary>
    public long pos;
    /// <summary>Serialized payload length in bytes.</summary>
    public long size;
    /// <summary>Ordinal, case-sensitive identifier used to look up this resource.</summary>
    public string key = string.Empty;
}
