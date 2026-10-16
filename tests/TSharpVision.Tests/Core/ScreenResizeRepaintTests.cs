// TProgram.HandleScreenResize repaint regression tests.
//
// A driver reports a resize by replacing TScreen.ScreenBuffer with a new, empty one and queueing
// cmScreenResized. TProgram answers by clearing the screen and calling ChangeBounds — which
// repaints through the children's own ChangeBounds, and so only when the size really changed.
// A report that left the size as it was therefore cleared the screen and painted nothing: every
// view still laid out and working, behind a blank screen, until something else happened to draw.
//
// That is what a console application looked like when its terminal had resized the console
// before the driver read the size: the record of that change was still in the input queue at the
// first pump, the size it reported was the one already in use, and the screen drawn during
// construction was wiped. Resizing the window by hand put it right, because that changes the size.
//
// The tests look at what the driver was left showing, not at the screen buffer: a "glass" driver
// keeps the cell it was last asked to put at each position, and blanks them all on ClearScreen.
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class ScreenResizeRepaintTests : IDisposable
{
    private const ushort Width = 80;
    private const ushort Height = 25;

    private readonly DriverScope _ds;
    private readonly Glass _glass;

    public ScreenResizeRepaintTests()
    {
        _ds = new DriverScope(Width, Height);
        _glass = new Glass(_ds.Driver);
        TDisplay.driver = _glass;
        TEventQueue.Resume();
        Drain();
    }

    public void Dispose()
    {
        Drain();
        _ds.Dispose();
    }

    /// <summary>What a person would see: the last cell written at each position, blanked by a clear.</summary>
    private sealed class Glass(NullDriver inner) : IDriver
    {
        private (char Character, byte Attr)[] _cells = [];
        private int _cols;
        private int _rows;

        public static readonly (char Character, byte Attr) Cleared = (' ', 0x07);

        public int Clears { get; private set; }

        public (char Character, byte Attr)[] Snapshot()
        {
            Fit();
            return ((char, byte)[])_cells.Clone();
        }

        public int Columns { get { Fit(); return _cols; } }

        private void Fit()
        {
            int cols = inner.GetCols();
            int rows = inner.GetRows();
            if (cols == _cols && rows == _rows && _cells.Length == cols * rows) return;

            _cols = cols;
            _rows = rows;
            _cells = new (char, byte)[cols * rows];
            Array.Fill(_cells, Cleared);
        }

        public void ClearScreen(ushort cols, ushort rows)
        {
            Fit();
            Clears++;
            Array.Fill(_cells, Cleared);
            inner.ClearScreen(cols, rows);
        }

        public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf)
        {
            Fit();
            for (int row = 0; row < h; row++)
            for (int col = 0; col < w; col++)
            {
                int cx = x + col;
                int cy = y + row;
                if (cx < 0 || cy < 0 || cx >= _cols || cy >= _rows) continue;

                TScreenChar cell = buf[(row * w) + col];
                _cells[(cy * _cols) + cx] = (cell.Character == '\0' ? ' ' : cell.Character, (byte)cell.Attr);
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

    private static void Drain()
    {
        for (int i = 0; i < 64; i++)
        {
            TEvent ev = default;
            TEventQueue.GetNextEvent(ref ev);
            if (ev.What == Events.evNothing) break;
        }
    }

    /// <summary>A program with a window on its desktop, so there is more on the screen than a background.</summary>
    private static TestProgram BuildProgram()
    {
        var app = new TestProgram();
        var window = new TWindow(new TRect(4, 2, 44, 14), "Listing", 1);
        window.Insert(new TStaticText(new TRect(2, 2, 30, 3), "contents of the window"));
        app.DeskTop!.Insert(window);
        return app;
    }

    /// <summary>Hands the program every resize the driver has queued, the way its event loop would.</summary>
    private static int DeliverResizes(TestProgram app)
    {
        int delivered = 0;
        for (int i = 0; i < 64; i++)
        {
            TEvent ev = default;
            TEventQueue.GetNextEvent(ref ev);
            if (ev.What == Events.evNothing) break;
            if (ev.What != Events.evCommand || ev.message.command != Views.cmScreenResized) continue;

            app.HandleEvent(ref ev);
            delivered++;
        }

        return delivered;
    }

    private void AssertNothingIsBlank(string when)
    {
        (char Character, byte Attr)[] screen = _glass.Snapshot();
        int cols = _glass.Columns;

        for (int i = 0; i < screen.Length; i++)
            Assert.True(screen[i] != Glass.Cleared, $"{when}: cell ({i % cols},{i / cols}) was left cleared");
    }

    private string Row(int y)
    {
        (char Character, byte Attr)[] screen = _glass.Snapshot();
        int cols = _glass.Columns;
        return new string(Enumerable.Range(0, cols).Select(x => screen[(y * cols) + x].Character).ToArray());
    }

    [Fact]
    public void ConstructionPaintsTheWholeScreen()
    {
        TestProgram app = BuildProgram();
        try
        {
            AssertNothingIsBlank("after construction");
            Assert.Contains("Listing", Row(3));
            Assert.Contains("contents of the window", Row(5));
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void AResizeReportThatLeavesTheSizeUnchangedLeavesTheScreenComplete()
    {
        TestProgram app = BuildProgram();
        try
        {
            (char, byte)[] before = _glass.Snapshot();

            // What a driver does when it is told the size changed and finds it did not.
            _ds.Driver.SimulateResize(Width, Height);
            Assert.Equal(1, DeliverResizes(app));

            Assert.Equal(1, _glass.Clears);
            Assert.Same(TScreen.ScreenBuffer, app.buffer);
            AssertNothingIsBlank("after a resize report with the size unchanged");
            Assert.Equal(before, _glass.Snapshot());
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void ARealResizeFollowedByAReportOfTheSameSizeLeavesTheScreenComplete()
    {
        TestProgram app = BuildProgram();
        try
        {
            // One drag can queue several records; each reads the size the window ended at.
            _ds.Driver.SimulateResize(100, 30);
            Assert.Equal(1, DeliverResizes(app));
            AssertNothingIsBlank("after the resize");
            (char, byte)[] resized = _glass.Snapshot();

            _ds.Driver.SimulateResize(100, 30);
            Assert.Equal(1, DeliverResizes(app));

            Assert.Equal(100, app.size.x);
            Assert.Equal(30, app.size.y);
            AssertNothingIsBlank("after the second report");
            Assert.Equal(resized, _glass.Snapshot());
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void ARealResizeStillLaysOutAndPaintsOnce()
    {
        TestProgram app = BuildProgram();
        try
        {
            _ds.Driver.SimulateResize(100, 30);
            Assert.Equal(1, DeliverResizes(app));

            Assert.Equal(100, app.size.x);
            Assert.Equal(30, app.size.y);
            Assert.Equal(100, app.DeskTop!.size.x);
            Assert.Equal(1, _glass.Clears);
            AssertNothingIsBlank("after a resize");
            Assert.Contains("Listing", Row(3));
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void AViewDrawnAfterAnUnchangedSizeReportStillReachesTheScreen()
    {
        TestProgram app = BuildProgram();
        try
        {
            _ds.Driver.SimulateResize(Width, Height);
            DeliverResizes(app);

            // The buffers the groups cache were released by the resize; what is drawn next must come from
            // the views, not from a released image.
            var label = new TStaticText(new TRect(50, 3, 70, 4), "added afterwards");
            app.DeskTop!.Insert(label);

            Assert.Contains("added afterwards", Row(app.DeskTop.origin.y + 3));
            Assert.Contains("Listing", Row(3));
            AssertNothingIsBlank("after drawing a new view");
        }
        finally { app.ShutDown(); }
    }
}
