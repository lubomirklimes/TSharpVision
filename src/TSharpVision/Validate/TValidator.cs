namespace TSharpVision;

/// Transfer-operation codes passed to TValidator.Transfer.
public enum TVTransfer
{
    vtDataSize = 0,
    vtSetData  = 1,
    vtGetData  = 2,
}

/// Abstract base for all input validators.
public class TValidator : TStreamable
{
    public const ushort VsOk      = 0;
    public const ushort VsSyntax  = 1;

    public const ushort VoFill      = 0x0001;
    public const ushort VoTransfer  = 0x0002;
    public const ushort VoOnAppend  = 0x0004;

    public ushort Status;
    public ushort Options;

    public static readonly string Name = "TValidator";
    public override string streamableName => "TValidator";

    public static readonly TStreamableClass StreamableClassTValidator =
        new TStreamableClass("TValidator",
            () => new TValidator(StreamableInit.streamableInit), 0);

    public TValidator()
    {
        Status  = VsOk;
        Options = 0;
    }

    protected TValidator(StreamableInit _) { }

    /// Called when validation fails. Default: no-op.
    public virtual void Error() { }

    /// Returns true if every character in <paramref name="s"/> would be
    /// acceptable input so far (partial-input check). Default: always true.
    public virtual bool IsValidInput(string s, bool suppressFill) => true;

    /// Returns true when the complete string <paramref name="s"/> is valid.
    /// Default: always true.
    public virtual bool IsValid(string s) => true;

    /// Data transfer for voTransfer-capable validators.
    /// Default: returns 0 (no-op for vtDataSize, vtGetData, vtSetData).
    public virtual ushort Transfer(string s, object buffer, TVTransfer flag) => 0;

    /// Validates <paramref name="s"/>: calls <see cref="IsValid"/>; if that
    /// returns false, calls <see cref="Error"/> and returns false.
    public bool Validate(string s)
    {
        if (IsValid(s)) return true;
        Error();
        return false;
    }

    /// Optional formatting of the input string. Default: no-op.
    public virtual void Format(ref string s) { }

    public override void Write(Opstream os)
    {
        os.WriteShort(Status);
        os.WriteShort(Options);
    }

    public override object Read(Ipstream isStream)
    {
        Status  = isStream.ReadShort();
        Options = isStream.ReadShort();
        return this;
    }

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
