using System.Text;

namespace TSharpVision.Text;

/// <summary>
/// Deterministic table-based single-byte encoding. Intended for legacy
/// encodings that should not depend on host code-page availability.
/// </summary>
public sealed class SingleByteTextEncoding : ILegacyTextEncoding
{
    private readonly char[] _byteToChar;
    private readonly Dictionary<char, byte> _charToByte;

    public SingleByteTextEncoding(string name, IReadOnlyList<char> byteToChar)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Encoding name must not be empty.", nameof(name));
        if (byteToChar == null)
            throw new ArgumentNullException(nameof(byteToChar));
        if (byteToChar.Count != 256)
            throw new ArgumentException("Single-byte encoding tables must contain exactly 256 characters.", nameof(byteToChar));

        Name = name;
        _byteToChar = new char[256];
        _charToByte = new Dictionary<char, byte>();

        for (int i = 0; i < 256; i++)
        {
            char ch = byteToChar[i];
            _byteToChar[i] = ch;
            if (!_charToByte.ContainsKey(ch))
                _charToByte.Add(ch, (byte)i);
        }
    }

    public string Name { get; }

    public string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return string.Empty;

        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            chars[i] = DecodeByte(bytes[i]);
        return new string(chars);
    }

    public byte[] Encode(string text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<byte>();

        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            if (!TryEncodeChar(text[i], out bytes[i]))
                throw new EncoderFallbackException(
                    $"Character U+{(int)text[i]:X4} is not representable in legacy encoding '{Name}'.");
        }
        return bytes;
    }

    public char DecodeByte(byte value)
        => _byteToChar[value];

    public bool TryEncodeChar(char ch, out byte value)
        => _charToByte.TryGetValue(ch, out value);
}
