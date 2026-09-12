namespace TSharpVision.Text;

/// <summary>
/// Explicit single-byte legacy text encoding used at import/export boundaries.
/// This is not a renderer or UI code-page setting.
/// </summary>
public interface ILegacyTextEncoding
{
    /// <summary>Stable descriptive identifier used to select the legacy encoding.</summary>
    string Name { get; }
    /// <summary>Decodes legacy bytes into Unicode text according to this encoding's mapping.</summary>
    string Decode(ReadOnlySpan<byte> bytes);
    /// <summary>Encodes Unicode text into legacy bytes; built-in implementations treat null as empty and reject unrepresentable characters.</summary>
    byte[] Encode(string text);
    /// <summary>Decodes one byte into its Unicode character; implementations may reject bytes that do not map to one character.</summary>
    char DecodeByte(byte value);
    /// <summary>Returns whether a UTF-16 character maps to exactly one byte, outputting that byte on success and zero on failure.</summary>
    bool TryEncodeChar(char ch, out byte value);
}
