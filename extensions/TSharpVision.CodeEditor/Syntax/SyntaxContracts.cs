namespace TSharpVision.CodeEditor.Syntax;

/// <summary>
/// The bounded set of semantic roles a syntax classifier assigns to text.
/// </summary>
/// <remarks>
/// A role says what a piece of text <em>is</em>, never how it looks. Backends map their own, much
/// richer vocabularies (TextMate scopes, for example) onto these values, and a
/// <see cref="SyntaxColorScheme"/> decides the presentation. New values may be added in a later
/// version; consumers should treat an unknown value as <see cref="Plain"/>.
/// </remarks>
public enum SyntaxClass : byte
{
    /// <summary>Ordinary text with no particular role.</summary>
    Plain = 0,

    /// <summary>A comment, including its delimiters.</summary>
    Comment,

    /// <summary>A string or character literal, including its quotes.</summary>
    String,

    /// <summary>A numeric literal.</summary>
    Number,

    /// <summary>A language constant such as <c>true</c>, <c>null</c> or an escape sequence.</summary>
    Constant,

    /// <summary>A keyword or storage modifier.</summary>
    Keyword,

    /// <summary>An operator.</summary>
    Operator,

    /// <summary>The name of a type, class, struct or namespace.</summary>
    Type,

    /// <summary>The name of a function or method, declared or called.</summary>
    Function,

    /// <summary>A variable, field, parameter or other identifier.</summary>
    Identifier,

    /// <summary>A preprocessor directive.</summary>
    Preprocessor,

    /// <summary>A markup tag name.</summary>
    Tag,

    /// <summary>A markup attribute name or a property key.</summary>
    Attribute,

    /// <summary>A document heading.</summary>
    Heading,

    /// <summary>Inline or block code inside prose.</summary>
    Code,

    /// <summary>Text the grammar marks as invalid.</summary>
    Invalid,
}

/// <summary>A classified range of one line, in UTF-16 code units.</summary>
/// <param name="Start">Zero-based index of the first code unit in the line.</param>
/// <param name="Length">Number of code units; never negative.</param>
/// <param name="Class">The semantic role of the range.</param>
public readonly record struct SyntaxSpan(int Start, int Length, SyntaxClass Class)
{
    /// <summary>Gets the index just past the last code unit of the span.</summary>
    public int End => Start + Length;
}

/// <summary>
/// Identifies a language a syntax service can classify, or plain text.
/// </summary>
/// <remarks>
/// Two languages are equal when their <see cref="Id"/> values are equal (ordinal). The identifier is
/// stable and backend-neutral in form (for example <c>csharp</c>, <c>json</c>, <c>plaintext</c>); the
/// display name is for people.
/// </remarks>
public sealed class SyntaxLanguage : IEquatable<SyntaxLanguage>
{
    /// <summary>The identifier of <see cref="PlainText"/>.</summary>
    public const string PlainTextId = "plaintext";

    /// <summary>Creates a language identity.</summary>
    /// <param name="id">Stable identifier; must not be empty.</param>
    /// <param name="displayName">Human-readable name; must not be empty.</param>
    public SyntaxLanguage(string id, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Id = id;
        DisplayName = displayName;
    }

    /// <summary>Gets the language used when nothing more specific is known. It is never an error.</summary>
    public static SyntaxLanguage PlainText { get; } = new(PlainTextId, "Plain Text");

    /// <summary>Gets the stable identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the human-readable name.</summary>
    public string DisplayName { get; }

    /// <summary>Gets a value indicating whether this is plain text, which has no classifier.</summary>
    public bool IsPlainText => string.Equals(Id, PlainTextId, StringComparison.Ordinal);

    /// <inheritdoc />
    public bool Equals(SyntaxLanguage? other)
        => other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SyntaxLanguage);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);

    /// <inheritdoc />
    public override string ToString() => DisplayName;
}

/// <summary>
/// The opaque state a classifier carries from the end of one line to the start of the next — for
/// example "inside a block comment".
/// </summary>
/// <remarks>
/// Implementations must override <see cref="object.Equals(object?)"/> and
/// <see cref="object.GetHashCode"/> so that two states compare equal exactly when classifying the
/// same text from either would produce the same result. Incremental caches rely on that equality to
/// stop re-classifying once an edit's effect has converged.
/// </remarks>
public abstract class SyntaxLineState
{
    /// <summary>Initializes a new state.</summary>
    protected SyntaxLineState()
    {
    }
}

/// <summary>The classification of one line and the state it leaves for the next line.</summary>
public readonly struct SyntaxLineResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="spans">Non-overlapping spans in ascending order; ranges not covered are plain.</param>
    /// <param name="endState">State at the end of the line.</param>
    public SyntaxLineResult(IReadOnlyList<SyntaxSpan> spans, SyntaxLineState endState)
    {
        Spans = spans ?? throw new ArgumentNullException(nameof(spans));
        EndState = endState ?? throw new ArgumentNullException(nameof(endState));
    }

    /// <summary>Gets the classified spans of the line.</summary>
    public IReadOnlyList<SyntaxSpan> Spans { get; }

    /// <summary>Gets the state at the end of the line.</summary>
    public SyntaxLineState EndState { get; }
}

/// <summary>
/// Classifies the lines of one language, one line at a time.
/// </summary>
/// <remarks>
/// A classifier holds no per-document state: everything a line needs from the lines before it is in
/// the <see cref="SyntaxLineState"/> passed in, so one classifier can serve any number of documents.
/// It never changes the text it is given. Calls are expected on one thread at a time per classifier.
/// </remarks>
public interface ISyntaxClassifier
{
    /// <summary>Gets the language this classifier understands.</summary>
    SyntaxLanguage Language { get; }

    /// <summary>Gets the state at the start of a document.</summary>
    SyntaxLineState InitialState { get; }

    /// <summary>Classifies one line, without its line terminator.</summary>
    /// <param name="lineText">The line's text.</param>
    /// <param name="startState">The state the previous line ended with, or <see cref="InitialState"/>.</param>
    /// <returns>The spans of the line and the state for the next line.</returns>
    SyntaxLineResult ClassifyLine(string lineText, SyntaxLineState startState);
}

/// <summary>
/// Detects languages and provides their classifiers. The shared entry point a text viewer and an
/// editor both use, so they agree about what a file is and how it is classified.
/// </summary>
public interface ISyntaxService
{
    /// <summary>
    /// Decides the language of a document from its name and, optionally, the beginning of its text.
    /// </summary>
    /// <param name="fileName">A file name or path, or null. Only the last segment is considered.</param>
    /// <param name="contentSample">The first characters of the document, or null. Only inspected, never executed.</param>
    /// <returns>The detected language, or <see cref="SyntaxLanguage.PlainText"/>; never null.</returns>
    SyntaxLanguage DetectLanguage(string? fileName, string? contentSample);

    /// <summary>Gets the classifier for <paramref name="language"/>, or null for plain text or an unknown language.</summary>
    ISyntaxClassifier? CreateClassifier(SyntaxLanguage language);
}

/// <summary>Random access to the lines of a document, without their terminators.</summary>
public interface ISyntaxLineSource
{
    /// <summary>Gets the number of lines.</summary>
    int LineCount { get; }

    /// <summary>Gets the text of line <paramref name="index"/>.</summary>
    string GetLine(int index);
}
