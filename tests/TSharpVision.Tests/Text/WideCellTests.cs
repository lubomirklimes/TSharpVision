using System.Runtime.CompilerServices;
using System.Text;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Tests.Infrastructure;
using TSharpVision.Text;
using Xunit;

namespace TSharpVision.Tests.Text;

/// <summary>The 8-byte cell, the cluster table and the cluster-aware draw-buffer writers.</summary>
public sealed class WideCellTests
{
    internal const string Sample1 = "A\U0001F600B\U0001F44D\U0001F3FDC\U0001F1E8\U0001F1FFD\U0001F468\u200D\U0001F469\u200D\U0001F467E";
    private static readonly TColorAttr Attr = new(0x1E);

    internal static string Text(ReadOnlySpan<TScreenChar> cells)
    {
        var sb = new StringBuilder();
        foreach (TScreenChar c in cells)
            if (c.Kind != TScreenCellKind.Trail) sb.Append(c.GetText());
        return sb.ToString();
    }

    internal static string Kinds(ReadOnlySpan<TScreenChar> cells)
    {
        var sb = new StringBuilder();
        foreach (TScreenChar c in cells)
            sb.Append(c.Kind switch { TScreenCellKind.Lead => 'L', TScreenCellKind.Trail => 't', _ => '.' });
        return sb.ToString();
    }

    /// <summary>I1/I2: every lead is followed by exactly its trails and every trail is covered by a lead.</summary>
    internal static void AssertWellFormed(ReadOnlySpan<TScreenChar> row, string context = "")
    {
        for (int x = 0; x < row.Length; x++)
        {
            switch (row[x].Kind)
            {
                case TScreenCellKind.Lead:
                    int w = row[x].CellWidth;
                    Assert.True(x + w <= row.Length, $"{context}: lead at {x} runs past the row");
                    for (int k = 1; k < w; k++)
                        Assert.True(row[x + k].Kind == TScreenCellKind.Trail, $"{context}: lead at {x} lacks trail {x + k}");
                    x += w - 1;
                    break;
                case TScreenCellKind.Trail:
                    Assert.Fail($"{context}: orphan trail at {x}");
                    break;
            }
        }
    }

    // ---- TScreenChar ------------------------------------------------------------------------------------

    [Fact]
    public void CellIsEightBytesAndUnmanaged()
    {
        Assert.Equal(8, Unsafe.SizeOf<TScreenChar>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<TScreenChar>());
        Span<TScreenChar> onStack = stackalloc TScreenChar[2];
        Assert.Equal(TScreenCellKind.Single, onStack[0].Kind);
        Assert.Equal(1, onStack[0].CellWidth);
    }

    [Fact]
    public void LegacyConstructorsAndFieldsBehaveAsBefore()
    {
        var a = new TScreenChar('x', Attr);
        Assert.Equal(('x', Attr, TScreenCellKind.Single, 1), (a.Character, a.Attr, a.Kind, a.CellWidth));
        Assert.Equal("x", a.GetText());
        var b = new TScreenChar('y', ConsoleColor.White, ConsoleColor.DarkBlue);
        Assert.Equal(0x1F, (byte)b.Attr);
        Assert.Equal(new TScreenChar('x', Attr), a);
    }

    [Fact]
    public void ClustersCarryTheirTextAndAFallbackCharacter()
    {
        TScreenChar cjk = TScreenChar.CreateCluster("漢", 2, Attr);
        Assert.Equal((TScreenCellKind.Lead, '漢', 2, "漢"), (cjk.Kind, cjk.Character, cjk.CellWidth, cjk.GetText()));

        TScreenChar emoji = TScreenChar.CreateCluster("\U0001F600", 2, Attr);
        Assert.Equal((TScreenCellKind.Lead, '�', "\U0001F600"), (emoji.Kind, emoji.Character, emoji.GetText()));

        TScreenChar family = TScreenChar.CreateCluster("\U0001F468\u200D\U0001F469\u200D\U0001F467", 2, Attr);
        Assert.Equal((TScreenCellKind.Lead, "\U0001F468\u200D\U0001F469\u200D\U0001F467"), (family.Kind, family.GetText()));

        TScreenChar accent = TScreenChar.CreateCluster("e\u0301", 1, Attr);
        Assert.Equal((TScreenCellKind.Lead, 'e', 1, "e\u0301"), (accent.Kind, accent.Character, accent.CellWidth, accent.GetText()));

        TScreenChar plain = TScreenChar.CreateCluster("a", 1, Attr);
        Assert.Equal(TScreenCellKind.Single, plain.Kind);

        TScreenChar trail = TScreenChar.CreateTrail(Attr);
        Assert.Equal((TScreenCellKind.Trail, '\0', 0, ""), (trail.Kind, trail.Character, trail.CellWidth, trail.GetText()));
        Span<char> scratch = stackalloc char[2];
        Assert.Equal("\U0001F600", emoji.GetText(scratch).ToString());
    }

    [Fact]
    public void WritingCharacterDirectlyTurnsALeadOrTrailIntoAnOrdinaryCell()
    {
        TScreenChar lead = TScreenChar.CreateCluster("\U0001F600", 2, Attr);
        lead.Character = 'x';
        Assert.Equal((TScreenCellKind.Single, "x", 1), (lead.Kind, lead.GetText(), lead.CellWidth));

        TScreenChar trail = TScreenChar.CreateTrail(Attr);
        trail.Character = 'y';
        Assert.Equal(TScreenCellKind.Single, trail.Kind);

        TScreenChar attrOnly = TScreenChar.CreateCluster("漢", 2, Attr);
        attrOnly.Attr = new TColorAttr(0x70);
        Assert.Equal(TScreenCellKind.Lead, attrOnly.Kind);
    }

    // ---- ClusterTable ------------------------------------------------------------------------------------

    [Fact]
    public void ClusterTableIsAppendOnlyAndNeverReusesHandles()
    {
        var table = new ClusterTable(4);
        Assert.True(table.TryIntern("e\u0301", out int h1));
        Assert.True(table.TryIntern("e\u0301", out int again));
        Assert.Equal(h1, again);
        Assert.True(table.TryIntern("a\u0308", out int h2));
        Assert.NotEqual(h1, h2);
        Assert.Equal("e\u0301", table.Get(h1));
        Assert.Null(table.Get(0));
        Assert.Null(table.Get(99));
        Assert.True(table.TryIntern("1", out _));
        Assert.True(table.TryIntern("2", out _));
        Assert.False(table.Exhausted);
        Assert.False(table.TryIntern("3", out int none));
        Assert.Equal(0, none);
        Assert.True(table.Exhausted);
        Assert.Equal("e\u0301", table.Get(h1));          // existing handles keep resolving
        Assert.True(table.TryIntern("a\u0308", out int known)); // and existing text is still found
        Assert.Equal(h2, known);
    }

    [Fact]
    public void AFullTableDrawsReplacementCharacters()
    {
        var table = new ClusterTable(1);
        Assert.True(table.TryIntern("x\u0301", out _));
        TScreenChar cell = TScreenChar.CreateCluster("\U0001F468\u200D\U0001F469", 2, Attr, table);
        Assert.Equal((TScreenCellKind.Lead, '�', "�", 2), (cell.Kind, cell.Character, cell.GetText(), cell.CellWidth));
    }

    [Fact]
    public void VeryLongClustersAreStoredCutAtAScalarBoundary()
    {
        var table = new ClusterTable(8);
        string longCluster = "a" + string.Concat(Enumerable.Repeat("\U0001D167", 200));   // combining marks outside the BMP
        Assert.True(table.TryIntern(longCluster, out int h));
        string stored = table.Get(h)!;
        Assert.True(stored.Length <= ClusterTable.MaxClusterLength);
        Assert.False(char.IsHighSurrogate(stored[^1]));
        Assert.StartsWith(stored, longCluster);
    }

    [Fact]
    public void ConcurrentInterningYieldsOneHandlePerText()
    {
        var table = new ClusterTable(1000);
        string[] texts = Enumerable.Range(0, 200).Select(i => "x" + (char)(0x0300 + (i % 100)) + i).ToArray();
        var handles = new int[8][];
        Parallel.For(0, 8, t =>
        {
            handles[t] = new int[texts.Length];
            for (int i = 0; i < texts.Length; i++) Assert.True(table.TryIntern(texts[i], out handles[t][i]));
        });
        for (int t = 1; t < 8; t++) Assert.Equal(handles[0], handles[t]);
        Assert.Equal(texts.Length, table.Count);
        Assert.Equal(texts.Length, handles[0].Distinct().Count());
    }

    // ---- TDrawBuffer -------------------------------------------------------------------------------------

    private static TScreenChar[] Row(int width, char fill = '.')
    {
        var row = new TScreenChar[width];
        Array.Fill(row, new TScreenChar(fill, new TColorAttr(0x07)));
        return row;
    }

    [Fact]
    public void MoveStrLaysOutClusters()
    {
        TScreenChar[] row = Row(16);
        new TDrawBuffer(row).moveStr(1, Sample1, 0x1E);
        Assert.Equal("..Lt.Lt.Lt.Lt...", Kinds(row));
        Assert.Equal("." + Sample1 + "..", Text(row));
        AssertWellFormed(row);
        Assert.Equal('A', row[1].Character);
        Assert.Equal('�', row[2].Character);   // supplementary emoji: fallback for character-only readers
    }

    [Fact]
    public void LegacyProfileKeepsOneCellPerCodeUnit()
    {
        try
        {
            TextLayout.ProfileOverride = TextWidthProfile.Legacy;
            TScreenChar[] row = Row(30);
            new TDrawBuffer(row).moveStr(0, Sample1, 0x1E);
            for (int i = 0; i < Sample1.Length; i++)
                Assert.Equal((Sample1[i], TScreenCellKind.Single), (row[i].Character, row[i].Kind));
            Assert.Equal('.', row[23].Character);

            TScreenChar[] tabs = Row(6);
            new TDrawBuffer(tabs).moveStr(0, "a\tb", 0x1E);
            Assert.Equal("a\tb...", new string(tabs.Select(c => c.Character).ToArray()));
        }
        finally
        {
            TextLayout.ProfileOverride = TextWidthProfile.Default;
        }
    }

    [Fact]
    public void AClusterThatDoesNotFitLeavesBlanks()
    {
        TScreenChar[] row = Row(4);
        new TDrawBuffer(row).moveStr(1, "a漢字", 0x1E);
        Assert.Equal("..Lt", Kinds(row));
        Assert.Equal(".a漢", Text(row));

        TScreenChar[] narrow = Row(4);
        new TDrawBuffer(narrow).moveStr(0, "ab漢", 0x1E, 3, 0);    // three cells: the CJK lead would lose its trail
        Assert.Equal("ab ", Text(narrow.AsSpan(0, 3)));
        Assert.Equal('.', narrow[3].Character);                    // never written past maxWidth
        AssertWellFormed(narrow);
    }

    [Fact]
    public void MoveStrWithOffsetPadsWithBlanks()
    {
        TScreenChar[] row = Row(8);
        new TDrawBuffer(row).moveStr(0, "xy漢z", 0x1E, 6, 2);
        Assert.Equal("漢z   ", Text(row.AsSpan(0, 6)).Replace("\0", ""));
        Assert.Equal("..", Text(row.AsSpan(6)));
        AssertWellFormed(row);
    }

    [Fact]
    public void MoveCStrHighlightsHotkeysByCluster()
    {
        TScreenChar[] row = Row(10);
        ushort written = new TDrawBuffer(row).moveCStr(0, "~漢~字 ok", 0x4F1E);
        Assert.Equal(7, written);
        Assert.Equal("漢字 ok...", Text(row));
        Assert.Equal(0x4F, (byte)row[0].Attr);
        Assert.Equal(0x4F, (byte)row[1].Attr);
        Assert.Equal(0x1E, (byte)row[2].Attr);
        Assert.Equal(HotkeyText.Measure("~漢~字 ok"), written);
    }

    [Fact]
    public void MoveBufCountLimitsCells()
    {
        TScreenChar[] row = Row(6);
        new TDrawBuffer(row).moveBuf(0, "漢字ab", 0x1E, 3);
        Assert.Equal("漢 ...", Text(row));
        AssertWellFormed(row);
    }

    [Fact]
    public void ZeroAttributeKeepsTheCellAttributes()
    {
        TScreenChar[] row = Row(4);
        new TDrawBuffer(row).moveStr(0, "漢x", 0);
        Assert.All(row, c => Assert.Equal(0x07, (byte)c.Attr));
        Assert.Equal("漢x.", Text(row));
    }

    [Fact]
    public void TabsLineBreaksControlsAndMalformedText()
    {
        TScreenChar[] row = Row(12);
        new TDrawBuffer(row).moveStr(0, "a\tb\nc\u0007\uD800", 0x1E);
        Assert.Equal("a       bc\u0007�", Text(row));
    }

    [Fact]
    public void OverwritingHalfAClusterBlanksTheOtherHalf()
    {
        TScreenChar[] row = Row(8);
        var b = new TDrawBuffer(row);
        b.moveStr(0, "漢字漢字", 0x1E);
        b.moveStr(1, "x", 0x1E);                  // over the trail of the first ideograph
        Assert.Equal(" x字漢字", Text(row));
        b.moveStr(4, "y", 0x1E);                  // over the lead of the third
        Assert.Equal(" x字y 字", Text(row));
        b.putChar(6, 'z');                         // putChar on a lead
        Assert.Equal(" x字y z ", Text(row));
        b.moveChar(3, '-', 0x1E, 1);               // raw fill over a trail
        Assert.Equal(" x -y z ", Text(row));
        AssertWellFormed(row);
        b.moveChar(0, '\0', 0x70, 8);              // attribute only: clusters stay
        AssertWellFormed(row);
    }

    [Fact]
    public void RandomWritesKeepEveryRowWellFormed()
    {
        string[] pieces = ["a", "漢", "\U0001F600", "e\u0301", "\U0001F468\u200D\U0001F469", "~", "\t", "xy", "字字"];
        var random = new Random(23);
        TScreenChar[] row = Row(20);
        for (int step = 0; step < 20_000; step++)
        {
            var b = new TDrawBuffer(row);
            string s = string.Concat(Enumerable.Range(0, random.Next(1, 5)).Select(_ => pieces[random.Next(pieces.Length)]));
            int at = random.Next(-1, 21);
            switch (random.Next(6))
            {
                case 0: b.moveStr(at, s, 0x1E); break;
                case 1: b.moveStr(at, s, 0x1E, random.Next(0, 8), random.Next(0, 3)); break;
                case 2: b.moveCStr(at, s, 0x4F1E); break;
                case 3: b.moveBuf(at, s, 0x1E, random.Next(0, 8)); break;
                case 4: b.moveChar(at, 'q', random.Next(2) == 0 ? (ushort)0x1E : (ushort)0, random.Next(0, 5)); break;
                default: b.putChar(at, 'p'); break;
            }
            AssertWellFormed(row, $"step {step}");
        }
    }
}

/// <summary>Wide cells through the view hierarchy: clipping, occlusion and the range sent to the driver.</summary>
[Collection("NonParallel")]
public sealed class WideCellViewTests : IDisposable
{
    private const int Width = 40;
    private const int Height = 10;
    private readonly DriverScope _ds;
    private readonly Recorder _recorder;

    public WideCellViewTests()
    {
        _ds = new DriverScope(Width, Height);
        _recorder = new Recorder(_ds.Driver);
        TDisplay.driver = _recorder;
    }

    public void Dispose() => _ds.Dispose();

    private sealed class Recorder(IDriver inner) : IDriver
    {
        public List<(int X, int Y, int W)> Writes { get; } = [];

        public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf)
        {
            Writes.Add((x, y, w));
            inner.WriteBuf(x, y, w, h, buf);
        }

        public void Initialize() => inner.Initialize();
        public void Suspend() => inner.Suspend();
        public void Resume() => inner.Resume();
        public void Shutdown() => inner.Shutdown();
        public ushort GetCols() => inner.GetCols();
        public ushort GetRows() => inner.GetRows();
        public TDisplay.SM GetScreenMode() => inner.GetScreenMode();
        public void SetScreenMode(TDisplay.SM mode) => inner.SetScreenMode(mode);
        public ScreenBuffer AllocateScreenBuffer() => inner.AllocateScreenBuffer();
        public void ClearScreen(ushort cols, ushort rows) => inner.ClearScreen(cols, rows);
        public ushort GetCursorType() => inner.GetCursorType();
        public void SetCursorType(ushort cursorType) => inner.SetCursorType(cursorType);
        public void SetCaretPosition(int x, int y) => inner.SetCaretPosition(x, y);
        public void MakeBeep() => inner.MakeBeep();
        public void PumpMessages() => inner.PumpMessages();
        public bool ReadKeyEvent(out TEvent ev) => inner.ReadKeyEvent(out ev);
        public bool SupportsMouse => inner.SupportsMouse;
        public bool SupportsTrueColor => inner.SupportsTrueColor;
        public bool SupportsGraphics => inner.SupportsGraphics;
    }

    /// <summary>Draws the same text on every row.</summary>
    private sealed class TextView(TRect bounds, string text) : TView(bounds)
    {
        public string Text { get; set; } = text;

        public override void Draw()
        {
            Span<TScreenChar> cells = stackalloc TScreenChar[Math.Max(1, size.x)];
            var b = new TDrawBuffer(cells);
            b.moveChar(0, ' ', 0x07, size.x);
            b.moveStr(0, Text, 0x1E);
            for (int y = 0; y < size.y; y++) WriteLine(0, y, size.x, 1, b);
        }
    }

    private static TGroup Root()
    {
        var root = new TGroup(new TRect(0, 0, Width, Height));
        root.buffer = TScreen.ScreenBuffer;
        root.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        root.Insert(new TextView(new TRect(0, 0, Width, Height), new string('.', Width)));
        return root;
    }

    private static Span<TScreenChar> ScreenRow(int y) => TScreen.ScreenBuffer!.Data.Slice(y * Width, Width);

    private static void AssertScreenWellFormed()
    {
        for (int y = 0; y < Height; y++) WideCellTests.AssertWellFormed(ScreenRow(y), $"row {y}");
    }

    [Fact]
    public void LeftClipThroughTheOwnerNeverShowsHalfACluster()
    {
        TGroup root = Root();
        var box = new TGroup(new TRect(10, 1, 20, 3));
        box.state |= Views.sfVisible;
        root.Insert(box);
        box.Insert(new TextView(new TRect(-1, 0, 9, 1), "漢字ab"));   // the first ideograph straddles the box's left edge
        root.Redraw();
        Assert.Equal(" 字ab", WideCellTests.Text(ScreenRow(1).Slice(10, 5)).TrimEnd());
        Assert.Equal('.', ScreenRow(1)[9].Character);
        AssertScreenWellFormed();
    }

    [Fact]
    public void RightClipThroughTheOwnerNeverShowsHalfACluster()
    {
        TGroup root = Root();
        var box = new TGroup(new TRect(10, 1, 15, 2));
        box.state |= Views.sfVisible;
        root.Insert(box);
        box.Insert(new TextView(new TRect(0, 0, 8, 1), "abcd漢"));   // the ideograph would need columns 14 and 15
        root.Redraw();
        Assert.Equal("abcd ", WideCellTests.Text(ScreenRow(1).Slice(10, 5)));
        Assert.Equal('.', ScreenRow(1)[15].Character);
        AssertScreenWellFormed();
    }

    [Fact]
    public void AWindowCoveringHalfAClusterBlanksTheVisibleHalfAndRepaintsIt()
    {
        TGroup root = Root();
        var under = new TextView(new TRect(0, 2, 12, 3), "漢字漢字漢字");
        root.Insert(under);
        root.Redraw();
        _recorder.Writes.Clear();

        var over = new TextView(new TRect(3, 2, 7, 3), "OVER");   // covers the trail of 字 at column 3
        root.Insert(over);
        Assert.Equal("漢 OVER 漢", WideCellTests.Text(ScreenRow(2).Slice(0, 10)));
        Assert.Contains(_recorder.Writes, w => w.Y == 2 && w.X == 2);          // the blanked lead reached the driver
        Assert.Contains(_recorder.Writes, w => w.Y == 2 && w.X <= 7 && w.X + w.W >= 8);   // and the blanked trail
        AssertScreenWellFormed();

        under.DrawView();                                                   // redrawing below the window agrees
        Assert.Equal("漢 OVER 漢", WideCellTests.Text(ScreenRow(2).Slice(0, 10)));
        AssertScreenWellFormed();
    }

    [Fact]
    public void MovingAndResizingWindowsOverWideTextKeepsTheScreenWellFormed()
    {
        TGroup root = Root();
        var text = new TextView(new TRect(0, 0, Width, Height), "漢\U0001F600字e\u0301\U0001F468\u200D\U0001F469x漢字" + new string('漢', 20));
        root.Insert(text);
        var window = new TGroup(new TRect(5, 2, 15, 6));
        window.state |= (ushort)(Views.sfVisible | Views.sfShadow);
        window.buffer = null;
        window.Insert(new TextView(new TRect(0, 0, 10, 4), "字W\U0001F44D\U0001F3FD漢"));
        root.Insert(window);
        root.Redraw();
        AssertScreenWellFormed();

        var random = new Random(3);
        for (int step = 0; step < 300; step++)
        {
            int x = random.Next(-4, Width), y = random.Next(-2, Height), w = random.Next(1, 14), h = random.Next(1, 6);
            window.Locate(new TRect(x, y, x + w, y + h));
            if (random.Next(4) == 0) text.DrawView();
            AssertScreenWellFormed();
        }
    }

    [Fact]
    public void LegacyProfileOutputIsUnchanged()
    {
        try
        {
            TextLayout.ProfileOverride = TextWidthProfile.Legacy;
            TGroup root = Root();
            root.Insert(new TextView(new TRect(0, 0, 10, 1), "a\U0001F600漢"));
            root.Redraw();
            Assert.Equal("a\uD83D\uDE00漢      ", new string(ScreenRow(0)[..10].ToArray().Select(c => c.Character).ToArray()));
            Assert.All(ScreenRow(0).ToArray(), c => Assert.Equal(TScreenCellKind.Single, c.Kind));
        }
        finally
        {
            TextLayout.ProfileOverride = TextWidthProfile.Default;
        }
    }

    [Fact]
    public void DriversDefaultToNoTextCapabilities()
    {
        Assert.Equal(TextCapabilities.None, ((IDriver)_ds.Driver).TextCapabilities);
        Assert.Equal(TextCapabilities.None, ((IDriver)_recorder).TextCapabilities);
    }
}
