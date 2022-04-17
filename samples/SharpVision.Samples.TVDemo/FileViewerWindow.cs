using SharpVision;
using SharpVision.Constants;

namespace SharpVision.Samples.TVDemo;

// ---------------------------------------------------------------------------
// FileViewerModel — pure text loading logic, no UI dependency.
// Testable without any TView or TEvent.
// ---------------------------------------------------------------------------
public sealed class FileViewerModel
{
    private string[] _lines = Array.Empty<string>();

    public IReadOnlyList<string> Lines => _lines;
    public int LineCount => _lines.Length;
    public bool IsValid { get; private set; }

    public FileViewerModel() { IsValid = false; }

    // Load a file; on success IsValid=true.  On failure IsValid=false, Lines=empty.
    public void Load(string path)
    {
        try
        {
            _lines   = File.ReadAllLines(path);
            IsValid  = true;
        }
        catch
        {
            _lines  = Array.Empty<string>();
            IsValid = false;
        }
    }

    // Safe load for already-known string content (used by smoke tests).
    public void LoadLines(IEnumerable<string> lines)
    {
        _lines  = lines.ToArray();
        IsValid = true;
    }
}

// ---------------------------------------------------------------------------
// FileViewerView — TScroller subclass that renders file lines.
// Inserted inside a TWindow and receives scrollbar instances from the window.
// ---------------------------------------------------------------------------
internal sealed class FileViewerView : TScroller
{
    private readonly FileViewerModel _model;

    public FileViewerView(TRect bounds,
                          TScrollBar? hBar,
                          TScrollBar? vBar,
                          FileViewerModel model)
        : base(bounds, hBar!, vBar!)
    {
        _model = model;
        growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
        UpdateLimit();
    }

    private void UpdateLimit()
    {
        int maxWidth = 0;
        foreach (var l in _model.Lines)
            if (l.Length > maxWidth) maxWidth = l.Length;
        SetLimit(maxWidth, _model.LineCount);
    }

    public override void Draw()
    {
        var color = (char)GetColor(1);
        for (int i = 0; i < size.y; i++)
        {
            var b = new TDrawBuffer();
            b.moveChar(0, ' ', color, size.x);
            int lineIdx = delta.y + i;
            if (lineIdx < _model.LineCount)
            {
                string line = _model.Lines[lineIdx];
                // Horizontal scroll: skip delta.x chars.
                if (delta.x < line.Length)
                {
                    string visible = line.Substring(delta.x,
                        Math.Min(size.x, line.Length - delta.x));
                    b.moveStr(0, visible, color);
                }
            }
            WriteLine(0, (short)i, size.x, 1, b);
        }
    }
}

// ---------------------------------------------------------------------------
// FileViewerWindow — TWindow that holds a FileViewerView + scrollbars.
// Resizable (wfMove | wfGrow | wfClose | wfZoom).
// Opened modeless by TVDemoApp after the user picks a file.
// ---------------------------------------------------------------------------
public sealed class FileViewerWindow : TWindow
{
    internal FileViewerView View { get; }
    public FileViewerModel Model { get; }

    // Shared window number counter, same as upstream approach.
    private static int _winNumber;

    public FileViewerWindow(string fileName, string[] lines)
        : base(new TRect(0, 0, 60, 16), fileName, (ushort)++_winNumber)
    {
        options |= Views.ofTileable;
        flags = (byte)(Views.wfMove | Views.wfGrow | Views.wfClose | Views.wfZoom);

        Model = new FileViewerModel();
        Model.LoadLines(lines);

        TRect r = GetExtent();
        r.Grow(-1, -1);

        TScrollBar hBar = StandardScrollBar((ushort)(Views.sbHorizontal | Views.sbHandleKeyboard));
        TScrollBar vBar = StandardScrollBar((ushort)(Views.sbVertical   | Views.sbHandleKeyboard));

        View = new FileViewerView(r, hBar, vBar, Model);
        Insert(View);
    }

    // Convenience constructor for loading from a file path.
    public FileViewerWindow(string filePath)
        : this(System.IO.Path.GetFileName(filePath), LoadFile(filePath))
    {
    }

    private static string[] LoadFile(string path)
    {
        try   { return File.ReadAllLines(path); }
        catch { return Array.Empty<string>(); }
    }
}
