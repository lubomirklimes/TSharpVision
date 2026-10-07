using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using TextMateSharp.Grammars;

namespace TSharpVision.CodeEditor.Syntax.TextMate;

/// <summary>
/// What the bundled grammar package knows about recognising its languages: extensions, exact file
/// names, file-name patterns and first-line patterns.
/// </summary>
/// <remarks>
/// <para>
/// Extensions come from <see cref="RegistryOptions.GetAvailableLanguages"/>. Exact file names
/// (<c>Dockerfile</c>, <c>Makefile</c>), file-name patterns (<c>Dockerfile.*</c>) and first-line patterns
/// are declared in the same package manifests, but TextMateSharp's language model does not surface
/// them, so they are read from those manifests directly. Nothing about them is hard-coded here beyond a
/// short table of script interpreters for a <c>#!</c> line.
/// </para>
/// <para>
/// Detection never executes or evaluates content: it compares names and matches at most the first
/// line of a sample against the package's own anchored patterns, with a timeout.
/// </para>
/// </remarks>
internal sealed class TextMateLanguageCatalog
{
    private const int MaxFirstLineLength = 512;
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly (string Interpreter, string LanguageId)[] Interpreters =
    {
        ("sh", "shellscript"), ("bash", "shellscript"), ("zsh", "shellscript"), ("ksh", "shellscript"),
        ("dash", "shellscript"), ("ash", "shellscript"),
        ("python", "python"), ("node", "javascript"), ("pwsh", "powershell"), ("powershell", "powershell"),
        ("perl", "perl"), ("ruby", "ruby"), ("php", "php"), ("lua", "lua"), ("make", "makefile"),
    };

    /// <summary>
    /// Languages detection knows but a chooser does not list, because each only repeats the grammar of the language
    /// it is listed under: choosing that one is the same choice.
    /// </summary>
    private static readonly (string Id, string ListedUnder)[] Variants =
    {
        ("dockercompose", "yaml"), ("properties", "ini"), ("typst-code", "typst"),
    };

    // In order of precedence: by priority, then in the order they were added. Detection takes the first match.
    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _byScope = new(StringComparer.Ordinal);
    private readonly Lazy<IReadOnlyList<SyntaxLanguageInfo>> _userLanguages;

    public TextMateLanguageCatalog(RegistryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (Language language in options.GetAvailableLanguages())
        {
            if (string.IsNullOrWhiteSpace(language.Id)) continue;

            string? scope = options.GetScopeByLanguageId(language.Id);
            if (string.IsNullOrWhiteSpace(scope)) continue;

            if (!_byId.TryGetValue(language.Id, out Entry? entry))
            {
                string display = language.Aliases?.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? language.Id;
                entry = new Entry(new SyntaxLanguage(language.Id, display), scope);
                _entries.Add(entry);
                _byId.Add(language.Id, entry);
                _byScope.TryAdd(scope, entry);
            }

            foreach (string alias in language.Aliases ?? new List<string>())
                entry.AddAlias(alias);

            foreach (string extension in language.Extensions ?? new List<string>())
                entry.AddExtension(extension);
        }

        ReadManifests(typeof(RegistryOptions).Assembly);
        AddOwnLanguages();
        _userLanguages = new Lazy<IReadOnlyList<SyntaxLanguageInfo>>(BuildUserLanguages, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>A catalog with one more language. Entries are immutable once published, so they are shared.</summary>
    private TextMateLanguageCatalog(TextMateLanguageCatalog source, Entry added)
    {
        _entries.AddRange(source._entries);
        int at = _entries.FindIndex(entry => entry.Priority < added.Priority);
        _entries.Insert(at < 0 ? _entries.Count : at, added);

        foreach (KeyValuePair<string, Entry> pair in source._byId) _byId.Add(pair.Key, pair.Value);
        foreach (KeyValuePair<string, Entry> pair in source._byScope) _byScope.Add(pair.Key, pair.Value);
        _byId.Add(added.Language.Id, added);
        if (added.ScopeName is not null) _byScope.TryAdd(added.ScopeName, added);
        _userLanguages = new Lazy<IReadOnlyList<SyntaxLanguageInfo>>(BuildUserLanguages, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// This catalog and <paramref name="added"/>, an entry made by <see cref="CreateEntry"/>. The catalog itself
    /// never changes, so a reader that holds it is not disturbed by a registration.
    /// </summary>
    public TextMateLanguageCatalog With(Entry added) => new(this, added);

    /// <summary>
    /// Checks an application's language against this catalog and makes its entry. Throws without side effects.
    /// </summary>
    /// <param name="definition">The language as the application describes it.</param>
    /// <param name="scopeName">The TextMate scope of its grammar, or null when it brings its own classifier.</param>
    /// <param name="classifierFactory">Its classifier factory, or null when a grammar classifies it.</param>
    public Entry CreateEntry(SyntaxLanguageDefinition definition, string? scopeName, Func<ISyntaxClassifier>? classifierFactory)
    {
        SyntaxLanguage language = definition.Language;
        string id = language.Id;

        if (string.Equals(id, SyntaxLanguage.PlainTextId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Plain Text is not a language that can be registered.");
        if (id.Any(char.IsWhiteSpace))
            throw new ArgumentException($"The language identifier '{id}' contains white space.", nameof(definition));
        if (_entries.FirstOrDefault(e => string.Equals(e.Language.Id, id, StringComparison.OrdinalIgnoreCase)) is { } existing)
            throw new InvalidOperationException(
                $"A language with the identifier '{existing.Language.Id}' ({existing.Language.DisplayName}) is already registered.");

        var entry = new Entry(language, scopeName) { Priority = definition.Priority };
        if (classifierFactory is not null)
        {
            entry.Custom = new Lazy<ISyntaxClassifier?>(
                () => GuardedSyntaxClassifier.Create(language, classifierFactory), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        foreach (string alias in Checked(definition.Aliases, "alias", plainName: false)) entry.AddAlias(alias);
        foreach (string extension in Checked(definition.FileExtensions, "file extension", plainName: true))
            entry.AddExtension(extension[0] == '.' ? extension : "." + extension);
        foreach (string name in Checked(definition.FileNames, "file name", plainName: true)) entry.FileNames.Add(name);
        foreach (string pattern in Checked(definition.FileNamePatterns, "file-name pattern", plainName: false))
            entry.FileNamePatterns.Add(pattern);

        if (definition.FirstLinePattern is { } firstLine)
        {
            if (string.IsNullOrWhiteSpace(firstLine))
                throw new ArgumentException("The first-line pattern is empty.", nameof(definition));
            try
            {
                entry.FirstLine = new Regex(firstLine, RegexOptions.CultureInvariant, PatternTimeout);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"The first-line pattern is not a valid regular expression: {ex.Message}", nameof(definition), ex);
            }
        }

        return entry;

        static List<string> Checked(IReadOnlyList<string> values, string what, bool plainName)
        {
            var result = new List<string>(values.Count);
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException($"A {what} is empty.", nameof(definition));
                if (plainName && (value == "." || value.AsSpan().IndexOfAny("*?/\\") >= 0))
                    throw new ArgumentException($"The {what} '{value}' is not a plain name.", nameof(definition));
                result.Add(value);
            }

            return result;
        }
    }

    /// <summary>
    /// What the bundled manifests leave out: one language with a grammar of this assembly's own (TOML), and a few
    /// common names for languages the package already has. Deliberately short — this is not a file-name database.
    /// </summary>
    private void AddOwnLanguages()
    {
        if (!_byId.ContainsKey("toml"))
        {
            var toml = new Entry(new SyntaxLanguage("toml", "TOML"), "source.toml");
            toml.AddExtension(".toml");
            foreach (string name in new[] { "Cargo.lock", "poetry.lock", "uv.lock", "Pipfile" }) toml.FileNames.Add(name);
            _entries.Add(toml);
            _byId.Add("toml", toml);
            _byScope.TryAdd(toml.ScopeName!, toml);
        }

        if (_byId.TryGetValue("xml", out Entry? xml))
        {
            foreach (string extension in new[] { ".config", ".resx", ".slnx", ".manifest", ".nuspec", ".vsixmanifest" })
                xml.AddExtension(extension);
        }

        // "KEY=value" files: the package knows the exact name ".env" only.
        if (_byId.TryGetValue("properties", out Entry? properties))
        {
            properties.AddExtension(".env");
            properties.FileNamePatterns.Add(".env.*");
        }
    }

    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>
    /// The languages a person can choose, by display name. A variant is not listed; its names are added to the
    /// language it repeats, so searching for <c>properties</c> finds INI.
    /// </summary>
    public IReadOnlyList<SyntaxLanguageInfo> UserLanguages() => _userLanguages.Value;

    private IReadOnlyList<SyntaxLanguageInfo> BuildUserLanguages()
    {
        var listed = new List<SyntaxLanguageInfo>(_entries.Count);
        foreach (Entry entry in _entries)
        {
            if (Variants.Any(variant => variant.Id == entry.Language.Id && _byId.ContainsKey(variant.ListedUnder))) continue;

            var aliases = new List<string>(entry.Aliases);
            var extensions = new List<string>(entry.Extensions);
            var names = new List<string>(entry.FileNames);
            foreach ((string id, string listedUnder) in Variants)
            {
                if (listedUnder != entry.Language.Id || !_byId.TryGetValue(id, out Entry? variant)) continue;
                aliases.Add(variant.Language.Id);
                aliases.Add(variant.Language.DisplayName);
                aliases.AddRange(variant.Aliases);
                extensions.AddRange(variant.Extensions);
                names.AddRange(variant.FileNames);
            }

            listed.Add(new SyntaxLanguageInfo(
                entry.Language,
                Distinct(aliases.Where(alias => !Same(alias, entry.Language.Id) && !Same(alias, entry.Language.DisplayName))),
                Distinct(extensions),
                Distinct(names)));
        }

        listed.Sort(static (a, b) =>
        {
            int byName = string.Compare(a.Language.DisplayName, b.Language.DisplayName, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.CompareOrdinal(a.Language.Id, b.Language.Id);
        });
        return listed.AsReadOnly();

        static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        static string[] Distinct(IEnumerable<string> values) => values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public Entry? FindById(string id) => _byId.TryGetValue(id, out Entry? entry) ? entry : null;

    public Entry? FindByScope(string scope) => _byScope.TryGetValue(scope, out Entry? entry) ? entry : null;

    public Entry? Detect(string? fileName, string? contentSample)
    {
        string name = LastSegment(fileName);
        if (name.Length > 0)
        {
            foreach (Entry entry in _entries)
                if (entry.FileNames.Contains(name))
                    return entry;

            foreach (Entry entry in _entries)
                if (entry.FileNamePatterns.Any(pattern => WildcardMatch(pattern, name)))
                    return entry;

            Entry? best = null;
            int bestLength = 0;
            foreach (Entry entry in _entries)
            {
                foreach (string extension in entry.Extensions)
                {
                    if (extension.Length > bestLength
                        && name.Length > extension.Length - (extension[0] == '.' ? 1 : 0)
                        && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        best = entry;
                        bestLength = extension.Length;
                    }
                }
            }

            if (best is not null) return best;
        }

        string firstLine = FirstLine(contentSample);
        if (firstLine.Length == 0) return ContentLanguageSniffer.Detect(contentSample) is { } sniffed ? FindById(sniffed) : null;

        foreach (Entry entry in _entries)
        {
            if (entry.FirstLine is null) continue;
            try
            {
                if (entry.FirstLine.IsMatch(firstLine)) return entry;
            }
            catch (RegexMatchTimeoutException)
            {
            }
        }

        if (DetectInterpreter(firstLine) is { } interpreted) return interpreted;

        // Last: a conservative look at the content itself, for JSON, XML and YAML only.
        return ContentLanguageSniffer.Detect(contentSample) is { } id ? FindById(id) : null;
    }

    private Entry? DetectInterpreter(string firstLine)
    {
        if (!firstLine.StartsWith("#!", StringComparison.Ordinal)) return null;

        string[] words = firstLine[2..].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return null;

        string program = LastSegment(words[0]);
        if (program == "env")
        {
            program = words.Skip(1).FirstOrDefault(w => !w.StartsWith('-')) ?? string.Empty;
            program = LastSegment(program);
        }

        string stem = program.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.');
        foreach ((string interpreter, string id) in Interpreters)
            if (string.Equals(stem, interpreter, StringComparison.Ordinal))
                return FindById(id);

        return null;
    }

    private void ReadManifests(Assembly assembly)
    {
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.EndsWith(".package.json", StringComparison.Ordinal)) continue;

            try
            {
                using Stream? stream = assembly.GetManifestResourceStream(resource);
                if (stream is null) continue;

                using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });

                if (!document.RootElement.TryGetProperty("contributes", out JsonElement contributes)
                    || !contributes.TryGetProperty("languages", out JsonElement languages)
                    || languages.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (JsonElement language in languages.EnumerateArray())
                {
                    if (!language.TryGetProperty("id", out JsonElement idElement)) continue;
                    if (idElement.GetString() is not string id || !_byId.TryGetValue(id, out Entry? entry)) continue;

                    foreach (string value in Strings(language, "filenames")) entry.FileNames.Add(value);
                    foreach (string value in Strings(language, "filenamePatterns")) entry.FileNamePatterns.Add(value);
                    foreach (string value in Strings(language, "extensions")) entry.AddExtension(value);

                    if (entry.FirstLine is null
                        && language.TryGetProperty("firstLine", out JsonElement firstLine)
                        && firstLine.GetString() is string pattern)
                    {
                        try
                        {
                            entry.FirstLine = new Regex(pattern, RegexOptions.CultureInvariant, PatternTimeout);
                        }
                        catch (ArgumentException)
                        {
                            // A pattern .NET cannot compile only loses first-line detection for that language.
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // A malformed manifest only loses the names it would have added.
            }
        }
    }

    private static IEnumerable<string> Strings(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (JsonElement item in array.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
                yield return value;
    }

    private static string LastSegment(string? path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        int slash = path.LastIndexOfAny(new[] { '/', '\\' });
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private static string FirstLine(string? sample)
    {
        if (string.IsNullOrEmpty(sample)) return string.Empty;

        ReadOnlySpan<char> span = sample.AsSpan();
        if (span.Length > 0 && span[0] == '﻿') span = span[1..];
        int end = span.IndexOfAny('\r', '\n');
        if (end >= 0) span = span[..end];
        if (span.Length > MaxFirstLineLength) span = span[..MaxFirstLineLength];
        return span.ToString();
    }

    /// <summary>Matches <c>*</c> and <c>?</c> against a whole file name, ignoring case.</summary>
    internal static bool WildcardMatch(string pattern, string name)
    {
        int p = 0, n = 0, star = -1, mark = 0;
        while (n < name.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(name[n])))
            {
                p++;
                n++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = n;
            }
            else if (star >= 0)
            {
                p = star + 1;
                n = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    internal sealed class Entry
    {
        // In the order they were declared: the first extension is the language's most characteristic one.
        private readonly List<string> _extensions = new();
        private readonly List<string> _aliases = new();

        public Entry(SyntaxLanguage language, string? scopeName)
        {
            Language = language;
            ScopeName = scopeName;
        }

        public SyntaxLanguage Language { get; }

        /// <summary>The TextMate scope of the grammar, or null for a language with a classifier of its own.</summary>
        public string? ScopeName { get; }

        /// <summary>The classifier an application registered, created on first use; null for a grammar's language.</summary>
        public Lazy<ISyntaxClassifier?>? Custom { get; set; }

        /// <summary>Precedence among equally specific claims; built-in languages have 0.</summary>
        public int Priority { get; init; }

        public IReadOnlyCollection<string> Extensions => _extensions;

        public IReadOnlyCollection<string> Aliases => _aliases;

        public void AddAlias(string alias)
        {
            if (!string.IsNullOrWhiteSpace(alias) && !_aliases.Contains(alias, StringComparer.OrdinalIgnoreCase)) _aliases.Add(alias);
        }

        public HashSet<string> FileNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> FileNamePatterns { get; } = new();

        public Regex? FirstLine { get; set; }

        public void AddExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) return;

            // A few manifests list patterns such as "*.log.?" among extensions.
            if (extension.Contains('*') || extension.Contains('?')) FileNamePatterns.Add(extension);
            else if (!_extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) _extensions.Add(extension);
        }
    }
}
