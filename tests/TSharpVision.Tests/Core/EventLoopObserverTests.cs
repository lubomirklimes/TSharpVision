using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

/// <summary>The IEventLoopObserver contract as TProgram.GetEvent enforces it.</summary>
[Collection("NonParallel")]
public sealed class EventLoopObserverTests : IDisposable
{
    private readonly DriverScope _scope = new();
    private readonly ObservingDriver _driver = new();
    public EventLoopObserverTests()
    {
        TDisplay.driver = _driver;
        TEventQueue.Resume();
        TEventQueue.ClearPosted();
        Drain();
    }
    public void Dispose() { TEventQueue.ClearPosted(); Drain(); _scope.Dispose(); }
    private static TEvent Command(ushort command) { var ev = new TEvent { What = Events.evCommand }; ev.message.command = command; return ev; }
    private static void Drain() { TEvent ev = default; do { TEventQueue.GetNextEvent(ref ev); } while (ev.What != 0); }

    [Fact]
    public void EmptyPassIsObservedOnUiThreadAfterIdleAndBeforeWait()
    {
        var app = new ProbeProgram { StopAfterCommands = 1 };
        _driver.OnObserve = _ =>
        {
            Assert.Equal(TEventQueue.UiThreadId, Environment.CurrentManagedThreadId);
            Assert.Equal(1, app.Idles);
            Assert.Equal(0, _driver.Waits);
            TEventQueue.Enqueue(Command(901));
        };
        try { app.Run(); }
        finally { app.ShutDown(); }
        Assert.Equal(new ushort[] { 901 }, app.Commands);
        Assert.Equal(1, _driver.Observations);
        // Queueing inside the callback is a wake: the loop did not sleep in that pass.
        Assert.Equal(0, _driver.Waits);
    }

    [Fact]
    public void DirectProbeOutsideAnyLoopIsNotObserved()
    {
        var app = new ProbeProgram();
        try
        {
            TEvent ev = default;
            app.GetEvent(ref ev);
            Assert.Equal(Events.evNothing, ev.What);
            Assert.Equal(1, app.Idles);
            Assert.Equal(0, _driver.Observations);
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void PassWithQueuedOrPostedWorkIsNotObserved()
    {
        var app = new ProbeProgram { StopAfterCommands = 2 };
        int idle = 0;
        app.OnIdle = () => { if (++idle == 1) { TEventQueue.Enqueue(Command(901)); app.Post(902); } };
        _driver.OnObserve = _ => Assert.Fail("Observed a pass that still had queued and posted work.");
        try { app.Run(); }
        finally { app.ShutDown(); }
        Assert.Equal(new ushort[] { 902, 901 }, app.Commands);
        Assert.Equal(0, _driver.Observations);
    }

    [Fact]
    public void NestedModalLoopIsObservedAndCallbackCanEndIt()
    {
        var app = new ProbeProgram();
        var dialog = new TDialog(new TRect(1, 1, 30, 12), "Modal");
        int depth = 0;
        _driver.OnObserve = _ => { depth = TGroup.EventLoopDepth; dialog.EndModal(Views.cmCancel); };
        try { Assert.Equal(Views.cmCancel, app.DeskTop!.ExecView(dialog)); }
        finally { dialog.ShutDown(); app.ShutDown(); }
        Assert.Equal(1, depth);
        Assert.Equal(1, _driver.Observations);
    }

    [Fact]
    public void ModalLoopStartedInsideCallbackIsNotReentered()
    {
        var app = new ProbeProgram { StopAfterCommands = 1 };
        var dialog = new TDialog(new TRect(1, 1, 30, 12), "Nested");
        int active = 0, maximum = 0;
        ushort nestedResult = 0;
        _driver.OnObserve = _ =>
        {
            maximum = Math.Max(maximum, ++active);
            try
            {
                if (_driver.Observations != 1) { TEventQueue.Enqueue(Command(901)); return; }
                // The nested loop must close through ordinary input, not through the observer.
                var cancel = new TEvent { What = Events.evKeyDown };
                cancel.keyDown.keyCode = Keys.kbEsc;
                TEventQueue.Enqueue(cancel);
                nestedResult = app.DeskTop!.ExecView(dialog);
            }
            finally { active--; }
        };
        try { app.Run(); }
        finally { dialog.ShutDown(); app.ShutDown(); }
        Assert.Equal(Views.cmCancel, nestedResult);
        Assert.Equal(1, maximum);
        Assert.Equal(2, _driver.Observations);
        Assert.Equal(new ushort[] { 901 }, app.Commands);
    }

    [Fact]
    public void CallbackExceptionPropagatesAndNextLoopObservesAgain()
    {
        var app = new ProbeProgram { StopAfterCommands = 1 };
        var failure = new InvalidOperationException("observer failed");
        _driver.OnObserve = _ => throw failure;
        try
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => app.Run()));
            Assert.Equal(0, TGroup.EventLoopDepth);
            _driver.OnObserve = _ => TEventQueue.Enqueue(Command(901));
            app.Run();
        }
        finally { app.ShutDown(); }
        Assert.Equal(2, _driver.Observations);
        Assert.Equal(new ushort[] { 901 }, app.Commands);
    }

    [Fact]
    public void WakeWithoutPayloadFromCallbackSkipsTheWaitOfThatPass()
    {
        var app = new ProbeProgram { StopAfterCommands = 1 };
        _driver.OnObserve = _ =>
        {
            if (_driver.Observations == 1) TEventQueue.WakeEventLoop();
            else TEventQueue.Enqueue(Command(901));
        };
        try { app.Run(); }
        finally { app.ShutDown(); }
        Assert.Equal(2, _driver.Observations);
        Assert.Equal(0, _driver.Waits);
    }

    [Fact]
    public void DriverWithoutObserverKeepsOrdinaryWait()
    {
        var plain = new PlainWaitDriver();
        TDisplay.driver = plain;
        var app = new ProbeProgram { StopAfterCommands = 1 };
        plain.OnWait = () => TEventQueue.Enqueue(Command(901));
        try { app.Run(); }
        finally { app.ShutDown(); }
        Assert.Equal(1, plain.Waits);
        Assert.Equal(new ushort[] { 901 }, app.Commands);
    }

    private sealed class ProbeProgram : TProgram
    {
        internal List<ushort> Commands = new();
        internal int StopAfterCommands, Idles;
        internal Action? OnIdle;
        public override void Idle() { base.Idle(); Idles++; OnIdle?.Invoke(); }
        public override void HandleEvent(ref TEvent ev)
        {
            if (ev.What == Events.evCommand && ev.message.command >= 900)
            {
                Commands.Add(ev.message.command);
                if (StopAfterCommands != 0 && Commands.Count == StopAfterCommands) EndModal(Views.cmQuit);
                ClearEvent(ref ev);
            }
            else base.HandleEvent(ref ev);
        }
    }

    private class PlainWaitDriver : IDriver, IIdleWaitDriver
    {
        private readonly NullDriver _inner = new();
        internal int Waits;
        internal Action? OnWait;
        public void WaitForEvent(int timeoutMilliseconds, WaitHandle wakeSignal)
        {
            Waits++;
            Assert.NotNull(OnWait);
            OnWait();
        }
        public void Initialize() => _inner.Initialize();
        public void Suspend() => _inner.Suspend();
        public void Resume() => _inner.Resume();
        public void Shutdown() => _inner.Shutdown();
        public ushort GetCols() => _inner.GetCols();
        public ushort GetRows() => _inner.GetRows();
        public TDisplay.SM GetScreenMode() => _inner.GetScreenMode();
        public void SetScreenMode(TDisplay.SM mode) => _inner.SetScreenMode(mode);
        public ScreenBuffer AllocateScreenBuffer() => _inner.AllocateScreenBuffer();
        public void ClearScreen(ushort cols, ushort rows) { }
        public ushort GetCursorType() => _inner.GetCursorType();
        public void SetCursorType(ushort type) => _inner.SetCursorType(type);
        public void SetCaretPosition(int x, int y) { }
        public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf) { }
        public void MakeBeep() { }
        public void PumpMessages() { }
        public bool ReadKeyEvent(out TEvent ev) { ev = default; return false; }
        public bool SupportsMouse => true;
        public bool SupportsTrueColor => false;
        public bool SupportsGraphics => false;
    }

    private sealed class ObservingDriver : PlainWaitDriver, IEventLoopObserver
    {
        internal int Observations;
        internal Action<TProgram>? OnObserve;
        public ObservingDriver() => OnWait = () => Assert.Fail("The loop waited although the observer queued work.");
        public void OnIdleBoundary(TProgram application)
        {
            Observations++;
            Assert.NotNull(OnObserve);
            OnObserve(application);
        }
    }
}
