using TextMateSharp.Grammars;

namespace TSharpVision.CodeEditor.Syntax.TextMate;

/// <summary>A TextMate rule stack wrapped as the backend-neutral line state.</summary>
internal sealed class TextMateLineState : SyntaxLineState
{
    public static readonly TextMateLineState Initial = new(null);

    public TextMateLineState(IStateStack? stack)
    {
        Stack = stack;
    }

    public IStateStack? Stack { get; }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj)) return true;
        if (obj is not TextMateLineState other) return false;
        if (Stack is null || other.Stack is null) return Stack is null && other.Stack is null;
        return ReferenceEquals(Stack, other.Stack) || Stack.Equals(other.Stack);
    }

    public override int GetHashCode() => Stack?.GetHashCode() ?? 0;
}

/// <summary>
/// Classifies lines with one TextMate grammar and maps the scopes to <see cref="SyntaxClass"/> roles.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bounded per line.</b> A line longer than <see cref="MaxLineLength"/> is not tokenized: it is plain
/// and passes its start state through unchanged, which keeps a pathological line (minified output,
/// base64) from stalling the view that asked. Tokenizing is also limited in time; a line TextMate stops
/// early is kept partially classified and — as VS Code does — hands on its <em>start</em> state, so a
/// half-finished rule stack never leaks into the lines after it.
/// </para>
/// <para>
/// <b>Never throws while drawing.</b> A grammar that fails on a line yields a plain line.
/// </para>
/// </remarks>
internal sealed class TextMateSyntaxClassifier : ISyntaxClassifier
{
    public const int MaxLineLength = 10_000;
    public static readonly TimeSpan LineTimeLimit = TimeSpan.FromSeconds(1);

    private readonly IGrammar _grammar;
    private readonly object _gate;

    public TextMateSyntaxClassifier(SyntaxLanguage language, string scopeName, IGrammar grammar, object gate)
    {
        Language = language;
        ScopeName = scopeName;
        _grammar = grammar;
        _gate = gate;
    }

    public SyntaxLanguage Language { get; }

    /// <summary>The TextMate scope of the grammar, for <see cref="TCodeEditor.SyntaxScopeName"/>.</summary>
    public string ScopeName { get; }

    public SyntaxLineState InitialState => TextMateLineState.Initial;

    public SyntaxLineResult ClassifyLine(string lineText, SyntaxLineState startState)
    {
        ArgumentNullException.ThrowIfNull(lineText);
        if (startState is not TextMateLineState state)
            throw new ArgumentException("The state was not produced by a TextMate classifier.", nameof(startState));

        if (lineText.Length > MaxLineLength)
            return new SyntaxLineResult(Array.Empty<SyntaxSpan>(), state);

        try
        {
            ITokenizeLineResult result;
            lock (_gate)
            {
                result = _grammar.TokenizeLine(lineText, state.Stack, LineTimeLimit);
            }

            IReadOnlyList<SyntaxSpan> spans = TextMateScopeMapper.ToSpans(result.Tokens, lineText.Length);
            SyntaxLineState end = StoppedEarly(result) || result.RuleStack is null
                ? state
                : new TextMateLineState(result.RuleStack);
            return new SyntaxLineResult(spans, end);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new SyntaxLineResult(Array.Empty<SyntaxSpan>(), state);
        }
    }

    /// <summary>
    /// Whether TextMate gave up on the line before its end.
    /// </summary>
    /// <remarks>
    /// TextMateSharp 2.0.4 reports this only on its internal result type, not on
    /// <see cref="ITokenizeLineResult"/>, and a partial result cannot be told apart from a fully plain line
    /// by its tokens. The property is therefore read by name, once per result type. Should a later
    /// TextMateSharp remove it, the answer is "no" and the generous per-line time limit is what remains.
    /// </remarks>
    private static bool StoppedEarly(ITokenizeLineResult result)
    {
        Func<object, bool>? read = StoppedEarlyReaders.GetOrAdd(result.GetType(), static type =>
        {
            System.Reflection.PropertyInfo? property = type.GetProperty("StoppedEarly");
            if (property is null || property.PropertyType != typeof(bool) || property.GetMethod is null) return null;
            return instance => (bool)property.GetValue(instance)!;
        });
        return read is not null && read(result);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<object, bool>?> StoppedEarlyReaders = new();
}
