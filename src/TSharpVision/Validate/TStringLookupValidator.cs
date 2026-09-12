namespace TSharpVision;

/// Validates by performing a linear search in a <see cref="TStringCollection"/>.
public class TStringLookupValidator : TLookupValidator
{
    /// <summary>Referenced allowed-string collection; null causes every lookup to fail.</summary>
    protected TStringCollection Strings;

    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TStringLookupValidator";
    /// <inheritdoc />
    public override string streamableName => "TStringLookupValidator";

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTStringLookupValidator =
        new TStreamableClass("TStringLookupValidator",
            () => new TStringLookupValidator(StreamableInit.streamableInit), 0);

    /// Constructs a string-lookup validator using the supplied collection.
    public TStringLookupValidator(TStringCollection aStrings) : base()
    {
        Strings = aStrings;
    }

    /// <summary>Creates a string lookup validator for restoration; Read supplies its allowed-string collection.</summary>
    protected TStringLookupValidator(StreamableInit _) : base(_) { }

    /// <inheritdoc />
    public override bool Lookup(string s)
    {
        if (Strings == null || s == null) return false;
        for (int i = 0; i < Strings.Count; i++)
            if (string.Equals(Strings[i], s, System.StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <inheritdoc />
    public override void Error() { }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WritePointer(Strings);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        Strings = isStream.ReadPointer() as TStringCollection;
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() =>
        new TStringLookupValidator(StreamableInit.streamableInit);
}
