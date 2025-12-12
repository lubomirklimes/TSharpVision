namespace TSharpVision;

/// One paragraph of help text. Mirrors upstream <c>TParagraph</c>.
/// Linked-list node — <see cref="next"/> chains paragraphs inside a topic.
public sealed class TParagraph
{
    public TParagraph next;
    public bool wrap;
    public ushort size;
    public byte[] text;
}
