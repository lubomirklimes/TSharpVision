using TSharpVision.CodeEditor.Syntax;
using Xunit;

namespace TSharpVision.CodeEditor.Tests;

public sealed class SyntaxHighlightCacheTests
{
    private static (SyntaxHighlightCache Cache, ToyClassifier Toy, ListLineSource Lines) Build(int count, Func<int, string>? line = null)
    {
        var toy = new ToyClassifier();
        var lines = new ListLineSource(Enumerable.Range(0, count).Select(i => line?.Invoke(i) ?? $"line {i}"));
        return (new SyntaxHighlightCache(toy), toy, lines);
    }

    private static IReadOnlyList<SyntaxSpan> Spans(SyntaxHighlightCache cache, ListLineSource lines, int index)
    {
        Assert.True(cache.TryGetSpans(index, lines, out IReadOnlyList<SyntaxSpan> spans));
        return spans;
    }

    [Fact]
    public void OnlyTheLinesUpToTheRequestedOneAreClassified()
    {
        (SyntaxHighlightCache cache, ToyClassifier toy, ListLineSource lines) = Build(1000);

        Spans(cache, lines, 4);

        Assert.Equal(5, toy.Calls.Count);
        Assert.Equal(5, cache.ValidLineCount);
    }

    [Fact]
    public void LinesAlreadyClassifiedAreServedFromTheCache()
    {
        (SyntaxHighlightCache cache, ToyClassifier toy, ListLineSource lines) = Build(100);
        Spans(cache, lines, 50);
        int before = toy.Calls.Count;

        for (int i = 50; i >= 0; i--) Spans(cache, lines, i);

        Assert.Equal(before, toy.Calls.Count);
        Assert.Equal(before, cache.ClassifiedLineCount);
    }

    [Fact]
    public void TheWorkPerRequestIsBoundedAndProgressIsKept()
    {
        (SyntaxHighlightCache cache, ToyClassifier toy, ListLineSource lines) = Build(100);
        cache.MaxLinesPerRequest = 3;

        Assert.False(cache.TryGetSpans(9, lines, out _));
        Assert.Equal(3, cache.ValidLineCount);
        Assert.False(cache.TryGetSpans(9, lines, out _));
        Assert.False(cache.TryGetSpans(9, lines, out _));
        Assert.True(cache.TryGetSpans(9, lines, out _));

        Assert.Equal(10, toy.Calls.Count);
    }

    [Fact]
    public void EditingOneLineReclassifiesThatLineAndStopsWhenTheStateConverges()
    {
        (SyntaxHighlightCache cache, ToyClassifier toy, ListLineSource lines) = Build(20);
        Spans(cache, lines, 19);
        toy.Calls.Clear();

        lines.Lines[5] = "if 5";
        cache.ApplyEdit(5, 1, 1);
        IReadOnlyList<SyntaxSpan> edited = Spans(cache, lines, 5);
        Spans(cache, lines, 19);

        Assert.Equal(new[] { "if 5" }, toy.Calls);
        Assert.Contains(new SyntaxSpan(0, 2, SyntaxClass.Keyword), edited);
        Assert.Equal(20, cache.ValidLineCount);
    }

    [Fact]
    public void OpeningABlockCommentReclassifiesTheFollowingLinesUntilItCloses()
    {
        (SyntaxHighlightCache cache, ToyClassifier toy, ListLineSource lines) =
            Build(12, i => i == 7 ? "x */ if" : $"if {i}");
        Spans(cache, lines, 11);
        Assert.Contains(new SyntaxSpan(0, 2, SyntaxClass.Keyword), Spans(cache, lines, 3));
        toy.Calls.Clear();

        lines.Lines[2] = "/* open";
        cache.ApplyEdit(2, 1, 1);
        Spans(cache, lines, 11);

        Assert.Equal(new[] { "/* open", "if 3", "if 4", "if 5", "if 6", "x */ if" }, toy.Calls);
        Assert.Equal(new[] { new SyntaxSpan(0, 4, SyntaxClass.Comment) }, Spans(cache, lines, 3));
        Assert.Contains(new SyntaxSpan(5, 2, SyntaxClass.Keyword), Spans(cache, lines, 7));
    }

    [Fact]
    public void InsertedLinesShiftTheCachedLinesAfterThem()
    {
        (SyntaxHighlightCache cache, ToyClassifier toy, ListLineSource lines) = Build(10, i => i == 6 ? "if six" : $"line {i}");
        Spans(cache, lines, 9);
        toy.Calls.Clear();

        lines.Lines.InsertRange(3, new[] { "new a", "new b" });
        cache.ApplyEdit(3, 0, 2);

        Assert.Contains(new SyntaxSpan(0, 2, SyntaxClass.Keyword), Spans(cache, lines, 8));
        Spans(cache, lines, 11);
        Assert.Equal(new[] { "new a", "new b" }, toy.Calls);
    }

    [Fact]
    public void DeletedLinesNeedNoReclassificationWhenTheStateIsUnchanged()
    {
        (SyntaxHighlightCache cache, ToyClassifier toy, ListLineSource lines) = Build(10);
        Spans(cache, lines, 9);
        toy.Calls.Clear();

        lines.Lines.RemoveRange(2, 2);
        cache.ApplyEdit(2, 2, 0);
        Spans(cache, lines, 7);

        Assert.Empty(toy.Calls);
        Assert.Equal(8, cache.ValidLineCount);
    }

    [Fact]
    public void EveryInvalidationChangesTheVersionAndInvalidateDiscards()
    {
        (SyntaxHighlightCache cache, ToyClassifier toy, ListLineSource lines) = Build(10);
        Spans(cache, lines, 9);
        int version = cache.Version;

        cache.ApplyEdit(4, 1, 1);
        Assert.NotEqual(version, cache.Version);
        version = cache.Version;

        cache.Invalidate(6);
        Assert.NotEqual(version, cache.Version);
        Assert.Equal(4, cache.ValidLineCount);

        toy.Calls.Clear();
        Spans(cache, lines, 9);
        // Line 4 was edited; line 5 was only shifted and converges; 6 onwards were discarded.
        Assert.Equal(new[] { "line 4", "line 6", "line 7", "line 8", "line 9" }, toy.Calls);
    }

    [Fact]
    public void ALineOutsideTheDocumentHasNoSpans()
    {
        (SyntaxHighlightCache cache, ToyClassifier toy, ListLineSource lines) = Build(3);

        Assert.True(cache.TryGetSpans(10, lines, out IReadOnlyList<SyntaxSpan> spans));
        Assert.Empty(spans);
        Assert.Empty(toy.Calls);
    }
}
