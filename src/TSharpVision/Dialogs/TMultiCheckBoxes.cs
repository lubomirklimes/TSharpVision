namespace TSharpVision;

/// <summary>A cluster whose options each occupy a configurable packed multi-state field in a 32-bit value.</summary>
public class TMultiCheckBoxes : TCluster
{
    /// <summary>One state bit per option with mask 1.</summary>
    public const ushort cfOneBit = 0x0101;
    /// <summary>Two state bits per option with mask 3.</summary>
    public const ushort cfTwoBits = 0x0203;
    /// <summary>Four state bits per option with mask 15.</summary>
    public const ushort cfFourBits = 0x040F;
    /// <summary>Eight state bits per option with mask 255.</summary>
    public const ushort cfEightBits = 0x08FF;

    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TMultiCheckBoxes";

    private byte selRange;
    private ushort flags;
    private string states = string.Empty;

    /// <summary>Creates packed multi-state options using the supplied state range, field-width flags, and marker table.</summary>
    public TMultiCheckBoxes(TRect bounds, TSItem aStrings, byte aSelRange, ushort aFlags, string aStates)
        : base(bounds, aStrings)
    {
        ArgumentNullException.ThrowIfNull(aStates);
        selRange = aSelRange;
        flags = aFlags;
        states = aStates;
    }

    /// <inheritdoc />
    public override ushort DataSize() => sizeof(uint);

    /// <inheritdoc />
    public override void Draw() => DrawMultiBox(" [ ] ", states);

    /// <inheritdoc />
    public override void GetData(ref object rec)
    {
        rec = value;
        DrawView();
    }

    /// <inheritdoc />
    public override byte MultiMark(int item)
    {
        int shift = item * (flags >> 8);
        if (item < 0 || shift is < 0 or >= 32) return 0;
        return (byte)((value >> shift) & (flags & 0xFF));
    }

    /// <inheritdoc />
    public override void Press(int item)
    {
        int bits = flags >> 8;
        int shift = item * bits;
        if (item < 0 || shift is < 0 or >= 32) return;
        uint fieldMask = (uint)(flags & 0xFF) << shift;
        int current = (int)((value & fieldMask) >> shift) - 1;
        if (current >= selRange || current < 0)
            current = selRange - 1;
        value = (value & ~fieldMask) | (((uint)current << shift) & fieldMask);
    }

    /// <inheritdoc />
    public override void SetData(object rec)
    {
        value = rec switch
        {
            uint unsignedValue => unsignedValue,
            int signedValue => unchecked((uint)signedValue),
            _ => throw new ArgumentException("TMultiCheckBoxes data must be a 32-bit integer value.", nameof(rec))
        };
        DrawView();
    }

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTMultiCheckBoxes =
        new TStreamableClass(Name, () => new TMultiCheckBoxes(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TMultiCheckBoxes(StreamableInit init) : base(init) { }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteInt(value);
        os.WriteInt(enableMask);
        os.WriteByte(selRange);
        os.WriteShort(flags);
        os.WriteString(states);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        value = isStream.ReadInt();
        enableMask = isStream.ReadInt();
        selRange = isStream.ReadByte();
        flags = isStream.ReadShort();
        states = isStream.ReadString()
            ?? throw new InvalidDataException("A streamed TMultiCheckBoxes state marker table cannot be null.");
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TMultiCheckBoxes(StreamableInit.streamableInit);

    /// <inheritdoc />
    public override string StreamableName() => Name;
}
