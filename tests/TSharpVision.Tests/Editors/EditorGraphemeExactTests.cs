using System.Globalization;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Text;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace TSharpVision.Tests.Editors;

/// <summary>
/// Exact grapheme boundaries at any offset, compared with forward segmentation from the real start of each line,
/// including clusters and regional-indicator runs far longer than any fixed look-behind (TV13i).
/// </summary>
[Collection("NonParallel")]
public sealed class EditorGraphemeExactTests : IDisposable
{
    private const string RegionalC = "\U0001F1E8";                     // REGIONAL INDICATOR SYMBOL LETTER C
    private const string RegionalZ = "\U0001F1FF";
    private const string Man = "\U0001F468";
    private const string Zwj = "\u200D";

    private readonly DriverScope _driver = new(80, 25);
    private readonly EditorClipboardScope _clipboard = new();
    private readonly TestGroup _host;
    private readonly ITestOutputHelper _output;

    public EditorGraphemeExactTests(ITestOutputHelper output)
    {
        _output = output;
        TEventQueue.Resume();
        DrainQueue();
        _host = new TestGroup(new TRect(0, 0, 40, 5)) { buffer = new ScreenBuffer(40 * 5) };
        _host.state |= (ushort)(Views.sfVisible | Views.sfExposed);
    }

    public void Dispose()
    {
        DrainQueue();
        _clipboard.Dispose();
        _driver.Dispose();
    }

    private static void DrainQueue()
    {
        var ev = new TEvent();
        do { TEventQueue.GetNextEvent(ref ev); } while (ev.What != Events.evNothing);
    }

    // ── pathological texts ───────────────────────────────────────────────────

    private static string Marks(int count) => new string('\u0301', count);
    private static string Regionals(int count)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < count; i++) builder.Append(i % 2 == 0 ? RegionalC : RegionalZ);
        return builder.ToString();
    }
    private static string ZwjChain(int people)
    {
        var builder = new StringBuilder(Man);
        for (int i = 1; i < people; i++) builder.Append(Zwj).Append(Man);
        return builder.ToString();
    }

    public static readonly string[] Pathological =
    {
        "a" + Marks(300) + "b",                                        // one 301-unit cluster
        "x" + Regionals(301) + "y",                                    // odd run: the last indicator stands alone
        "q" + Regionals(400) + "\n" + Regionals(257) + "z",            // even run, then an odd run on the next line
        ZwjChain(110) + " " + ZwjChain(3),                             // a 329-unit ZWJ sequence
        "e" + Marks(600) + "\n\n" + "\U0001F44D" + new string('\uFE0F', 400) + "!",
        "\r" + Marks(270) + "\r\n" + Regionals(129) + "\uD83D" + Marks(3), // CR, a lone surrogate and marks
    };

    public static IEnumerable<object[]> PathologicalTexts() => Pathological.Select((_, i) => new object[] { i });

    // Boundaries by forward segmentation from the start of every line; an LF always stands alone.
    private static List<int> Reference(string text)
    {
        var result = new List<int> { 0 };
        int lineStart = 0;
        foreach (string line in text.Split('\n'))
        {
            int at = 0;
            while (at < line.Length)
            {
                at += StringInfo.GetNextTextElementLength(line.AsSpan(at));
                result.Add(lineStart + at);
            }
            lineStart += line.Length;
            if (lineStart < text.Length) { lineStart++; result.Add(lineStart); }
        }
        return result;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private TEditor Load(string text, uint caret = 0)
    {
        var editor = new TEditor(new TRect(0, 0, 40, 5), null, null, null, (uint)Math.Max(4096, 2 * text.Length + 64));
        _host.Insert(editor);
        editor.Select();
        editor.InsertText(text);
        editor.SetCurPtr(caret, 0);
        editor.ClearUndoHistory();
        editor.MarkUnmodified();
        return editor;
    }

    private static string Text(TEditor editor)
    {
        var builder = new StringBuilder((int)editor.bufLen);
        for (uint p = 0; p < editor.bufLen; p++) builder.Append(editor.BufChar(p));
        return builder.ToString();
    }

    private static void Command(TEditor editor, ushort command)
    {
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = command;
        editor.HandleEvent(ref ev);
    }

    private static void ShiftKey(TEditor editor, ushort keyCode)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        ev.keyDown.controlKeyState = (ushort)Keys.kbShift;
        editor.HandleEvent(ref ev);
    }

    private static void AssertBoundaries(TEditor editor, string text)
    {
        List<int> boundaries = Reference(text);
        int k = 0;
        for (int p = 0; p <= text.Length; p++)
        {
            while (k + 1 < boundaries.Count && boundaries[k + 1] <= p) k++;
            int prev = boundaries[k] < p ? boundaries[k] : k > 0 ? boundaries[k - 1] : 0;
            int next = p >= text.Length ? text.Length : boundaries[k + 1];
            Assert.True(editor.NextGraphemeBoundary((uint)p) == (uint)next, $"next({p}) = {editor.NextGraphemeBoundary((uint)p)}, expected {next}");
            Assert.True(editor.PrevGraphemeBoundary((uint)p) == (uint)prev, $"prev({p}) = {editor.PrevGraphemeBoundary((uint)p)}, expected {prev}");
        }
    }

    // ── every offset ─────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(PathologicalTexts))]
    public void BoundariesMatchLineSegmentationAtEveryOffset(int index)
    {
        string text = Pathological[index];
        AssertBoundaries(Load(text), text);
    }

    [Theory]
    [MemberData(nameof(PathologicalTexts))]
    public void BoundariesMatchWhateverWayTheOffsetsAreVisited(int index)
    {
        string text = Pathological[index];
        TEditor backwards = Load(text);
        List<int> boundaries = Reference(text);
        for (int p = text.Length; p >= 0; p--)
        {
            int next = p >= text.Length ? text.Length : boundaries.First(b => b > p);
            Assert.Equal((uint)next, backwards.NextGraphemeBoundary((uint)p));
        }
        TEditor random = Load(text);
        var rng = new Random(index + 13);
        for (int i = 0; i < 2000; i++)
        {
            int p = rng.Next(text.Length + 1);
            int prev = boundaries.LastOrDefault(b => b < p, 0);
            Assert.Equal((uint)prev, random.PrevGraphemeBoundary((uint)p));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(150)]
    [InlineData(301)]
    [InlineData(500)]
    public void BoundariesDoNotDependOnWhereTheGapIs(int gap)
    {
        string text = "a" + Marks(300) + "b" + Regionals(200);
        TEditor editor = Load(text);
        editor.SetSelect((uint)Math.Min(gap, text.Length), (uint)Math.Min(gap, text.Length), false);
        AssertBoundaries(editor, text);
    }

    // ── user actions on every boundary ───────────────────────────────────────

    [Theory]
    [MemberData(nameof(PathologicalTexts))]
    public void ArrowsAndShiftArrowsStepClusterByCluster(int index)
    {
        string text = Pathological[index];
        List<int> boundaries = Reference(text);
        TEditor editor = Load(text);
        foreach (int b in boundaries.Skip(1))
        {
            Command(editor, Views.cmCharRight);
            Assert.Equal((uint)b, editor.curPtr);
        }
        foreach (int b in Enumerable.Reverse(boundaries).Skip(1))
        {
            Command(editor, Views.cmCharLeft);
            Assert.Equal((uint)b, editor.curPtr);
        }
        editor.SetCurPtr(0, 0);
        foreach (int b in boundaries.Skip(1))
        {
            ShiftKey(editor, Keys.kbRight);
            Assert.Equal((0u, (uint)b), (editor.selStart, editor.selEnd));
        }
    }

    [Theory]
    [MemberData(nameof(PathologicalTexts))]
    public void BackspaceAndDeleteRemoveWholeClustersAndUndoRedoRestore(int index)
    {
        string text = Pathological[index];
        List<int> boundaries = Reference(text);
        TEditor editor = Load(text);
        for (int i = 1; i < boundaries.Count; i++)
        {
            int start = boundaries[i - 1], end = boundaries[i];
            string removed = text.Remove(start, end - start);

            editor.SetCurPtr((uint)end, 0);
            Command(editor, Views.cmBackSpace);
            Assert.Equal(removed, Text(editor));
            editor.Undo();
            Assert.Equal(text, Text(editor));
            editor.Redo();
            Assert.Equal(removed, Text(editor));
            editor.Undo();

            editor.SetCurPtr((uint)start, 0);
            Command(editor, Views.cmDelChar);
            Assert.Equal(removed, Text(editor));
            editor.Undo();
            Assert.Equal(text, Text(editor));
        }
    }

    // ── long lines: work per query ───────────────────────────────────────────

    [Theory]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    public void RepeatedQueriesAtTheFarRightOfALongLineDoNotRescanIt(int length)
    {
        // Mostly plain text, ending in a 40-indicator run and a cluster of 300 marks.
        string tail = Regionals(40) + "a" + Marks(300) + "z";
        string text = new string('w', length - tail.Length) + tail;
        TEditor editor = Load(text, caret: (uint)text.Length);
        List<int> boundaries = Reference(text);
        int far = text.Length - 1;

        // TV13o: placing the caret at the end already laid the line out once (its column), leaving checkpoints, so
        // loading plus the first query read the line about twice (one LF scan back and one layout pass) in total.
        long before = 0;
        Assert.Equal((uint)text.Length, editor.NextGraphemeBoundary((uint)far));
        long first = editor.GraphemeUnitsRead - before;
        Assert.InRange(first, length, 3L * length);

        const int queries = 2000;
        var rng = new Random(7);
        var offsets = new int[queries];
        var expectedNext = new uint[queries];
        var expectedPrev = new uint[queries];
        for (int i = 0; i < queries; i++)
        {
            int p = offsets[i] = text.Length - 1 - rng.Next(600);
            expectedNext[i] = (uint)boundaries.First(b => b > p);
            expectedPrev[i] = (uint)boundaries.Last(b => b < p);
        }
        var actualNext = new uint[queries];
        var actualPrev = new uint[queries];

        before = editor.GraphemeUnitsRead;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < queries; i++)
        {
            actualNext[i] = editor.NextGraphemeBoundary((uint)offsets[i]);
            actualPrev[i] = editor.PrevGraphemeBoundary((uint)offsets[i]);
        }
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        long perQuery = (editor.GraphemeUnitsRead - before) / (2 * queries);
        Assert.Equal(expectedNext, actualNext);
        Assert.Equal(expectedPrev, actualPrev);
        // A query reads about one checkpoint interval plus the cluster it lands in (here at most 301 units), never
        // the line, and a query that needs no larger chunk allocates nothing.
        _output.WriteLine($"length {length}: first query read {first} units; {2 * queries} later queries read {perQuery} units each on average and allocated {allocated} bytes in total");
        Assert.True(perQuery < 2 * 301 + 4 * 64, $"{perQuery} units read per query");
        Assert.True(allocated < 64 * 1024, $"{allocated} bytes allocated by {2 * queries} queries");

        // Caret steps at the far right stay cheap too.
        before = editor.GraphemeUnitsRead;
        for (int i = 0; i < 50; i++) Command(editor, Views.cmCharLeft);
        for (int i = 0; i < 50; i++) Command(editor, Views.cmCharRight);
        Assert.Equal((uint)text.Length, editor.curPtr);
        Assert.True((editor.GraphemeUnitsRead - before) / 100 < 2 * 301 + 4 * 64);
    }

    [Fact]
    public void AGiantClusterIsReadWholeOnlyWhenQueriedInside()
    {
        string text = "a" + Marks(20_000) + "b";
        TEditor editor = Load(text);
        Assert.Equal(20_001u, editor.NextGraphemeBoundary(10_000));
        Assert.Equal(0u, editor.PrevGraphemeBoundary(10_000));
        long before = editor.GraphemeUnitsRead;
        Assert.Equal(20_002u, editor.NextGraphemeBoundary(20_001));   // "b": the checkpoint after the cluster
        Assert.True(editor.GraphemeUnitsRead - before < 4 * 64);
    }

    // ── invalidation ─────────────────────────────────────────────────────────

    private static void AssertNear(TEditor editor, int from, int to)
    {
        string text = Text(editor);
        List<int> boundaries = Reference(text);
        for (int p = Math.Max(0, from); p <= Math.Min(to, text.Length); p++)
        {
            int next = p >= text.Length ? text.Length : boundaries.First(b => b > p);
            int prev = boundaries.LastOrDefault(b => b < p, 0);
            Assert.True(editor.NextGraphemeBoundary((uint)p) == (uint)next, $"next({p}) in \"{text.Length}\"-unit text");
            Assert.Equal((uint)prev, editor.PrevGraphemeBoundary((uint)p));
        }
    }

    [Fact]
    public void ARegionalIndicatorParityChangeEarlyInTheLineMovesEveryLaterBoundary()
    {
        string text = "x" + Regionals(400) + "y";
        TEditor editor = Load(text);
        AssertNear(editor, 700, 802);                                  // warm the checkpoints near the end
        uint pairAtEnd = editor.NextGraphemeBoundary(797);

        editor.SetSelect(1, 3, false);
        editor.DeleteSelect();                                         // one indicator fewer: odd run
        AssertNear(editor, 700, 800);
        Assert.NotEqual(pairAtEnd - 2, editor.NextGraphemeBoundary(795));

        editor.Undo();                                                 // even again
        Assert.Equal(text, Text(editor));
        AssertNear(editor, 700, 802);
        editor.Redo();
        AssertNear(editor, 700, 800);
        editor.Undo();

        editor.SetCurPtr(1, 0);
        editor.InsertText(RegionalZ);                                  // programmatic insertion: odd run
        AssertNear(editor, 0, 804);
    }

    [Fact]
    public void TypingPasteCutAndReplaceAllInvalidateCheckpoints()
    {
        var clip = new TEditor(new TRect(0, 0, 10, 2), null, null, null, 8192);
        TEditor.clipboard = clip;
        string text = "a" + Marks(300) + "b" + Regionals(300) + "\nz";
        TEditor editor = Load(text);
        AssertNear(editor, 0, text.Length);

        editor.SetCurPtr(1, 0);
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.text = "q";
        editor.HandleEvent(ref ev);                                    // splits the mark cluster
        AssertNear(editor, 0, 320);

        editor.SetSelect(0, 2, false);
        Command(editor, Views.cmCut);                                  // the marks now start the line
        AssertNear(editor, 0, 320);
        editor.SetCurPtr(305, 0);
        Command(editor, Views.cmPaste);
        AssertNear(editor, 0, (int)editor.bufLen);

        editor.SetCurPtr(0, 0);
        RegionalC.CopyTo(0, TEditor.findStr, 0, 2);
        TEditor.findStr[2] = '\0';
        TEditor.replaceStr[0] = 'c';
        TEditor.replaceStr[1] = '\0';
        TEditor.editorFlags = (ushort)(Views.efDoReplace | Views.efReplaceAll | Views.efCaseSensitive);
        editor.DoSearchReplace();                                      // every C indicator becomes "c"
        AssertNear(editor, 0, (int)editor.bufLen);
        editor.Undo();
        AssertNear(editor, 0, (int)editor.bufLen);
    }

    [Fact]
    public void DeletingALineBreakJoinsLinesAndInvalidatesBoth()
    {
        string text = Regionals(3) + "\n" + RegionalC + "x";
        TEditor editor = Load(text);
        AssertNear(editor, 0, text.Length);
        editor.SetCurPtr(7, 0);
        Command(editor, Views.cmBackSpace);                            // the odd run joins the next indicator
        AssertNear(editor, 0, (int)editor.bufLen);
        Assert.Equal(8u, editor.NextGraphemeBoundary(4));
    }

    [Fact]
    public void LoadingAndMemoDataReplaceTheText()
    {
        TEditor editor = Load("x" + Regionals(300));
        AssertNear(editor, 500, 601);
        editor.SetBufLen(0);
        editor.InsertText(Regionals(301) + "y");
        AssertNear(editor, 0, 603);

        var memo = new TMemo(new TRect(0, 0, 40, 5), null, null, null, 4096);
        _host.Insert(memo);
        var data = new TMemoData(4096);
        string first = "x" + Regionals(300), second = Regionals(300) + "x";
        first.CopyTo(0, data.buffer, 0, first.Length);
        data.length = (uint)first.Length;
        memo.SetData(data);
        AssertNear(memo, 590, first.Length);
        second.CopyTo(0, data.buffer, 0, second.Length);               // same length, different parity
        data.length = (uint)second.Length;
        memo.SetData(data);
        AssertNear(memo, 590, second.Length);
    }

    [Fact]
    public void ManyLinesKeepIndependentCheckpoints()
    {
        var builder = new StringBuilder();
        for (int line = 0; line < 10; line++)
            builder.Append(line % 2 == 0 ? "x" : "").Append(Regionals(100 + line)).Append('\n');
        string text = builder.ToString();
        TEditor editor = Load(text);
        var rng = new Random(3);
        List<int> boundaries = Reference(text);
        for (int i = 0; i < 3000; i++)
        {
            int p = rng.Next(text.Length + 1);
            Assert.Equal((uint)(p >= text.Length ? text.Length : boundaries.First(b => b > p)), editor.NextGraphemeBoundary((uint)p));
        }
    }

    [Fact]
    public void AClickInsideAGiantClusterSnapsToItsStart()
    {
        // Under the legacy profile columns count code units, so the cell in column c holds offset c of the line
        // (TV13o: the default profile gives such a cluster its visual width; see EditorVisualColumnTests).
        TextLayout.ProfileOverride = TextWidthProfile.Legacy;
        try { ClickInsideAGiantClusterUnderTheLegacyProfile(); }
        finally { TextLayout.ProfileOverride = TextWidthProfile.Default; }
    }

    private void ClickInsideAGiantClusterUnderTheLegacyProfile()
    {
        foreach (string text in new[] { "a" + Marks(300) + "bc", "x" + Regionals(301) + "y" })
        {

            TEditor editor = Load(text);
            List<int> boundaries = Reference(text);
            editor.ScrollTo(text.Length - 30, 0);
            int left = editor.delta.x;
            Assert.True(left > 256);
            for (int x = 0; x < 40; x++)
            {
                int column = Math.Min(left + x, text.Length);
                Assert.Equal((uint)boundaries.Last(b => b <= column), editor.GetMousePtr(new TPoint(x, 0)));
            }
        }
    }
}
