namespace TSharpVision;

/// Abstract base for validators that validate by looking up the input
/// in an external data source.
public class TLookupValidator : TValidator
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TLookupValidator";
    /// <inheritdoc />
    public override string streamableName => "TLookupValidator";

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTLookupValidator =
        new TStreamableClass("TLookupValidator",
            () => new TLookupValidator(StreamableInit.streamableInit), 0);

    /// <summary>Creates a lookup validator whose base lookup rejects every value until overridden.</summary>
    public TLookupValidator() : base() { }

    /// <summary>Creates a lookup validator whose persisted state will be supplied by Read.</summary>
    protected TLookupValidator(StreamableInit _) : base(_) { }

    /// <inheritdoc />
    public override bool IsValid(string s) => Lookup(s);

    /// Lookup the input in whatever collection the subclass provides.
    /// Default: returns false (always invalid).
    public virtual bool Lookup(string s) => false;

    /// <inheritdoc />
    public override void Write(Opstream os) => base.Write(os);

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() =>
        new TLookupValidator(StreamableInit.streamableInit);
}
