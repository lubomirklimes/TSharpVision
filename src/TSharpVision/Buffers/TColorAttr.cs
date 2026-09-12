using TSharpVision.Constants;

namespace TSharpVision;

/// <summary>
/// One byte of color attribute matching VGA text mode: foreground in the low
/// nibble, background in the high nibble. Implicitly converts to/from byte
/// and ushort so existing call sites such as <c>moveChar(0, ' ', getColor(1), size.x)</c>
/// keep working as in upstream Turbo Vision.
/// </summary>
public readonly struct TColorAttr : IEquatable<TColorAttr>
{
    private readonly byte _raw;

    /// <summary>Creates a packed attribute with foreground in the low nibble and background in the high nibble.</summary>
    public TColorAttr(byte raw) { _raw = raw; }
    /// <summary>Creates a packed attribute from the low eight bits of the supplied integer.</summary>
    public TColorAttr(int raw) { _raw = (byte)raw; }

    /// <summary>Foreground palette index in the low nibble, from zero through fifteen.</summary>
    public byte Foreground => (byte)(_raw & Colors.fgMask);
    /// <summary>Background bits in their packed high-nibble position; shift right by four to obtain the palette index.</summary>
    public byte Background => (byte)(_raw & Colors.bgMask);
    /// <summary>Whether all packed attribute bits are zero.</summary>
    public bool IsDefault => _raw == 0;

    /// <summary>Wraps a byte as a packed text color attribute.</summary>
    public static implicit operator TColorAttr(byte raw)   => new TColorAttr(raw);
    /// <summary>Wraps the low eight bits as a packed text color attribute.</summary>
    public static implicit operator TColorAttr(ushort raw) => new TColorAttr((byte)raw);
    /// <summary>Returns the packed attribute byte.</summary>
    public static implicit operator byte(TColorAttr a)     => a._raw;
    /// <summary>Returns the packed attribute byte widened to an unsigned 16-bit value.</summary>
    public static implicit operator ushort(TColorAttr a)   => a._raw;

    /// <summary>Tests equality of the packed attribute bits.</summary>
    public bool Equals(TColorAttr other) => _raw == other._raw;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TColorAttr o && Equals(o);
    /// <inheritdoc />
    public override int GetHashCode() => _raw.GetHashCode();
    /// <summary>Tests whether packed foreground and background bits match.</summary>
    public static bool operator ==(TColorAttr a, TColorAttr b) => a._raw == b._raw;
    /// <summary>Tests whether any packed attribute bit differs.</summary>
    public static bool operator !=(TColorAttr a, TColorAttr b) => a._raw != b._raw;

    /// <inheritdoc />
    public override string ToString() => $"0x{_raw:X2}";
}
