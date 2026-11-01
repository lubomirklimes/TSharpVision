using System.Globalization;
using System.IO;
using System.Text;

namespace TSharpVision;

/// <summary>
/// An ordered set of help files used as one help system: context lookups go to the first registered file that has
/// the context, and full-text search covers every registered file.
/// </summary>
/// <remarks>
/// <para>
/// Registration order decides context collisions: when two files define the same context, <see cref="GetTopic"/>,
/// <see cref="FindContext"/> and F1 help use the file registered first. Removing that file exposes the next one.
/// </para>
/// <para>
/// Search reads each file's topics once, on the first search after the file is registered, and keeps a case-folded
/// copy of their text in memory (about two bytes per character of help text) until the file is removed or the
/// collection is cleared or disposed. The <c>.hlp</c> files are only read, never changed. Use the collection on the
/// UI thread: topics are read through each file's shared stream.
/// </para>
/// </remarks>
public sealed class THelpCollection : IDisposable
{
    private sealed class Entry
    {
        internal THelpFile File = null!;
        internal string Name = "";
        internal Fpstream? OwnedStream;
        internal List<SearchDocument>? Documents;
    }

    private sealed class SearchDocument
    {
        internal int Context;
        internal string Title = "";
        internal string FoldedTitle = "";
        internal string FoldedText = "";
    }

    private const int MaxTitleLength = 70;
    // The help compiler writes spaces inside link text as U+00FF; U+00A0 is shown as a space too (THelpViewer.Draw).
    private const char LinkSpace = (char)0xFF, NoBreakSpace = (char)0xA0;
    private readonly List<Entry> _entries = new();

    /// <summary>Registered help files in registration order.</summary>
    public IReadOnlyList<THelpFile> Files => _entries.Select(e => e.File).ToArray();

    /// <summary>Opens a compiled help file read-only and registers it after the files already registered.</summary>
    /// <param name="path">Path of a V1 or V2 <c>.hlp</c> file.</param>
    /// <param name="name">Name that tells this file apart in search results; defaults to the file name.</param>
    /// <returns>The opened help file. The collection closes it when it is removed or the collection is disposed.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not a readable compiled help file.</exception>
    public THelpFile Open(string path, string? name = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        THelpFile.RegisterStreamableTypes();
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var stream = new Fpstream(fs, ownsStream: true);
        THelpFile file;
        try
        {
            file = new THelpFile(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            stream.Close();
            throw new InvalidDataException($"'{path}' is not a readable help file.", ex);
        }
        // THelpFile starts an empty store for a file without a help header; a read-only registration rejects it.
        if (file.modified)
        {
            stream.Close();
            throw new InvalidDataException($"'{path}' is not a compiled help file.");
        }
        _entries.Add(new Entry { File = file, Name = name ?? Path.GetFileName(path), OwnedStream = stream });
        return file;
    }

    /// <summary>Registers an open help file after the files already registered; the caller keeps its stream.</summary>
    /// <param name="file">The help file.</param>
    /// <param name="name">Name that tells this file apart in search results.</param>
    /// <exception cref="ArgumentException">The file is already registered.</exception>
    public void Add(THelpFile file, string name)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(name);
        if (IndexOf(file) >= 0)
            throw new ArgumentException("The help file is already registered.", nameof(file));
        _entries.Add(new Entry { File = file, Name = name });
    }

    /// <summary>
    /// Unregisters a help file, drops its search data and closes its stream when the collection opened it. Search
    /// results and history entries that point to it no longer open.
    /// </summary>
    /// <returns>Whether the file was registered.</returns>
    public bool Remove(THelpFile file)
    {
        int i = IndexOf(file);
        if (i < 0) return false;
        var entry = _entries[i];
        _entries.RemoveAt(i);
        entry.Documents = null;
        entry.OwnedStream?.Close();
        return true;
    }

    /// <summary>Unregisters every help file, closing the streams the collection opened.</summary>
    public void Clear()
    {
        while (_entries.Count > 0)
            Remove(_entries[^1].File);
    }

    /// <summary>Closes the help files the collection opened and unregisters all files.</summary>
    public void Dispose() => Clear();

    /// <summary>Whether the file is registered.</summary>
    public bool Contains(THelpFile file) => IndexOf(file) >= 0;

    /// <summary>The name a registered file shows in search results, or null when it is not registered.</summary>
    public string? GetName(THelpFile file)
    {
        int i = IndexOf(file);
        return i < 0 ? null : _entries[i].Name;
    }

    /// <summary>Returns the first registered file that defines the context, or null when none does.</summary>
    public THelpFile? FindContext(int context)
    {
        foreach (var entry in _entries)
            if (entry.File.index.Position(context) > 0)
                return entry.File;
        return null;
    }

    /// <summary>
    /// Loads the context's topic from the first registered file that defines it, or a "no help available" topic when
    /// none does.
    /// </summary>
    public THelpTopic GetTopic(int context)
    {
        var file = FindContext(context);
        if (file != null) return file.GetTopic(context);
        if (_entries.Count > 0) return _entries[0].File.InvalidTopic();
        var topic = new THelpTopic();
        topic.AddParagraph(new TParagraph { wrap = false, Text = THelpFile.InvalidContext });
        return topic;
    }

    /// <summary>
    /// Finds topics whose text contains every word of the query, across all registered files.
    /// </summary>
    /// <param name="query">
    /// Words separated by white space. Matching ignores case (Unicode, culture-invariant simple case mapping, so
    /// "ČTENÍ" finds "čtení") but not diacritics, and each word may occur anywhere in the topic, also inside a longer
    /// word. Characters have no special meaning; there are no operators, quotes or wildcards.
    /// </param>
    /// <param name="maxResults">Upper bound on the number of results.</param>
    /// <returns>
    /// Matches ordered by score (title occurrences weigh 8, body occurrences 1, the whole query as a phrase adds 20),
    /// then by registration order, then by context number. An empty or blank query finds nothing.
    /// </returns>
    public IReadOnlyList<THelpSearchResult> Search(string query, int maxResults = 200)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResults);
        string foldedQuery = Fold(query);
        string[] terms = foldedQuery.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (terms.Length == 0) return Array.Empty<THelpSearchResult>();
        string phrase = string.Join(' ', foldedQuery.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        var hits = new List<(THelpSearchResult Result, int Order)>();
        for (int order = 0; order < _entries.Count; order++)
        {
            var entry = _entries[order];
            foreach (var doc in Documents(entry))
            {
                int score = 0;
                bool all = true;
                foreach (string term in terms)
                {
                    int inTitle = Count(doc.FoldedTitle, term), inBody = Count(doc.FoldedText, term);
                    if (inBody == 0 && inTitle == 0) { all = false; break; }
                    score += inTitle * 8 + inBody;
                }
                if (!all) continue;
                if (terms.Length > 1 && doc.FoldedText.Contains(phrase, StringComparison.Ordinal)) score += 20;
                hits.Add((new THelpSearchResult(entry.File, entry.Name, doc.Context, doc.Title, score), order));
            }
        }
        return hits
            .OrderByDescending(h => h.Result.Score)
            .ThenBy(h => h.Order)
            .ThenBy(h => h.Result.Context)
            .Take(maxResults)
            .Select(h => h.Result)
            .ToArray();
    }

    /// <summary>Plain text of a topic: its paragraphs joined, with link spaces shown as spaces.</summary>
    internal static string PlainText(THelpTopic topic)
    {
        var sb = new StringBuilder();
        for (var p = topic.paragraphs; p != null; p = p.next)
            sb.Append(p.chars, 0, Math.Min((int)p.size, p.chars?.Length ?? 0));
        sb.Replace(LinkSpace, ' ').Replace(NoBreakSpace, ' ');
        return sb.ToString();
    }

    // Case-insensitive, culture-invariant comparison key: composed (NFC) and lower-cased, white space kept as is.
    internal static string Fold(string s)
    {
        s = s.Replace(LinkSpace, ' ').Replace(NoBreakSpace, ' ');
        if (!s.IsNormalized(NormalizationForm.FormC)) s = s.Normalize(NormalizationForm.FormC);
        return s.ToLower(CultureInfo.InvariantCulture);
    }

    internal static string TitleOf(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string t = string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (t.Length == 0) continue;
            return t.Length <= MaxTitleLength ? t : t.Substring(0, MaxTitleLength - 3) + "...";
        }
        return string.Empty;
    }

    private static int Count(string text, string term)
    {
        int n = 0;
        for (int i = text.IndexOf(term, StringComparison.Ordinal); i >= 0; i = text.IndexOf(term, i + term.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    // Reads every topic of the file once. Contexts that share a topic are listed once, under the lowest number; a
    // topic that cannot be read (a damaged file) is left out instead of failing the whole search.
    private static List<SearchDocument> Documents(Entry entry)
    {
        if (entry.Documents != null) return entry.Documents;
        var documents = new List<SearchDocument>();
        var index = entry.File.index;
        var seen = new HashSet<long>();
        for (int context = 0; context < index.size; context++)
        {
            long position = index.Position(context);
            if (position <= 0 || !seen.Add(position)) continue;
            THelpTopic? topic;
            try { topic = ReadTopic(entry.File, position); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { topic = null; }
            if (topic == null) continue;
            string text = PlainText(topic);
            string title = TitleOf(text);
            documents.Add(new SearchDocument
            {
                Context = context, Title = title, FoldedTitle = Fold(title), FoldedText = Fold(text),
            });
        }
        entry.Documents = documents;
        return documents;
    }

    // THelpFile.GetTopic without its "no help available" substitute, so damaged topics are not indexed as that text.
    private static THelpTopic? ReadTopic(THelpFile file, long position)
    {
        var input = file.stream.In;
        input.Seekg(position);
        input.HelpFormatVersion = file.formatVersion;
        input.HelpLegacyEncoding = file.LegacyEncoding;
        return input.ReadPointer() as THelpTopic;
    }

    private int IndexOf(THelpFile file)
    {
        for (int i = 0; i < _entries.Count; i++)
            if (ReferenceEquals(_entries[i].File, file)) return i;
        return -1;
    }
}

/// <summary>One topic found by <see cref="THelpCollection.Search"/>.</summary>
public sealed class THelpSearchResult
{
    internal THelpSearchResult(THelpFile file, string source, int context, string title, int score)
    {
        File = file; Source = source; Context = context; Title = title; Score = score;
    }

    /// <summary>The help file that contains the topic.</summary>
    public THelpFile File { get; }
    /// <summary>The registered name of that file, which tells apart topics with the same title.</summary>
    public string Source { get; }
    /// <summary>The topic's help context in that file.</summary>
    public int Context { get; }
    /// <summary>The first non-blank line of the topic, shortened to 70 characters.</summary>
    public string Title { get; }
    /// <summary>The ranking score; higher is a better match.</summary>
    public int Score { get; }
}
