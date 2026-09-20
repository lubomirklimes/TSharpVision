using TSharpVision.Constants;

namespace TSharpVision.CodeEditor.Syntax;

/// <summary>
/// Draws the lines of any <see cref="TEditor"/> with syntax highlighting.
/// </summary>
/// <remarks>
/// <para>
/// <b>One renderer for every editor.</b> <see cref="TCodeEditor"/> uses it, and so can an editor that
/// cannot derive from <see cref="TFileEditor"/> — one whose text comes from somewhere other than a local
/// path. The owning editor calls <see cref="Draw"/> from its <c>Draw</c> override,
/// <see cref="HandleEvent"/> first from its <c>HandleEvent</c> override, and <see cref="Shutdown"/> when it
/// closes or shuts down.
/// </para>
/// <para>
/// <b>Text, roles and colours stay apart.</b> The editor's buffer is only read. A classifier turns lines
/// into <see cref="SyntaxSpan"/> roles, cached per line; <see cref="ColorScheme"/> turns roles into
/// attributes over the editor's own palette while drawing. Nothing is written into the document, so what
/// the editor saves is exactly what was typed.
/// </para>
/// <para>
/// <b>Drawing never classifies.</b> A draw synchronises the change tracker (a memory comparison) and draws
/// each line with the spans the cache already knows to be current; a line without them is drawn as correct
/// plain text, and the lines up to the last visible one are requested from a
/// <see cref="SyntaxClassificationWorker"/>. The worker classifies an immutable snapshot of those lines off
/// the event-loop thread; the result comes back through <see cref="TView.Post"/> to the editor, where it is
/// committed only if the document has not changed since the snapshot, and the view is redrawn.
/// </para>
/// <para>
/// <b>Correct after any edit.</b> Before every draw the highlighter compares the editor's text with the
/// lines it has classified (see the tracker) and hands the cache the precise lines an edit replaced, which
/// changes the cache version. Classification resumes at the first changed line, carries the line state from
/// line to line and stops as soon as it converges. Results computed for an older version are discarded.
/// </para>
/// <para>
/// <b>Size limit.</b> A document longer than <see cref="MaxHighlightedCharacters"/> is drawn plain —
/// exactly as <c>TEditor</c> draws it — until it is short enough again, which bounds the memory the
/// highlighter adds to the document itself.
/// </para>
/// <para>
/// Selection wins over syntax colour: selected text uses the editor's selected-text attribute.
/// Lines are separated by LF, as TSharpVision editors store them.
/// </para>
/// </remarks>
public sealed class EditorSyntaxHighlighter
{
    /// <summary>The default for <see cref="MaxHighlightedCharacters"/>: 4 MiB of UTF-16 code units.</summary>
    public const int DefaultMaxHighlightedCharacters = 4 * 1024 * 1024;

    // Delivered only to the owning editor through TView.Post; the payload identifies it.
    internal const ushort cmClassificationReady = 0xFE20;

    private readonly TEditor _editor;
    private readonly EditorTextTracker _tracker;
    private Func<Func<Task>, Task>? _startBackground;
    private SyntaxHighlightCache? _cache;
    private SyntaxClassificationWorker? _worker;
    private SyntaxColorScheme _colorScheme = SyntaxColorScheme.Default;
    private int _maxLinesPerRequest = SyntaxClassificationWorker.DefaultMaxLinesPerRequest;
    private bool _shutDown;

    /// <summary>Creates a highlighter for <paramref name="editor"/>, initially drawing plain text.</summary>
    /// <param name="editor">The editor whose lines are drawn. It must forward Draw and HandleEvent.</param>
    /// <param name="syntaxService">The service used by <see cref="SetLanguage"/>.</param>
    /// <param name="startBackground">
    /// Starts classification work away from the event loop; null uses the thread pool. A scheduler that runs
    /// work inline makes drawing classify synchronously and is meant for tests only.
    /// </param>
    public EditorSyntaxHighlighter(TEditor editor, ISyntaxService syntaxService, Func<Func<Task>, Task>? startBackground = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        SyntaxService = syntaxService ?? throw new ArgumentNullException(nameof(syntaxService));
        _startBackground = startBackground;
        _tracker = new EditorTextTracker(editor);
    }

    /// <summary>Gets the editor this highlighter draws.</summary>
    public TEditor Editor => _editor;

    /// <summary>Gets the service that detects languages and supplies classifiers.</summary>
    public ISyntaxService SyntaxService { get; }

    /// <summary>Gets the language being highlighted; plain text when there is no classifier.</summary>
    public SyntaxLanguage Language { get; private set; } = SyntaxLanguage.PlainText;

    /// <summary>Gets the active classifier, or null when drawing plain text.</summary>
    public ISyntaxClassifier? Classifier => _cache?.Classifier;

    /// <summary>Gets or sets how roles become attributes.</summary>
    public SyntaxColorScheme ColorScheme
    {
        get => _colorScheme;
        set
        {
            _colorScheme = value ?? throw new ArgumentNullException(nameof(value));
            _editor.DrawView();
        }
    }

    /// <summary>Gets or sets the longest document, in UTF-16 code units, that is highlighted.</summary>
    public int MaxHighlightedCharacters { get; set; } = DefaultMaxHighlightedCharacters;

    /// <summary>Gets or sets how many lines one background classification request covers; at least 1.</summary>
    public int MaxLinesPerRequest
    {
        get => _maxLinesPerRequest;
        set
        {
            _maxLinesPerRequest = Math.Max(1, value);
            if (_worker is not null) _worker.MaxLinesPerRequest = _maxLinesPerRequest;
        }
    }

    /// <summary>Gets a value indicating whether the document is currently too large to highlight.</summary>
    public bool IsSuspended { get; private set; }

    /// <summary>Gets a value indicating whether classification work is in flight.</summary>
    public bool IsClassifying => _worker?.IsWorking == true;

    internal SyntaxHighlightCache? Cache => _cache;

    internal EditorTextTracker Tracker => _tracker;

    /// <summary>Replaces the scheduler for classifiers set from now on. For tests.</summary>
    internal Func<Func<Task>, Task>? StartBackground
    {
        get => _startBackground;
        set => _startBackground = value;
    }

    internal SyntaxClassificationWorker? Worker => _worker;

    /// <summary>Highlights <paramref name="language"/> using a classifier from <see cref="SyntaxService"/>.</summary>
    public void SetLanguage(SyntaxLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);
        SetClassifier(SyntaxService.CreateClassifier(language), language);
    }

    /// <summary>Highlights with <paramref name="classifier"/>, or draws plain text when null.</summary>
    public void SetClassifier(ISyntaxClassifier? classifier)
        => SetClassifier(classifier, classifier?.Language ?? SyntaxLanguage.PlainText);

    /// <summary>Forgets every cached classification; the next draw starts again from the first visible need.</summary>
    public void InvalidateAll()
    {
        _tracker.Reset();
        _cache?.Clear();
        _editor.DrawView();
    }

    /// <summary>
    /// Stops highlighting for good: cancels the work in flight and discards every later result. Call when the
    /// editor closes or shuts down. Safe to call more than once; the editor keeps drawing plain text.
    /// </summary>
    public void Shutdown()
    {
        _shutDown = true;
        _worker?.Dispose();
        _worker = null;
    }

    /// <summary>
    /// Draws the whole view, exactly as <c>TEditor.Draw</c> does but highlighted. Call from the editor's
    /// <c>Draw</c> override.
    /// </summary>
    public void Draw()
    {
        if (_editor.drawLine != _editor.delta.y)
        {
            _editor.drawPtr = _editor.LineMove(_editor.drawPtr, _editor.delta.y - _editor.drawLine);
            _editor.drawLine = _editor.delta.y;
        }

        DrawLines(0, _editor.size.y, _editor.drawPtr);
    }

    /// <summary>Draws <paramref name="count"/> lines starting at logical offset <paramref name="linePtr"/> into view row <paramref name="y"/>.</summary>
    public void DrawLines(int y, int count, uint linePtr)
    {
        TEditor editor = _editor;
        if (editor.buffer is null) return;

        bool highlight = PrepareForDraw();
        int lineIndex = highlight ? _tracker.LineOfPointer(linePtr) : 0;
        int lineCount = _tracker.LineCount;

        ushort color = editor.GetColor(0x0201);
        ushort normal = (byte)color;
        ushort selected = (byte)(color >> 8);
        int width = Math.Max(editor.delta.x + editor.size.x, 1);
        var cells = new TScreenChar[width];
        int lastMissing = -1;

        while (count-- > 0)
        {
            IReadOnlyList<SyntaxSpan> spans = Array.Empty<SyntaxSpan>();
            if (highlight && _cache is not null && lineIndex < lineCount && !_cache.TryGetValidSpans(lineIndex, out spans))
                lastMissing = lineIndex;

            FormatLine(cells, linePtr, width, normal, selected, spans);
            int visible = Math.Max(0, Math.Min(editor.size.x, width - editor.delta.x));
            editor.WriteLine(0, y, editor.size.x, 1, cells.AsSpan(Math.Min(editor.delta.x, width), visible));

            linePtr = editor.NextLine(linePtr);
            lineIndex++;
            y++;
        }

        if (lastMissing >= 0) _worker?.Request(lastMissing, _tracker);
    }

    /// <summary>
    /// Consumes the highlighter's own classification results. Call first from the editor's <c>HandleEvent</c>;
    /// returns true when the event was the highlighter's and has been handled.
    /// </summary>
    public bool HandleEvent(ref TEvent ev)
    {
        if (ev.What != Events.evCommand
            || ev.message.command != cmClassificationReady
            || ev.message.infoPtr is not ResultInfo info
            || !ReferenceEquals(info.Highlighter, this))
            return false;

        _editor.ClearEvent(ref ev);
        if (!_shutDown && ReferenceEquals(info.Worker, _worker) && _worker.Commit(info.Result))
            _editor.DrawView();
        return true;
    }

    private void SetClassifier(ISyntaxClassifier? classifier, SyntaxLanguage language)
    {
        _worker?.Dispose();
        _worker = null;

        Language = classifier is null ? SyntaxLanguage.PlainText : language;
        _cache = classifier is null ? null : new SyntaxHighlightCache(classifier);
        if (_cache is not null && !_shutDown)
        {
            SyntaxClassificationWorker? worker = null;
            worker = new SyntaxClassificationWorker(
                _cache,
                result => _editor.Post(cmClassificationReady, new ResultInfo(this, worker!, result)),
                _startBackground)
            {
                MaxLinesPerRequest = _maxLinesPerRequest,
            };
            _worker = worker;
        }

        _tracker.Reset();
        IsSuspended = false;
        _editor.DrawView();
    }

    private bool PrepareForDraw()
    {
        if (_cache is null) return false;

        if (_editor.bufLen > MaxHighlightedCharacters)
        {
            if (!IsSuspended)
            {
                IsSuspended = true;
                _tracker.Reset();
                _cache.Clear();
            }

            return false;
        }

        if (IsSuspended)
        {
            IsSuspended = false;
            _tracker.Reset();
            _cache.Clear();
        }

        EditorTextChange change = _tracker.Synchronize();
        switch (change.Kind)
        {
            case EditorTextChangeKind.Edit:
                _cache.ApplyEdit(change.FirstLine, change.RemovedLines, change.InsertedLines);
                break;
            case EditorTextChangeKind.Invalidate:
                _cache.Invalidate(change.FirstLine);
                break;
        }

        return true;
    }

    private void FormatLine(TScreenChar[] cells, uint linePtr, int width, ushort normal, ushort selected, IReadOnlyList<SyntaxSpan> spans)
    {
        TEditor editor = _editor;
        var buffer = new TDrawBuffer(cells);
        int tab = (int)Math.Max(1, TEditor.tabSize);
        int x = 0;
        uint p = linePtr;
        int index = 0;
        int span = 0;
        bool atEnd = false;

        while (x < width)
        {
            if (atEnd || p >= editor.bufLen)
            {
                buffer.moveChar(x++, ' ', normal, 1);
                continue;
            }

            char c = editor.BufChar(p);
            if (c is '\r' or '\n')
            {
                atEnd = true;
                continue;
            }

            ushort attr;
            if (p >= editor.selStart && p < editor.selEnd)
            {
                attr = selected;
            }
            else
            {
                while (span < spans.Count && spans[span].End <= index) span++;
                attr = span < spans.Count && spans[span].Start <= index
                    ? _colorScheme.Apply(spans[span].Class, normal)
                    : normal;
            }

            if (c == '\t')
            {
                int next = x + tab - (x % tab);
                while (x < next && x < width) buffer.moveChar(x++, ' ', attr, 1);
            }
            else
            {
                buffer.moveChar(x++, c, attr, 1);
            }

            p++;
            index++;
        }
    }

    private sealed class ResultInfo : IInfo
    {
        public ResultInfo(EditorSyntaxHighlighter highlighter, SyntaxClassificationWorker worker, SyntaxClassificationResult result)
        {
            Highlighter = highlighter;
            Worker = worker;
            Result = result;
        }

        public EditorSyntaxHighlighter Highlighter { get; }

        public SyntaxClassificationWorker Worker { get; }

        public SyntaxClassificationResult Result { get; }
    }
}
