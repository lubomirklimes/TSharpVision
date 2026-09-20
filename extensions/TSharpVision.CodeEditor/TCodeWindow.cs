using TSharpVision.Constants;

namespace TSharpVision.CodeEditor;

/// <summary>An editor window whose document control is a syntax-aware <see cref="TCodeEditor"/>.</summary>
public class TCodeWindow : TEditWindow
{
    /// <summary>Creates a code window using default file-decoding options.</summary>
    public TCodeWindow(TRect bounds, string? fileName, int aNumber)
        : this(bounds, fileName, aNumber, null)
    {
    }

    /// <summary>Creates a code window using the supplied decoding options, or defaults when options are null.</summary>
    public TCodeWindow(
        TRect bounds,
        string? fileName,
        int aNumber,
        TFileEditorOpenOptions? openOptions)
        : base(bounds, fileName, aNumber, openOptions)
    {
        state &= unchecked((ushort)~Views.sfShadow);

        TFileEditor oldEditor = editor
            ?? throw new InvalidOperationException("The base editor window did not create an editor.");
        TRect editorBounds = oldEditor.GetBounds();
        TScrollBar? hScrollBar = oldEditor.hScrollBar;
        TScrollBar? vScrollBar = oldEditor.vScrollBar;
        TIndicator? indicator = oldEditor.indicator;

        Remove(oldEditor);
        editor = new TCodeEditor(editorBounds, hScrollBar, vScrollBar, indicator, fileName, openOptions);
        Insert(editor);
    }

    /// <summary>Gets the syntax-aware editor owned by this window.</summary>
    public TCodeEditor CodeEditor => editor as TCodeEditor
        ?? throw new InvalidOperationException("The code editor is not initialized.");

    /// <summary>Loads and activates a TextMate grammar for the owned editor.</summary>
    public bool LoadGrammar(string grammarPath, string? scopeName = null)
        => CodeEditor.LoadGrammar(grammarPath, scopeName);

    /// <summary>Creates an instance whose state will be restored by the streaming system.</summary>
    protected TCodeWindow(StreamableInit init) : base(init)
    {
    }

    /// <summary>Creates an uninitialized instance for stream restoration.</summary>
    public new static TStreamable Build() => new TCodeWindow(StreamableInit.streamableInit);

    /// <summary>Stream registry descriptor for <see cref="TCodeWindow"/>.</summary>
    public static readonly TStreamableClass StreamableClassTCodeWindow =
        new("TCodeWindow", () => new TCodeWindow(StreamableInit.streamableInit), 0);
}
