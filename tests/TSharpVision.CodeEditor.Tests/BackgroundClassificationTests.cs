using System.Collections.Concurrent;
using TSharpVision.Constants;
using TSharpVision.CodeEditor.Syntax;
using Xunit;

namespace TSharpVision.CodeEditor.Tests;

/// <summary>
/// Classification away from the event loop: drawing never waits for it, stale results are rejected by cache
/// version, multiline state stays correct and closed editors cannot be touched.
/// </summary>
/// <remarks>
/// Every wait here is on a gate or on the captured background task, with a timeout only as a deadlock
/// guard. No sleeps.
/// </remarks>
public sealed class BackgroundClassificationTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    /// <summary>A toy classifier that waits on a gate before every line.</summary>
    private sealed class GatedClassifier : ISyntaxClassifier
    {
        private readonly ToyClassifier _inner = new();

        public ManualResetEventSlim Gate { get; } = new(initialState: true);

        public SemaphoreSlim Entered { get; } = new(0);

        public ConcurrentQueue<string> Calls { get; } = new();

        public SyntaxLanguage Language => _inner.Language;

        public SyntaxLineState InitialState => _inner.InitialState;

        public SyntaxLineResult ClassifyLine(string lineText, SyntaxLineState startState)
        {
            Entered.Release();
            Assert.True(Gate.Wait(Guard), "the gate was never opened");
            Calls.Enqueue(lineText);
            lock (_inner) return _inner.ClassifyLine(lineText, startState);
        }
    }

    /// <summary>Runs work on the thread pool and keeps every task, so a test can wait for exactly that work.</summary>
    private sealed class PoolScheduler
    {
        public ConcurrentQueue<Task> Tasks { get; } = new();

        public Task Start(Func<Task> work)
        {
            Task task = Task.Run(work);
            Tasks.Enqueue(task);
            return task;
        }

        public void WaitAll() => Assert.True(Task.WaitAll(Tasks.ToArray(), Guard), "background work did not finish");
    }

    private static (EditorScreen Screen, GatedClassifier Classifier, PoolScheduler Pool) Build(string text, bool blocked)
    {
        var screen = new EditorScreen(40, 8);
        var pool = new PoolScheduler();
        screen.Syntax.StartBackground = pool.Start;
        screen.Load(text);

        var classifier = new GatedClassifier();
        if (blocked) classifier.Gate.Reset();
        screen.Syntax.SetClassifier(classifier);
        return (screen, classifier, pool);
    }

    private static ushort Normal(EditorScreen screen) => (byte)screen.Editor.GetColor(0x0201);

    private static ushort Role(EditorScreen screen, SyntaxClass role) => SyntaxColorScheme.Default.Apply(role, Normal(screen));

    // ── drawing does not wait ────────────────────────────────────────────────

    [Fact]
    public void DrawReturnsWithPlainTextWhileTheClassifierIsBlockedAndColoursArriveByPost()
    {
        (EditorScreen screen, GatedClassifier classifier, PoolScheduler pool) = Build("if 1\nword\nif 3", blocked: true);
        using (screen)
        {
            // SetClassifier drew the view; the classifier is now blocked inside its first line.
            Assert.True(classifier.Entered.Wait(Guard));
            screen.Editor.Draw();                                         // returns although work is blocked

            Assert.Equal("if 1", screen.Row(0));
            Assert.Equal("word", screen.Row(1));
            Assert.Equal(Normal(screen), screen.AttributeAt(0, 0));
            Assert.True(screen.Syntax.IsClassifying);

            classifier.Gate.Set();
            pool.WaitAll();
            EditorScreen.Pump();

            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, 0));
            Assert.Equal(Role(screen, SyntaxClass.Number), screen.AttributeAt(3, 0));
            Assert.Equal("if 1", screen.Row(0));
            Assert.False(screen.Syntax.IsClassifying);
        }
    }

    [Fact]
    public void TypingWhileClassificationIsBlockedDiscardsTheOldVersionAndAppliesTheNewOne()
    {
        (EditorScreen screen, GatedClassifier classifier, PoolScheduler pool) = Build("word 0\nword 1\nword 2", blocked: true);
        using (screen)
        {
            Assert.True(classifier.Entered.Wait(Guard));
            int oldVersion = screen.Syntax.Cache!.Version;

            screen.Editor.SetCurPtr(0, 0);
            screen.Type("if ");                                          // version N+1 while N is blocked

            Assert.NotEqual(oldVersion, screen.Syntax.Cache.Version);
            Assert.Equal("if word 0", screen.Row(0));                    // the text is right at once
            Assert.Equal(Normal(screen), screen.AttributeAt(0, 0));      // but not coloured yet
            Assert.Equal("if word 0", screen.Text.Split('\n')[0]);

            classifier.Gate.Set();
            pool.WaitAll();
            EditorScreen.Pump();

            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, 0));
            Assert.Equal(3, screen.Syntax.Cache.ValidLineCount);
            Assert.Contains("if word 0", classifier.Calls);
        }
    }

    [Fact]
    public void AResultForAnOlderVersionIsNeverCommitted()
    {
        var toy = new ToyClassifier();
        var cache = new SyntaxHighlightCache(toy);
        var lines = new ListLineSource(new[] { "if 0", "if 1", "if 2" });

        SyntaxClassificationRequest request = cache.CreateRequest(2, lines, 100)!;
        lines.Lines[1] = "word";
        cache.ApplyEdit(1, 1, 1);                                        // the document moved on

        SyntaxClassificationResult stale = request.Classify(CancellationToken.None);
        Assert.False(cache.Commit(stale));
        Assert.Equal(0, cache.ValidLineCount);
        Assert.False(cache.TryGetValidSpans(0, out _));

        SyntaxClassificationRequest fresh = cache.CreateRequest(2, lines, 100)!;
        Assert.True(cache.Commit(fresh.Classify(CancellationToken.None)));
        Assert.True(cache.TryGetValidSpans(1, out IReadOnlyList<SyntaxSpan> spans));
        Assert.Empty(spans);                                             // "word", not the old "if 1"
    }

    [Fact]
    public void ARequestClassifiesTheTextCapturedWhenItWasCreated()
    {
        var cache = new SyntaxHighlightCache(new ToyClassifier());
        var lines = new ListLineSource(new[] { "if 0" });

        SyntaxClassificationRequest request = cache.CreateRequest(0, lines, 10)!;
        lines.Lines[0] = "changed afterwards";

        SyntaxClassificationResult result = request.Classify(CancellationToken.None);
        Assert.True(cache.Commit(result));                               // no edit was reported: same version
        Assert.True(cache.TryGetValidSpans(0, out IReadOnlyList<SyntaxSpan> spans));
        Assert.Contains(new SyntaxSpan(0, 2, SyntaxClass.Keyword), spans);
    }

    [Fact]
    public void ACommitThatDoesNotStartAtTheFirstUnverifiedLineIsRejected()
    {
        var cache = new SyntaxHighlightCache(new ToyClassifier());
        var lines = new ListLineSource(Enumerable.Range(0, 10).Select(i => $"line {i}"));

        SyntaxClassificationRequest first = cache.CreateRequest(9, lines, 3)!;
        SyntaxClassificationRequest duplicate = cache.CreateRequest(9, lines, 3)!;
        Assert.True(cache.Commit(first.Classify(CancellationToken.None)));
        Assert.False(cache.Commit(duplicate.Classify(CancellationToken.None)));   // lines 0-2 already applied
        Assert.Equal(3, cache.ValidLineCount);
    }

    [Fact]
    public void ChangingAMultilineCommentWhileWorkIsPendingEndsInTheLatestDocumentsState()
    {
        (EditorScreen screen, GatedClassifier classifier, PoolScheduler pool) =
            Build("/* open\ninside\nclose */ if\nif 3", blocked: false);
        using (screen)
        {
            pool.WaitAll();
            EditorScreen.Pump();
            Assert.Equal(Role(screen, SyntaxClass.Comment), screen.AttributeAt(0, 1));    // "inside" is comment

            classifier.Gate.Reset();

            // Remove the opening delimiter ...
            screen.Editor.SetSelect(0, 2, true);
            screen.Editor.InsertText("", selectText: false);
            screen.Draw();
            Assert.True(classifier.Entered.Wait(Guard));

            // ... and open a comment on the last line before that work can finish.
            uint last = screen.Editor.LineMove(0, 3);
            screen.Editor.SetSelect(last, last, true);
            screen.Editor.InsertText("/*", selectText: false);
            screen.Draw();

            classifier.Gate.Set();
            pool.WaitAll();
            EditorScreen.Pump();
            screen.Draw();

            Assert.Equal(" open", screen.Row(0));
            Assert.Equal(Normal(screen), screen.AttributeAt(0, 1));                        // "inside" is code now
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(9, 2));     // "if" after "*/"
            Assert.Equal(Role(screen, SyntaxClass.Comment), screen.AttributeAt(0, 3));     // "/*if 3"
            Assert.Equal(4, screen.Syntax.Cache!.ValidLineCount);
        }
    }

    // ── lifetime and isolation ───────────────────────────────────────────────

    [Fact]
    public void ALateResultAfterShutdownChangesNothing()
    {
        (EditorScreen screen, GatedClassifier classifier, PoolScheduler pool) = Build("if 1\nif 2", blocked: true);
        using (screen)
        {
            Assert.True(classifier.Entered.Wait(Guard));
            SyntaxHighlightCache cache = screen.Syntax.Cache!;

            screen.Syntax.Shutdown();
            screen.Syntax.Shutdown();                                     // idempotent
            classifier.Gate.Set();
            pool.WaitAll();
            EditorScreen.Pump();
            screen.Draw();

            Assert.Equal(0, cache.ValidLineCount);
            Assert.False(screen.Syntax.IsClassifying);
            Assert.Equal(Normal(screen), screen.AttributeAt(0, 0));
            Assert.Equal("if 1", screen.Row(0));
        }
    }

    [Fact]
    public void ShuttingTheCodeEditorDownStopsItsClassification()
    {
        (EditorScreen screen, GatedClassifier classifier, PoolScheduler pool) = Build("if 1", blocked: true);
        using (screen)
        {
            Assert.True(classifier.Entered.Wait(Guard));
            SyntaxHighlightCache cache = screen.Syntax.Cache!;

            screen.Host.Remove(screen.Editor);
            screen.Editor.ShutDown();
            classifier.Gate.Set();
            pool.WaitAll();
            EditorScreen.Pump();

            Assert.Equal(0, cache.ValidLineCount);
            Assert.Equal(0, TEventQueue.PostedCount);
        }
    }

    [Fact]
    public void ABlockedEditorDoesNotHoldUpAnotherEditor()
    {
        (EditorScreen screen, GatedClassifier blockedClassifier, PoolScheduler pool) = Build("if a", blocked: true);
        using (screen)
        {
            Assert.True(blockedClassifier.Entered.Wait(Guard));

            var other = new TCodeEditor(new TRect(0, 4, 40, 8), null, null, null, null);
            screen.Host.Insert(other);
            other.SyntaxHighlighter.StartBackground = work => work();
            other.InsertText("word", false);
            other.SetCurPtr(0, 0);
            other.SyntaxHighlighter.SetClassifier(new ToyClassifier());
            EditorScreen.Pump();

            // Type into the second editor while the first is still blocked.
            foreach (char c in "if ")
            {
                var key = new TEvent { What = Events.evKeyDown };
                key.keyDown.keyCode = c;
                key.keyDown.charScan.charCode = (byte)c;
                key.keyDown.text = c.ToString();
                other.HandleEvent(ref key);
                EditorScreen.Pump();
            }

            Assert.Equal("if a", screen.Row(0));
            Assert.Equal(Normal(screen), screen.AttributeAt(0, 0));        // first editor: plain, still waiting
            Assert.Equal("if word", screen.Row(4));
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, 4));   // second: highlighted

            blockedClassifier.Gate.Set();
            pool.WaitAll();
            EditorScreen.Pump();
            Assert.Equal(Role(screen, SyntaxClass.Keyword), screen.AttributeAt(0, 0));
        }
    }

    [Fact]
    public void OnlyTheLinesUpToTheVisibleOnesAreRequested()
    {
        (EditorScreen screen, GatedClassifier classifier, PoolScheduler pool) =
            Build(string.Join("\n", Enumerable.Range(0, 5000).Select(i => $"line {i}")), blocked: false);
        using (screen)
        {
            pool.WaitAll();
            EditorScreen.Pump();

            Assert.Equal(8, classifier.Calls.Count);                     // the 8 visible lines, not 5000
            Assert.Equal(8, screen.Syntax.Cache!.ValidLineCount);
        }
    }
}
