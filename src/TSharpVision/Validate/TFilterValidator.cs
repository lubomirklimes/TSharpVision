namespace TSharpVision;

/// Validates by checking that every character in the input belongs to
/// a set of acceptable characters.
public class TFilterValidator : TValidator
{
    /// The set of characters that are valid for this input. Any character
    /// not in this string is rejected.
    protected string ValidChars;

    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TFilterValidator";
    /// <inheritdoc />
    public override string streamableName => "TFilterValidator";

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTFilterValidator =
        new TStreamableClass("TFilterValidator",
            () => new TFilterValidator(StreamableInit.streamableInit), 0);

    /// Constructs a filter validator that accepts only characters in
    /// <paramref name="validChars"/>.
    public TFilterValidator(string validChars) : base()
    {
        ValidChars = validChars ?? string.Empty;
    }

    /// <summary>Creates a filter validator for restoration; Read must supply its valid-character set before use.</summary>
    protected TFilterValidator(StreamableInit _) : base(_) { ValidChars = string.Empty; }

    /// <inheritdoc />
    public override bool IsValid(string? s)
    {
        if (s == null) return true;
        foreach (char c in s)
            if (ValidChars.IndexOf(c) < 0) return false;
        return true;
    }

    /// <inheritdoc />
    public override bool IsValidInput(string? s, bool suppressFill) => IsValid(s);

    /// <inheritdoc />
    public override void Error() { }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteString(ValidChars);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        ValidChars = isStream.ReadString() ?? string.Empty;
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() =>
        new TFilterValidator(StreamableInit.streamableInit);
}
