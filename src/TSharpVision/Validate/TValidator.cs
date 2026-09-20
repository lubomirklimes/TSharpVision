namespace TSharpVision;

/// Transfer-operation codes passed to TValidator.Transfer.
public enum TVTransfer
{
    /// <summary>Queries the byte size required for the validator's transferred data.</summary>
    vtDataSize = 0,
    /// <summary>Requests conversion from application data to input text.</summary>
    vtSetData  = 1,
    /// <summary>Requests conversion from input text to application data.</summary>
    vtGetData  = 2,
}

/// Abstract base for all input validators.
public class TValidator : TStreamable
{
    /// <summary>Validator configuration has no reported syntax error.</summary>
    public const ushort VsOk      = 0;
    /// <summary>Validator configuration contains a syntax error, such as an invalid picture mask.</summary>
    public const ushort VsSyntax  = 1;

    /// <summary>Enables picture-literal filling when partial-input validation does not suppress it.</summary>
    public const ushort VoFill      = 0x0001;
    /// <summary>Enables validator-specific conversion between input text and application data.</summary>
    public const ushort VoTransfer  = 0x0002;
    /// <summary>Compatibility option for validation on appended input; currently not interpreted by the input-line implementation.</summary>
    public const ushort VoOnAppend  = 0x0004;

    /// <summary>Configuration status, normally VsOk or VsSyntax; this is not the result of the latest input check.</summary>
    public ushort Status;
    /// <summary>Combined option bits controlling filling, data transfer, and append-validation policy.</summary>
    public ushort Options;

    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public static readonly string Name = "TValidator";
    /// <inheritdoc />
    public override string streamableName => "TValidator";

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTValidator =
        new TStreamableClass("TValidator",
            () => new TValidator(StreamableInit.streamableInit), 0);

    /// <summary>Creates a validator with successful configuration status and no options; base input checks accept all text.</summary>
    public TValidator()
    {
        Status  = VsOk;
        Options = 0;
    }

    /// <summary>Creates a validator whose persisted configuration will be supplied by Read.</summary>
    protected TValidator(StreamableInit _) { }

    /// Called when validation fails. Default: no-op.
    public virtual void Error() { }

    /// Returns true if every character in <paramref name="s"/> would be
    /// acceptable input so far (partial-input check). Default: always true.
    public virtual bool IsValidInput(string? s, bool suppressFill) => true;

    /// Returns true when the complete string <paramref name="s"/> is valid.
    /// Default: always true.
    public virtual bool IsValid(string? s) => true;

    /// Data transfer for voTransfer-capable validators.
    /// Default: returns 0 (no-op for vtDataSize, vtGetData, vtSetData).
    public virtual ushort Transfer(string? s, object buffer, TVTransfer flag) => 0;

    /// Validates <paramref name="s"/>: calls <see cref="IsValid"/>; if that
    /// returns false, calls <see cref="Error"/> and returns false.
    public bool Validate(string? s)
    {
        if (IsValid(s)) return true;
        Error();
        return false;
    }

    /// Optional formatting of the input string. Default: no-op.
    public virtual void Format(ref string s) { }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        os.WriteShort(Status);
        os.WriteShort(Options);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        Status  = isStream.ReadShort();
        Options = isStream.ReadShort();
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public static TStreamable Build() =>
        new TValidator(StreamableInit.streamableInit);

    /// Register all six validator classes with the stream subsystem.
    /// Call this before serialising or deserialising any stream that may
    /// contain validator objects.
    /// Registers (or re-registers after a Pstream.DeInitTypes() call) all
    /// validator streamable types plus TInputLine so that stream round-trips
    /// involving validators continue to work.
    public static void RegisterStreamableTypes()
    {
        // Re-register by passing the existing singleton instances directly to
        // Pstream.RegisterType().  This is safe to call multiple times.
        Pstream.RegisterType(StreamableClassTValidator);
        Pstream.RegisterType(TFilterValidator.StreamableClassTFilterValidator);
        Pstream.RegisterType(TRangeValidator.StreamableClassTRangeValidator);
        Pstream.RegisterType(TPXPictureValidator.StreamableClassTPXPictureValidator);
        Pstream.RegisterType(TLookupValidator.StreamableClassTLookupValidator);
        Pstream.RegisterType(TStringLookupValidator.StreamableClassTStringLookupValidator);
        // TInputLine wraps a Validator pointer; re-register it too so that
        // TInputLine stream round-trips work after a DeInitTypes() reset.
        Pstream.RegisterType(TInputLine.StreamableClassTInputLine);
    }
}
