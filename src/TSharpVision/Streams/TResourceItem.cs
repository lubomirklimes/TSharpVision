namespace TSharpVision;

/// One entry in a <see cref="TResourceCollection"/>: the key and the
/// (offset, length) at which the value can be found in the surrounding
/// <see cref="TResourceFile"/>.
public sealed class TResourceItem
{
    public long pos;
    public long size;
    public string key;
}
