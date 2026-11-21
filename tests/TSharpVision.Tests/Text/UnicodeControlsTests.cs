// TV13p / U6a: core controls measure, fit, scroll and hit-test by cells (TextLayout through CellText), and keep the
// 1.2 behaviour (one cell per UTF-16 unit) under the legacy cell-width profile.
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using TSharpVision.Text;
using Xunit;

namespace TSharpVision.Tests.Text;

[Collection("NonParallel")]
public sealed class UnicodeControlsTests : IDisposable
{
    // Wide (CJK, two cells each), emoji (two cells), combining (one cell, two units).
    private const string Nihongo = "\u65E5\u672C\u8A9E";          // three wide characters, 6 cells
    private const string Bunken = "\u6587\u4EF6";                 // two wide characters, 4 cells
    private const string Emoji = "\U0001F600";                    // one cluster, 2 units, 2 cells
    private const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467"; // one ZWJ cluster, 2 cells
    private const string Combining = "e\u0301";                   // one cluster, 1 cell

    private const ushort CmdA = 700, CmdB = 701, CmdC = 702, CmdE = 703;

    private readonly DriverScope _driver = new(100, 30);
    private readonly TCommandSet _commands = new();

    public UnicodeControlsTests()
    {
        TEventQueue.Resume();
        TView.GetCommands(_commands);
        foreach (ushort c in new[] { CmdA, CmdB, CmdC, CmdE }) TView.EnableCommand(c);
    }

    public void Dispose()
    {
        TextLayout.ProfileOverride = TextWidthProfile.Default;
        TView.SetCommands(_commands);
        _driver.Dispose();
    }

    private static T Under<T>(TextWidthProfile profile, Func<T> body)
    {
        TextWidthProfile old = TextLayout.ProfileOverride;
        TextLayout.ProfileOverride = profile;
        try { return body(); }
        finally { TextLayout.ProfileOverride = old; }
    }

    private static void Under(TextWidthProfile profile, Action body) => Under(profile, () => { body(); return 0; });

    // ---- host -------------------------------------------------------------------------------------------

    /// <summary>A buffered group that stands in for the desktop: views draw into its cells.</summary>
    private sealed class Host : TGroup
    {
        public TDialog? Dialog;

        public Host(int width, int height) : base(new TRect(0, 0, width, height))
        {
            options = (ushort)(options & ~Views.ofSelectable);
            buffer = new ScreenBuffer(width * height * ScreenBuffer.GetSize());
            state |= (ushort)(Views.sfVisible | Views.sfExposed);
        }

        public override ushort ExecView(TView p)
        {
            Dialog = p as TDialog;
            Insert(p);
            p.DrawView();
            return Views.cmOK;
        }

        public TScreenChar[] Cells(int y) => buffer!.Data.Slice(y * size.x, size.x).ToArray();

        /// <summary>Text of cells [x, x + count): a cluster's text at its lead, nothing for trails.</summary>
        public string Text(int y, int x = 0, int count = -1)
        {
            if (count < 0) count = size.x - x;
            var sb = new StringBuilder();
            foreach (TScreenChar c in Cells(y).AsSpan(x, count))
            {
                if (c.Kind == TScreenCellKind.Trail) continue;
                string t = c.GetText();
                sb.Append(t == "\0" ? " " : t);
            }
            return sb.ToString();
        }

        /// <summary>One character per cell: the 1.2 view of a row (Character only).</summary>
        public string Chars(int y, int x = 0, int count = -1)
        {
            if (count < 0) count = size.x - x;
            var sb = new StringBuilder();
            foreach (TScreenChar c in Cells(y).AsSpan(x, count)) sb.Append(c.Character == '\0' ? ' ' : c.Character);
            return sb.ToString();
        }

        public void AssertWellFormed()
        {
            for (int y = 0; y < size.y; y++) WideCellTests.AssertWellFormed(Cells(y), $"row {y}");
        }

        public TScreenChar[] Snapshot() => buffer!.Data.ToArray();
    }

    private static Host Show(int w, int h, params TView[] views)
    {
        var host = new Host(w, h);
        foreach (TView v in views) host.Insert(v);
        host.Redraw();
        return host;
    }

    // ---- shared helper ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("~F~ile", 4, 4)]
    [InlineData("~\u6587~\u4EF6", 4, 2)]
    [InlineData("A\U0001F600~B~", 4, 4)]
    [InlineData("e\u0301~x~", 2, 3)]
    [InlineData("~~", 0, 0)]
    [InlineData("", 0, 0)]
    public void HotkeyWidthIsCellsAndCStrLenUnderLegacy(string text, int unicode, int legacy)
    {
        Assert.Equal(unicode, Under(TextWidthProfile.Unicode, () => CellText.HotkeyWidth(text)));
        Assert.Equal(legacy, Under(TextWidthProfile.Legacy, () => CellText.HotkeyWidth(text)));
        int oldCStrLen = text.Count(c => c != '~');
        Assert.Equal(oldCStrLen, Under(TextWidthProfile.Legacy, () => CellText.HotkeyWidth(text)));
    }

    [Fact]
    public void HotkeyLetterIsTheCharacterAfterTheFirstTilde()
    {
        Assert.Equal('F', CellText.Hotkey("~f~ile"));
        Assert.Equal('\u6587', CellText.Hotkey("~\u6587~\u4EF6"));
        Assert.Equal('\0', CellText.Hotkey("plain"));
        Assert.Equal('\0', CellText.Hotkey("end~"));
        Assert.Equal('\0', CellText.Hotkey(null));
        Assert.Equal('O', new TMenuItem("~o~pen", CmdA, 0).HotChar());
    }

    [Fact]
    public void FitTruncateSkipAndTailNeverSplitAClusterAndMatch12UnderLegacy()
    {
        string s = "a" + Nihongo + Emoji + Combining + "z"; // cells: a1 + 6 + 2 + 1 + 1 = 11
        Under(TextWidthProfile.Unicode, () =>
        {
            Assert.Equal(11, CellText.Measure(s));
            Assert.Equal(2, CellText.Fit(s, 4, out int used)); // a + one ideograph; the next would need cells 3-4
            Assert.Equal(3, used);
            Assert.Equal("a" + Nihongo, CellText.Truncate(s, 7));
            Assert.Equal("a" + Nihongo, CellText.Truncate(s, 8)); // the emoji needs 2 cells
            Assert.Equal(2, CellText.SkipColumns(s, 2, out int pad));   // column 2 is inside the first ideograph
            Assert.Equal(1, pad);
            Assert.Equal(1, CellText.SkipColumns(s, 1, out pad));
            Assert.Equal(0, pad);
            Assert.Equal(s.Length, CellText.SkipColumns(s, 50, out pad));
            Assert.Equal(s.IndexOf(Combining, StringComparison.Ordinal), CellText.TailStart(s, 2));
            Assert.Equal(s.IndexOf(Emoji, StringComparison.Ordinal), CellText.TailStart(s, 4));
            Assert.Equal(s.IndexOf(Combining, StringComparison.Ordinal), CellText.TailStart(s, 3)); // emoji does not fit 3
            Assert.Equal(7, CellText.ColumnAt(s, 4));   // the emoji starts at column 7
            Assert.Equal(7, CellText.ColumnAt(s, 5));   // its low surrogate belongs to the same cluster
            Assert.Equal(13, CellText.ColumnAt(s, s.Length + 2));
        });
        Under(TextWidthProfile.Legacy, () =>
        {
            Assert.Equal(s.Length, CellText.Measure(s));
            Assert.Equal(4, CellText.Fit(s, 4));
            Assert.Equal(s.Substring(0, 5), CellText.Truncate(s, 5)); // splits the pair exactly like Substring did
            Assert.Equal(2, CellText.SkipColumns(s, 2, out int pad));
            Assert.Equal(0, pad);
            Assert.Equal(s.Length - 2, CellText.TailStart(s, 2));
            Assert.Equal(5, CellText.ColumnAt(s, 5));
        });
    }

    // ---- TButton ----------------------------------------------------------------------------------------

    [Fact]
    public void ButtonCentresItsTitleByCells()
    {
        // 14 columns: face 13, title area s = 13; l = (s - width - 1) / 2, drawn from column 1 + l.
        string title = "~\u65E5~\u672C\u8A9E"; // 6 cells, 3 units without the markers
        Host u = Under(TextWidthProfile.Unicode, () => Show(20, 3, new TButton(new TRect(0, 0, 14, 2), title, CmdA, 0)));
        u.AssertWellFormed();
        Assert.Equal("   " + Nihongo + "   ", u.Text(0, 1, 12)); // l = (13 - 6 - 1) / 2 = 3
        Assert.Equal(TScreenCellKind.Lead, u.Cells(0)[4].Kind);

        Host l = Under(TextWidthProfile.Legacy, () => Show(20, 3, new TButton(new TRect(0, 0, 14, 2), title, CmdA, 0)));
        Assert.Equal("    " + Nihongo, l.Chars(0, 1, 7)); // 1.2: l = (13 - 3 - 1) / 2 = 4
    }

    [Fact]
    public void ButtonHotkeyStillUsesTheLetterAfterTheTilde()
    {
        var owner = new Host(30, 5);
        var button = new TButton(new TRect(0, 0, 14, 2), "\u65E5~O~K", CmdA, 0);
        owner.Insert(button);
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbAltO;
        button.HandleEvent(ref ev);
        Assert.Equal(Events.evNothing, ev.What); // pressed: the event is consumed
    }

    // ---- TLabel / TStaticText ---------------------------------------------------------------------------

    [Fact]
    public void LabelDrawsClustersWholeAndClipsAtTheViewEdge()
    {
        Host h = Show(20, 2, new TLabel(new TRect(0, 0, 8, 1), "~\u65E5~\u672C\u8A9E" + Emoji, null));
        h.AssertWellFormed();
        // Cells 1..6 hold the ideographs; the emoji would need cells 7-8 of an 8-cell label: it is cut, not halved.
        Assert.Equal(" " + Nihongo + " ", h.Text(0, 0, 8));
    }

    [Fact]
    public void StaticTextWrapsByCellsAndNeverSplitsClusters()
    {
        string text = Nihongo + Nihongo + Nihongo + Nihongo; // 12 ideographs, no spaces: hard breaks
        Host h = Show(20, 4, new TStaticText(new TRect(0, 0, 10, 4), text));
        h.AssertWellFormed();
        Assert.Equal(new[] { "\u65E5\u672C\u8A9E\u65E5\u672C", "\u8A9E\u65E5\u672C\u8A9E\u65E5", "\u672C\u8A9E" },
                     new[] { h.Text(0, 0, 10).TrimEnd(), h.Text(1, 0, 10).TrimEnd(), h.Text(2, 0, 10).TrimEnd() });
        Assert.Equal(3, TStaticText.CountRows(text, 10));

        // Odd width: a row of 9 cells holds four ideographs, never half of a fifth.
        Assert.Equal(3, TStaticText.CountRows(text, 9));
        // Width 1: a wide cluster still makes progress (one per row).
        Assert.Equal(12, TStaticText.CountRows(text, 1));
        Assert.Equal(1, TStaticText.CountRows(Family, 1));
    }

    [Fact]
    public void StaticTextBreaksAtSpacesByCellsAndCentresByCells()
    {
        string text = "\x03" + Nihongo + " " + Bunken + " " + Nihongo;
        Host h = Show(20, 4, new TStaticText(new TRect(0, 0, 12, 4), text));
        h.AssertWellFormed();
        // Row 1: "\u65E5\u672C\u8A9E \u6587\u4EF6" is 11 cells and fits 12; the next word would not. Centred: (12 - 11) / 2 = 0.
        Assert.Equal(Nihongo + " " + Bunken, h.Text(0, 0, 12).TrimEnd());
        // Row 2: 6 cells centred in 12 -> starts at column 3.
        Assert.Equal("   " + Nihongo, h.Text(1, 0, 12).TrimEnd());
        Assert.Equal(TScreenCellKind.Lead, h.Cells(1)[3].Kind);
    }

    // The 1.2 row breaker (one cell per UTF-16 unit), kept as the reference for the legacy profile and ASCII text.
    private static void OldNextRow(string s, ref int p, int width, ref bool center, out int start, out int end)
    {
        int l = s.Length;
        if (s[p] == (char)3) { center = true; p++; }
        int i = p;
        int j;
        do
        {
            j = p;
            while (p < l && s[p] == ' ') p++;
            while (p < l && s[p] != ' ' && s[p] != '\n') p++;
        } while (p < l && p < i + width && s[p] != '\n');
        if (p > i + width) p = j > i ? j : i + width;
        start = i;
        end = p;
        while (p < l && s[p] == ' ') p++;
        if (p < l && s[p] == '\n') { center = false; p++; }
    }

    private static List<(int, int)> Rows(string s, int width, bool old)
    {
        var rows = new List<(int, int)>();
        int p = 0;
        bool center = false;
        while (p < s.Length)
        {
            int start, end;
            if (old) OldNextRow(s, ref p, width, ref center, out start, out end);
            else TStaticText.NextRow(s, ref p, width, ref center, out start, out end, out _);
            rows.Add((start, end));
        }
        return rows;
    }

    [Fact]
    public void StaticTextRowsEqual12ForAsciiAndUnderLegacy()
    {
        var random = new Random(1302);
        string[] asciiPieces = { "a", "bb", "word", "longerword", " ", "  ", "\n", "x\ny", "\x03" };
        string[] widePieces = { Nihongo, Emoji, Combining, Family, " ", "\n", "ab" };
        for (int n = 0; n < 400; n++)
        {
            var ascii = new StringBuilder();
            var wide = new StringBuilder();
            for (int k = random.Next(1, 25); k > 0; k--)
            {
                ascii.Append(asciiPieces[random.Next(asciiPieces.Length)]);
                wide.Append(widePieces[random.Next(widePieces.Length)]);
            }
            int width = random.Next(1, 14);
            string a = ascii.ToString(), w = wide.ToString();
            Assert.Equal(Rows(a, width, true), Under(TextWidthProfile.Unicode, () => Rows(a, width, false)));
            Assert.Equal(Rows(w, width, true), Under(TextWidthProfile.Legacy, () => Rows(w, width, false)));

            // Unicode: every row fits its width (unless one cluster alone is wider) and ends on a cluster boundary.
            Under(TextWidthProfile.Unicode, () =>
            {
                foreach ((int start, int end) in Rows(w, width, false))
                {
                    string row = w[start..end];
                    int cells = CellText.Measure(row.TrimEnd(' '));
                    Assert.True(cells <= width || CellText.Fit(row, width) == 0, $"'{row}' is {cells} cells > {width}");
                    Assert.False(end < w.Length && end > 0 && char.IsLowSurrogate(w[end]), "row ends inside a pair");
                }
            });
        }
    }

    [Fact]
    public void MessageBoxSizesWideTextByCells()
    {
        string msg = string.Concat(Enumerable.Repeat(Nihongo + Bunken + " ", 12)).TrimEnd();
        var host = new Host(80, 25);
        Assert.Equal(Views.cmOK, MsgBox.MessageBox(host, msg, MsgBox.mfInformation | MsgBox.mfOKButton));
        TDialog d = host.Dialog!;
        host.AssertWellFormed();
        Assert.True(d.origin.x >= 0 && d.origin.x + d.size.x <= 80 && d.origin.y + d.size.y <= 25);
        var drawn = new StringBuilder();
        for (int y = 2; y < d.size.y - 3; y++)
            drawn.Append(host.Text(d.origin.y + y, d.origin.x + 3, d.size.x - 5));
        Assert.Equal(msg.Replace(" ", ""), drawn.ToString().Replace(" ", ""));
    }

    [Fact]
    public void InputBoxMeasuresItsLabelInCells()
    {
        TDialog u = Under(TextWidthProfile.Unicode, () => MsgBox.BuildInputBox(new TRect(0, 0, 50, 7), "T", "~\u65E5~\u672C", "", 10));
        TDialog l = Under(TextWidthProfile.Legacy, () => MsgBox.BuildInputBox(new TRect(0, 0, 50, 7), "T", "~\u65E5~\u672C", "", 10));
        TInputLine Input(TDialog d) => (TInputLine)Children(d).Single(v => v is TInputLine);
        Assert.Equal(4 + 6, Input(u).origin.x); // "~\u65E5~\u672C" is 6 cells counting the markers, as 1.2 counted units
        Assert.Equal(4 + 4, Input(l).origin.x);
    }

    private static IEnumerable<TView> Children(TGroup g)
    {
        TView? first = g.First();
        if (first == null) yield break;
        TView v = first;
        do
        {
            yield return v;
            v = v.Next!;
        } while (v != first);
    }

    // ---- TCluster ---------------------------------------------------------------------------------------

    private sealed class ProbeCheckBoxes : TCheckBoxes
    {
        public ProbeCheckBoxes(TRect bounds, TSItem items) : base(bounds, items) { }
        public int ColumnOf(int item) => Column(item);
        public int Hit(int x, int y) => FindSel(new TPoint(x, y));
    }

    private static TSItem ClusterItems() => new("~\u65E5~\u672C", new TSItem("~B~ee", new TSItem("~C~", null)));

    [Fact]
    public void ClusterColumnsAndHitTestingUseCells()
    {
        // One row: item 0 is "\u65E5\u672C" (4 cells, 2 units), so item 1 starts at 0 + 4 + 6 = 10 (1.2: 8).
        var u = Under(TextWidthProfile.Unicode, () => new ProbeCheckBoxes(new TRect(0, 0, 30, 1), ClusterItems()));
        Under(TextWidthProfile.Unicode, () =>
        {
            Assert.Equal(10, u.ColumnOf(1));
            Assert.Equal(19, u.ColumnOf(2));
            Assert.Equal(0, u.Hit(9, 0));
            Assert.Equal(1, u.Hit(10, 0));
            Assert.Equal(2, u.Hit(19, 0));
        });
        Host h = Under(TextWidthProfile.Unicode, () => Show(40, 2, u));
        h.AssertWellFormed();
        Assert.Equal("[ ] " + "\u65E5\u672C", h.Text(0, 1, 8));
        Assert.Equal("[ ] Bee", h.Text(0, 11, 7));

        Under(TextWidthProfile.Legacy, () =>
        {
            var l = new ProbeCheckBoxes(new TRect(0, 0, 30, 1), ClusterItems());
            Assert.Equal(8, l.ColumnOf(1));
            Assert.Equal(17, l.ColumnOf(2));
            Assert.Equal(1, l.Hit(9, 0));
        });
    }

    [Fact]
    public void ClusterHotkeyStillUsesTheLetterAfterTheTilde()
    {
        var owner = new Host(40, 3);
        var boxes = new ProbeCheckBoxes(new TRect(0, 0, 30, 1), ClusterItems());
        owner.Insert(boxes);
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbAltB;
        boxes.HandleEvent(ref ev);
        Assert.True(boxes.Mark(1));
    }

    // ---- menus and status line --------------------------------------------------------------------------

    private static TMenu BarMenu() => new(new TMenuItem("~F~ile", CmdA, 0, Views.hcNoContext, null,
        new TMenuItem("\u7DE8~\u96C6~", CmdB, 0, Views.hcNoContext, null,
        new TMenuItem("~H~elp", CmdC, 0))));

    private sealed class EventBar : TMenuBar
    {
        private readonly Queue<TEvent> _events;
        public EventBar(TMenu menu, params TEvent[] events) : base(new TRect(0, 0, 40, 1), menu) => _events = new(events);
        public override void GetEvent(ref TEvent ev) => ev = _events.Dequeue();
    }

    private static TEvent Mouse(int x, int y, ushort what)
    {
        TEvent ev = default;
        ev.What = what;
        ev.mouse.where = new TPoint(x, y);
        ev.mouse.buttons = 1;
        return ev;
    }

    [Fact]
    public void MenuBarItemRectsDrawingAndMouseAgreeInCells()
    {
        // Items at x = 1: "File" [1,7), "\u7DE8\u96C6" [7,13) (4 cells), "Help" [13,19). Under 1.2: [7,11) and [11,17).
        Under(TextWidthProfile.Unicode, () =>
        {
            var bar = new EventBar(BarMenu());
            TMenuItem edit = bar.Menu!.Items!.Next!;
            Assert.Equal(new TRect(7, 0, 13, 1), bar.GetItemRect(edit));
            Host h = Show(40, 1, bar);
            h.AssertWellFormed();
            Assert.Equal("  File  \u7DE8\u96C6  Help  ", h.Text(0, 0, 20));
            Assert.Equal(TScreenCellKind.Lead, h.Cells(0)[8].Kind);
            Assert.Equal('H', h.Cells(0)[14].Character);

            Assert.Equal(CmdB, new EventBar(BarMenu(), Mouse(12, 0, Events.evMouseUp)).Execute());
            Assert.Equal(CmdC, new EventBar(BarMenu(), Mouse(13, 0, Events.evMouseUp)).Execute());
        });
        Under(TextWidthProfile.Legacy, () =>
        {
            var bar = new EventBar(BarMenu());
            Assert.Equal(new TRect(7, 0, 11, 1), bar.GetItemRect(bar.Menu!.Items!.Next!));
            Assert.Equal(CmdC, new EventBar(BarMenu(), Mouse(12, 0, Events.evMouseUp)).Execute());
        });
    }

    [Fact]
    public void MenuBoxWidthAndParamAlignmentUseCells()
    {
        var menu = new TMenu(new TMenuItem("~\u65E5~\u672C\u8A9E", CmdA, 0, Views.hcNoContext, "Ctrl+\u6587",
            new TMenuItem("~X~", CmdB, 0)));
        // Width = name 6 + 6 + param 7 + 2 = 21 (1.2 counted 3 + 6 + 6 + 2 = 17).
        var box = Under(TextWidthProfile.Unicode, () => new TMenuBox(new TRect(0, 0, 60, 20), menu));
        Assert.Equal(21, box.size.x);
        Assert.Equal(17, Under(TextWidthProfile.Legacy, () => new TMenuBox(new TRect(0, 0, 60, 20), menu)).size.x);

        Host h = Under(TextWidthProfile.Unicode, () => Show(40, 6, box));
        h.AssertWellFormed();
        // The parameter ends at size.x - 3; the frame stays intact.
        Assert.Equal("Ctrl+\u6587", h.Text(1, 21 - 3 - 7, 7));
        Assert.Equal('\u2502', h.Cells(1)[21 - 2].Character);
        Assert.Equal(Nihongo, h.Text(1, 3, 6));
    }

    private static TStatusLine Status(TStatusItem items) =>
        new(new TRect(0, 0, 60, 1), new TStatusDef(0, 0xFFFF, items));

    private static ushort Click(TStatusLine status, int x)
    {
        var ev = new TEvent { What = Events.evMouseDown };
        ev.mouse.where = new TPoint(x, 0);
        ev.mouse.buttons = 1;
        status.HandleEvent(ref ev);
        return ev.What == Events.evCommand ? ev.message.command : (ushort)0;
    }

    [Fact]
    public void StatusLineHitTestingAgreesWithDrawingInCells()
    {
        TStatusItem Items() => new("~F1~ \u5E2E\u52A9", Keys.kbF1, CmdA, new TStatusItem("~F2~ Save", Keys.kbF2, CmdB));
        // Item 1 is "F1 \u5E2E\u52A9" = 7 cells, so it covers [0, 9); item 2 covers [9, 18). 1.2: [0, 7) and [7, 16).
        Under(TextWidthProfile.Unicode, () =>
        {
            TStatusLine status = Status(Items());
            Host h = Show(60, 1, status);
            h.AssertWellFormed();
            Assert.Equal(" F1 \u5E2E\u52A9  F2 Save ", h.Text(0, 0, 18));
            Assert.Equal(CmdA, Click(status, 8));
            Assert.Equal(CmdB, Click(status, 9));
            Assert.Equal(CmdB, Click(status, 17));
            Assert.Equal(0, Click(status, 18));
        });
        Under(TextWidthProfile.Legacy, () =>
        {
            TStatusLine status = Status(Items());
            Assert.Equal(CmdB, Click(status, 8));
            Assert.Equal(0, Click(status, 16));
        });
    }

    [Fact]
    public void StatusLineEmptyTextItemTakesTheSameCellsForDrawingAndHitTesting()
    {
        // An empty (non-null) text draws two padding cells, so the next item starts at 2 and ends at 11.
        TStatusLine status = Status(new TStatusItem("", Keys.kbF5, CmdE, new TStatusItem("~F2~ Save", Keys.kbF2, CmdB)));
        Host h = Show(60, 1, status);
        Assert.Equal("   F2 Save ", h.Text(0, 0, 11));
        Assert.Equal(CmdB, Click(status, 10));
        Assert.Equal(CmdE, Click(status, 1));
    }

    // ---- TFrame -----------------------------------------------------------------------------------------

    private static TWindow Window(int width, string title) => new(new TRect(0, 0, width, 6), title, Views.wnNoNumber);

    [Fact]
    public void FrameCentresAWideTitleByCells()
    {
        // 40 columns with close and zoom: budget l = 24. A 12-cell title is centred at (40 - 12) / 2 = 14.
        string title = Nihongo + Nihongo;
        Host h = Under(TextWidthProfile.Unicode, () => Show(50, 8, Window(40, title)));
        h.AssertWellFormed();
        Assert.Equal(" " + title + " ", h.Text(0, 13, 14));
        Assert.Equal(TScreenCellKind.Lead, h.Cells(0)[14].Kind);

        Host l = Under(TextWidthProfile.Legacy, () => Show(50, 8, Window(40, title)));
        Assert.Equal(" " + title + " ", l.Chars(0, 16, 8)); // 1.2: 6 units centred at (40 - 6) / 2 = 17
    }

    [Fact]
    public void FrameTruncatesALongWideTitleAtAClusterBoundary()
    {
        // 26 cells > 24: " .." and the longest tail within 22 cells. "a" + 12 ideographs + "b": the tail cannot
        // start in the middle of an ideograph, so it is 10 ideographs + "b" (21 cells).
        string ideographs = string.Concat(Enumerable.Repeat(Nihongo, 4));
        string title = "a" + ideographs + "b";
        Host h = Under(TextWidthProfile.Unicode, () => Show(50, 8, Window(40, title)));
        h.AssertWellFormed();
        int i = (40 - 24) >> 1;
        Assert.Equal(" ..", h.Text(0, i - 1, 3));
        Assert.Equal(ideographs[2..] + "b", h.Text(0, i + 2, 22).TrimEnd());
        Assert.Equal(TScreenCellKind.Lead, h.Cells(0)[i + 2].Kind);
    }

    [Fact]
    public void FrameTitleSurvivesResizingThroughEveryWidth()
    {
        string title = "A" + Emoji + Nihongo + Combining + Family + Bunken + "Z";
        var window = Window(60, title);
        Host h = Show(70, 10, window);
        for (int w = 60; w >= 16; w--)
        {
            window.ChangeBounds(new TRect(0, 0, w, 6));
            h.Redraw();
            h.AssertWellFormed();
        }
    }

    // ---- lists ------------------------------------------------------------------------------------------

    private sealed class Lines : TListViewer
    {
        private readonly string[] _items;
        public Lines(TRect bounds, TScrollBar? h, params string[] items) : base(bounds, 1, h, null)
        {
            _items = items;
            SetRange(items.Length);
        }
        public override string GetText(int item, int maxChars) => CellText.Truncate(_items[item], maxChars);
    }

    [Fact]
    public void ListViewerScrollsHorizontallyByCells()
    {
        var hsb = new TScrollBar(new TRect(0, 5, 12, 6));
        string item = Nihongo + "abc";
        Host h = Under(TextWidthProfile.Unicode, () =>
        {
            var host = Show(20, 6, hsb, new Lines(new TRect(0, 0, 12, 4), hsb, item, Emoji + Family + "x"));
            hsb.SetParams(1, 0, 20, 1, 1);
            host.Redraw();
            return host;
        });
        h.AssertWellFormed();
        // Scrolled by one cell: the cut first ideograph leaves a blank, then the rest starts at its own column.
        Assert.Equal("  \u672C\u8A9Eabc ", h.Text(0, 0, 10));
        Assert.Equal(TScreenCellKind.Lead, h.Cells(0)[2].Kind);
        Assert.Equal("  " + Family + "x ", h.Text(1, 0, 6));

        Host l = Under(TextWidthProfile.Legacy, () =>
        {
            var hsb2 = new TScrollBar(new TRect(0, 5, 12, 6));
            var host = Show(20, 6, hsb2, new Lines(new TRect(0, 0, 12, 4), hsb2, item));
            hsb2.SetParams(1, 0, 20, 1, 1);
            host.Redraw();
            return host;
        });
        Assert.Equal(" \u672C\u8A9Eabc", l.Chars(0, 0, 6)); // 1.2: one unit per cell from offset 1
    }

    [Fact]
    public void ListBoxItemsAreTruncatedByCells()
    {
        var list = new TListBox(new TRect(0, 0, 7, 3), 1, null);
        list.NewList(new TStringCollection { Items = { Nihongo + Nihongo, "ab" + Emoji + "cd" } });
        Assert.Equal("\u65E5\u672C", Under(TextWidthProfile.Unicode, () => list.GetText(0, 5)));
        Assert.Equal("ab", Under(TextWidthProfile.Unicode, () => list.GetText(1, 3)));
        Assert.Equal("\u65E5\u672C\u8A9E\u65E5\u672C", Under(TextWidthProfile.Legacy, () => list.GetText(0, 5)));
        Host h = Show(10, 3, list);
        h.AssertWellFormed();
    }

    [Fact]
    public void OutlineScrollsByCellsAndMeasuresWideNodes()
    {
        var root = new TNode(Nihongo + "x");
        var outline = new TOutline(new TRect(0, 0, 6, 3), null, null, root);
        Assert.Equal(CellText.Measure(outline.GetGraph(0, 0, Views.ovExpanded | Views.ovLast)) + 7, outline.limit.x);
        Host h = Show(25, 3, outline);
        h.AssertWellFormed();
        string graph = outline.GetGraph(0, 0, Views.ovExpanded | Views.ovLast);
        outline.delta = new TPoint(graph.Length + 1, 0); // into the first ideograph
        h.Redraw();
        h.AssertWellFormed();
        Assert.Equal(" \u672C\u8A9Ex", h.Text(0, 0, 6));
    }

    // ---- group box and combo box ------------------------------------------------------------------------

    [Fact]
    public void GroupBoxTitleFitsItsBudgetInCells()
    {
        var box = new TGroupBox(new TRect(0, 0, 12, 4), Nihongo + Nihongo + Emoji);
        Host h = Show(20, 5, box);
        h.AssertWellFormed();
        // Budget 12 - 4 = 8 cells: four ideographs, drawn after one space from column 1.
        Assert.Equal(" \u65E5\u672C\u8A9E\u65E5 ", h.Text(0, 1, 10));
    }

    [Fact]
    public void ComboBoxClipsByCells()
    {
        Assert.Equal("\u65E5\u672C", Under(TextWidthProfile.Unicode, () => TComboBox.ClipCells(Nihongo, 5)));
        Assert.Equal("ab", Under(TextWidthProfile.Unicode, () => TComboBox.ClipCells("ab" + Family, 3)));
        Assert.Equal("ab", Under(TextWidthProfile.Legacy, () => TComboBox.ClipCells("ab" + Emoji, 3))); // pair-safe Clip
        Assert.Equal("\u65E5\u672C\u8A9E", Under(TextWidthProfile.Legacy, () => TComboBox.ClipCells(Nihongo, 5)));

        var combo = new TComboBox(new TRect(0, 0, 9, 1), new[] { Nihongo + Bunken, "x" });
        combo.SelectedIndex = 0;
        Host h = Show(15, 1, combo);
        h.AssertWellFormed();
        Assert.StartsWith(" \u65E5\u672C", h.Text(0, 0, 9));

        combo.SetState(Views.sfDisabled, true);
        h.Redraw();
        h.AssertWellFormed();
    }

    // ---- help -------------------------------------------------------------------------------------------

    [Fact]
    public void HelpTopicWrapsByCellsWithoutSplittingClusters()
    {
        var topic = new THelpTopic();
        string text = string.Concat(Enumerable.Repeat(Nihongo + " ", 6)) + "\n";
        topic.AddParagraph(new TParagraph { wrap = true, Text = text });
        topic.SetWidth(10);
        var lines = new List<string>();
        int n = topic.NumLines();
        for (int i = 1; i <= n; i++)
        {
            var buf = new char[256];
            topic.GetLine(i, buf);
            lines.Add(new string(buf, 0, Array.IndexOf(buf, '\0')));
        }
        foreach (string line in lines)
            Assert.True(CellText.Measure(line.TrimEnd()) <= 10, $"'{line}' wider than 10 cells");
        Assert.Equal(text.Replace(" ", "").Replace("\n", ""), string.Concat(lines).Replace(" ", ""));

        // 1.2 wrapping is kept under the legacy profile: units, so two words of 3 + space per line fit 10 units.
        int legacyLines = Under(TextWidthProfile.Legacy, () => { topic.SetWidth(10); return topic.NumLines(); });
        Assert.True(legacyLines < n);
    }

    [Fact]
    public void HelpViewerHighlightsAndHitTestsCrossReferencesByCells()
    {
        using var streams = new StreamableRegistryScope();
        using var tmp = new TempDirectory();
        THelpFile.RegisterStreamableTypes();
        string path = Path.Combine(tmp.Path, "wide.hlp");
        // "\u65E5\u672C\u8A9E see Link here." : "Link" is units [8, 12) but cells [11, 15).
        string line = Nihongo + " see Link here.";
        var fp = new Fpstream(path);
        var hf = new THelpFile(fp, THelpFile.FormatV2Utf16, LegacyTextEncodings.Latin1);
        var t1 = new THelpTopic();
        t1.AddParagraph(new TParagraph { wrap = false, Text = line + "\n" });
        t1.AddCrossRef(new TCrossRef { @ref = 2, offset = 8, length = 4 });
        hf.RecordPositionInIndex(1);
        hf.PutTopic(t1);
        var t2 = new THelpTopic();
        t2.AddParagraph(new TParagraph { wrap = false, Text = "Target.\n" });
        hf.RecordPositionInIndex(2);
        hf.PutTopic(t2);
        hf.Flush();
        fp.Close();

        var rfp = new Fpstream(path);
        try
        {
            var viewer = new THelpViewer(new TRect(0, 0, 30, 4), null, null, new THelpFile(rfp), 1);
            Host h = Show(30, 4, viewer);
            h.AssertWellFormed();
            Assert.Equal(line, h.Text(0, 0, 21));
            TScreenChar[] row = h.Cells(0);
            Assert.NotEqual(row[10].Attr, row[11].Attr);
            Assert.Equal(row[11].Attr, row[14].Attr);
            Assert.NotEqual(row[14].Attr, row[15].Attr);

            // The click at column 9 (the 'e' before the link) misses; column 14 (the 'k') follows the link.
            TEvent miss = Mouse(9, 0, Events.evMouseDown);
            viewer.HandleEvent(ref miss);
            Assert.Equal(1, viewer.topic.GetNumCrossRefs());
            TEvent hit = Mouse(14, 0, Events.evMouseDown);
            viewer.HandleEvent(ref hit);
            Assert.Equal(0, viewer.topic.GetNumCrossRefs());
        }
        finally { rfp.Close(); }
    }

    // ---- nested dialogs, resize, legacy identity --------------------------------------------------------

    private static TDialog BuildDialog(bool ascii)
    {
        string w(string wide, string plain) => ascii ? plain : wide;
        var d = new TDialog(new TRect(0, 0, 50, 18), w(Nihongo + " dialog", "Dialog title"));
        d.Insert(new TLabel(new TRect(2, 2, 20, 3), w("~\u65E5~\u672C label", "~L~abel text"), null));
        d.Insert(new TButton(new TRect(2, 14, 16, 16), w("~O~K " + Bunken, "~O~K button"), CmdA, 0));
        d.Insert(new TCheckBoxes(new TRect(2, 4, 40, 6), new TSItem(w("~\u6587~\u4EF6", "~F~iles"),
            new TSItem(w("E" + Emoji, "Emoji"), new TSItem("~T~hird", null)))));
        d.Insert(new TStaticText(new TRect(2, 7, 30, 10), w("\x03" + Nihongo + " " + Combining + " " + Family + " text",
            "\x03centred static text that wraps")));
        var inner = new TGroupBox(new TRect(30, 7, 48, 13), w(Bunken + " group", "Group box"));
        inner.Insert(new TRadioButtons(new TRect(1, 1, 16, 3), new TSItem(w(Nihongo, "One"), new TSItem("~T~wo", null))));
        d.Insert(inner);
        return d;
    }

    [Fact]
    public void AsciiDialogsDrawIdenticallyUnderUnicodeAndLegacy()
    {
        TScreenChar[] u = Under(TextWidthProfile.Unicode, () => Show(60, 20, BuildDialog(true)).Snapshot());
        TScreenChar[] l = Under(TextWidthProfile.Legacy, () => Show(60, 20, BuildDialog(true)).Snapshot());
        Assert.Equal(l.Length, u.Length);
        for (int k = 0; k < u.Length; k++)
            Assert.True(u[k].Equals(l[k]) && u[k].Kind == l[k].Kind,
                $"cell {k % 60},{k / 60}: '{u[k].Character}' vs '{l[k].Character}'");

        // The menus and the status line too.
        TScreenChar[] Chrome()
        {
            var status = Status(new TStatusItem("~F1~ Help", Keys.kbF1, CmdA, new TStatusItem("~Alt-X~ Exit", Keys.kbAltX, CmdB)));
            status.MoveTo(0, 19);
            var bar = new TMenuBar(new TRect(0, 0, 60, 1), BarMenuAscii());
            var box = new TMenuBox(new TRect(5, 1, 60, 18), new TMenu(new TMenuItem("~O~pen", CmdA, 0, Views.hcNoContext, "F3",
                new TMenuItem("E~x~it", CmdB, 0, Views.hcNoContext, "Alt-X"))));
            return Show(60, 20, bar, box, status).Snapshot();
        }
        Assert.Equal(Under(TextWidthProfile.Legacy, Chrome), Under(TextWidthProfile.Unicode, Chrome));
    }

    private static TMenu BarMenuAscii() => new(new TMenuItem("~F~ile", CmdA, 0, Views.hcNoContext, null,
        new TMenuItem("~E~dit", CmdB, 0)));

    [Fact]
    public void NestedWideDialogsStayWellFormedThroughResizesAndMoves()
    {
        Host h = Under(TextWidthProfile.Unicode, () => Show(70, 24, BuildDialog(false)));
        h.AssertWellFormed();
        TDialog outer = (TDialog)h.First()!;
        var nested = BuildDialog(false);
        h.Insert(nested);
        var random = new Random(77);
        for (int n = 0; n < 60; n++)
        {
            int x = random.Next(-10, 60), y = random.Next(-3, 20);
            nested.ChangeBounds(new TRect(x, y, x + random.Next(20, 55), y + random.Next(8, 18)));
            if (n % 3 == 0) outer.ChangeBounds(new TRect(random.Next(0, 10), 0, random.Next(30, 70), random.Next(10, 24)));
            h.Redraw();
            h.AssertWellFormed();
        }
    }
}
