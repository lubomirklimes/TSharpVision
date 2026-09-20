using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace TSharpVision.CodeEditor.Syntax.TextMate;

/// <summary>
/// The default <see cref="ISyntaxService"/>: language detection and classification backed by
/// TextMateSharp and the grammars bundled in TextMateSharp.Grammars.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing TextMate leaks out.</b> Callers see <see cref="SyntaxLanguage"/>,
/// <see cref="ISyntaxClassifier"/>, <see cref="SyntaxLineState"/> and <see cref="SyntaxSpan"/> only; the
/// grammar, its rule stacks, scope names and themes stay behind this type, so another backend can
/// replace it without changing a consumer.
/// </para>
/// <para>
/// <b>Shared.</b> <see cref="Default"/> is created once per process and loads each grammar at most once.
/// Classifiers are stateless and cached per language, so a text viewer and an editor showing the same
/// kind of file use the very same classifier while each keeps its own per-document cache.
/// </para>
/// <para>
/// <b>Threading.</b> Detection and classifier creation are safe from any thread. Tokenizing is
/// serialised per service.
/// </para>
/// </remarks>
public sealed class TextMateSyntaxService : ISyntaxService
{
    private static readonly Lazy<TextMateSyntaxService> DefaultInstance =
        new(() => new TextMateSyntaxService(), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly object _gate = new();
    private readonly RegistryOptions _builtIn;
    private readonly TextMateRegistryOptions _options;
    private readonly Registry _registry;
    private readonly Lazy<TextMateLanguageCatalog> _catalog;
    private readonly Dictionary<string, TextMateSyntaxClassifier?> _classifiers = new(StringComparer.Ordinal);

    /// <summary>Creates a service with its own grammar registry.</summary>
    public TextMateSyntaxService()
    {
        // The theme name only selects a theme resource TextMateSharp would load on request; this service
        // never requests one. Colour comes from SyntaxColorScheme.
        _builtIn = new RegistryOptions(ThemeName.DarkPlus);
        _options = new TextMateRegistryOptions(_builtIn);
        _registry = new Registry(_options);
        _catalog = new Lazy<TextMateLanguageCatalog>(() => new TextMateLanguageCatalog(_builtIn), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Gets the process-wide shared service.</summary>
    public static TextMateSyntaxService Default => DefaultInstance.Value;

    /// <inheritdoc />
    public SyntaxLanguage DetectLanguage(string? fileName, string? contentSample)
    {
        TextMateLanguageCatalog.Entry? entry = _catalog.Value.Detect(fileName, contentSample);
        return entry?.Language ?? SyntaxLanguage.PlainText;
    }

    /// <inheritdoc />
    public ISyntaxClassifier? CreateClassifier(SyntaxLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (language.IsPlainText) return null;

        TextMateLanguageCatalog.Entry? entry = _catalog.Value.FindById(language.Id);
        return entry is null ? null : GetClassifier(entry.ScopeName, entry.Language);
    }

    /// <summary>Loads a grammar file into this service's registry and returns its scope name.</summary>
    internal bool TryLoadGrammarFile(string path, out string? scopeName)
    {
        lock (_gate)
        {
            try
            {
                scopeName = _options.TryLoadGrammar(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException)
            {
                scopeName = null;
            }

            if (scopeName is not null) _classifiers.Remove(scopeName);
            return scopeName is not null;
        }
    }

    /// <summary>Gets the classifier for a TextMate scope name, or null when no grammar provides it.</summary>
    internal ISyntaxClassifier? CreateClassifierForScope(string? scopeName)
    {
        if (string.IsNullOrWhiteSpace(scopeName)) return null;

        SyntaxLanguage language = _catalog.Value.FindByScope(scopeName)?.Language ?? new SyntaxLanguage(scopeName, scopeName);
        return GetClassifier(scopeName, language);
    }

    private TextMateSyntaxClassifier? GetClassifier(string scopeName, SyntaxLanguage language)
    {
        lock (_gate)
        {
            if (_classifiers.TryGetValue(scopeName, out TextMateSyntaxClassifier? cached)) return cached;

            TextMateSyntaxClassifier? classifier = null;
            try
            {
                IGrammar? grammar = _registry.LoadGrammar(scopeName);
                if (grammar is not null) classifier = new TextMateSyntaxClassifier(language, scopeName, grammar, _gate);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A grammar that cannot be loaded leaves its language plain rather than failing a view.
            }

            _classifiers[scopeName] = classifier;
            return classifier;
        }
    }
}
