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

    public TColorAttr(byte raw) { _raw = raw; }
    public TColorAttr(int raw) { _raw = (byte)raw; }

    public byte Foreground => (byte)(_raw & Colors.fgMask);
    public byte Background => (byte)(_raw & Colors.bgMask);
    public bool IsDefault => _raw == 0;

    public static implicit operator TColorAttr(byte raw)   => new TColorAttr(raw);
    public static implicit operator TColorAttr(ushort raw) => new TColorAttr((byte)raw);
    public static implicit operator byte(TColorAttr a)     => a._raw;
    public static implicit operator ushort(TColorAttr a)   => a._raw;

    public bool Equals(TColorAttr other) => _raw == other._raw;
    public override bool Equals(object? obj) => obj is TColorAttr o && Equals(o);
    public override int GetHashCode() => _raw.GetHashCode();
    public static bool operator ==(TColorAttr a, TColorAttr b) => a._raw == b._raw;
    public static bool operator !=(TColorAttr a, TColorAttr b) => a._raw != b._raw;

    public override string ToString() => $"0x{_raw:X2}";
}
