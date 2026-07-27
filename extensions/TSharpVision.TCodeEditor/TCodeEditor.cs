using System.Text;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TSharpVision.Constants;

namespace TSharpVision.TCodeEditor;

public class TCodeEditor : TFileEditor
{
    private static readonly TimeSpan TokenizeTimeout = TimeSpan.FromMilliseconds(100);
    private readonly TextMateRegistryOptions registryOptions = new();
    private readonly Registry registry;
    private IGrammar? grammar;

    public TCodeEditor(
        TRect bounds,
        TScrollBar aHScrollBar,
        TScrollBar aVScrollBar,
        TIndicator aIndicator,
        string aFileName)
        : this(bounds, aHScrollBar, aVScrollBar, aIndicator, aFileName, null!)
    {
    }

    public TCodeEditor(
        TRect bounds,
        TScrollBar aHScrollBar,
        TScrollBar aVScrollBar,
        TIndicator aIndicator,
        string aFileName,
        TFileEditorOpenOptions openOptions)
        : base(bounds, aHScrollBar, aVScrollBar, aIndicator, aFileName, openOptions)
    {
        registry = new Registry(registryOptions);
        SyntaxScopeName = GuessScopeName(aFileName);
    }

    public string? SyntaxScopeName { get; private set; }

    public bool LoadGrammar(string grammarPath, string? scopeName = null)
    {
        if (!registryOptions.TryLoadGrammar(grammarPath))
            return false;

        SyntaxScopeName = scopeName ?? GuessScopeName(grammarPath);
        grammar = !string.IsNullOrWhiteSpace(SyntaxScopeName)
            ? registry.LoadGrammar(SyntaxScopeName)
            : null;
        Update(Views.ufView);
        return grammar != null;
    }

    public void SetSyntaxScope(string? scopeName)
    {
        SyntaxScopeName = scopeName;
        grammar = !string.IsNullOrWhiteSpace(SyntaxScopeName)
            ? registry.LoadGrammar(SyntaxScopeName)
            : null;
        Update(Views.ufView);
    }

    public override void Draw()
    {
        if (drawLine != delta.y)
        {
            drawPtr = LineMove(drawPtr, delta.y - drawLine);
            drawLine = delta.y;
        }

        DrawCodeLines(0, size.y, drawPtr);
    }

    public void DrawCodeLines(int y, int count, uint linePtr)
    {
        ushort normal = GetColor(0x0201);
        IStateStack? stateStack = null;

        uint tokenizerPtr = 0;
        while (tokenizerPtr < linePtr)
        {
            string text = GetLineText(tokenizerPtr);
            stateStack = Tokenize(text, stateStack, out _);
            tokenizerPtr = NextLine(tokenizerPtr);
        }

        while (count-- > 0)
        {
            string text = GetLineText(linePtr);
            stateStack = Tokenize(text, stateStack, out IToken[] tokens);

            var b = new TDrawBuffer();
            FormatCodeLine(b, linePtr, text, tokens, delta.x + size.x, normal);
            WriteLine(0, y, size.x, 1, Slice(b, delta.x, size.x));

            linePtr = NextLine(linePtr);
            y++;
        }
    }

    private IStateStack? Tokenize(string text, IStateStack? stateStack, out IToken[] tokens)
    {
        tokens = Array.Empty<IToken>();
        if (grammar == null)
            return stateStack;

        try
        {
            ITokenizeLineResult result = grammar.TokenizeLine(text, stateStack, TokenizeTimeout);
            tokens = result.Tokens ?? Array.Empty<IToken>();
            return result.RuleStack;
        }
        catch
        {
            grammar = null;
            return null;
        }
    }

    private void FormatCodeLine(
        TDrawBuffer b,
        uint linePtr,
        string text,
        IToken[] tokens,
        int width,
        ushort normal)
    {
        int x = 0;
        int index = 0;
        while (x < width)
        {
            if (index >= text.Length)
            {
                b.moveChar(x++, ' ', normal, 1);
                continue;
            }

            char c = text[index];
            ushort attr = GetTokenAttribute(tokens, index, normal);
            uint ptr = linePtr + (uint)index;
            if (ptr >= selStart && ptr < selEnd)
                attr = Invert(attr);

            if (c == '\t')
            {
                int next = x + (int)tabSize - (x % (int)tabSize);
                while (x < next && x < width)
                    b.moveChar(x++, ' ', attr, 1);
            }
            else
            {
                b.moveChar(x++, c, attr, 1);
            }

            index++;
        }
    }

    private ushort GetTokenAttribute(IToken[] tokens, int index, ushort normal)
    {
        foreach (IToken token in tokens)
            if (index >= token.StartIndex && index < token.EndIndex)
                return WithForeground(normal, ScopeColor(token.Scopes));

        return normal;
    }

    private static byte ScopeColor(IEnumerable<string> scopes)
    {
        foreach (string scope in scopes.Reverse())
        {
            if (scope.Contains("comment", StringComparison.Ordinal))
                return Colors.fgDarkGray;
            if (scope.Contains("string", StringComparison.Ordinal))
                return Colors.fgLightGreen;
            if (scope.Contains("constant.numeric", StringComparison.Ordinal)
                || scope.Contains("constant.language", StringComparison.Ordinal))
                return Colors.fgLightMagenta;
            if (scope.Contains("keyword", StringComparison.Ordinal)
                || scope.Contains("storage", StringComparison.Ordinal))
                return Colors.fgLightCyan;
            if (scope.Contains("entity.name.function", StringComparison.Ordinal)
                || scope.Contains("support.function", StringComparison.Ordinal))
                return Colors.fgYellow;
            if (scope.Contains("entity.name.type", StringComparison.Ordinal)
                || scope.Contains("support.type", StringComparison.Ordinal))
                return Colors.fgLightBlue;
            if (scope.Contains("variable", StringComparison.Ordinal))
                return Colors.fgWhite;
        }

        return Colors.fgLightGray;
    }

    private string GetLineText(uint linePtr)
    {
        uint end = LineEnd(linePtr);
        var sb = new StringBuilder((int)(end - linePtr));
        for (uint p = linePtr; p < end; p++)
            sb.Append((char)BufChar(p));
        return sb.ToString();
    }

    private static ushort WithForeground(ushort attr, byte foreground)
        => (ushort)((attr & Colors.bgMask) | foreground);

    private static ushort Invert(ushort attr)
        => (ushort)(((attr & Colors.fgMask) << 4) | ((attr & Colors.bgMask) >> 4));

    private static Span<TScreenChar> Slice(TDrawBuffer b, int start, int count)
    {
        if (start >= b.Length)
            return Span<TScreenChar>.Empty;

        count = Math.Min(count, b.Length - start);
        return b.Data.Slice(start, count);
    }

    private static string? GuessScopeName(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".cs" => "source.cs",
            ".fs" => "source.fsharp",
            ".vb" => "source.vbnet",
            ".js" => "source.js",
            ".ts" => "source.ts",
            ".json" => "source.json",
            ".xml" => "text.xml",
            ".html" or ".htm" => "text.html.basic",
            ".css" => "source.css",
            ".md" or ".markdown" => "text.html.markdown",
            ".ps1" => "source.powershell",
            ".csproj" or ".props" or ".targets" => "text.xml",
            _ => null
        };
    }

    protected TCodeEditor(StreamableInit init) : base(init)
    {
        registry = new Registry(registryOptions);
    }

    public new static TStreamable Build() => new TCodeEditor(StreamableInit.streamableInit);

    public static readonly TStreamableClass StreamableClassTCodeEditor =
        new("TCodeEditor", () => new TCodeEditor(StreamableInit.streamableInit), 0);
}
