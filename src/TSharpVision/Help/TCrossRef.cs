namespace TSharpVision;

/// Cross-reference inside a help topic. Mirrors upstream <c>TCrossRef</c>:
/// the substring at byte <see cref="offset"/> of the topic text spanning
/// <see cref="length"/> bytes is a hot link to topic id <see cref="@ref"/>.
public sealed class TCrossRef
{
    public int @ref;
    public int offset;
    public byte length;
}
