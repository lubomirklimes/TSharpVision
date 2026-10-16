using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class IdleWaitTests : IDisposable
{
    private readonly DriverScope _scope = new();
    private readonly WaitDriver _driver = new();
    public IdleWaitTests()
    {
        TDisplay.driver = _driver;
        TEventQueue.Resume();
        TEventQueue.ClearPosted();
        Drain();
    }
    public void Dispose() { TEventQueue.ClearPosted(); Drain(); _scope.Dispose(); }
    private static TEvent Command(ushort command) { var ev = new TEvent { What = Events.evCommand }; ev.message.command = command; return ev; }
    private static void Drain() { TEvent ev = default; do { TEventQueue.GetNextEvent(ref ev); } while (ev.What != 0); }
    private static Thread Worker(Action action) { var worker = new Thread(() => action()) { IsBackground = true }; worker.Start(); return worker; }

    [Fact]
    public void QueuedBeforeWaitSkipsWaitAndDrainResetsSignal()
    {
        long generation = TEventQueue.WakeGeneration;
        TEventQueue.Enqueue(Command(901));
        Assert.Null(TEventQueue.PrepareWait(generation));
        Assert.Null(TEventQueue.PrepareWait(TEventQueue.WakeGeneration));
        TEvent ev = default;
        TEventQueue.GetNextEvent(ref ev);
        Assert.Equal((ushort)901, ev.message.command);
        WaitHandle signal = Assert.IsAssignableFrom<WaitHandle>(TEventQueue.PrepareWait(TEventQueue.WakeGeneration));
        Assert.False(signal.WaitOne(0));
    }

    [Fact]
    public void OrdinaryQueueWakesBlockedLoopAndPreservesMultipleEvents()
    {
        var app = new ProbeProgram { StopAfterCommands = 3 };
        using var entered = new ManualResetEventSlim();
        using var producerFinished = new ManualResetEventSlim();
        _driver.OnWait = (_, signal) =>
        {
            entered.Set();
            Assert.True(signal.WaitOne(2000));
            Assert.True(producerFinished.Wait(2000));
        };
        var producer = Worker(() =>
        {
            if (!entered.Wait(2000)) return;
            foreach (ushort command in new ushort[] { 901, 902, 903 }) TEventQueue.Enqueue(Command(command));
            producerFinished.Set();
        });
        try { app.Run(); Assert.True(producer.Join(2000)); Assert.Equal(new ushort[] { 901, 902, 903 }, app.Commands); Assert.Equal(1, _driver.Waits); }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void PostExactlyBetweenEmptyCheckAndNativeWaitIsDeliveredOnceOnUiThread()
    {
        var app = new ProbeProgram { StopAfterCommands = 1 };
        int producerThread = 0;
        _driver.OnWait = (_, signal) =>
        {
            // This callback is the deterministic barrier: framework PrepareWait has
            // reset the signal; the native driver has not begun blocking yet.
            var producer = Worker(() => { producerThread = Environment.CurrentManagedThreadId; app.Post(901); });
            Assert.True(producer.Join(2000));
            Assert.True(signal.WaitOne(0));
        };
        try
        {
            int ui = Environment.CurrentManagedThreadId;
            app.Run();
            Assert.Equal(new ushort[] { 901 }, app.Commands);
            Assert.Equal(ui, Assert.Single(app.DeliveryThreads));
            Assert.NotEqual(ui, producerThread);
            Assert.Equal(0, TEventQueue.PostedCount);
        }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void TargetedPostsWakeNestedModalLoopWithoutDispatchingToModalView()
    {
        var app = new ProbeProgram();
        var dialog = new TDialog(new TRect(1, 1, 30, 12), "Modal");
        var target = new TargetView(new TRect(1, 1, 5, 3)) { AfterThird = () => dialog.EndModal(Views.cmCancel) };
        app.DeskTop!.Insert(target);
        using var entered = new ManualResetEventSlim();
        _driver.OnWait = (_, signal) => { entered.Set(); Assert.True(signal.WaitOne(2000)); };
        var producer = Worker(() =>
        {
            if (!entered.Wait(2000)) return;
            target.Post(901); target.Post(902); target.Post(903);
        });
        try
        {
            Assert.Equal(Views.cmCancel, app.DeskTop!.ExecView(dialog));
            Assert.True(producer.Join(2000));
            Assert.Equal(new ushort[] { 901, 902, 903 }, target.Commands);
            Assert.All(target.Threads, thread => Assert.Equal(Environment.CurrentManagedThreadId, thread));
            Assert.Empty(app.Commands);
            Assert.Equal(0, TGroup.EventLoopDepth);
        }
        finally { dialog.ShutDown(); app.ShutDown(); }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(75)]
    [InlineData(0)]
    public void ApplicationDeadlineReachesDriverAndExpirationResumesLoop(int timeout)
    {
        var app = new ProbeProgram { Timeout = timeout, StopAfterCommands = 1 };
        _driver.OnWait = (actual, _) => { Assert.Equal(timeout, actual); TEventQueue.Enqueue(Command(901)); };
        try { app.Run(); Assert.Single(app.Commands); Assert.Equal(1, _driver.Waits); }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void EarlierDeadlineChangePostedWhileWaitingWakesAndRecomputes()
    {
        var app = new ProbeProgram { Timeout = 500 };
        _driver.OnWait = (timeout, signal) =>
        {
            Assert.Equal(_driver.Waits == 1 ? 500 : 10, timeout);
            if (_driver.Waits == 1)
            {
                var producer = Worker(() => { app.Timeout = 10; app.Post(901); });
                Assert.True(producer.Join(2000)); Assert.True(signal.WaitOne(0));
            }
            else app.EndModal(Views.cmQuit);
        };
        try { app.Run(); Assert.Equal(2, _driver.Waits); Assert.Single(app.Commands); }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void IdleGeneratedPendingWorkIsDeliveredBeforeWaiting()
    {
        var app = new ProbeProgram { StopAfterCommands = 1 };
        app.OnIdle = () => { TEvent ev = Command(901); app.PutEvent(ref ev); };
        try { app.Run(); Assert.Single(app.Commands); Assert.Equal(0, _driver.Waits); }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void DoNotReleaseCpuPreservesContinuousIdlePasses()
    {
        var app = new ProbeProgram();
        app.OnIdle = () => { if (app.Idles == 3) app.EndModal(Views.cmQuit); };
        try { app.SetBusy(true); app.Run(); Assert.Equal(3, app.Idles); Assert.Equal(0, _driver.Waits); }
        finally { app.SetBusy(false); app.ShutDown(); }
    }

    [Fact]
    public void EndModalFromWorkerWakesBlockedLoop()
    {
        var app = new ProbeProgram();
        _driver.OnWait = (_, signal) =>
        {
            var worker = Worker(() => app.EndModal(Views.cmQuit));
            Assert.True(worker.Join(2000)); Assert.True(signal.WaitOne(0));
        };
        try { app.Run(); Assert.Equal(1, _driver.Waits); }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void WaitFailureEscapesAndRestoresModalDepth()
    {
        var app = new ProbeProgram();
        _driver.OnWait = (_, _) => throw new IOException("Native wait failed");
        try { Assert.Throws<IOException>(() => app.Run()); Assert.Equal(1, _driver.Waits); Assert.Equal(0, TGroup.EventLoopDepth); }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void MenuLoopWaitsAndRestoresDepthAfterEscape()
    {
        var app = new ProbeProgram();
        var menu = new TMenuBox(new TRect(1, 1, 30, 12),
            new TMenu(new TMenuItem("~T~est", 901, Keys.kbNoKey)), null);
        _driver.OnWait = (_, _) =>
        {
            var ev = new TEvent { What = Events.evKeyDown };
            ev.keyDown.keyCode = Keys.kbEsc;
            TEventQueue.Enqueue(ev);
        };
        try
        {
            Assert.Equal((ushort)0, app.DeskTop!.ExecView(menu));
            Assert.Equal(1, _driver.Waits);
            Assert.Equal(0, TGroup.EventLoopDepth);
        }
        finally { menu.ShutDown(); app.ShutDown(); }
    }

    [Fact]
    public void DirectGetEventProbeRemainsNonblocking()
    {
        var app = new ProbeProgram();
        try { TEvent ev = default; app.GetEvent(ref ev); Assert.Equal(0, ev.What); Assert.Equal(0, _driver.Waits); Assert.Equal(1, app.Idles); }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void LegacyDriverContinuesThroughBoundedWakeableFallback()
    {
        TDisplay.driver = _scope.Driver; // deliberately does not implement IIdleWaitDriver
        var app = new ProbeProgram { Timeout = 0 };
        app.OnIdle = () => { if (app.Idles == 3) app.EndModal(Views.cmQuit); };
        try { app.Run(); Assert.Equal(3, app.Idles); }
        finally { app.ShutDown(); }
    }

    [Fact]
    public void ShutdownDuringBlockedWaitStopsLoop()
    {
        var app = new ProbeProgram();
        _driver.OnWait = (_, signal) =>
        {
            var worker = Worker(app.ShutDown);
            Assert.True(worker.Join(2000)); Assert.True(signal.WaitOne(0));
        };
        app.Run();
        Assert.Equal(1, _driver.Waits);
        Assert.Equal(0, TGroup.EventLoopDepth);
    }

    [Fact]
    public void WaitCapabilityIsAdditiveAndDoesNotChangeLegacyDriverContract()
    {
        Assert.DoesNotContain(typeof(IDriver).GetMethods(), method => method.Name == "WaitForEvent");
        var method = Assert.Single(typeof(IIdleWaitDriver).GetMethods());
        Assert.Equal(typeof(void), method.ReturnType);
        Assert.Equal(new[] { typeof(int), typeof(WaitHandle) }, method.GetParameters().Select(parameter => parameter.ParameterType));
        var timeout = typeof(TProgram).GetMethod("GetIdleWaitTimeout", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.True(timeout.IsFamily && timeout.IsVirtual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedOrTargetedRootQuitWakesAndUnwindsActiveModal(bool targeted)
    {
        var app = new ProbeProgram();
        var dialog = new TDialog(new TRect(1, 1, 30, 12), "Quit while modal");
        _driver.OnWait = (_, signal) =>
        {
            var worker = Worker(() =>
            {
                if (targeted) app.Post(Views.cmQuit);
                else TEventQueue.Enqueue(Command(Views.cmQuit));
            });
            Assert.True(worker.Join(2000)); Assert.True(signal.WaitOne(0));
        };
        try
        {
            Assert.Equal(Views.cmCancel, app.DeskTop!.ExecView(dialog));
            Assert.Equal(Views.cmQuit, app.endState);
            Assert.Equal(1, _driver.Waits);
        }
        finally { dialog.ShutDown(); app.ShutDown(); }
    }

    [Fact]
    public void StatusLineKeyboardQuitAlsoUnwindsTheActiveModal()
    {
        var app = new ProbeProgram();
        var dialog = new TDialog(new TRect(1, 1, 30, 12), "Keyboard quit while modal");
        _driver.OnWait = (_, _) =>
        {
            var ev = new TEvent { What = Events.evKeyDown };
            ev.keyDown.keyCode = Keys.kbAltX;
            TEventQueue.Enqueue(ev);
        };
        try
        {
            Assert.Equal(Views.cmCancel, app.DeskTop!.ExecView(dialog));
            Assert.Equal(Views.cmQuit, app.endState);
            Assert.Equal(1, _driver.Waits);
        }
        finally { dialog.ShutDown(); app.ShutDown(); }
    }

    private sealed class TargetView(TRect bounds) : TView(bounds)
    {
        internal List<ushort> Commands = new();
        internal List<int> Threads = new();
        internal Action? AfterThird;
        public override void HandleEvent(ref TEvent ev)
        {
            if (ev.What == Events.evCommand)
            {
                Commands.Add(ev.message.command); Threads.Add(Environment.CurrentManagedThreadId);
                if (Commands.Count == 3) AfterThird?.Invoke();
                ClearEvent(ref ev);
            }
        }
    }
    private sealed class ProbeProgram : TProgram
    {
        internal List<ushort> Commands = new();
        internal List<int> DeliveryThreads = new();
        internal int StopAfterCommands, Idles;
        internal int Timeout = -1;
        internal Action? OnIdle;
        public override void Idle() { base.Idle(); Idles++; OnIdle?.Invoke(); }
        protected override int GetIdleWaitTimeout() => Timeout;
        internal void SetBusy(bool value) => DoNotReleaseCPU = (byte)(value ? 1 : 0);
        public override void HandleEvent(ref TEvent ev)
        {
            if (ev.What == Events.evCommand && ev.message.command >= 900)
            {
                Commands.Add(ev.message.command); DeliveryThreads.Add(Environment.CurrentManagedThreadId);
                if (StopAfterCommands != 0 && Commands.Count == StopAfterCommands) EndModal(Views.cmQuit);
                ClearEvent(ref ev);
            }
            else base.HandleEvent(ref ev);
        }
    }
    private sealed class WaitDriver : IDriver, IIdleWaitDriver
    {
        private readonly NullDriver _inner = new();
        internal int Waits;
        internal Action<int, WaitHandle>? OnWait;
        public void WaitForEvent(int timeoutMilliseconds, WaitHandle wakeSignal) { Waits++; Assert.NotNull(OnWait); OnWait(timeoutMilliseconds, wakeSignal); }
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
}
