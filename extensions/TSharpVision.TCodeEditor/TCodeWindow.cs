using TSharpVision.Constants;

namespace TSharpVision.TCodeEditor;

public class TCodeWindow : TEditWindow
{
    public TCodeWindow(TRect bounds, string fileName, int aNumber)
        : this(bounds, fileName, aNumber, null!)
    {
    }

    public TCodeWindow(
        TRect bounds,
        string fileName,
        int aNumber,
        TFileEditorOpenOptions openOptions)
        : base(bounds, fileName, aNumber, openOptions)
    {
        state &= unchecked((ushort)~Views.sfShadow);

        TFileEditor oldEditor = editor;
        TRect editorBounds = oldEditor.GetBounds();
        TScrollBar hScrollBar = oldEditor.hScrollBar;
        TScrollBar vScrollBar = oldEditor.vScrollBar;
        TIndicator indicator = oldEditor.indicator;

        Remove(oldEditor);
        editor = new TCodeEditor(editorBounds, hScrollBar, vScrollBar, indicator, fileName, openOptions);
        Insert(editor);
    }

    public TCodeEditor CodeEditor => (TCodeEditor)editor;

    public bool LoadGrammar(string grammarPath, string? scopeName = null)
        => CodeEditor.LoadGrammar(grammarPath, scopeName);

    protected TCodeWindow(StreamableInit init) : base(init)
    {
    }

    public new static TStreamable Build() => new TCodeWindow(StreamableInit.streamableInit);

    public static readonly TStreamableClass StreamableClassTCodeWindow =
        new("TCodeWindow", () => new TCodeWindow(StreamableInit.streamableInit), 0);
}
