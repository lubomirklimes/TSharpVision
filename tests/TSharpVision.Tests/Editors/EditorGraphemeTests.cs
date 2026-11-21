using System.Globalization;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Text;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Editors;

/// <summary>Grapheme-aware caret movement, deletion, overwrite, hit-testing and input sanitising (TV13d).</summary>
[Collection("NonParallel")]
public sealed class EditorGraphemeTests : IDisposable
{
    private const string Emoji = "\U0001F600";                         // one surrogate pair
    private const string Combining = "e\u0301";                        // e + combining acute
    private const string SkinTone = "\U0001F44D\U0001F3FD";            // thumbs up + medium skin tone
    private const string Flag = "\U0001F1E8\U0001F1FF";                // CZ regional indicators
    private const string Flag2 = "\U0001F1F8\U0001F1F0";               // SK regional indicators
    private const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
    private const string Cjk = "漢字";
    private const string Czech = "č";

    private static readonly string[] Mixed =
    {
        "a" + Emoji + "b",
        Combining + "x" + Combining + Combining,
        SkinTone + " " + Flag + Flag2,
        Family + "\n" + Cjk + Czech,
        "e\u0301\u0302\u0323!" + Emoji + "\n\n" + Flag,
        "\t" + Family + Emoji + "\r\n" + Combining,
    };

    private readonly DriverScope _driver = new(80, 25);
    private readonly EditorClipboardScope _clipboard = new();
    private readonly ClipboardServiceScope _service = new();
    private readonly TestGroup _host;

    public EditorGraphemeTests()
    {
        TEventQueue.Resume();
        _host = new TestGroup(new TRect(0, 0, 40, 5)) { buffer = new ScreenBuffer(40 * 5) };
        _host.state |= (ushort)(Views.sfVisible | Views.sfExposed);
    }

    public void Dispose()
    {
        _service.Dispose();
        _clipboard.Dispose();
        _driver.Dispose();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    public static IEnumerable<object[]> MixedTexts() => Mixed.Select(s => new object[] { s });

    private TEditor Load(string text, uint caret = 0)
    {
        var editor = new TEditor(new TRect(0, 0, 40, 5), null, null, null, 4096);
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
        var builder = new StringBuilder();
        for (uint p = 0; p < editor.bufLen; p++) builder.Append(editor.BufChar(p));
        return builder.ToString();
    }

    // Cluster boundaries of a string by the same rules, LF always standing alone; the editor never treats CR LF as one.
    private static List<int> Boundaries(string text)
    {
        var result = new List<int> { 0 };
        foreach (string line in text.Split('\n'))
        {
            int start = result[^1];
            int at = 0;
            while (at < line.Length)
            {
                at += StringInfo.GetNextTextElementLength(line.AsSpan(at));
                result.Add(start + at);
            }
            if (start + line.Length < text.Length) result.Add(start + line.Length + 1);
        }
        return result;
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

    private static void AssertWellFormed(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i])) { Assert.True(i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]), $"lone high surrogate at {i}"); i++; }
            else Assert.False(char.IsLowSurrogate(text[i]), $"lone low surrogate at {i}");
        }
    }

    // ── boundaries at every caret/gap position ───────────────────────────────

    [Theory]
    [MemberData(nameof(MixedTexts))]
    public void BoundaryHelpersMatchTheClustersAtEveryGapPosition(string text)
    {
        List<int> boundaries = Boundaries(text);
        for (int gap = 0; gap <= text.Length; gap++)
        {
            TEditor editor = Load(text);
            editor.SetSelect((uint)gap, (uint)gap, false);              // the gap may sit inside a cluster
            for (int p = 0; p <= text.Length; p++)
            {
                int next = boundaries.FirstOrDefault(b => b > p, text.Length);
                int prev = boundaries.LastOrDefault(b => b < p, 0);
                Assert.Equal((uint)next, editor.NextGraphemeBoundary((uint)p));
                Assert.Equal((uint)prev, editor.PrevGraphemeBoundary((uint)p));
            }
            Assert.Equal((uint)gap, editor.curPtr);
        }
    }

    [Theory]
    [MemberData(nameof(MixedTexts))]
    public void ArrowKeysStepOverWholeClusters(string text)
    {
        List<int> boundaries = Boundaries(text);
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
    }

    [Fact]
    public void LegacyCodeUnitHelpersAreUnchanged()
    {
        TEditor editor = Load("a" + Emoji);
        Assert.Equal(2u, editor.NextChar(1));
        Assert.Equal(2u, editor.PrevChar(3));
        // TV13o: columns are visual; column 2 is the emoji's second cell, which maps to the emoji's start.
        Assert.Equal(1u, editor.CharPtr(0, 2));
        Assert.Equal(3u, editor.CharPtr(0, 3));
        // Under the legacy cell-width profile column-to-offset stays per code unit, inside the pair (TSharpVision 1.2).
        TextLayout.ProfileOverride = TextWidthProfile.Legacy;
        try { Assert.Equal(2u, editor.CharPtr(0, 2)); }
        finally { TextLayout.ProfileOverride = TextWidthProfile.Default; }
    }

    [Fact]
    public void ShiftArrowSelectsWholeClusters()
    {
        TEditor editor = Load(Flag + Family);
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbRight;
        ev.keyDown.controlKeyState = (ushort)Keys.kbShift;
        editor.HandleEvent(ref ev);
        Assert.Equal((0u, (uint)Flag.Length), (editor.selStart, editor.selEnd));
    }

    // ── deletion, overwrite ──────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(MixedTexts))]
    public void BackspaceAndDeleteRemoveOneClusterAndUndoRestoresIt(string text)
    {
        List<int> boundaries = Boundaries(text);
        for (int i = 1; i < boundaries.Count; i++)
        {
            int start = boundaries[i - 1], end = boundaries[i];
            string expected = text.Remove(start, end - start);

            TEditor back = Load(text, caret: (uint)end);
            Command(back, Views.cmBackSpace);
            Assert.Equal(expected, Text(back));
            AssertWellFormed(Text(back));
            back.Undo();
            Assert.Equal(text, Text(back));

            TEditor del = Load(text, caret: (uint)start);
            Command(del, Views.cmDelChar);
            Assert.Equal(expected, Text(del));
            del.Undo();
            Assert.Equal(text, Text(del));
        }
    }

    [Fact]
    public void BackspaceRemovesTheWholeCombiningSequence()
    {
        TEditor editor = Load("a" + Combining, caret: 3);
        Command(editor, Views.cmBackSpace);
        Assert.Equal("a", Text(editor));
    }

    [Theory]
    [InlineData(Emoji)]
    [InlineData(Combining)]
    [InlineData(Family)]
    [InlineData(Flag)]
    public void OverwriteReplacesOneCluster(string cluster)
    {
        TEditor editor = Load(cluster + "z");
        editor.overwrite = true;
        Type(editor, "x");
        Assert.Equal("xz", Text(editor));
        TEditor wide = Load("abc");
        wide.overwrite = true;
        Type(wide, cluster);
        Assert.Equal(cluster + "bc", Text(wide));
    }

    [Fact]
    public void ACaretInsideAClusterStepsToItsEdges()
    {
        TEditor editor = Load("a" + Family + "b");
        editor.SetSelect(3, 3, false);                                 // inside the ZWJ sequence
        Command(editor, Views.cmCharRight);
        Assert.Equal((uint)(1 + Family.Length), editor.curPtr);
        editor.SetSelect(3, 3, false);
        Command(editor, Views.cmCharLeft);
        Assert.Equal(1u, editor.curPtr);
    }

    // ── hit-testing, vertical movement, words ────────────────────────────────

    [Fact]
    public void ClickingTheSecondCellOfAPairLandsBeforeThePair()
    {
        TEditor editor = Load("a" + Emoji + "b");
        Assert.Equal(1u, editor.GetMousePtr(new TPoint(2, 0)));        // the low-surrogate cell
        Assert.Equal(3u, editor.GetMousePtr(new TPoint(3, 0)));
    }

    [Fact]
    public void VerticalMovementNeverLandsInsideACluster()
    {
        TEditor editor = Load("abc\n" + Emoji + "x", caret: 1);       // column 1 below is the pair's low half
        Command(editor, Views.cmLineDown);
        Assert.Equal(4u, editor.curPtr);
    }

    [Fact]
    public void WordMovementTreatsSupplementaryLettersAndMarksAsWordCharacters()
    {
        const string math = "\U0001D400\U0001D401";                    // MATHEMATICAL BOLD CAPITAL A, B
        TEditor editor = Load("ab " + math + " cd");
        Assert.Equal(3u, editor.NextWord(0));
        Assert.Equal(8u, editor.NextWord(3));
        Assert.Equal(3u, editor.PrevWord(8));
        TEditor accent = Load("cafe\u0301 x");
        Assert.Equal(6u, accent.NextWord(0));
        Assert.Equal(0u, accent.PrevWord(5));
    }

    [Fact]
    public void CtrlBackspaceAndCtrlDeleteKeepClustersWhole()
    {
        TEditor editor = Load("x " + Emoji + Emoji, caret: 6);
        Command(editor, Views.cmDelWordLeft);
        AssertWellFormed(Text(editor));
        TEditor fwd = Load(Emoji + " y");
        Command(fwd, Views.cmDelWord);
        AssertWellFormed(Text(fwd));
        Assert.Equal("y", Text(fwd));
    }

    [Theory]
    [InlineData(false, 3)]                                             // the family's second cell (columns 2-3)
    [InlineData(true, 4)]                                              // legacy: a code unit inside the family
    public void DoubleClickOnAnEmojiSelectsTheWholeCluster(bool legacy, int cell)
    {
        if (legacy) TextLayout.ProfileOverride = TextWidthProfile.Legacy;
        try
        {
            TEditor editor = Load("a " + Family + " b");
            var up = new TEvent { What = Events.evMouseUp };
            TEventQueue.Enqueue(up);
            var down = new TEvent { What = Events.evMouseDown };
            down.mouse.where = new TPoint(cell, 0);
            down.mouse.buttons = 1;
            down.mouse.doubleClick = true;
            editor.HandleEvent(ref down);
            Assert.Equal((2u, (uint)(2 + Family.Length)), (editor.selStart, editor.selEnd));
        }
        finally { TextLayout.ProfileOverride = TextWidthProfile.Default; }
    }

    // ── malformed input ──────────────────────────────────────────────────────

    [Fact]
    public void TypedUnpairedSurrogatesBecomeReplacementCharacters()
    {
        TEditor editor = Load("");
        Type(editor, "\uD83D");
        Type(editor, "\uDE00");
        Assert.Equal("��", Text(editor));
        Type(editor, Emoji);
        Assert.Equal("��" + Emoji, Text(editor));
    }

    [Fact]
    public void PastedAndInsertTextCommandUnpairedSurrogatesBecomeReplacementCharacters()
    {
        var service = new InMemoryClipboardService();
        ClipboardService.Current = service;
        service.SetText("x\uDE00y" + Emoji);
        TEditor editor = Load("");
        editor.ClipPaste();
        Assert.Equal("x�y" + Emoji, Text(editor));

        TEditor command = Load("");
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = Views.cmInsertText;
        ev.message.infoPtr = new TextInfo("\uD83Dz");
        command.HandleEvent(ref ev);
        Assert.Equal("�z", Text(command));
    }

    [Fact]
    public void ProgrammaticInsertTextStaysVerbatim()
    {
        TEditor editor = Load("");
        editor.InsertText("\uD83D");
        Assert.Equal("\uD83D", Text(editor));
        Assert.Equal(1u, editor.NextGraphemeBoundary(0));              // a lone surrogate is one step of its own
    }

    // ── search ───────────────────────────────────────────────────────────────

    [Fact]
    public void SearchFindsClustersAndReplaceAllKeepsTextWellFormed()
    {
        TEditor editor = Load("a" + Emoji + "b" + Emoji);
        Assert.True(editor.Search(Emoji.ToCharArray().Append('\0').ToArray(), Views.efCaseSensitive));
        Assert.Equal((1u, 3u), (editor.selStart, editor.selEnd));

        editor.SetCurPtr(0, 0);
        Emoji.CopyTo(0, TEditor.findStr, 0, 2);
        "\U0001F642".CopyTo(0, TEditor.replaceStr, 0, 2);
        TEditor.editorFlags = (ushort)(Views.efDoReplace | Views.efReplaceAll | Views.efCaseSensitive);
        editor.DoSearchReplace();
        Assert.Equal("a\U0001F642b\U0001F642", Text(editor));
        AssertWellFormed(Text(editor));
    }

    [Fact]
    public void WholeWordSearchUsesSupplementaryLetters()
    {
        TEditor editor = Load("\U0001D400ab ab");
        Assert.True(editor.Search("ab\0".ToCharArray(), (ushort)(Views.efCaseSensitive | Views.efWholeWordsOnly)));
        Assert.Equal(5u, editor.selStart);
    }

    [Fact]
    public void ATruncatedFindPatternKeepsWholePairs()
    {
        string pattern = new string('a', 78) + Emoji;                  // 80 units; the buffer keeps 79
        TEditor.editorDialog = (d, info) =>
        {
            if (info is TFindDialogRec rec) rec.Find = pattern.ToCharArray();
            return d == Views.edFind ? Views.cmOK : Views.cmCancel;
        };
        TEditor editor = Load("zz");
        editor.Find();
        Assert.Equal('\0', TEditor.findStr[78]);
        Assert.Equal('a', TEditor.findStr[77]);
    }

    // ── rendering limits and a long random session ───────────────────────────

    // The interim rendering limit of TV13d-TV13i (one cell per UTF-16 code unit) is now the legacy profile's
    // documented behaviour; the default profile draws clusters (EditorVisualColumnTests).
    [Fact]
    public void AClusterStillDrawsOneCellPerCodeUnit()
    {
        TextLayout.ProfileOverride = TextWidthProfile.Legacy;
        try
        {
            TEditor editor = Load("a" + Emoji + "b" + Combining);
            editor.Draw();
            Assert.Equal('\uD83D', _host.buffer!.Data[1].Character);
            Assert.Equal('\uDE00', _host.buffer!.Data[2].Character);
            Assert.Equal('b', _host.buffer!.Data[3].Character);
            Assert.Equal('\u0301', _host.buffer!.Data[5].Character);
            editor.SetCurPtr(3, 0);
            Assert.Equal(3, editor.curPos.x);                          // the caret column counts code units
        }
        finally { TextLayout.ProfileOverride = TextWidthProfile.Default; }
    }


    [Fact]
    public void ARandomEditingSessionNeverProducesMalformedText()
    {
        var random = new Random(1302);
        string[] pieces = { Emoji, Combining, SkinTone, Flag, Family, Cjk, Czech, "a", " ", "\n", "\u0301" };
        ushort[] commands =
        {
            Views.cmCharLeft, Views.cmCharRight, Views.cmBackSpace, Views.cmDelChar, Views.cmWordLeft,
            Views.cmWordRight, Views.cmDelWord, Views.cmDelWordLeft, Views.cmLineUp, Views.cmLineDown, Views.cmUndo,
            Views.cmRedo, Views.cmLineEnd, Views.cmLineStart,
        };
        TEditor editor = Load("");
        for (int step = 0; step < 3000; step++)
        {
            int action = random.Next(10);
            if (action < 4) Type(editor, pieces[random.Next(pieces.Length)]);
            else if (action == 4) editor.overwrite = !editor.overwrite;
            else Command(editor, commands[random.Next(commands.Length)]);
            AssertWellFormed(Text(editor));
        }
    }
}
