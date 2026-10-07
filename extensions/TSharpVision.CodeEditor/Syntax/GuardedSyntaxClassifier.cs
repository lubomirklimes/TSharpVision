namespace TSharpVision.CodeEditor.Syntax;

/// <summary>
/// A classifier supplied by an application, made safe to share: calls are serialised, and a failure classifies
/// the line as plain instead of reaching the view that is drawing.
/// </summary>
/// <remarks>
/// A registered classifier serves every document of its language, and a viewer classifies off the UI thread, so
/// two calls can arrive at once; <see cref="ISyntaxClassifier"/> promises its implementer one thread at a time,
/// and this is where that promise is kept. A line the classifier fails on — an exception, a missing result, spans
/// outside the line — is plain and hands on its start state, as a TextMate grammar's failure does.
/// </remarks>
internal sealed class GuardedSyntaxClassifier : ISyntaxClassifier
{
    private readonly ISyntaxClassifier _inner;
    private readonly object _sync = new();

    private GuardedSyntaxClassifier(SyntaxLanguage language, ISyntaxClassifier inner, SyntaxLineState initialState)
    {
        Language = language;
        _inner = inner;
        InitialState = initialState;
    }

    /// <summary>Runs <paramref name="factory"/>; null when it fails or yields nothing usable.</summary>
    public static ISyntaxClassifier? Create(SyntaxLanguage language, Func<ISyntaxClassifier> factory)
    {
        try
        {
            ISyntaxClassifier? inner = factory();
            SyntaxLineState? initial = inner?.InitialState;
            return inner is null || initial is null ? null : new GuardedSyntaxClassifier(language, inner, initial);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    public SyntaxLanguage Language { get; }

    public SyntaxLineState InitialState { get; }

    public SyntaxLineResult ClassifyLine(string lineText, SyntaxLineState startState)
    {
        ArgumentNullException.ThrowIfNull(lineText);
        ArgumentNullException.ThrowIfNull(startState);

        try
        {
            SyntaxLineResult result;
            lock (_sync)
            {
                result = _inner.ClassifyLine(lineText, startState);
            }

            if (result.Spans is not null && result.EndState is not null && WellFormed(result.Spans, lineText.Length))
                return result;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }

        return new SyntaxLineResult(Array.Empty<SyntaxSpan>(), startState);
    }

    /// <summary>Whether the spans are in ascending order, do not overlap and lie inside the line.</summary>
    private static bool WellFormed(IReadOnlyList<SyntaxSpan> spans, int lineLength)
    {
        int end = 0;
        for (int i = 0; i < spans.Count; i++)
        {
            SyntaxSpan span = spans[i];
            if (span.Start < end || span.Length < 0 || span.Length > lineLength - span.Start) return false;
            end = span.End;
        }

        return true;
    }
}
