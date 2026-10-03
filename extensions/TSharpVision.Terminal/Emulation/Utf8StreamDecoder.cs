namespace TSharpVision.Terminal;

/// <summary>
/// An incremental UTF-8 decoder whose state survives between chunks, so a character split across reads decodes exactly
/// as if it had arrived in one. Malformed input becomes U+FFFD per the WHATWG algorithm (one replacement per maximal
/// invalid subpart) and decoding resumes at the next byte; no more than three bytes of state are ever held.
/// </summary>
internal struct Utf8StreamDecoder
{
    public const int Replacement = 0xFFFD;

    private int _codePoint;
    private int _needed;
    private int _seen;
    private int _lower;
    private int _upper;

    /// <summary>Whether a multi-byte character has started and not yet completed.</summary>
    public readonly bool HasPending => _needed != 0;

    /// <summary>
    /// Takes one byte and writes the code points it completes to <paramref name="output"/> (at most two: a replacement
    /// for an interrupted sequence, then the byte itself decoded afresh). Returns how many were written.
    /// </summary>
    public int Push(byte value, Span<int> output)
    {
        if (_needed == 0) return Start(value, output, 0);

        if (value < _lower || value > _upper)
        {
            Reset();
            output[0] = Replacement;
            return Start(value, output, 1);
        }

        _lower = 0x80;
        _upper = 0xBF;
        _codePoint = (_codePoint << 6) | (value & 0x3F);
        if (++_seen < _needed) return 0;

        output[0] = _codePoint;
        Reset();
        return 1;
    }

    /// <summary>Discards a pending partial character.</summary>
    public void Reset()
    {
        _codePoint = 0;
        _needed = 0;
        _seen = 0;
        _lower = 0x80;
        _upper = 0xBF;
    }

    private int Start(byte value, Span<int> output, int written)
    {
        _lower = 0x80;
        _upper = 0xBF;
        switch (value)
        {
            case <= 0x7F:
                output[written] = value;
                return written + 1;
            case >= 0xC2 and <= 0xDF:
                _needed = 1;
                _codePoint = value & 0x1F;
                return written;
            case >= 0xE0 and <= 0xEF:
                if (value == 0xE0) _lower = 0xA0;
                else if (value == 0xED) _upper = 0x9F;
                _needed = 2;
                _codePoint = value & 0x0F;
                return written;
            case >= 0xF0 and <= 0xF4:
                if (value == 0xF0) _lower = 0x90;
                else if (value == 0xF4) _upper = 0x8F;
                _needed = 3;
                _codePoint = value & 0x07;
                return written;
            default:
                output[written] = Replacement;
                return written + 1;
        }
    }
}
