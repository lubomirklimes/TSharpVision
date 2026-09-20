using System;
using System.Globalization;
namespace TSharpVision;

/// Validates that the input string represents a long integer in
/// the range [<see cref="Min"/>, <see cref="Max"/>].
public class TRangeValidator : TFilterValidator
{
    private const string CtValidChars    = "-+xX0123456789ABCDEFabcdef";
    private const string CtValidCharsPos = "+xX0123456789ABCDEFabcdef";
    private const string CtValidCharsNeg = "-xX0123456789ABCDEFabcdef";

    /// <summary>Inclusive lower bound accepted by final numeric validation.</summary>
    public long Min;
    /// <summary>Inclusive upper bound accepted by final numeric validation.</summary>
    public long Max;

    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TRangeValidator";
    /// <inheritdoc />
    public override string streamableName => "TRangeValidator";

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTRangeValidator =
        new TStreamableClass("TRangeValidator",
            () => new TRangeValidator(StreamableInit.streamableInit), 0);

    /// Constructs a range validator for the interval [<paramref name="min"/>,
    /// <paramref name="max"/>].
    public TRangeValidator(long min, long max) : base(ChooseValidChars(min))
    {
        Min = min;
        Max = max;
    }

    /// <summary>Creates a range validator for restoration; Read supplies the bounds and character filter.</summary>
    protected TRangeValidator(StreamableInit _) : base(_) { }

    private static string ChooseValidChars(long min)
    {
        if (min < 0) return CtValidChars;
        if (min > 0) return CtValidCharsPos;
        return CtValidCharsNeg;
    }

    /// <inheritdoc />
    public override bool IsValid(string? s)
    {
        if (!base.IsValid(s)) return false;
        if (string.IsNullOrWhiteSpace(s)) return false;

        string trimmed = s.Trim();
        long value;
        // Support hex (0x / 0X prefix)
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!long.TryParse(trimmed.Substring(2),
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                    out value))
                return false;
        }
        else
        {
            if (!long.TryParse(trimmed, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out value))
                return false;
        }
        return value >= Min && value <= Max;
    }

    /// vtGetData: parse current string into buffer as long.
    /// vtSetData: format long in buffer back to string (out via ref of long).
    /// vtDataSize: return sizeof(long) = 8.
    public override ushort Transfer(string? s, object buffer, TVTransfer flag)
    {
        if ((Options & VoTransfer) == 0) return 0;
        switch (flag)
        {
            case TVTransfer.vtDataSize:
                return sizeof(long);  // 8

            case TVTransfer.vtGetData:
                // Parse s → store in buffer if it's a boxed long ref
                if (buffer is long[] arr && arr.Length > 0)
                {
                    if (long.TryParse(s?.Trim() ?? "0",
                            NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out long v))
                        arr[0] = v;
                }
                return sizeof(long);

            case TVTransfer.vtSetData:
                // Not commonly used from C#; return size
                return sizeof(long);

            default:
                return 0;
        }
    }

    // Upstream writes min/max as 4-byte longs (32-bit Borland). We use
    // Write32 (cast to uint with sign extension to preserve negatives).
    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.Write32(unchecked((uint)(int)Min));   // lo 32 bits
        os.Write32(unchecked((uint)(int)Max));
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        Min = (int)isStream.Read32();   // sign-extend 32→64
        Max = (int)isStream.Read32();
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() =>
        new TRangeValidator(StreamableInit.streamableInit);
}
