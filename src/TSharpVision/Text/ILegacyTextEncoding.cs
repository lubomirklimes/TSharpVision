namespace TSharpVision.Text;

/// <summary>
/// Explicit single-byte legacy text encoding used at import/export boundaries.
/// This is not a renderer or UI code-page setting.
/// </summary>
public interface ILegacyTextEncoding
{
    string Name { get; }
    string Decode(ReadOnlySpan<byte> bytes);
    byte[] Encode(string text);
    char DecodeByte(byte value);
    bool TryEncodeChar(char ch, out byte value);
}
