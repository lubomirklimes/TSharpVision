using System.Text;
using TSharpVision.Constants;
using TSharpVision.CodeEditor.Syntax;
using TSharpVision.Tests.Infrastructure;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TSharpVision.CodeEditor.Tests;

/// <summary>
/// A tiny deterministic classifier: <c>/* ... */</c> comments that span lines, the keywords <c>if</c> and
/// <c>else</c>, and digits. Every call is recorded, so a test can say exactly which lines were classified.
/// </summary>
internal sealed class ToyClassifier : ISyntaxClassifier
{
    public static readonly SyntaxLanguage ToyLanguage = new("toy", "Toy");

    public List<string> Calls { get; } = new();

    public SyntaxLanguage Language => ToyLanguage;

    public SyntaxLineState InitialState => ToyState.Code;

    public SyntaxLineResult ClassifyLine(string lineText, SyntaxLineState startState)
    {
        Calls.Add(lineText);
        bool inComment = ((ToyState)startState).InComment;
        var spans = new List<SyntaxSpan>();
        int i = 0;
        while (i < lineText.Length)
        {
            if (inComment)
            {
                int close = lineText.IndexOf("*/", i, StringComparison.Ordinal);
                int end = close < 0 ? lineText.Length : close + 2;
                spans.Add(new SyntaxSpan(i, end - i, SyntaxClass.Comment));
                i = end;
                inComment = close < 0;
                continue;
            }

            if (string.CompareOrdinal(lineText, i, "/*", 0, 2) == 0)
            {
                inComment = true;
                continue;
            }

            if (char.IsDigit(lineText[i]))
            {
                int start = i;
                while (i < lineText.Length && char.IsDigit(lineText[i])) i++;
                spans.Add(new SyntaxSpan(start, i - start, SyntaxClass.Number));
                continue;
            }

            if (char.IsLetter(lineText[i]))
            {
                int start = i;
                while (i < lineText.Length && char.IsLetter(lineText[i])) i++;
                string word = lineText[start..i];
                if (word is "if" or "else") spans.Add(new SyntaxSpan(start, i - start, SyntaxClass.Keyword));
                continue;
            }

            i++;
        }

        return new SyntaxLineResult(spans, inComment ? ToyState.Comment : ToyState.Code);
    }

    private sealed class ToyState : SyntaxLineState
    {
        public static readonly ToyState Code = new(false);
        public static readonly ToyState Comment = new(true);

        private ToyState(bool inComment) => InComment = inComment;

        public bool InComment { get; }

        public override bool Equals(object? obj) => obj is ToyState other && other.InComment == InComment;

        public override int GetHashCode() => InComment ? 1 : 0;
    }
}

/// <summary>Lines held in a list, for cache tests that do not need an editor.</summary>
internal sealed class ListLineSource : ISyntaxLineSource
{
    public ListLineSource(IEnumerable<string> lines) => Lines = lines.ToList();

    public List<string> Lines { get; }

    public int LineCount => Lines.Count;

    public string GetLine(int index) => Lines[index];
}

/// <summary>
/// A <see cref="TCodeEditor"/> on a headless screen, drawn into a real buffer and read back.
/// </summary>
/// <remarks>
/// A code editor with no file name is an empty, unnamed <see cref="TFileEditor"/>, whose buffer grows,
/// so documents of any test size fit.
/// </remarks>
internal sealed class EditorScreen : IDisposable
{
    private readonly DriverScope _driver;
    private readonly SynchronizationContext? _savedContext = SynchronizationContext.Current;

    public EditorScreen(int width = 40, int height = 8)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        _driver = new DriverScope((ushort)width, (ushort)height);
        TEventQueue.ClearPosted();
        TEventQueue.ClaimUiThread();

        Width = width;
        Height = height;
        Host = new TestGroup(new TRect(0, 0, width, height)) { buffer = new ScreenBuffer(width * height) };
        Host.state |= (ushort)(Views.sfVisible | Views.sfExposed);

        Editor = new TCodeEditor(new TRect(0, 0, width, height), null, null, null, null);

        // Classification normally runs on the thread pool. Inline here, so a draw classifies synchronously and
        // the result arrives as a post the test delivers with Pump - no waiting.
        Editor.SyntaxHighlighter.StartBackground = work => work();
        Host.Insert(Editor);
    }

    public int Width { get; }

    public int Height { get; }

    public TestGroup Host { get; }

    public TCodeEditor Editor { get; }

    public EditorSyntaxHighlighter Syntax => Editor.SyntaxHighlighter;

    public void Load(string text)
    {
        Editor.SetBufLen(0);
        Editor.InsertText(text, selectText: false);
        Editor.SetCurPtr(0, 0);
        Editor.modified = false;
    }

    public string Text
    {
        get
        {
            var builder = new StringBuilder((int)Editor.bufLen);
            for (uint p = 0; p < Editor.bufLen; p++) builder.Append(Editor.BufChar(p));
            return builder.ToString();
        }
    }

    public void Draw()
    {
        Editor.Draw();
        Pump();
    }

    public static void Pump()
    {
        while (TEventQueue.DeliverPostedEvent())
        {
        }
    }

    public void Key(ushort keyCode, char charCode = '\0')
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        ev.keyDown.charScan.charCode = (byte)charCode;
        if (charCode >= ' ') ev.keyDown.text = charCode.ToString();
        Editor.HandleEvent(ref ev);
        Pump();
    }

    public void Type(string text)
    {
        foreach (char c in text) Key((ushort)c, c);
    }

    public string Row(int y)
    {
        Span<TScreenChar> cells = Host.buffer!.Data;
        var line = new StringBuilder(Width);
        for (int x = 0; x < Width; x++)
        {
            char c = cells[(y * Width) + x].Character;
            line.Append(c == '\0' ? ' ' : c);
        }

        return line.ToString().TrimEnd();
    }

    public byte AttributeAt(int x, int y) => (byte)Host.buffer!.Data[(y * Width) + x].Attr;

    public void Dispose()
    {
        TEventQueue.ClearPosted();
        _driver.Dispose();
        SynchronizationContext.SetSynchronizationContext(_savedContext);
    }
}
