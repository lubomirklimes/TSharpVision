using TSharpVision.Constants;
using TSharpVision.CodeEditor.Syntax;
using TSharpVision.CodeEditor.Syntax.TextMate;

namespace TSharpVision.CodeEditor;

/// <summary>A file editor that highlights source text by syntax while drawing.</summary>
/// <remarks>
/// <para>
/// The language is detected from the file name (and, for files without a telling name, the first line)
/// through <see cref="TextMateSyntaxService.Default"/>, and highlighting is incremental: see
/// <see cref="EditorSyntaxHighlighter"/>, which does the drawing and which an editor that is not a
/// <see cref="TFileEditor"/> can use directly. Editing, selection, undo and saving are
/// <see cref="TFileEditor"/>'s, unchanged; highlighting never writes to the buffer.
/// </para>
/// </remarks>
public class TCodeEditor : TFileEditor
{
    private const int DetectionSampleLength = 1024;

    private readonly EditorSyntaxHighlighter highlighter;
    private TextMateSyntaxService? privateService;

    /// <summary>Creates a code editor using default file-decoding options.</summary>
    public TCodeEditor(
        TRect bounds,
        TScrollBar? aHScrollBar,
        TScrollBar? aVScrollBar,
        TIndicator? aIndicator,
        string? aFileName)
        : this(bounds, aHScrollBar, aVScrollBar, aIndicator, aFileName, null)
    {
    }

    /// <summary>Creates a code editor using the supplied file-decoding options, or defaults when options are null.</summary>
    public TCodeEditor(
        TRect bounds,
        TScrollBar? aHScrollBar,
        TScrollBar? aVScrollBar,
        TIndicator? aIndicator,
        string? aFileName,
        TFileEditorOpenOptions? openOptions)
        : base(bounds, aHScrollBar, aVScrollBar, aIndicator, aFileName, openOptions)
    {
        highlighter = new EditorSyntaxHighlighter(this, TextMateSyntaxService.Default);
        DetectLanguage();
    }

    /// <summary>Gets the highlighter that draws this editor: language, colour scheme and limits.</summary>
    public EditorSyntaxHighlighter SyntaxHighlighter => highlighter;

    /// <summary>Gets the active TextMate scope name, or null when syntax highlighting is disabled.</summary>
    public string? SyntaxScopeName => (highlighter.Classifier as TextMateSyntaxClassifier)?.ScopeName;

    /// <summary>Loads a grammar file and activates its explicit or declared scope.</summary>
    /// <returns>True when the grammar was loaded and activated.</returns>
    public bool LoadGrammar(string grammarPath, string? scopeName = null)
    {
        privateService ??= new TextMateSyntaxService();
        if (!privateService.TryLoadGrammarFile(grammarPath, out string? declaredScope))
            return false;

        ISyntaxClassifier? classifier = privateService.CreateClassifierForScope(scopeName ?? declaredScope);
        highlighter.SetClassifier(classifier);
        Update(Views.ufView);
        return classifier != null;
    }

    /// <summary>Activates a registered scope, or disables syntax highlighting when null or empty.</summary>
    public void SetSyntaxScope(string? scopeName)
    {
        TextMateSyntaxService service = privateService ?? TextMateSyntaxService.Default;
        highlighter.SetClassifier(string.IsNullOrWhiteSpace(scopeName) ? null : service.CreateClassifierForScope(scopeName));
        Update(Views.ufView);
    }

    /// <inheritdoc />
    public override void Draw()
    {
        if (drawLine != delta.y)
        {
            drawPtr = LineMove(drawPtr, delta.y - drawLine);
            drawLine = delta.y;
        }

        DrawCodeLines(0, size.y, drawPtr);
    }

    /// <summary>Draws a run of syntax-highlighted logical lines beginning at a buffer offset.</summary>
    /// <param name="y">First destination row.</param>
    /// <param name="count">Number of rows to draw.</param>
    /// <param name="linePtr">Logical editor-buffer offset of the first line.</param>
    protected virtual void DrawCodeLines(int y, int count, uint linePtr)
        => highlighter.DrawLines(y, count, linePtr);

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent ev)
    {
        if (highlighter.HandleEvent(ref ev)) return;

        bool mayEdit = ev.What is Events.evKeyDown or Events.evCommand or Events.evMouseDown;
        base.HandleEvent(ref ev);

        // TEditor repaints a line it has just edited through its own non-virtual line painter, which
        // knows nothing about syntax; repaint the view so that line is highlighted again straight away.
        if (mayEdit && ev.What == Events.evNothing && highlighter.Classifier is not null)
            DrawView();
    }

    /// <inheritdoc />
    /// <remarks>Stops highlighting first, so classification still in flight can no longer reach the editor.</remarks>
    public override void ShutDown()
    {
        highlighter.Shutdown();
        base.ShutDown();
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        object result = base.Read(isStream);
        DetectLanguage();
        return result;
    }

    private void DetectLanguage()
    {
        int length = (int)Math.Min(bufLen, (uint)DetectionSampleLength);
        var sample = new char[length];
        for (int i = 0; i < length; i++) sample[i] = BufChar((uint)i);
        highlighter.SetLanguage(TextMateSyntaxService.Default.DetectLanguage(fileName, new string(sample)));
    }

    /// <summary>Creates an instance whose state will be restored by the streaming system.</summary>
    protected TCodeEditor(StreamableInit init) : base(init)
    {
        highlighter = new EditorSyntaxHighlighter(this, TextMateSyntaxService.Default);
    }

    /// <summary>Creates an uninitialized instance for stream restoration.</summary>
    public new static TStreamable Build() => new TCodeEditor(StreamableInit.streamableInit);

    /// <summary>Stream registry descriptor for <see cref="TCodeEditor"/>.</summary>
    public static readonly TStreamableClass StreamableClassTCodeEditor =
        new("TCodeEditor", () => new TCodeEditor(StreamableInit.streamableInit), 0);
}
