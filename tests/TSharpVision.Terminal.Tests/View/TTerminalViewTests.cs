using System.Text;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Terminal.Tests.View;

[CollectionDefinition("TerminalView", DisableParallelization = true)]
public sealed class TerminalViewCollection
{
}

/// <summary>
/// U-1b: <see cref="TTerminal"/> over the emulator — drawing from the cell grid, the event-loop boundary, input,
/// paste, Ctrl+C, resize and session lifecycle.
/// </summary>
[Collection("TerminalView")]
public sealed class TTerminalViewTests : IDisposable
{
    private readonly DriverScope _driver = new(40, 12);
    private readonly TestGroup _host;
    private readonly CountingTerminal _terminal;
    private readonly InMemoryTerminalSession _session = new();
    private readonly InMemoryClipboardService _clipboard = new();

    public TTerminalViewTests()
    {
        TEventQueue.ClearPosted();
        ClipboardService.Current = _clipboard;
        _host = new TestGroup(new TRect(0, 0, 40, 12)) { buffer = new ScreenBuffer(40 * 12) };
        _host.state |= (ushort)(Views.sfVisible | Views.sfExposed);
        _terminal = new CountingTerminal(new TRect(0, 0, 20, 5)) { InputMode = TerminalInputMode.RawSession };
        _host.Insert(_terminal);
    }

    public void Dispose()
    {
        _terminal.DetachSession();
        TEventQueue.ClearPosted();
        ClipboardService.Reset();
        _driver.Dispose();
    }

    private sealed class CountingTerminal(TRect bounds) : TTerminal(bounds, 100)
    {
        public int Draws;

        public override void Draw()
        {
            Draws++;
            base.Draw();
        }
    }

    private TScreenChar Cell(int x, int y) => _host.buffer!.Data[y * 40 + x];

    private string Row(int y) => new string(Enumerable.Range(0, 20).Select(x => Cell(x, y).Character).ToArray()).TrimEnd();

    private void Start()
    {
        TEventQueue.ClaimUiThread();
        _session.StartAsync();
        _terminal.AttachSession(_session);
    }

    private void Key(ushort code, string text = "", uint modifiers = 0)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = code;
        ev.keyDown.text = text;
        ev.keyDown.controlKeyState = modifiers;
        _terminal.HandleEvent(ref ev);
    }

    // A dedicated thread: never the one that claimed the event loop, which a pool thread after an await could be.
    private static void OnAnotherThread(Action work)
    {
        var thread = new Thread(() => work());
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
    }

    private static void Pump()
    {
        while (TEventQueue.DeliverPostedEvent())
        {
        }
    }

    // ── drawing ──────────────────────────────────────────────────────────────

    [Fact]
    public void CellsAreDrawnWithTheirColoursAndTheCursorIsInverted()
    {
        Start();
        _session.Emit("\x1b[31mR\x1b[44mB\x1b[0m");

        Assert.Equal(('R', (byte)0x04), (Cell(0, 0).Character, (byte)Cell(0, 0).Attr));
        Assert.Equal(('B', (byte)0x14), (Cell(1, 0).Character, (byte)Cell(1, 0).Attr));
        Assert.Equal((byte)0x70, (byte)Cell(2, 0).Attr);   // the cursor: default colours inverted

        _session.Emit("\x1b[?25l");
        Assert.Equal((byte)0x07, (byte)Cell(2, 0).Attr);   // hidden cursor: nothing inverted
    }

    [Fact]
    public void WideAndSupplementaryCharactersDrawWithoutShiftingTheRow()
    {
        Start();
        _session.Emit("中x😀y𝄞z");
        Assert.Equal("中 x� y�z", Row(0));   // a one-unit cell cannot hold 😀 or 𝄞: U+FFFD stands in
        Assert.Equal('中', Cell(0, 0).Character);
        Assert.Equal('x', Cell(2, 0).Character);
        Assert.Equal('y', Cell(5, 0).Character);
        Assert.Equal('z', Cell(7, 0).Character);
    }

    [Fact]
    public void TheAlternateScreenIsDrawnAndThePrimaryComesBack()
    {
        Start();
        _session.Emit("shell$ ");
        _session.Emit("\x1b[?1049h\x1b[H\x1b[7mSTATUS\x1b[0m");
        Assert.Equal("STATUS", Row(0));
        Assert.Equal((byte)0x70, (byte)Cell(0, 0).Attr);

        _session.Emit("\x1b[?1049l");
        Assert.Equal("shell$", Row(0));
    }

    // ── the event-loop boundary ──────────────────────────────────────────────

    [Fact]
    public void OutputFromAnotherThreadIsParsedThereAndDrawnOnceThroughOnePost()
    {
        Start();
        int drawsBefore = _terminal.Draws;

        OnAnotherThread(() =>
        {
            for (int i = 0; i < 200; i++) _session.Emit($"{i % 10}");
        });

        Assert.Equal(1, TEventQueue.PostedCount);              // coalesced: one outstanding post for 200 chunks
        Assert.Equal(drawsBefore, _terminal.Draws);            // nothing drawn off the event loop
        lock (_terminal.SyncRoot) Assert.Equal(19, _terminal.Emulator.CursorColumn);   // already parsed: 200 = 10 full rows

        Pump();
        Assert.Equal(drawsBefore + 1, _terminal.Draws);
        Assert.Equal(0, TEventQueue.PostedCount);
    }

    [Fact]
    public void IdleEventsDoNotRedraw()
    {
        Start();
        _session.Emit("ready");
        int draws = _terminal.Draws;

        for (int i = 0; i < 100; i++)
        {
            var idle = new TEvent { What = Events.evNothing };
            _terminal.HandleEvent(ref idle);
        }

        Assert.Equal(draws, _terminal.Draws);
    }

    [Fact]
    public void TheTitleChangesOnTheEventLoopThread()
    {
        Start();
        int? raisedOn = null;
        _terminal.TitleChanged += (_, _) => raisedOn = Environment.CurrentManagedThreadId;

        OnAnotherThread(() => _session.Emit("\x1b]0;vim README\x07"));
        Assert.Null(raisedOn);

        Pump();
        Assert.Equal(Environment.CurrentManagedThreadId, raisedOn);
        Assert.Equal("vim README", _terminal.Title);
    }

    [Fact]
    public void QueriesFromTheProgramAreAnswered()
    {
        Start();
        _session.Emit("ab\x1b[6n");
        Assert.Equal("\x1b[1;3R", Assert.Single(_session.SentInputs));
    }

    // ── input ────────────────────────────────────────────────────────────────

    [Fact]
    public void KeysAreEncodedForTheProgramIncludingApplicationCursorMode()
    {
        Start();
        Key(0, "č");
        Key(Keys.kbUp);
        _session.Emit("\x1b[?1h");
        Key(Keys.kbUp);
        Key(Keys.kbF5);
        Key(Keys.kbCtrlZ);

        Assert.Equal(new[] { "č", "\x1b[A", "\x1bOA", "\x1b[15~", "\x1a" }, _session.SentInputs);
    }

    [Fact]
    public void CtrlCInterruptsWithoutASelectionAndCopiesWithOne()
    {
        Start();
        _session.Emit("copy me");

        Key(Keys.kbCtrlC);
        Assert.Equal(1, _session.InterruptCount);
        Assert.Empty(_session.SentInputs);
        Assert.Null(_clipboard.GetText());

        _terminal.SetSelection(new TerminalTextPosition(0, 0), new TerminalTextPosition(0, 4));
        Key(Keys.kbCtrlC);
        Assert.Equal(1, _session.InterruptCount);           // copying is not interrupting
        Assert.Equal("copy", _clipboard.GetText());
        Assert.False(_terminal.HasSelection);
    }

    [Fact]
    public void WithoutAnInterruptibleSessionCtrlCSendsEtx()
    {
        TEventQueue.ClaimUiThread();
        var session = new ByteOnlySession();
        _terminal.AttachSession(session);
        Key(Keys.kbCtrlC);
        Assert.Equal("\x03", Encoding.UTF8.GetString(Assert.Single(session.Sent)));
    }

    [Fact]
    public void PasteIsPlainOrBracketedAsTheProgramAsks()
    {
        Start();
        _clipboard.SetText("echo ✓\nls");
        Key(Keys.kbCtrlV);
        _session.Emit("\x1b[?2004h");
        Key(Keys.kbShiftIns);

        Assert.Equal(new[] { "echo ✓\rls", "\x1b[200~echo ✓\rls\x1b[201~" }, _session.SentInputs);
    }

    [Fact]
    public void KeysWithoutMeaningAreLeftUnconsumed()
    {
        Start();
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = Keys.kbCtrlPrtSc;
        _terminal.HandleEvent(ref ev);

        Assert.Equal(Events.evKeyDown, ev.What);
        Assert.Empty(_session.SentInputs);
    }

    [Fact]
    public void TheWheelOnTheAlternateScreenSendsCursorKeys()
    {
        Start();
        _session.Emit("\x1b[?1049h\x1b[?1h");
        var ev = new TEvent { What = Events.evMouseWheel };
        ev.mouse.eventFlags = Events.meWheelDown;
        _terminal.HandleEvent(ref ev);

        Assert.Equal("\x1bOB\x1bOB\x1bOB", Assert.Single(_session.SentInputs));
    }

    // ── scrollback viewing ───────────────────────────────────────────────────

    [Fact]
    public void ScrollbackStaysPutWhileOutputArrivesAndTypingReturnsToTheBottom()
    {
        Start();
        for (int i = 0; i < 30; i++) _session.Emit($"line {i}\r\n");

        Key(Keys.kbPgUp, modifiers: Keys.kbLeftShift);
        Assert.False(_terminal.IsAtBottom);
        string shown = Row(0);

        for (int i = 30; i < 40; i++) _session.Emit($"line {i}\r\n");
        Assert.Equal(shown, Row(0));   // what is being read does not move

        Key(0, "x");
        Assert.True(_terminal.IsAtBottom);
        Assert.Equal("x", _session.SentInputs[^1]);
    }

    // ── resize and lifecycle ─────────────────────────────────────────────────

    [Fact]
    public void ResizeUpdatesTheEmulatorBeforeTheSession()
    {
        TEventQueue.ClaimUiThread();
        var session = new ResizeObservingSession(_terminal);
        _terminal.AttachSession(session);
        _terminal.ChangeBounds(new TRect(0, 0, 30, 8));

        Assert.Equal(new TerminalSize(30, 8), session.Sizes[^1]);
        Assert.Equal((30, 8), session.EmulatorSizeSeen[^1]);   // already resized when the session heard of it
    }

    [Fact]
    public void OutputStillInFlightNeverReachesADetachedTerminal()
    {
        var session = new FakePtyTerminalSession();
        session.StartAsync();
        TEventQueue.ClaimUiThread();
        _terminal.AttachSession(session);
        session.Emit("before");

        using var stop = new CancellationTokenSource();
        var writer = new Thread(() =>
        {
            while (!stop.IsCancellationRequested) session.Emit("x");
        });
        writer.Start();

        Thread.Sleep(20);
        _terminal.ShutDown();          // detaches while output is arriving
        string after = State();
        Thread.Sleep(20);
        stop.Cancel();
        Assert.True(writer.Join(TimeSpan.FromSeconds(30)));

        Assert.Equal(after, State());  // nothing more was fed after the detach returned
        Pump();                        // a post already queued for the view is discarded or harmless

        string State()
        {
            lock (_terminal.SyncRoot)
            {
                TerminalEmulator e = _terminal.Emulator;
                return $"{e.ScrollbackTotalAdded}:{e.CursorRow},{e.CursorColumn}:" +
                       string.Join('|', Enumerable.Range(0, e.Rows).Select(e.GetRowText));
            }
        }
    }

    [Fact]
    public void SessionExitIsReportedOnceAndNeverAfterDetaching()
    {
        Start();
        int exits = 0;
        _terminal.SessionExited += (_, _) => exits++;
        _session.Emit("bye\r\n");
        _session.StopAsync();
        Assert.Equal(1, exits);

        var detached = new FakePtyTerminalSession();
        detached.StartAsync();
        _terminal.AttachSession(detached);
        _terminal.DetachSession();
        detached.Complete();
        Assert.Equal(1, exits);
    }

    // ── text ─────────────────────────────────────────────────────────────────

    [Fact]
    public void LogStyleTextViewsFollowTheCursor()
    {
        _terminal.NewLineMode = true;
        _terminal.Write("a\nb\nc");

        Assert.Equal(new[] { "a", "b" }, _terminal.Lines);
        Assert.Equal("c", _terminal.CurrentLine);
        Assert.Equal("a\nb\nc", _terminal.GetText());
    }

    [Fact]
    public void AWrappedLineIsOneLineForSelectionAndFind()
    {
        _terminal.NewLineMode = true;
        _terminal.Write("the quick brown fox jumps\nnext");   // 25 characters wrap at 20

        Assert.Equal("the quick brown fox jumps", _terminal.GetLineText(0));
        Assert.Equal("next", _terminal.GetLineText(1));
        Assert.True(_terminal.TryFind("fox jumps", 0, StringComparison.Ordinal, out TerminalTextPosition start, out TerminalTextPosition end));
        Assert.Equal((0, 16), (start.LineIndex, start.Column));
        Assert.Equal((0, 25), (end.LineIndex, end.Column));

        _terminal.SetSelection(start, end);
        Assert.Equal("fox jumps", _terminal.GetSelectedText());
        Assert.True(_terminal.TryFind("NEXT", 1, StringComparison.OrdinalIgnoreCase, out start, out _));
        Assert.Equal(1, start.LineIndex);
    }

    [Fact]
    public void TheCommandLineSubmitsCommandsAndKeepsARowForItself()
    {
        _terminal.InputMode = TerminalInputMode.Command;
        string? submitted = null;
        _terminal.CommandSubmitted += (_, e) => submitted = e.Command;

        foreach (char c in "dir") Key(0, c.ToString());
        Key(Keys.kbEnter);

        Assert.Equal("dir", submitted);
        Assert.Equal(new[] { "> dir" }, _terminal.Lines);
        lock (_terminal.SyncRoot) Assert.Equal(4, _terminal.Emulator.Rows);   // the prompt row is not part of the grid
        Assert.Equal(">", Row(4));
    }

    [Fact]
    public void ClearEmptiesScreenAndScrollback()
    {
        _terminal.NewLineMode = true;
        for (int i = 0; i < 20; i++) _terminal.Write($"{i}\n");
        _terminal.Clear();

        Assert.Equal(string.Empty, _terminal.GetText());
        lock (_terminal.SyncRoot) Assert.Equal(0, _terminal.Emulator.ScrollbackCount);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed class ByteOnlySession : ITerminalSession
    {
        public List<byte[]> Sent { get; } = new();

        public event EventHandler<TerminalOutputEventArgs>? OutputReceived { add { } remove { } }

        public event EventHandler? Exited { add { } remove { } }

        public bool IsRunning => true;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendInputAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default)
        {
            Sent.Add(input.ToArray());
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class ResizeObservingSession(TTerminal terminal) : ITerminalSession, IResizableTerminalSession
    {
        public List<TerminalSize> Sizes { get; } = new();

        public List<(int, int)> EmulatorSizeSeen { get; } = new();

        public event EventHandler<TerminalOutputEventArgs>? OutputReceived { add { } remove { } }

        public event EventHandler? Exited { add { } remove { } }

        public bool IsRunning => true;

        public Task ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default)
        {
            Sizes.Add(size);
            lock (terminal.SyncRoot) EmulatorSizeSeen.Add((terminal.Emulator.Columns, terminal.Emulator.Rows));
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendInputAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
