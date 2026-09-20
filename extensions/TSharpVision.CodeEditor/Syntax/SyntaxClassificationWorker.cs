namespace TSharpVision.CodeEditor.Syntax;

/// <summary>
/// An immutable description of classification work, captured on the owner's thread by
/// <see cref="SyntaxHighlightCache.CreateRequest"/>, that can run on any thread.
/// </summary>
/// <remarks>
/// It holds copies of the line texts and the (immutable) line states it needs, never the document or the
/// cache's mutable state, so running it cannot race with editing.
/// </remarks>
public sealed class SyntaxClassificationRequest
{
    private readonly string[] _lines;
    private readonly SyntaxLineState?[] _expectedStarts;

    internal SyntaxClassificationRequest(
        SyntaxHighlightCache cache, int version, int firstLine, SyntaxLineState startState,
        string[] lines, SyntaxLineState?[] expectedStarts)
    {
        Cache = cache;
        Version = version;
        FirstLine = firstLine;
        StartState = startState;
        _lines = lines;
        _expectedStarts = expectedStarts;
    }

    /// <summary>Gets the cache the request was captured from.</summary>
    public SyntaxHighlightCache Cache { get; }

    /// <summary>Gets the cache version the request belongs to.</summary>
    public int Version { get; }

    /// <summary>Gets the first line the request classifies.</summary>
    public int FirstLine { get; }

    /// <summary>Gets the number of lines the request covers at most.</summary>
    public int LineCount => _lines.Length;

    /// <summary>Gets the state flowing into <see cref="FirstLine"/>.</summary>
    public SyntaxLineState StartState { get; }

    /// <summary>
    /// Classifies the captured lines in order, carrying the state from each line to the next. Stops early
    /// where the state flowing into a line equals the state that line was cached from — the effect of an
    /// edit has converged — or when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <exception cref="OperationCanceledException">The request was cancelled between lines.</exception>
    public SyntaxClassificationResult Classify(CancellationToken cancellationToken)
    {
        var results = new List<(SyntaxLineState, SyntaxLineState, IReadOnlyList<SyntaxSpan>)>(_lines.Length);
        SyntaxLineState state = StartState;
        bool converged = false;

        for (int i = 0; i < _lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_expectedStarts[i] is { } expected && expected.Equals(state))
            {
                converged = true;
                break;
            }

            SyntaxLineResult line = Cache.Classifier.ClassifyLine(_lines[i], state);
            results.Add((state, line.EndState, line.Spans));
            state = line.EndState;
        }

        return new SyntaxClassificationResult(this, results, converged);
    }
}

/// <summary>The outcome of a <see cref="SyntaxClassificationRequest"/>, applied with <see cref="SyntaxHighlightCache.Commit"/>.</summary>
public sealed class SyntaxClassificationResult
{
    internal SyntaxClassificationResult(
        SyntaxClassificationRequest request,
        IReadOnlyList<(SyntaxLineState Start, SyntaxLineState End, IReadOnlyList<SyntaxSpan> Spans)> lines,
        bool converged)
    {
        Request = request;
        Lines = lines;
        Converged = converged;
    }

    /// <summary>Gets the request this result answers.</summary>
    public SyntaxClassificationRequest Request { get; }

    /// <summary>Gets how many lines were classified.</summary>
    public int ClassifiedLineCount => Lines.Count;

    /// <summary>Gets a value indicating whether classification stopped because the state converged.</summary>
    public bool Converged { get; }

    internal IReadOnlyList<(SyntaxLineState Start, SyntaxLineState End, IReadOnlyList<SyntaxSpan> Spans)> Lines { get; }
}

/// <summary>
/// Runs a <see cref="SyntaxHighlightCache"/>'s classification off the owner's thread, one request at a time,
/// and commits results back on it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ownership.</b> The worker, like the cache, belongs to one view and is used only on that view's
/// event-loop thread: <see cref="Request"/>, <see cref="Commit"/> and <see cref="Dispose"/>. The classification
/// itself runs through the background scheduler over an immutable <see cref="SyntaxClassificationRequest"/>;
/// its result is handed to the <c>deliver</c> callback on the background thread, and the owner must marshal it
/// to its thread (for a TSharpVision view, <c>TView.Post</c>) and call <see cref="Commit"/> there.
/// </para>
/// <para>
/// <b>Never stale.</b> A result is applied only if its request is the one currently in flight and the cache
/// still has the version and first unverified line the request was captured at. A newer version (any edit),
/// a satisfied target, a new request or <see cref="Dispose"/> cancels the request in flight; its result, if it
/// still arrives, is discarded.
/// </para>
/// <para>
/// <b>Lazy.</b> Work only ever advances from the verified prefix towards the last line the owner asked for,
/// in chunks of <see cref="MaxLinesPerRequest"/> lines, continuing after each commit until that line is
/// reached. A classifier that throws stops the work for that cache version rather than being retried in a
/// loop; the document is simply shown plain.
/// </para>
/// </remarks>
public sealed class SyntaxClassificationWorker : IDisposable
{
    /// <summary>The default for <see cref="MaxLinesPerRequest"/>.</summary>
    public const int DefaultMaxLinesPerRequest = 500;

    private readonly Action<SyntaxClassificationResult> _deliver;
    private readonly Func<Func<Task>, Task> _startBackground;
    private SyntaxClassificationRequest? _inFlight;
    private CancellationTokenSource? _inFlightCancellation;
    private ISyntaxLineSource? _source;
    private int _target = -1;
    private int _failedVersion = -1;
    private volatile Exception? _lastFailure;
    private bool _disposed;
    private int _maxLinesPerRequest = DefaultMaxLinesPerRequest;

    /// <summary>Creates a worker for <paramref name="cache"/>.</summary>
    /// <param name="cache">The cache whose lines are classified; owned by the same view.</param>
    /// <param name="deliver">Called on the background thread with each finished result; must only marshal it.</param>
    /// <param name="startBackground">Starts background work; null uses the thread pool.</param>
    public SyntaxClassificationWorker(
        SyntaxHighlightCache cache,
        Action<SyntaxClassificationResult> deliver,
        Func<Func<Task>, Task>? startBackground = null)
    {
        Cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
        _startBackground = startBackground ?? (work => Task.Run(work));
    }

    /// <summary>Gets the cache this worker fills.</summary>
    public SyntaxHighlightCache Cache { get; }

    /// <summary>Gets or sets the most lines one background request classifies; at least 1.</summary>
    public int MaxLinesPerRequest
    {
        get => _maxLinesPerRequest;
        set => _maxLinesPerRequest = Math.Max(1, value);
    }

    /// <summary>Gets a value indicating whether a request is in flight.</summary>
    public bool IsWorking => _inFlight is not null;

    /// <summary>Gets the exception the classifier last threw, or null.</summary>
    public Exception? LastFailure => _lastFailure;

    /// <summary>
    /// Asks for lines up to <paramref name="lastLine"/> to be classified. Returns at once; never classifies on
    /// the calling thread (unless the background scheduler itself runs work inline).
    /// </summary>
    public void Request(int lastLine, ISyntaxLineSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_disposed) return;

        _target = lastLine;
        _source = source;

        if (_inFlight is not null)
        {
            bool stillUseful = _inFlight.Version == Cache.Version
                               && _inFlight.FirstLine == Cache.ValidLineCount
                               && lastLine >= _inFlight.FirstLine;
            if (stillUseful) return;
            CancelInFlight();
        }

        if (Cache.Version == _failedVersion) return;

        SyntaxClassificationRequest? request = Cache.CreateRequest(lastLine, source, _maxLinesPerRequest);
        if (request is null) return;

        var cancellation = new CancellationTokenSource();
        _inFlight = request;
        _inFlightCancellation = cancellation;
        CancellationToken token = cancellation.Token;

        _ = _startBackground(() =>
        {
            SyntaxClassificationResult result;
            try
            {
                result = request.Classify(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _lastFailure = ex;
                result = new SyntaxClassificationResult(request, Array.Empty<(SyntaxLineState, SyntaxLineState, IReadOnlyList<SyntaxSpan>)>(), converged: false);
            }

            if (!token.IsCancellationRequested) _deliver(result);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Applies a delivered result on the owner's thread and continues towards the requested line.
    /// </summary>
    /// <returns>True when the result changed the cache; false when it was stale or empty.</returns>
    public bool Commit(SyntaxClassificationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (_disposed || !ReferenceEquals(result.Request, _inFlight)) return false;

        _inFlight = null;
        _inFlightCancellation?.Dispose();
        _inFlightCancellation = null;

        if (result.ClassifiedLineCount == 0 && !result.Converged)
        {
            // The classifier failed on the first line; do not retry the same version in a loop.
            _failedVersion = result.Request.Version;
            return false;
        }

        bool applied = Cache.Commit(result);
        if (applied && _source is not null && _target >= Cache.ValidLineCount) Request(_target, _source);
        return applied;
    }

    /// <summary>Cancels the work in flight; every later result is discarded.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelInFlight();
        _source = null;
    }

    private void CancelInFlight()
    {
        // Cancelled, not disposed: the background work may still be observing the token.
        _inFlightCancellation?.Cancel();
        _inFlightCancellation = null;
        _inFlight = null;
    }
}
