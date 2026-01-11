namespace TSharpVision;

/// One paragraph of help text. Mirrors upstream <c>TParagraph</c>.
/// Linked-list node: <see cref="next"/> chains paragraphs inside a topic.
public sealed class TParagraph
{
    public TParagraph next;
    public bool wrap;
    public ushort size;
    public char[] chars;

    public string Text
    {
        get => chars == null ? string.Empty : new string(chars, 0, size);
        set
        {
            chars = value?.ToCharArray() ?? System.Array.Empty<char>();
            size = checked((ushort)chars.Length);
        }
    }

    // Compatibility shim for old Latin1-oriented tests and callers. Help v2
    // stores chars internally; this property maps legacy byte payloads to the
    // same code-unit values.
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
