namespace TSharpVision;

/// Validates by checking that every character in the input belongs to
/// a set of acceptable characters.
public class TFilterValidator : TValidator
{
    /// The set of characters that are valid for this input. Any character
    /// not in this string is rejected.
    protected string ValidChars;

    public new static readonly string Name = "TFilterValidator";
    public override string streamableName => "TFilterValidator";

    public static readonly TStreamableClass StreamableClassTFilterValidator =
        new TStreamableClass("TFilterValidator",
            () => new TFilterValidator(StreamableInit.streamableInit), 0);

    /// Constructs a filter validator that accepts only characters in
    /// <paramref name="validChars"/>.
    public TFilterValidator(string validChars) : base()
    {
        ValidChars = validChars ?? string.Empty;
    }

    protected TFilterValidator(StreamableInit _) : base(_) { }

    public override bool IsValid(string s)
    {
        if (s == null) return true;
        foreach (char c in s)
            if (ValidChars.IndexOf(c) < 0) return false;
        return true;
    }

    public override bool IsValidInput(string s, bool suppressFill) => IsValid(s);

    public override void Error() { }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteString(ValidChars);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        ValidChars = isStream.ReadString() ?? string.Empty;
        return this;
    }

    public new static TStreamable Build() =>
        new TFilterValidator(StreamableInit.streamableInit);
}
