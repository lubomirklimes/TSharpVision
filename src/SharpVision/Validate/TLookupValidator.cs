namespace SharpVision;

/// Abstract base for validators that validate by looking up the input
/// in an external data source.
public class TLookupValidator : TValidator
{
    public new static readonly string Name = "TLookupValidator";
    public override string streamableName => "TLookupValidator";

    public static readonly TStreamableClass StreamableClassTLookupValidator =
        new TStreamableClass("TLookupValidator",
            () => new TLookupValidator(StreamableInit.streamableInit), 0);

    public TLookupValidator() : base() { }

    protected TLookupValidator(StreamableInit _) : base(_) { }

    public override bool IsValid(string s) => Lookup(s);

    /// Lookup the input in whatever collection the subclass provides.
    /// Default: returns false (always invalid).
    public virtual bool Lookup(string s) => false;

    public override void Write(Opstream os) => base.Write(os);

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        return this;
    }

    public new static TStreamable Build() =>
        new TLookupValidator(StreamableInit.streamableInit);
}
