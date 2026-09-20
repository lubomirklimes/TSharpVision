namespace TSharpVision.CodeEditor.Syntax;

/// <summary>
/// The incremental classification of one document: per-line spans and the line states between them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lazy.</b> Nothing is classified until a line is asked for, and then only the lines from the
/// last verified line up to it. Scrolling back reuses what is cached; scrolling far ahead classifies
/// the lines in between once, because a line's state depends on every line before it.
/// </para>
/// <para>
/// <b>Bounded work per request.</b> <see cref="TryGetSpans"/> classifies at most
/// <see cref="MaxLinesPerRequest"/> lines. When that is not enough it returns false after keeping the
/// progress it made, so a view can draw the line plain and ask again later instead of stalling.
/// </para>
/// <para>
/// <b>Edits.</b> <see cref="ApplyEdit"/> discards the lines an edit replaced and shifts the lines after
/// it. Those shifted lines are kept but unverified: when classification reaches one of them and the
/// state flowing into it equals the state it was classified from, the edit's effect has converged,
/// that line and the unbroken run of cached lines after it become valid again without being
/// re-classified. When the states differ — an edit opened a block comment, say — classification
/// simply continues line by line until they agree.
/// </para>
/// <para>
/// <b>Never stale.</b> Every line past an edit is re-verified before its spans are returned, and
/// <see cref="Version"/> changes with every invalidation, so a caller holding spans from before an
/// edit can tell they belong to an older document.
/// </para>
/// <para>
/// Not thread-safe: a cache belongs to the view that draws its document.
/// </para>
/// </remarks>
public sealed class SyntaxHighlightCache
{
    /// <summary>The default for <see cref="MaxLinesPerRequest"/>.</summary>
    public const int DefaultMaxLinesPerRequest = 2000;

    private readonly List<Entry?> _entries = new();
    private int _valid;
    private int _maxLinesPerRequest = DefaultMaxLinesPerRequest;

    /// <summary>Creates an empty cache for documents classified by <paramref name="classifier"/>.</summary>
    public SyntaxHighlightCache(ISyntaxClassifier classifier)
    {
        Classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    }

    /// <summary>Gets the classifier the cache runs.</summary>
    public ISyntaxClassifier Classifier { get; }

    /// <summary>Gets or sets the most lines one <see cref="TryGetSpans"/> call may classify; at least 1.</summary>
    public int MaxLinesPerRequest
    {
        get => _maxLinesPerRequest;
        set => _maxLinesPerRequest = Math.Max(1, value);
    }

    /// <summary>Gets how many times a line has been classified since the cache was created.</summary>
    public long ClassifiedLineCount { get; private set; }

    /// <summary>Gets the number of lines, from the first, whose cached classification is known to be current.</summary>
    public int ValidLineCount => _valid;

    /// <summary>Gets a number that changes whenever cached classification is discarded or shifted.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Gets the spans of line <paramref name="lineIndex"/>, classifying the lines before it as needed.
    /// </summary>
    /// <param name="lineIndex">Zero-based line index.</param>
    /// <param name="source">The document's current lines.</param>
    /// <param name="spans">The spans; empty for a line outside the document.</param>
    /// <returns>False when the per-request budget ran out first; call again to continue.</returns>
    public bool TryGetSpans(int lineIndex, ISyntaxLineSource source, out IReadOnlyList<SyntaxSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(source);
        spans = Array.Empty<SyntaxSpan>();

        int lineCount = source.LineCount;
        if (_entries.Count > lineCount) _entries.RemoveRange(lineCount, _entries.Count - lineCount);
        if (_valid > lineCount) _valid = lineCount;
        if (lineIndex < 0 || lineIndex >= lineCount) return true;

        int budget = _maxLinesPerRequest;
        int i = _valid;
        while (i <= lineIndex)
        {
            SyntaxLineState start = i == 0 ? Classifier.InitialState : _entries[i - 1]!.End;
            Entry? cached = i < _entries.Count ? _entries[i] : null;

            if (cached is not null && cached.Start.Equals(start))
            {
                // Converged: this line and the run of cached lines after it were classified by an
                // unbroken chain starting from this same state, over text an edit did not touch.
                int run = i + 1;
                while (run < _entries.Count && _entries[run] is not null) run++;
                _valid = run;
                i = run;
                continue;
            }

            if (budget-- == 0)
            {
                _valid = i;
                return false;
            }

            SyntaxLineResult result = Classifier.ClassifyLine(source.GetLine(i), start);
            ClassifiedLineCount++;
            Set(i, new Entry(start, result.EndState, result.Spans));
            i++;
            _valid = i;
        }

        spans = _entries[lineIndex]!.Spans;
        return true;
    }

    /// <summary>
    /// Gets the spans of line <paramref name="lineIndex"/> only if they are already known to be current.
    /// Never classifies; this is what drawing uses.
    /// </summary>
    /// <returns>False when the line has not been (re)classified yet; draw it plain and request work.</returns>
    public bool TryGetValidSpans(int lineIndex, out IReadOnlyList<SyntaxSpan> spans)
    {
        if (lineIndex >= 0 && lineIndex < _valid && lineIndex < _entries.Count && _entries[lineIndex] is { } entry)
        {
            spans = entry.Spans;
            return true;
        }

        spans = Array.Empty<SyntaxSpan>();
        return false;
    }

    /// <summary>
    /// Captures, on the owner's thread, everything needed to classify the next lines towards
    /// <paramref name="lastLine"/> somewhere else: the cache version, the first unverified line, the state
    /// flowing into it, copies of the line texts and the states the cached lines after an edit were
    /// classified from (so the work can stop where the edit's effect converges).
    /// </summary>
    /// <param name="lastLine">The last line the owner needs.</param>
    /// <param name="source">The document's current lines, read now and not afterwards.</param>
    /// <param name="maxLines">The most lines the request may cover; at least 1.</param>
    /// <returns>Null when nothing up to <paramref name="lastLine"/> needs classifying.</returns>
    public SyntaxClassificationRequest? CreateRequest(int lastLine, ISyntaxLineSource source, int maxLines)
    {
        ArgumentNullException.ThrowIfNull(source);

        int lineCount = source.LineCount;
        if (_entries.Count > lineCount) _entries.RemoveRange(lineCount, _entries.Count - lineCount);
        if (_valid > lineCount) _valid = lineCount;

        // Lines already classified by an unbroken chain are accepted here, on the owner's thread, before
        // any work is handed out.
        AdvanceThroughConvergedRun();

        lastLine = Math.Min(lastLine, lineCount - 1);
        if (lastLine < _valid) return null;

        int first = _valid;
        int count = Math.Min(Math.Max(1, maxLines), lastLine - first + 1);
        var lines = new string[count];
        var expected = new SyntaxLineState?[count];
        for (int i = 0; i < count; i++)
        {
            lines[i] = source.GetLine(first + i);
            int index = first + i;
            expected[i] = index < _entries.Count ? _entries[index]?.Start : null;
        }

        SyntaxLineState start = first == 0 ? Classifier.InitialState : _entries[first - 1]!.End;
        return new SyntaxClassificationRequest(this, Version, first, start, lines, expected);
    }

    /// <summary>
    /// Applies a result on the owner's thread when, and only when, it still describes this cache: the same
    /// version (no edit, invalidation or clear since the request) and the same first unverified line.
    /// </summary>
    /// <returns>True when the result was applied; false when it was stale and has been discarded.</returns>
    public bool Commit(SyntaxClassificationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        SyntaxClassificationRequest request = result.Request;
        if (!ReferenceEquals(request.Cache, this) || request.Version != Version || request.FirstLine != _valid)
            return false;

        for (int i = 0; i < result.Lines.Count; i++)
        {
            (SyntaxLineState start, SyntaxLineState end, IReadOnlyList<SyntaxSpan> spans) = result.Lines[i];
            Set(request.FirstLine + i, new Entry(start, end, spans));
        }

        ClassifiedLineCount += result.Lines.Count;
        _valid = request.FirstLine + result.Lines.Count;
        if (result.Converged) AdvanceThroughConvergedRun();
        return true;
    }

    private void AdvanceThroughConvergedRun()
    {
        if (_valid >= _entries.Count || _entries[_valid] is not { } cached) return;

        SyntaxLineState start = _valid == 0 ? Classifier.InitialState : _entries[_valid - 1]!.End;
        if (!cached.Start.Equals(start)) return;

        int run = _valid + 1;
        while (run < _entries.Count && _entries[run] is not null) run++;
        _valid = run;
    }

    /// <summary>
    /// Records that lines <paramref name="firstLine"/> to <c>firstLine + removedLineCount - 1</c> were
    /// replaced by <paramref name="insertedLineCount"/> new lines, and every line after them is
    /// unchanged apart from its index.
    /// </summary>
    public void ApplyEdit(int firstLine, int removedLineCount, int insertedLineCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstLine);
        ArgumentOutOfRangeException.ThrowIfNegative(removedLineCount);
        ArgumentOutOfRangeException.ThrowIfNegative(insertedLineCount);

        Version++;
        if (firstLine < _valid) _valid = firstLine;
        if (firstLine >= _entries.Count) return;

        int removed = Math.Min(removedLineCount, _entries.Count - firstLine);
        _entries.RemoveRange(firstLine, removed);
        if (insertedLineCount > 0)
            _entries.InsertRange(firstLine, Enumerable.Repeat<Entry?>(null, insertedLineCount));
    }

    /// <summary>Discards the classification of line <paramref name="firstLine"/> and every line after it.</summary>
    public void Invalidate(int firstLine)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstLine);

        Version++;
        if (firstLine < _valid) _valid = firstLine;
        if (firstLine < _entries.Count) _entries.RemoveRange(firstLine, _entries.Count - firstLine);
    }

    /// <summary>Discards everything.</summary>
    public void Clear() => Invalidate(0);

    private void Set(int index, Entry entry)
    {
        while (_entries.Count <= index) _entries.Add(null);
        _entries[index] = entry;
    }

    private sealed class Entry
    {
        public Entry(SyntaxLineState start, SyntaxLineState end, IReadOnlyList<SyntaxSpan> spans)
        {
            Start = start;
            End = end;
            Spans = spans;
        }

        public SyntaxLineState Start { get; }

        public SyntaxLineState End { get; }

        public IReadOnlyList<SyntaxSpan> Spans { get; }
    }
}
