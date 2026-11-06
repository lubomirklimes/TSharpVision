using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Text;
using TSharpVision.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace TSharpVision.Tests.Editors;

/// <summary>
/// TV13o: editor positions follow visual columns (TextLayout widths, tab stops), the column checkpoints extend the
/// TV13i boundary cache, drawing emits lead/trail cells, and the legacy profile keeps one column per code unit.
/// </summary>
[Collection("NonParallel")]
public sealed class EditorVisualColumnTests : IDisposable
{
    private const string Cjk = "\u6F22";                               // one unit, two columns
    private const string Cjk2 = "\u5B57";
    private const string Emoji = "\U0001F600";                         // two units, two columns
    private const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
    private const string FlagCz = "\U0001F1E8\U0001F1FF";
    private const string Combining = "e\u0301";                        // two units, one column
    private const string RegionalC = "\U0001F1E8";

    private readonly DriverScope _driver = new(80, 25);
    private readonly EditorClipboardScope _clipboard = new();
    private readonly ClipboardServiceScope _service = new();
    private readonly TestGroup _host;
    private readonly ITestOutputHelper _output;
    private readonly uint _savedTabSize = TEditor.tabSize;

    public EditorVisualColumnTests(ITestOutputHelper output)
    {
        _output = output;
        TEventQueue.Resume();
        DrainQueue();
        _host = new TestGroup(new TRect(0, 0, 40, 5)) { buffer = new ScreenBuffer(40 * 5) };
        _host.state |= (ushort)(Views.sfVisible | Views.sfExposed);
    }

    public void Dispose()
    {
        TextLayout.ProfileOverride = TextWidthProfile.Default;
        TEditor.tabSize = _savedTabSize;
        DrainQueue();
        _service.Dispose();
        _clipboard.Dispose();
        _driver.Dispose();
    }

    private static void DrainQueue()
    {
        var ev = new TEvent();
        do { TEventQueue.GetNextEvent(ref ev); } while (ev.What != Events.evNothing);
    }

    // \u2500\u2500 helpers \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    private TEditor Load(string text, uint caret = 0)
    {
        var editor = new TEditor(new TRect(0, 0, 40, 5), null, null, null, (uint)Math.Max(4096, 2 * text.Length + 4096));
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

    private static void Type(TEditor editor, string text)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.text = text;
        editor.HandleEvent(ref ev);
    }

    private static void ShiftKey(TEditor editor, ushort keyCode)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        ev.keyDown.controlKeyState = (ushort)Keys.kbShift;
        editor.HandleEvent(ref ev);
    }

    private static TextLayoutOptions Options =>
        TextLayoutOptions.Default with { TabSize = (int)Math.Max(1u, TEditor.tabSize), ZeroWidthAsOne = true };

    // Reference layout of one line (no LF): the start and column of every cluster, and the end.
    private static (List<int> Starts, List<int> Columns) Layout(string line)
    {
        var starts = new List<int>();
        var columns = new List<int>();
        int end = 0;
        foreach (TextCluster c in TextLayout.EnumerateClusters(line, Options, 0))
        {
            starts.Add(c.Start);
            columns.Add(c.Column);
            end = c.EndColumn;
        }
        starts.Add(line.Length);
        columns.Add(end);
        return (starts, columns);
    }

    // Expected CharPos(lineStart, lineStart + p): the column of the cluster containing p.
    private static int ColumnOf((List<int> Starts, List<int> Columns) layout, int p)
    {
        int k = layout.Starts.BinarySearch(p);
        return layout.Columns[k >= 0 ? k : ~k - 1];
    }

    // Expected CharPtr(lineStart, column): the start of the cluster covering the column, or the line end.
    private static int OffsetOf((List<int> Starts, List<int> Columns) layout, int column)
    {
        if (column <= 0) return 0;
        int k = layout.Columns.BinarySearch(column);
        return layout.Starts[k >= 0 ? k : ~k - 1];
    }

    // Every offset and column of every line, or `samples` random ones per line.
    private static void AssertLineColumns(TEditor editor, string text, string because, int samples = 0)
    {
        var rng = new Random(11);
        int lineStart = 0;
        foreach (string line in text.Split('\n'))
        {
            var layout = Layout(line);
            int width = layout.Columns[^1];
            if (samples > 0)
            {
                for (int i = 0; i < samples; i++)
                {
                    int p = rng.Next(line.Length + 1), column = rng.Next(width + 3) - 1;
                    Assert.True(editor.CharPos((uint)lineStart, (uint)(lineStart + p)) == ColumnOf(layout, p), $"{because}: CharPos +{p}");
                    Assert.True(editor.CharPtr((uint)lineStart, column) == (uint)(lineStart + OffsetOf(layout, column)), $"{because}: CharPtr {column}");
                }
                lineStart += line.Length + 1;
                continue;
            }
            for (int p = 0; p <= line.Length; p++)
            {
                int actual = editor.CharPos((uint)lineStart, (uint)(lineStart + p));
                if (actual != ColumnOf(layout, p))
                    Assert.Fail($"{because}: CharPos({lineStart}, +{p}) = {actual}, expected {ColumnOf(layout, p)}");
            }
            for (int column = -1; column <= width + 2; column++)
            {
                uint actual = editor.CharPtr((uint)lineStart, column);
                if (actual != (uint)(lineStart + OffsetOf(layout, column)))
                    Assert.Fail($"{because}: CharPtr({lineStart}, {column}) = {actual}, expected {lineStart + OffsetOf(layout, column)}");
            }
            lineStart += line.Length + 1;
        }
    }

    private TScreenChar Cell(int x, int y = 0) => _host.buffer!.Data[y * 40 + x];

    // \u2500\u2500 columns \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    public static IEnumerable<object[]> MixedLines() => new[]
    {
        "a" + Cjk + "b" + Emoji + "c" + Combining + "d" + FlagCz + "e",
        Family + "\t" + Cjk + Cjk2 + "\tx\r",
        "\u0301\u0302 lead marks\t" + RegionalC + RegionalC + RegionalC + "!",
        "\uFF21\uFF22\t\u3042\u200B\u00E9" + Emoji + "\uFE0F" + "\u2764\uFE0F" + "\u2764\uFE0E",
    }.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(MixedLines))]
    public void CharPosAndCharPtrFollowVisualColumns(string text)
    {
        foreach (uint tab in new uint[] { 0, 3, 8 })
        {
            TEditor.tabSize = tab;
            TEditor editor = Load(text + "\n" + text);
            AssertLineColumns(editor, text + "\n" + text, $"tab {tab}");
            var layout = Layout(text);
            for (int k = 0; k < layout.Starts.Count; k++)
            {
                editor.SetCurPtr((uint)layout.Starts[k], 0);
                Assert.Equal(layout.Columns[k], editor.curPos.x);
            }
        }
    }

    [Theory]
    [MemberData(nameof(PathologicalTexts))]
    public void EveryTV13iPathologicalTextHasExactColumnsAtEveryGap(int index)
    {
        string text = EditorGraphemeExactTests.Pathological[index];
        foreach (int gap in new[] { 0, 1, text.Length / 3, text.Length / 2, text.Length })
        {
            TEditor editor = Load(text);
            editor.SetSelect((uint)gap, (uint)gap, false);
            AssertLineColumns(editor, text, $"gap {gap}");
        }
    }

    public static IEnumerable<object[]> PathologicalTexts() => EditorGraphemeExactTests.PathologicalTexts();

    [Fact]
    public void TheSecondCellOfAWideCharacterMapsToItsStart()
    {
        TEditor editor = Load("a" + Cjk + "b" + Emoji + "c");
        uint[] expected = { 0, 1, 1, 2, 3, 3, 5, 6, 6 };               // cells 0..8
        for (int x = 0; x < expected.Length; x++)
            Assert.Equal(expected[x], editor.GetMousePtr(new TPoint(x, 0)));
    }

    [Fact]
    public void VerticalMovementKeepsTheVisualColumnAndNeverLandsInsideAWideCharacter()
    {
        string text = Cjk + Cjk2 + Cjk + Cjk2 + "\nabcdefgh\n" + Emoji + Emoji + "x";
        TEditor editor = Load(text, caret: 2);                          // column 4
        Command(editor, Views.cmLineDown);
        Assert.Equal(9u, editor.curPtr);                                // "abcd|efgh"
        Assert.Equal(4, editor.curPos.x);
        Command(editor, Views.cmLineDown);
        Assert.Equal(18u, editor.curPtr);                               // after the two emoji
        Assert.Equal(4, editor.curPos.x);
        editor.SetCurPtr(8, 0);                                         // "abc|d": column 3
        Command(editor, Views.cmLineUp);
        Assert.Equal(1u, editor.curPtr);                                // column 3 is the second CJK's second cell
        Assert.Equal(2, editor.curPos.x);
    }

    [Fact]
    public void TabStopsCountVisualColumns()
    {
        TEditor.tabSize = 8;
        TEditor editor = Load(Cjk + "\tx" + Emoji + "\ty");
        Assert.Equal(8, editor.CharPos(0, 2));
        Assert.Equal(16, editor.CharPos(0, 6));
        editor.Draw();
        Assert.Equal(TScreenCellKind.Lead, Cell(0).Kind);
        for (int x = 2; x < 8; x++) Assert.Equal(' ', Cell(x).Character);
        Assert.Equal('x', Cell(8).Character);
        Assert.Equal(TScreenCellKind.Lead, Cell(9).Kind);
        Assert.Equal('y', Cell(16).Character);
        TEditor.tabSize = 3;
        Assert.Equal(3, editor.CharPos(0, 2));                          // the same editor: a new tab size drops the cache
        editor = Load(Cjk + "\tx");
        Assert.Equal(3, editor.CharPos(0, 2));
    }

    [Fact]
    public void TheHorizontalExtentAndScrollingUseVisualColumns()
    {
        string line = string.Concat(Enumerable.Repeat(Cjk, 300));
        TEditor editor = Load(line + "\nshort", caret: 300);
        Assert.Equal(601, editor.limit.x);
        Assert.Equal(600, editor.curPos.x);
        editor.TrackCursor(false);
        Assert.Equal(600 - 40 + 1, editor.delta.x);
        Type(editor, Emoji + Emoji);                                     // the extent grows by visual width
        Assert.Equal(605, editor.limit.x);
        Assert.Equal(604, editor.curPos.x);
    }

    // \u2500\u2500 drawing \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    [Fact]
    public void ClustersDrawAsLeadAndTrailCells()
    {
        TEditor editor = Load("a" + Cjk + "b" + Emoji + Combining + Family + "\r!");
        editor.Draw();
        Assert.Equal(('a', TScreenCellKind.Single), (Cell(0).Character, Cell(0).Kind));
        Assert.Equal((TScreenCellKind.Lead, 2, Cjk), (Cell(1).Kind, Cell(1).CellWidth, Cell(1).GetText()));
        Assert.Equal(TScreenCellKind.Trail, Cell(2).Kind);
        Assert.Equal('b', Cell(3).Character);
        Assert.Equal((TScreenCellKind.Lead, 2, Emoji), (Cell(4).Kind, Cell(4).CellWidth, Cell(4).GetText()));
        Assert.Equal(TScreenCellKind.Trail, Cell(5).Kind);
        Assert.Equal((TScreenCellKind.Lead, 1, Combining), (Cell(6).Kind, Cell(6).CellWidth, Cell(6).GetText()));
        Assert.Equal((TScreenCellKind.Lead, 2, Family), (Cell(7).Kind, Cell(7).CellWidth, Cell(7).GetText()));
        Assert.Equal(TScreenCellKind.Trail, Cell(8).Kind);
        Assert.Equal(' ', Cell(9).Character);                           // a CR is a blank column
        Assert.Equal('!', Cell(10).Character);
        Assert.Equal(' ', Cell(11).Character);
    }

    [Fact]
    public void AWideCharacterCutByEitherWindowEdgeDrawsBlanks()
    {
        TEditor editor = Load("a" + Cjk + "b" + "\n" + new string('x', 39) + Cjk + "z");
        editor.ScrollTo(2, 0);                                          // column 2 is the CJK's second cell
        Assert.Equal(2, editor.delta.x);
        editor.Draw();
        Assert.Equal((' ', TScreenCellKind.Single), (Cell(0).Character, Cell(0).Kind));
        Assert.Equal('b', Cell(1).Character);
        editor.ScrollTo(0, 0);
        editor.Draw();
        Assert.Equal('x', Cell(38, 1).Character);
        Assert.Equal((' ', TScreenCellKind.Single), (Cell(39, 1).Character, Cell(39, 1).Kind));   // its first half only
        editor.ScrollTo(1, 0);
        editor.Draw();
        Assert.Equal((TScreenCellKind.Lead, Cjk), (Cell(38, 1).Kind, Cell(38, 1).GetText()));
        Assert.Equal(TScreenCellKind.Trail, Cell(39, 1).Kind);
    }

    [Fact]
    public void SelectionCoversWholeClusters()
    {
        TEditor editor = Load("a" + Cjk + "b" + Emoji + "c");
        ushort color = editor.GetColor(0x0201);
        byte normal = (byte)color, selected = (byte)(color >> 8);
        editor.SetSelect(1, 2, false);
        editor.Draw();
        Assert.Equal(new[] { normal, selected, selected, normal, normal, normal, normal },
                     Enumerable.Range(0, 7).Select(x => (byte)Cell(x).Attr).ToArray());
        editor.SetSelect(4, 5, false);                                  // inside the surrogate pair
        editor.Draw();
        Assert.Equal(new[] { normal, normal, normal, normal, selected, selected, normal },
                     Enumerable.Range(0, 7).Select(x => (byte)Cell(x).Attr).ToArray());
    }

    [Fact]
    public void AGiantClusterTakesOneColumnAndDrawsAsOneCell()
    {
        string text = "a" + new string('\u0301', 20_000) + "b" + Cjk;
        TEditor editor = Load(text);
        Assert.Equal(1, editor.CharPos(0, 20_001));
        Assert.Equal(0, editor.CharPos(0, 10_000));                     // inside the cluster: its column
        Assert.Equal(20_001u, editor.CharPtr(0, 1));
        Command(editor, Views.cmCharRight);
        Assert.Equal((20_001u, 1), (editor.curPtr, editor.curPos.x));
        editor.Draw();
        Assert.Equal(TScreenCellKind.Lead, Cell(0).Kind);
        Assert.Equal('a', Cell(0).Character);
        Assert.Equal('b', Cell(1).Character);
        Assert.Equal((TScreenCellKind.Lead, Cjk), (Cell(2).Kind, Cell(2).GetText()));
    }

    // \u2500\u2500 editing \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    [Fact]
    public void RegionalIndicatorPairingChangesMoveLaterColumns()
    {
        // x + 5 indicators: two flags and a lone one (each two columns), then y.
        string run = string.Concat(Enumerable.Repeat(RegionalC, 5));
        TEditor editor = Load("x" + run + "y" + Cjk);
        Assert.Equal(7, editor.CharPos(0, 11));
        editor.SetCurPtr(3, 0);                                         // after the first indicator
        Command(editor, Views.cmBackSpace);
        Assert.Equal(5, editor.CharPos(0, 9));                          // four indicators: two flags
        AssertLineColumns(editor, Text(editor), "after Backspace");
        Command(editor, Views.cmUndo);
        AssertLineColumns(editor, Text(editor), "after Undo");
        Command(editor, Views.cmRedo);
        AssertLineColumns(editor, Text(editor), "after Redo");
    }

    [Fact]
    public void OverwriteReplacesAWideCharacterWhole()
    {
        TEditor editor = Load(Cjk + Emoji + "z");
        editor.overwrite = true;
        Type(editor, "x");
        Type(editor, "y");
        Assert.Equal("xyz", Text(editor));
        Assert.Equal(2, editor.curPos.x);
    }

    [Fact]
    public void CopyPasteUndoRedoAndTheSavePointKeepVisualColumns()
    {
        TEditor clipboard = new TEditor(new TRect(0, 0, 10, 1), null, null, null, 4096);
        TEditor.clipboard = clipboard;
        TEditor editor = Load(Cjk + Emoji + "a\n" + "b");
        ShiftKey(editor, Keys.kbRight);
        ShiftKey(editor, Keys.kbRight);
        Assert.Equal((0u, 3u), (editor.selStart, editor.selEnd));
        Command(editor, Views.cmCopy);
        editor.SetCurPtr(editor.LineMove(0, 1) + 1, 0);                 // after "b"
        Command(editor, Views.cmPaste);
        Assert.Equal(Cjk + Emoji + "a\nb" + Cjk + Emoji, Text(editor));
        Assert.Equal(5, editor.curPos.x);
        Assert.True(editor.modified);
        Command(editor, Views.cmUndo);
        Assert.Equal(Cjk + Emoji + "a\nb", Text(editor));
        Assert.False(editor.modified);                                  // back at the save point
        Assert.Equal(1, editor.curPos.x);
        Command(editor, Views.cmRedo);
        Assert.Equal(5, editor.curPos.x);
        AssertLineColumns(editor, Text(editor), "after Redo");
        editor.MarkUnmodified();
        Type(editor, Cjk2);
        Command(editor, Views.cmUndo);
        Assert.False(editor.modified);
    }

    // Random edits anywhere in long mixed lines, after the lines were laid out far to the right: the shifted and
    // re-verified checkpoints give exactly the columns of a fresh layout.
    [Theory]
    [InlineData(0u, 1)]
    [InlineData(3u, 2)]
    [InlineData(8u, 3)]
    public void EditsKeepEveryColumnExact(uint tab, int seed)
    {
        TEditor.tabSize = tab;
        var rng = new Random(seed);
        string[] pieces = { "a", "bc ", "\t", Cjk, Emoji, Combining, "\u0301", RegionalC, FlagCz, Family, "\u200D", "\uFE0F", "\r" };
        string Piece() => pieces[rng.Next(pieces.Length)];
        var doc = new StringBuilder();
        for (int line = 0; line < 3; line++)
        {
            if (line > 0) doc.Append('\n');
            while (doc.Length < (line + 1) * 500) doc.Append(Piece());
        }
        TEditor editor = Load(doc.ToString());
        for (int step = 0; step < 60; step++)
        {
            string text = Text(editor);
            // Warm far columns of every line, so there are checkpoints after the edit to shift and verify.
            uint lineStart = 0;
            foreach (string line in text.Split('\n'))
            {
                editor.CharPos(lineStart, lineStart + (uint)line.Length);
                editor.CharPtr(lineStart, int.MaxValue / 2);
                lineStart += (uint)line.Length + 1;
            }
            uint at = editor.NextGraphemeBoundary((uint)rng.Next(text.Length));
            editor.SetCurPtr(at, 0);
            switch (rng.Next(6))
            {
                case 0: case 1: Type(editor, Piece()); break;
                case 2: Command(editor, Views.cmBackSpace); break;
                case 3: Command(editor, Views.cmDelChar); break;
                case 4: Command(editor, Views.cmUndo); break;
                default: editor.InsertText(Piece() + Piece()); break;
            }
            text = Text(editor);
            AssertLineColumns(editor, text, $"step {step}");
            uint caretLine = editor.LineStart(editor.curPtr);
            Assert.Equal(ColumnOf(Layout(text.Substring((int)caretLine, (int)(editor.LineEnd(caretLine) - caretLine))),
                                  (int)(editor.curPtr - caretLine)), editor.curPos.x);
            int widest = text.Split('\n').Max(l => Layout(l).Columns[^1]);
            Assert.True(editor.limit.x >= widest + 1, $"step {step}: extent {editor.limit.x} < {widest + 1}");
        }
    }

    // \u2500\u2500 legacy profile \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    [Fact]
    public void TheLegacyProfileKeepsOneColumnPerCodeUnit()
    {
        TextLayout.ProfileOverride = TextWidthProfile.Legacy;
        TEditor.tabSize = 8;
        TEditor editor = Load("a" + Cjk + "b" + Emoji + "c\t" + Combining + "d");
        Assert.Equal(5, editor.CharPos(0, 5));                          // "c": every unit one column
        Assert.Equal(4, editor.CharPos(0, 4));                          // inside the pair
        Assert.Equal(8, editor.CharPos(0, 7));                          // tabs still expand
        Assert.Equal(4u, editor.CharPtr(0, 4));                         // column-to-offset per code unit
        Assert.Equal(11, editor.CharPos(0, 10));
        editor.Draw();
        Assert.Equal(Cjk[0], Cell(1).Character);
        Assert.Equal(TScreenCellKind.Single, Cell(1).Kind);
        Assert.Equal('b', Cell(2).Character);
        Assert.Equal('\uD83D', Cell(3).Character);
        Assert.Equal('\uDE00', Cell(4).Character);
        Assert.Equal('\u0301', Cell(9).Character);
        Command(editor, Views.cmCharRight);                             // the caret still steps by cluster
        Command(editor, Views.cmCharRight);
        Command(editor, Views.cmCharRight);
        Command(editor, Views.cmCharRight);
        Assert.Equal((5u, 5), (editor.curPtr, editor.curPos.x));
        TextLayout.ProfileOverride = TextWidthProfile.Default;
        Assert.Equal(6, editor.CharPos(0, 5));                          // a profile change drops the cache
    }

    // ProvenanceRemediationTests.Editor_TabColumnsAcrossEveryGap pins per-code-unit columns (inside the surrogate
    // pair too): they hold under the legacy profile; the default profile gives the same text visual columns.
    [Theory]
    [InlineData(0u)] [InlineData(1u)] [InlineData(2u)] [InlineData(4u)] [InlineData(8u)]
    public void TabColumnsAcrossEveryGapPerProfile(uint width)
    {
        TEditor.tabSize = width;
        const string text = "a\t\tb\u010d\U0001F600\nend";
        int stop = (int)Math.Max(1u, width);
        var columns = new List<int> { 0 };
        foreach (char c in text[..text.IndexOf('\n')]) columns.Add(c == '\t' ? columns[^1] + stop - columns[^1] % stop : columns[^1] + 1);
        for (int gap = 0; gap <= text.Length; gap++)
        {
            TextLayout.ProfileOverride = TextWidthProfile.Legacy;
            TEditor editor = DirectEditor(text, gap);
            for (int offset = 0; offset < columns.Count; offset++) Assert.Equal(columns[offset], editor.CharPos(0, (uint)offset));
            for (int column = -1; column <= columns[^1] + 3; column++)
                Assert.Equal((uint)columns.FindLastIndex(c => c <= Math.Max(0, column)), editor.CharPtr(0, column));
            TextLayout.ProfileOverride = TextWidthProfile.Default;
            AssertLineColumns(DirectEditor(text, gap), text, $"gap {gap}");
        }
    }

    // The text laid out in the buffer directly, the gap at `gap` (as the provenance tests build it).
    private static TEditor DirectEditor(string text, int gap)
    {
        var editor = new TEditor(new TRect(0, 0, 40, 10), null, null, null, 256);
        editor.bufLen = (uint)text.Length; editor.curPtr = (uint)gap; editor.gapLen = editor.bufSize - editor.bufLen;
        text.AsSpan(0, gap).CopyTo(editor.buffer);
        text.AsSpan(gap).CopyTo(editor.buffer.AsSpan(gap + (int)editor.gapLen));
        return editor;
    }

    // \u2500\u2500 cost on long lines (counted, not timed) \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    [Theory]
    [InlineData(100_000)]
    [InlineData(1_000_000)]
    public void KeystrokesCaretMovesAndDrawsOnALongLineDoNotRescanIt(int length)
    {
        string unit = "word " + Cjk + "\t" + Emoji + Combining + " ";
        var builder = new StringBuilder(length + 64);
        while (builder.Length < length) builder.Append(unit);
        string line = builder.ToString();
        TEditor editor = Load(line + "\n" + line + "\nend", caret: (uint)line.Length);
        editor.TrackCursor(false);
        editor.Draw();

        // Caret steps at the far right.
        long before = editor.GraphemeUnitsRead;
        for (int i = 0; i < 100; i++) Command(editor, Views.cmCharLeft);
        for (int i = 0; i < 100; i++) Command(editor, Views.cmCharRight);
        long perStep = (editor.GraphemeUnitsRead - before) / 200;
        Assert.Equal((uint)line.Length, editor.curPtr);

        // Vertical moves between the two long lines, once each line is known.
        Command(editor, Views.cmLineDown);
        Command(editor, Views.cmLineUp);
        before = editor.GraphemeUnitsRead;
        for (int i = 0; i < 20; i++) { Command(editor, Views.cmLineDown); Command(editor, Views.cmLineUp); }
        long perVertical = (editor.GraphemeUnitsRead - before) / 40;

        // Typing and deleting in the middle of the line.
        editor.SetCurPtr(editor.NextGraphemeBoundary((uint)line.Length / 2), 0);
        editor.TrackCursor(false);
        before = editor.GraphemeUnitsRead;
        for (int i = 0; i < 50; i++) Type(editor, i % 2 == 0 ? "x" : Cjk);
        for (int i = 0; i < 20; i++) Command(editor, Views.cmBackSpace);
        long perKeystroke = (editor.GraphemeUnitsRead - before) / 70;

        // Vertical moves from the middle of the edited line to the other long line and back.
        before = editor.GraphemeUnitsRead;
        for (int i = 0; i < 20; i++) { Command(editor, Views.cmLineDown); Command(editor, Views.cmLineUp); }
        perVertical = Math.Max(perVertical, (editor.GraphemeUnitsRead - before) / 40);
        Assert.InRange(editor.curPtr, (uint)line.Length / 2 - 64, (uint)line.Length / 2 + 200);

        // Full redraws far to the right.
        before = editor.GraphemeUnitsRead;
        for (int i = 0; i < 20; i++) editor.Draw();
        long perDraw = (editor.GraphemeUnitsRead - before) / 20;

        // Queries and draws allocate nothing once warm.
        var rng = new Random(5);
        uint lineEnd = editor.LineEnd(0);
        int width = editor.CharPos(0, lineEnd);
        for (int i = 0; i < 50; i++) { editor.CharPos(0, (uint)rng.Next((int)lineEnd)); editor.CharPtr(0, rng.Next(width)); }
        editor.Draw();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++)
        {
            uint p = editor.NextGraphemeBoundary((uint)(lineEnd - 1 - rng.Next(2000)));
            editor.CharPos(0, p);
            editor.CharPtr(0, width - rng.Next(2000));
        }
        for (int i = 0; i < 20; i++) editor.Draw();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        _output.WriteLine($"length {length}: {perStep} units per caret step, {perVertical} per vertical move, " +
                          $"{perKeystroke} per keystroke, {perDraw} per full draw, {allocated} bytes allocated by queries and draws");
        Assert.True(perStep < 1024, $"{perStep} units per caret step");
        Assert.True(perVertical < 2048, $"{perVertical} units per vertical move");
        Assert.True(perKeystroke < 4096, $"{perKeystroke} units per keystroke");
        Assert.True(perDraw < 5 * 1024, $"{perDraw} units per draw");
        Assert.Equal(0, allocated);
        AssertLineColumns(editor, Text(editor).Substring(0, (int)editor.LineEnd(0)), "after the session", samples: 20_000);

    }
}
