// TSharpVision Resource Tools
// ResourceEntry — metadata for a single .tvr resource.
// A single entry in the resource index.
namespace TSharpVision.ResourceTools;

/// <summary>
/// Metadata for one resource stored in a <c>.tvr</c> file.
/// </summary>
public sealed class ResourceEntry
{
    /// <summary>Resource key (e.g. "dialog.hello").</summary>
    public string Key { get; init; }

    /// <summary>Byte offset of the payload within the <c>.tvr</c> file (relative to the FBPR header).</summary>
    public long Position { get; init; }

    /// <summary>Payload size in bytes.</summary>
    public long Size { get; init; }

    /// <summary>
    /// Streamable type name extracted from the first few bytes of the payload,
    /// e.g. "TDialog". <c>null</c> if the prefix cannot be parsed.
    /// </summary>
    public string TypeName { get; init; }
}
