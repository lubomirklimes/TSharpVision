using TextMateSharp.Grammars;
using TSharpVision.CodeEditor.Syntax;
using TSharpVision.CodeEditor.Syntax.TextMate;
using Xunit;

namespace TSharpVision.CodeEditor.Tests;

public sealed class TextMateTimeBudgetTests
{
    [Fact]
    public void ProductionClassificationUsesTheBoundedTimeLimit()
    {
        var grammar = new PartialGrammar();
        var classifier = new TextMateSyntaxClassifier(new SyntaxLanguage("test", "Test"), "source.test", grammar, new object());

        classifier.ClassifyLine("using System; // note", classifier.InitialState);

        Assert.Equal(TextMateSyntaxClassifier.LineTimeLimit, grammar.ObservedLimit);
    }

    [Fact]
    public void AStoppedEarlyResultKeepsPartialSpansAndTheStartState()
    {
        var grammar = new PartialGrammar();
        var classifier = new TextMateSyntaxClassifier(new SyntaxLanguage("test", "Test"), "source.test", grammar, new object());
        var start = new TextMateLineState(null);

        SyntaxLineResult result = classifier.ClassifyLine("using System; // note", start);

        SyntaxSpan span = Assert.Single(result.Spans);
        Assert.Equal(0, span.Start);
        Assert.Equal(5, span.Length);
        Assert.Equal(SyntaxClass.Keyword, span.Class);
        Assert.Same(start, result.EndState);
    }

    private sealed class PartialGrammar : IGrammar
    {
        public TimeSpan ObservedLimit { get; private set; }
        public bool IsCompiling => false;
        public string GetName() => "Test";
        public string GetScopeName() => "source.test";
        public ICollection<string> GetFileTypes() => Array.Empty<string>();
        public ITokenizeLineResult TokenizeLine(LineText line) => throw new NotSupportedException();
        public ITokenizeLineResult TokenizeLine(LineText line, IStateStack state, TimeSpan timeLimit)
        {
            ObservedLimit = timeLimit;
            return new PartialResult();
        }
        public ITokenizeLineResult2 TokenizeLine2(LineText line) => throw new NotSupportedException();
        public ITokenizeLineResult2 TokenizeLine2(LineText line, IStateStack state, TimeSpan timeLimit)
            => throw new NotSupportedException();
    }

    private sealed class PartialResult : ITokenizeLineResult
    {
        public IToken[] Tokens { get; set; } = [new KeywordToken()];
        // The runtime property is intentionally absent from ITokenizeLineResult in TextMateSharp 2.0.4.
        public bool StoppedEarly => true;
        public IStateStack RuleStack { get; set; } = new PartialStack();
    }

    private sealed class PartialStack : IStateStack
    {
        public int Depth => 1;
        public TextMateSharp.Internal.Rules.RuleId RuleId => default!;
        public string EndRule => string.Empty;
    }

    private sealed class KeywordToken : IToken
    {
        public int StartIndex { get; set; } = 0;
        public int EndIndex { get; set; } = 5;
        public int Length => EndIndex - StartIndex;
        public List<string> Scopes { get; set; } = ["source.cs", "keyword.other.directive.using.cs"];
    }
}
