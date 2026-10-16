// TView.PutInFrontOf / MakeFirst drawing regression tests.
//
// Bringing a view forward used to go through Hide() and Show(). Hide() repaints what lies under
// the view — for a window nothing else covers, the desktop background — and Show() then paints
// the view again. Nothing locks the owner in between, so both passes reach the driver, and a
// driver that puts every write on the screen at once (Win32 console, ANSI terminal) shows the
// background for a moment where the window is. Selecting a window that is not front-most does
// exactly this, which is how TSharpCommander's two panels flashed on every Tab.
//
// Every test records what the driver is given, write by write, and asserts on that sequence as
// well as on the final image. All are headless (NullDriver behind a recording decorator).
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class ViewOrderDrawingTests : IDisposable
{
    private const int Width = 80;
    private const int Height = 25;
    private const char Background = '.';

    private readonly DriverScope _ds;
    private readonly Recorder _recorder;

    public ViewOrderDrawingTests()
    {
        _ds = new DriverScope(Width, Height);
        _recorder = new Recorder(_ds.Driver);
        TDisplay.driver = _recorder;
        TEventQueue.Resume();
    }

    public void Dispose() => _ds.Dispose();

    /// <summary>Passes everything to the real driver and keeps each row it was asked to write.</summary>
    private sealed class Recorder(IDriver inner) : IDriver
    {
        public List<(int X, int Y, string Text)> Writes { get; } = [];

        public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf)
        {
            for (int row = 0; row < h; row++)
            {
                var chars = new char[w];
                for (int col = 0; col < w; col++) chars[col] = buf[(row * w) + col].Character;
                Writes.Add((x, y + row, new string(chars)));
            }

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

    /// <summary>Fills its whole extent with one character.</summary>
    private sealed class Fill : TView
    {
        private readonly char _ch;

        public Fill(TRect bounds, char ch) : base(bounds) => _ch = ch;

        public override void Draw()
        {
            var b = new TDrawBuffer();
            b.moveChar(0, _ch, 0x07, size.x);
            for (int y = 0; y < size.y; y++) WriteBuf(0, y, size.x, 1, b);
        }
    }

    /// <summary>
    /// Fills its extent with the lower-case letter of its window, or the upper-case one while
    /// the window is selected — so the image says which window is the current one.
    /// </summary>
    private sealed class Face : TView
    {
        private readonly char _ch;
        public int Draws;

        public Face(TRect bounds, char ch) : base(bounds) => _ch = ch;

        public override void Draw()
        {
            Draws++;
            bool selected = owner is not null && (owner.state & Views.sfSelected) != 0;
            var b = new TDrawBuffer();
            b.moveChar(0, selected ? char.ToUpperInvariant(_ch) : _ch, 0x1F, size.x);
            for (int y = 0; y < size.y; y++) WriteBuf(0, y, size.x, 1, b);
        }
    }

    /// <summary>A buffered, selectable group that comes forward when selected, as a window does.</summary>
    private sealed class Pane : TGroup
    {
        public Pane(TRect bounds, char ch) : base(bounds)
        {
            options |= (ushort)(Views.ofSelectable | Views.ofTopSelect);
            Content = new Face(new TRect(0, 0, bounds.b.x - bounds.a.x, bounds.b.y - bounds.a.y), ch);
            Insert(Content);
        }

        public Face Content { get; }

        public override void SetState(ushort aState, bool enable)
        {
            base.SetState(aState, enable);
            if ((aState & Views.sfSelected) != 0) Content.DrawView();
        }
    }

    private sealed class Scene
    {
        public required TestGroup Root { get; init; }
        public required Fill Back { get; init; }
        public required Pane A { get; init; }
        public required Pane B { get; init; }
    }

    /// <summary>
    /// A root drawing through the screen buffer (standing in for TProgram), a background, and
    /// two panes: A inserted first, B second, so B is front-most and current.
    /// </summary>
    private Scene Build(TRect a, TRect b)
    {
        var root = new TestGroup(new TRect(0, 0, Width, Height));
        root.buffer = TScreen.ScreenBuffer;
        root.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        var back = new Fill(new TRect(0, 0, Width, Height), Background);
        root.Insert(back);

        var paneA = new Pane(a, 'a');
        var paneB = new Pane(b, 'b');
        root.Insert(paneA);
        root.Insert(paneB);
        root.Redraw();
        _recorder.Writes.Clear();
        return new Scene { Root = root, Back = back, A = paneA, B = paneB };
    }

    private static char ScreenChar(int x, int y) => TScreen.ScreenBuffer!.Data[(y * Width) + x].Character;

    private static bool Contains(TRect r, int x, int y) => x >= r.a.x && x < r.b.x && y >= r.a.y && y < r.b.y;

    /// <summary>Whether any recorded write put <paramref name="ch"/> on a cell of <paramref name="r"/>.</summary>
    private bool Wrote(char ch, TRect r)
    {
        foreach ((int x, int y, string text) in _recorder.Writes)
            for (int i = 0; i < text.Length; i++)
                if (text[i] == ch && Contains(r, x + i, y)) return true;
        return false;
    }

    private static void AssertFilled(TRect r, char expected, TRect? except = null)
    {
        for (int y = r.a.y; y < r.b.y; y++)
        for (int x = r.a.x; x < r.b.x; x++)
        {
            if (except is { } hole && Contains(hole, x, y)) continue;
            Assert.True(expected == ScreenChar(x, y), $"cell ({x},{y}): expected '{expected}', found '{ScreenChar(x, y)}'");
        }
    }

    private static readonly TRect Left = new(0, 1, 40, 23);
    private static readonly TRect Right = new(40, 1, 80, 23);

    // ── side by side: the Commander's two panels ─────────────────────────────

    [Fact]
    public void TheSceneStartsWithTheLastInsertedPaneCurrent()
    {
        Scene scene = Build(Left, Right);

        Assert.Same(scene.B, scene.Root.current);
        AssertFilled(Left, 'a');
        AssertFilled(Right, 'B');
    }

    [Fact]
    public void SelectingThePaneBehindNeverWritesTheBackgroundWhereItStands()
    {
        Scene scene = Build(Left, Right);

        scene.A.Select();

        Assert.False(Wrote(Background, Left), "the background was written over the pane being selected");
        Assert.False(Wrote(Background, Right), "the background was written over the pane losing the selection");
    }

    [Fact]
    public void SelectingThePaneBehindMakesItCurrentAndFrontMostAndShowsBothPanes()
    {
        Scene scene = Build(Left, Right);

        scene.A.Select();

        Assert.Same(scene.A, scene.Root.current);
        Assert.Same(scene.A, scene.Root.First());
        Assert.True((scene.A.state & Views.sfSelected) != 0);
        Assert.True((scene.B.state & Views.sfSelected) == 0);
        AssertFilled(Left, 'A');
        AssertFilled(Right, 'b');
    }

    [Fact]
    public void SelectingBackAndForthNeverWritesTheBackgroundOverEitherPane()
    {
        Scene scene = Build(Left, Right);

        for (int i = 0; i < 6; i++)
        {
            Pane target = i % 2 == 0 ? scene.A : scene.B;
            target.Select();
            Assert.Same(target, scene.Root.current);
        }

        Assert.False(Wrote(Background, Left));
        Assert.False(Wrote(Background, Right));
        AssertFilled(Left, 'a');
        AssertFilled(Right, 'B');
    }

    [Fact]
    public void AViewBroughtForwardStaysExposedAndKeepsItsBuffer()
    {
        Scene scene = Build(Left, Right);
        ScreenBuffer? before = scene.A.buffer;
        Assert.NotNull(before);

        scene.A.MakeFirst();

        Assert.Same(before, scene.A.buffer);
        Assert.True((scene.A.state & Views.sfVisible) != 0);
        Assert.True((scene.A.state & Views.sfExposed) != 0);
    }

    // ── overlapping ──────────────────────────────────────────────────────────

    private static readonly TRect Under = new(4, 2, 30, 12);
    private static readonly TRect Over = new(20, 6, 50, 16);

    [Fact]
    public void AnOverlappedViewBroughtForwardIsShownWholeOverItsSibling()
    {
        Scene scene = Build(Under, Over);
        AssertFilled(Over, 'B');

        scene.A.MakeFirst();

        Assert.Same(scene.A, scene.Root.current);
        AssertFilled(Under, 'A');
        AssertFilled(Over, 'b', except: Under);
        Assert.False(Wrote(Background, Under), "the background was written where the view comes forward");
    }

    [Fact]
    public void AViewSentBehindItsSiblingIsCoveredByItAndLosesTheSelection()
    {
        Scene scene = Build(Under, Over);
        scene.A.MakeFirst();
        _recorder.Writes.Clear();

        scene.A.PutInFrontOf(scene.Back);

        Assert.Same(scene.B, scene.Root.First());
        Assert.Same(scene.B, scene.Root.current);
        AssertFilled(Over, 'B');
        AssertFilled(Under, 'a', except: Over);
        Assert.False(Wrote(Background, Under), "the background was written over a view that only went back");
    }

    [Fact]
    public void EverythingOutsideTheViewsStaysBackground()
    {
        Scene scene = Build(Under, Over);

        scene.A.MakeFirst();
        scene.A.PutInFrontOf(scene.Back);
        scene.A.MakeFirst();

        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
            if (!Contains(Under, x, y) && !Contains(Over, x, y))
                Assert.True(Background == ScreenChar(x, y), $"cell ({x},{y}) is '{ScreenChar(x, y)}'");
    }

    // ── no-ops and hidden views ──────────────────────────────────────────────

    [Fact]
    public void BringingForwardTheViewThatIsAlreadyFirstWritesNothing()
    {
        Scene scene = Build(Left, Right);

        scene.B.MakeFirst();

        Assert.Empty(_recorder.Writes);
        Assert.Same(scene.B, scene.Root.current);
    }

    [Fact]
    public void AHiddenViewIsReorderedWithoutBeingDrawnOrSelected()
    {
        Scene scene = Build(Left, Right);
        scene.A.Hide();
        _recorder.Writes.Clear();
        int draws = scene.A.Content.Draws;

        scene.A.MakeFirst();

        Assert.Same(scene.A, scene.Root.First());
        Assert.Same(scene.B, scene.Root.current);
        Assert.True((scene.A.state & Views.sfVisible) == 0);
        Assert.Equal(draws, scene.A.Content.Draws);
        Assert.Empty(_recorder.Writes);
        AssertFilled(Left, Background);
    }

    [Fact]
    public void AHiddenViewBroughtForwardIsShownInFrontWhenItIsShownAgain()
    {
        Scene scene = Build(Under, Over);
        scene.A.Hide();
        scene.A.MakeFirst();

        scene.A.Show();

        Assert.Same(scene.A, scene.Root.current);
        AssertFilled(Under, 'A');
        AssertFilled(Over, 'b', except: Under);
    }
}
