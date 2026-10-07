namespace TSharpVision.CodeEditor.Syntax;

/// <summary>
/// Everything a syntax service needs to know about a language an application adds to it: the identity, the names
/// a person finds it by, and the file names it is detected from.
/// </summary>
/// <remarks>
/// <para>
/// Backend-neutral: nothing here says how the language is classified. The classifier is given separately, to
/// <see cref="ISyntaxLanguageRegistry.RegisterLanguage"/> or to a backend's own convenience method such as
/// <see cref="TextMate.TextMateSyntaxService.RegisterTextMateLanguage(SyntaxLanguageDefinition, Stream)"/>.
/// </para>
/// <para>
/// The detection metadata is the same a built-in language has, and takes part in the same detection order: exact
/// file name, file-name pattern, longest extension, first-line pattern. See
/// <see cref="ISyntaxLanguageRegistry"/> for what happens when two languages claim the same name.
/// </para>
/// <para>
/// Collection inputs are retained by this definition until registration. Do not mutate them during registration.
/// The service validates and copies their contents when registering, so later input changes cannot alter its catalog.
/// Collections default to empty; assigning null is rejected, and null or blank elements are rejected at registration.
/// </para>
/// </remarks>
public sealed class SyntaxLanguageDefinition
{
    private readonly IReadOnlyList<string> _aliases = Array.Empty<string>();
    private readonly IReadOnlyList<string> _fileExtensions = Array.Empty<string>();
    private readonly IReadOnlyList<string> _fileNames = Array.Empty<string>();
    private readonly IReadOnlyList<string> _fileNamePatterns = Array.Empty<string>();

    /// <summary>Creates a definition of <paramref name="language"/> with no names and no detection metadata.</summary>
    /// <param name="language">The language identity. Its <see cref="SyntaxLanguage.Id"/> is the stable key.</param>
    public SyntaxLanguageDefinition(SyntaxLanguage language)
    {
        Language = language ?? throw new ArgumentNullException(nameof(language));
    }

    /// <summary>Gets the language identity: stable identifier and display name.</summary>
    public SyntaxLanguage Language { get; }

    /// <summary>Gets other names the language is known by (<c>6502</c>, <c>asm65</c>), for searching a list.</summary>
    public IReadOnlyList<string> Aliases
    {
        get => _aliases;
        init => _aliases = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the file extensions that name the language, most characteristic first. A leading dot is optional;
    /// an extension may have several parts (<c>.d.ts</c>). Compared ignoring case.
    /// </summary>
    public IReadOnlyList<string> FileExtensions
    {
        get => _fileExtensions;
        init => _fileExtensions = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets the exact file names that name the language, such as <c>Makefile</c>. Compared ignoring case.</summary>
    public IReadOnlyList<string> FileNames
    {
        get => _fileNames;
        init => _fileNames = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets wildcard patterns matched against the whole file name, ignoring case: <c>*</c> is any run of characters
    /// and <c>?</c> is one character (<c>Dockerfile.*</c>).
    /// </summary>
    public IReadOnlyList<string> FileNamePatterns
    {
        get => _fileNamePatterns;
        init => _fileNamePatterns = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets a .NET regular expression matched against the first line of a document whose name decided nothing, or
    /// null. Only the first 512 characters of that line are tried, with a short time limit.
    /// </summary>
    public string? FirstLinePattern { get; init; }

    /// <summary>
    /// Gets the precedence of this language when another language claims the same file name, pattern, extension or
    /// first line equally well. Higher wins; built-in languages have 0, which is also the default.
    /// </summary>
    /// <remarks>
    /// Specificity is decided first — an exact name beats a pattern, a pattern beats an extension, a longer
    /// extension beats a shorter one — so a priority never makes <c>.ts</c> win over <c>.d.ts</c>. Among equal
    /// priorities the language registered first wins, and built-in languages are registered before any other.
    /// </remarks>
    public int Priority { get; init; }

    /// <inheritdoc />
    public override string ToString() => Language.DisplayName;
}

/// <summary>
/// A syntax service languages can be added to. The standard service,
/// <see cref="TextMate.TextMateSyntaxService"/>, is one.
/// </summary>
/// <remarks>
/// <para>
/// <b>One catalog.</b> A registered language is a language of the service like any built-in one: it is listed by
/// <see cref="ISyntaxService.GetLanguages"/>, found by <see cref="ISyntaxService.DetectLanguage"/> in the same
/// detection order, and classified through <see cref="ISyntaxService.CreateClassifier"/>.
/// </para>
/// <para>
/// <b>Owned by the instance.</b> A registration belongs to the service it was made on and lasts as long as that
/// service; there is no process-wide registry and no way to remove or replace a language. Register languages
/// after creating the service and before opening documents with it: a document that is already open keeps the
/// language it was given.
/// </para>
/// </remarks>
public interface ISyntaxLanguageRegistry
{
    /// <summary>Adds a language classified by a classifier the caller supplies.</summary>
    /// <param name="definition">The language and the names it is found and detected by.</param>
    /// <param name="classifierFactory">
    /// Creates the classifier. Called at most once, the first time the language is needed; the classifier is then
    /// shared by every document of that language, so it must hold no per-document state (see
    /// <see cref="ISyntaxClassifier"/>). A factory that throws or returns null leaves the language listed and
    /// detected but without highlighting.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The definition holds an empty or malformed name, extension or pattern.</exception>
    /// <exception cref="InvalidOperationException">
    /// A language with the same identifier (ignoring case) is already registered, or the identifier is that of
    /// <see cref="SyntaxLanguage.PlainText"/>. Nothing is changed.
    /// </exception>
    void RegisterLanguage(SyntaxLanguageDefinition definition, Func<ISyntaxClassifier> classifierFactory);
}
