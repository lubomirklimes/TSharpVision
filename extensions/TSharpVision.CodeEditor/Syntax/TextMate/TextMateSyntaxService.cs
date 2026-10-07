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
/// <b>Extensible.</b> An application adds languages of its own with <see cref="RegisterLanguage"/> (any
/// <see cref="ISyntaxClassifier"/>) or <see cref="RegisterTextMateLanguage(SyntaxLanguageDefinition, Stream)"/>
/// (a TextMate grammar it supplies). They join the one catalog the built-in languages are in. A registration
/// belongs to the service instance it was made on - registering on <see cref="Default"/> is what makes a language
/// known to everything that uses the shared service - and cannot be removed or replaced.
/// </para>
/// <para>
/// <b>Threading.</b> Detection, enumeration, classifier creation and registration are safe from any thread.
/// Tokenizing is serialised per service; a registered classifier is called by one thread at a time.
/// </para>
/// </remarks>
public sealed class TextMateSyntaxService : ISyntaxService, ISyntaxLanguageRegistry
{
    private static readonly Lazy<TextMateSyntaxService> DefaultInstance =
        new(() => new TextMateSyntaxService(), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly object _gate = new();
    private readonly RegistryOptions _builtIn;
    private readonly TextMateRegistryOptions _options;
    private readonly Registry _registry;
    private readonly object _catalogGate = new();
    private TextMateLanguageCatalog? _catalog;
    private readonly Dictionary<string, TextMateSyntaxClassifier?> _classifiers = new(StringComparer.Ordinal);

    /// <summary>Creates a service with its own grammar registry.</summary>
    public TextMateSyntaxService()
    {
        // The theme name only selects a theme resource TextMateSharp would load on request; this service
        // never requests one. Colour comes from SyntaxColorScheme.
        _builtIn = new RegistryOptions(ThemeName.DarkPlus);
        _options = new TextMateRegistryOptions(_builtIn);
        LoadOwnGrammars(_options);
        _registry = new Registry(_options);
    }

    /// <summary>
    /// The catalog in force: built on first use, replaced as a whole by a registration, never changed in place.
    /// </summary>
    private TextMateLanguageCatalog Catalog
    {
        get
        {
            TextMateLanguageCatalog? catalog = Volatile.Read(ref _catalog);
            if (catalog is not null) return catalog;

            lock (_catalogGate)
            {
                if (_catalog is null) Volatile.Write(ref _catalog, new TextMateLanguageCatalog(_builtIn));
                return _catalog!;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Safe to call from any thread, also while other threads detect and classify: they see the catalog either
    /// without or with the new language, never in between.
    /// </remarks>
    public void RegisterLanguage(SyntaxLanguageDefinition definition, Func<ISyntaxClassifier> classifierFactory)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(classifierFactory);

        lock (_catalogGate)
        {
            TextMateLanguageCatalog catalog = Catalog;
            Volatile.Write(ref _catalog, catalog.With(catalog.CreateEntry(definition, null, classifierFactory)));
        }
    }

    /// <summary>Adds a language classified by a TextMate grammar the caller supplies as JSON text.</summary>
    /// <param name="definition">The language and the names it is found and detected by.</param>
    /// <param name="grammarJson">The grammar, a <c>.tmLanguage.json</c> document. Its <c>scopeName</c> must be new to this service.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The definition holds an empty or malformed name, extension or pattern.</exception>
    /// <exception cref="FormatException">The grammar cannot be read or declares no scope name. Nothing is changed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The language identifier or the grammar's scope name is already registered, or the identifier is that of
    /// <see cref="SyntaxLanguage.PlainText"/>. Nothing is changed.
    /// </exception>
    public void RegisterTextMateLanguage(SyntaxLanguageDefinition definition, string grammarJson)
    {
        ArgumentNullException.ThrowIfNull(grammarJson);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(grammarJson));
        RegisterTextMateLanguage(definition, stream);
    }

    /// <summary>Adds a language classified by a TextMate grammar the caller supplies as a stream.</summary>
    /// <param name="definition">The language and the names it is found and detected by.</param>
    /// <param name="grammar">
    /// A readable stream over a <c>.tmLanguage.json</c> document — an assembly resource, a file, memory. It is read
    /// to its end during the call and left open; nothing keeps a reference to it afterwards.
    /// </param>
    /// <remarks>
    /// The grammar's own <c>scopeName</c> identifies it; it may include the scopes of bundled grammars. Injection
    /// grammars are not supported for registered languages.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The definition holds an empty or malformed name, extension or pattern.</exception>
    /// <exception cref="FormatException">The grammar cannot be read or declares no scope name. Nothing is changed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The language identifier or the grammar's scope name is already registered, or the identifier is that of
    /// <see cref="SyntaxLanguage.PlainText"/>. Nothing is changed.
    /// </exception>
    public void RegisterTextMateLanguage(SyntaxLanguageDefinition definition, Stream grammar)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(grammar);

        lock (_catalogGate)
        {
            TextMateLanguageCatalog catalog = Catalog;

            // The definition first: a rejected language must not leave its grammar behind.
            catalog.CreateEntry(definition, null, null);

            lock (_gate)
            {
                string scopeName = _options.AddGrammar(grammar, scope => catalog.FindByScope(scope) is not null || BuiltInHasScope(scope));

                // Loaded now rather than at the first draw, so a grammar TextMate cannot use is refused here.
                IGrammar? loaded = null;
                Exception? failure = null;
                try
                {
                    loaded = _registry.LoadGrammar(scopeName);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failure = ex;
                }

                if (loaded is null)
                {
                    _options.RemoveGrammar(scopeName);
                    throw new FormatException($"The TextMate grammar '{scopeName}' cannot be loaded.", failure);
                }

                _classifiers.Remove(scopeName);
                Volatile.Write(ref _catalog, catalog.With(catalog.CreateEntry(definition, scopeName, null)));
            }
        }
    }

    private bool BuiltInHasScope(string scopeName)
    {
        try
        {
            return _builtIn.GetGrammar(scopeName) is not null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>
    /// Loads the grammars embedded in this assembly — languages the bundled grammar package has none for (TOML).
    /// </summary>
    private static void LoadOwnGrammars(TextMateRegistryOptions options)
    {
        var assembly = typeof(TextMateSyntaxService).Assembly;
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.EndsWith(".tmLanguage.json", StringComparison.Ordinal)) continue;

            try
            {
                using Stream? stream = assembly.GetManifestResourceStream(resource);
                if (stream is not null) options.TryLoadGrammar(stream);
            }
            catch (Exception ex) when (ex is IOException or FormatException or InvalidOperationException or ArgumentException)
            {
                // A grammar that cannot be read leaves its language plain.
            }
        }
    }

    /// <summary>Gets the process-wide shared service.</summary>
    public static TextMateSyntaxService Default => DefaultInstance.Value;

    /// <inheritdoc />
    public SyntaxLanguage DetectLanguage(string? fileName, string? contentSample)
    {
        TextMateLanguageCatalog.Entry? entry = Catalog.Detect(fileName, contentSample);
        return entry?.Language ?? SyntaxLanguage.PlainText;
    }

    /// <inheritdoc />
    public ISyntaxClassifier? CreateClassifier(SyntaxLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (language.IsPlainText) return null;

        TextMateLanguageCatalog.Entry? entry = Catalog.FindById(language.Id);
        if (entry is null) return null;
        return entry.ScopeName is { } scopeName ? GetClassifier(scopeName, entry.Language) : entry.Custom?.Value;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The languages of the bundled grammar package and of this assembly (TOML), and the languages registered on
    /// this service. Three entries that only repeat another language's grammar are folded into it: Compose into
    /// YAML, Properties into INI, Typst code mode into Typst. Detection still reports them by their own identity.
    /// The list is the same instance until a language is registered; a list taken earlier is not changed.
    /// </remarks>
    public IReadOnlyList<SyntaxLanguageInfo> GetLanguages() => Catalog.UserLanguages();

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

        SyntaxLanguage language = Catalog.FindByScope(scopeName)?.Language ?? new SyntaxLanguage(scopeName, scopeName);
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
