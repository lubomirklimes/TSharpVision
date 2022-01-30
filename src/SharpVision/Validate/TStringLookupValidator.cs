namespace SharpVision;

/// Validates by performing a linear search in a <see cref="TStringCollection"/>.
public class TStringLookupValidator : TLookupValidator
{
    protected TStringCollection Strings;

    public new static readonly string Name = "TStringLookupValidator";
    public override string streamableName => "TStringLookupValidator";

    public static readonly TStreamableClass StreamableClassTStringLookupValidator =
        new TStreamableClass("TStringLookupValidator",
            () => new TStringLookupValidator(StreamableInit.streamableInit), 0);

    /// Constructs a string-lookup validator using the supplied collection.
    public TStringLookupValidator(TStringCollection aStrings) : base()
    {
        Strings = aStrings;
    }

    protected TStringLookupValidator(StreamableInit _) : base(_) { }

    public override bool Lookup(string s)
    {
        if (Strings == null || s == null) return false;
        for (int i = 0; i < Strings.Count; i++)
            if (string.Equals(Strings[i], s, System.StringComparison.Ordinal))
                return true;
        return false;
    }

    public override void Error() { }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WritePointer(Strings);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        Strings = isStream.ReadPointer() as TStringCollection;
        return this;
    }

    public new static TStreamable Build() =>
        new TStringLookupValidator(StreamableInit.streamableInit);
}
