using System.Text;

namespace TSharpVision.Text;

/// <summary>
/// Wraps a .NET single-byte <see cref="Encoding"/> behind the legacy encoding
/// contract. Encoding is strict by default so export paths do not silently
/// replace unsupported Unicode characters.
/// </summary>
public sealed class DotNetLegacyTextEncoding : ILegacyTextEncoding
{
    private readonly Encoding _encoding;

    public DotNetLegacyTextEncoding(string name, int codePage)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Encoding name must not be empty.", nameof(name));

        LegacyTextEncodings.EnsureCodePagesRegistered();

        Name = name;
        _encoding = Encoding.GetEncoding(
            codePage,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    }

    public string Name { get; }

    public string Decode(ReadOnlySpan<byte> bytes)
        => _encoding.GetString(bytes);

    public byte[] Encode(string text)
        => _encoding.GetBytes(text ?? string.Empty);

    public char DecodeByte(byte value)
    {
        Span<byte> bytes = stackalloc byte[] { value };
        Span<char> chars = stackalloc char[2];
        int count = _encoding.GetChars(bytes, chars);
        if (count != 1)
            throw new InvalidOperationException(
                $"Encoding '{Name}' did not decode byte 0x{value:X2} to exactly one character.");
        return chars[0];
    }

    public bool TryEncodeChar(char ch, out byte value)
    {
        Span<char> chars = stackalloc char[] { ch };
        Span<byte> bytes = stackalloc byte[2];
        try
        {
            int count = _encoding.GetBytes(chars, bytes);
            if (count == 1)
            {
                value = bytes[0];
                return true;
            }
        }
        catch (EncoderFallbackException)
        {
        }

        value = 0;
        return false;
    }
}
