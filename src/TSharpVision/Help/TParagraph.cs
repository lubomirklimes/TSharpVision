namespace TSharpVision;

/// One paragraph of help text. Mirrors upstream <c>TParagraph</c>.
/// Linked-list node: <see cref="next"/> chains paragraphs inside a topic.
public sealed class TParagraph
{
    /// <summary>Next paragraph in the topic, or null at the end of the chain.</summary>
    public TParagraph next;
    /// <summary>Whether the paragraph wraps at the topic's configured width.</summary>
    public bool wrap;
    /// <summary>Number of meaningful UTF-16 code units in the character array, limited to 65535.</summary>
    public ushort size;
    /// <summary>UTF-16 text storage; the first size entries form the paragraph text.</summary>
    public char[] chars;

    /// <summary>Paragraph text; assigning null creates empty storage and assigning more than 65535 code units throws an overflow exception.</summary>
    public string Text
    {
        get => chars == null ? string.Empty : new string(chars, 0, size);
        set
        {
            chars = value?.ToCharArray() ?? System.Array.Empty<char>();
            size = checked((ushort)chars.Length);
        }
    }

    /// <summary>Legacy byte projection of paragraph text; characters above U+00FF become question marks on read, and assigned bytes map directly to code units.</summary>
    public byte[] text
    {
        get
        {
            if (chars == null) return System.Array.Empty<byte>();
            var bytes = new byte[size];
            for (int i = 0; i < size; i++)
                bytes[i] = chars[i] <= 0xFF ? (byte)chars[i] : (byte)'?';
            return bytes;
        }
        set
        {
            if (value == null)
            {
                chars = System.Array.Empty<char>();
                size = 0;
                return;
            }

            chars = new char[value.Length];
            for (int i = 0; i < value.Length; i++)
                chars[i] = (char)value[i];
            size = checked((ushort)value.Length);
        }
    }
}
