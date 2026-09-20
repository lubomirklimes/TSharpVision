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

    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _byScope = new(StringComparer.Ordinal);

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

            foreach (string extension in language.Extensions ?? new List<string>())
                entry.AddExtension(extension);
        }

        ReadManifests(typeof(RegistryOptions).Assembly);
    }

    public IReadOnlyList<Entry> Entries => _entries;

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
        if (firstLine.Length == 0) return null;

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

        return DetectInterpreter(firstLine);
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
        private readonly HashSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase);

        public Entry(SyntaxLanguage language, string scopeName)
        {
            Language = language;
            ScopeName = scopeName;
        }

        public SyntaxLanguage Language { get; }

        public string ScopeName { get; }

        public IReadOnlyCollection<string> Extensions => _extensions;

        public HashSet<string> FileNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> FileNamePatterns { get; } = new();

        public Regex? FirstLine { get; set; }

        public void AddExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) return;

            // A few manifests list patterns such as "*.log.?" among extensions.
            if (extension.Contains('*') || extension.Contains('?')) FileNamePatterns.Add(extension);
            else _extensions.Add(extension);
        }
    }
}
