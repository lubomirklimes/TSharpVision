// Targeted posting: delivering a command to one specific view, on the event-loop thread,
// from any thread, and regardless of who currently owns event dispatch.
//
// The ordinary queue cannot do this. An event taken from it is handed to whichever group is
// running the loop, so while a modal view executes through TGroup.ExecView anything meant for
// a view underneath it is dispatched to the modal view, goes unhandled, and is dropped by
// TGroup.EventError.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class PostedEventTests : IDisposable
{
    private readonly DriverScope _driver = new DriverScope();

    public PostedEventTests()
    {
        // TApplication constructs a TEventQueue (which resumes it); a bare TProgram does not.
        TEventQueue.Resume();
        TEventQueue.ClearPosted();
    }

    public void Dispose()
    {
        TEventQueue.ClearPosted();
        _driver.Dispose();
    }

    /// <summary>Records every command and payload its HandleEvent is given.</summary>
    private sealed class RecordingView : TView
    {
        public RecordingView(TRect bounds) : base(bounds)
        {
            options |= Views.ofSelectable;
            eventMask = 0xFFFF;
        }

        public List<ushort> Commands { get; } = new();

        public List<IInfo?> Payloads { get; } = new();

        public Action<RecordingView>? OnCommand { get; set; }

        public override void HandleEvent(ref TEvent ev)
        {
            if (ev.What == Events.evCommand)
            {
                Commands.Add(ev.message.command);
                Payloads.Add(ev.message.infoPtr);
                OnCommand?.Invoke(this);
                ClearEvent(ref ev);
                return;
            }

            base.HandleEvent(ref ev);
        }
    }

    private sealed class Payload : IInfo
    {
        public Payload(string name) => Name = name;
        public string Name { get; }
    }

    /// <summary>A program whose desktop holds two recording views.</summary>
    private sealed class TestApp : TProgram
    {
        public RecordingView A { get; private set; } = null!;
        public RecordingView B { get; private set; } = null!;

        public override TDeskTop InitDesktop(TRect r)
        {
            r.a.y += 1;
            r.b.y -= 1;

            var desktop = new TDeskTop(r);
            A = new RecordingView(new TRect(0, 0, 10, 3));
            B = new RecordingView(new TRect(0, 4, 10, 7));
            desktop.Insert(A);
            desktop.Insert(B);
            return desktop;
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a genuinely separate thread and waits for it. A pooled
    /// task could be inlined onto the calling thread, which would defeat the point.
    /// </summary>
    private static void OnWorker(Action work)
    {
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try { work(); }
            catch (Exception ex) { failure = ex; }
        });

        worker.IsBackground = true;
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(10)), "worker thread did not finish");
        if (failure is not null) throw failure;
    }

    /// <summary>Runs the loop the way TGroup.Execute does, for a bounded number of passes.</summary>
    private static void Pump(TProgram app, int passes = 8)
    {
        for (int i = 0; i < passes; i++)
        {
            TEvent ev = default;
            app.GetEvent(ref ev);
            if (ev.What != Events.evNothing)
                app.HandleEvent(ref ev);
        }
    }

    private static void PumpUntil(TProgram app, Func<bool> done, int timeoutMs = 5000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (done()) return;
            Pump(app, 1);
            Thread.Sleep(1);
        }

        Assert.True(done(), "the posted event was never delivered");
    }

    // ── ordinary targeted delivery ───────────────────────────────────────────

    [Fact]
    public void APostReachesOnlyItsTarget()
    {
        var app = new TestApp();
        var payload = new Payload("result");

        app.B.Post(200, payload);
        Pump(app);

        Assert.Equal(new ushort[] { 200 }, app.B.Commands);
        Assert.Same(payload, Assert.Single(app.B.Payloads));
        Assert.Empty(app.A.Commands);
    }

    [Fact]
    public void APostIsDeliveredExactlyOnce()
    {
        var app = new TestApp();

        app.B.Post(201);
        Pump(app, passes: 20);

        Assert.Single(app.B.Commands);
        Assert.Equal(0, TEventQueue.PostedCount);
    }

    [Fact]
    public void ACommandAboveTwoFiftyFiveSurvivesIntact()
    {
        var app = new TestApp();
        var payload = new Payload("high");

        // The command set now covers the whole ushort range; a post must not truncate.
        app.B.Post(1001, payload);
        app.B.Post(ushort.MaxValue);
        Pump(app);

        Assert.Equal(new ushort[] { 1001, ushort.MaxValue }, app.B.Commands);
        Assert.Same(payload, app.B.Payloads[0]);
        Assert.Null(app.B.Payloads[1]);
    }

    [Fact]
    public void PostingFromTheUiThreadStillDefersToTheLoop()
    {
        var app = new TestApp();

        app.B.Post(202);

        // Same semantics from either thread: nothing runs until the loop picks it up.
        Assert.Empty(app.B.Commands);
        Assert.Equal(1, TEventQueue.PostedCount);

        Pump(app);
        Assert.Single(app.B.Commands);
    }

    [Fact]
    public void PostingFromAWorkerThreadIsSafe()
    {
        var app = new TestApp();
        var payload = new Payload("from worker");
        int postingThread = 0;

        // Not awaited: the test thread is the UI thread, and awaiting would resume it on the
        // thread pool.
        OnWorker(() =>
        {
            postingThread = Environment.CurrentManagedThreadId;
            app.B.Post(203, payload);
        });

        Assert.NotEqual(Environment.CurrentManagedThreadId, postingThread);
        Assert.Empty(app.B.Commands);           // nothing ran on the worker

        PumpUntil(app, () => app.B.Commands.Count == 1);
        Assert.Same(payload, Assert.Single(app.B.Payloads));
    }

    [Fact]
    public void DeliveryNeverHappensOnAWorkerThread()
    {
        var app = new TestApp();
        int handledOn = 0;
        app.B.OnCommand = _ => handledOn = Environment.CurrentManagedThreadId;

        app.B.Post(204);

        // A worker pumping is refused outright, so the post stays queued.
        bool deliveredOnWorker = true;
        OnWorker(() => deliveredOnWorker = TEventQueue.DeliverPostedEvent());

        Assert.False(deliveredOnWorker);
        Assert.Equal(0, handledOn);
        Assert.Equal(1, TEventQueue.PostedCount);

        Pump(app);
        Assert.Equal(Environment.CurrentManagedThreadId, handledOn);
    }

    // ── modal loops: the reason this exists ──────────────────────────────────

    /// <summary>A dialog that ends itself once the test has seen what it needs.</summary>
    private sealed class ScriptedDialog : TDialog
    {
        public ScriptedDialog(TRect bounds, string title) : base(bounds, title) { }

        public List<ushort> Commands { get; } = new();

        public Action? BeforeEachPass { get; set; }

        public bool Finish { get; set; }

        private readonly DateTime _deadline = DateTime.UtcNow.AddSeconds(10);

        public override void HandleEvent(ref TEvent ev)
        {
            if (ev.What == Events.evCommand)
                Commands.Add(ev.message.command);

            base.HandleEvent(ref ev);
        }

        public override void GetEvent(ref TEvent ev)
        {
            BeforeEachPass?.Invoke();
            base.GetEvent(ref ev);

            // Never spin forever: if routing is broken the test should fail on its
            // assertions rather than hang.
            if (Finish || DateTime.UtcNow > _deadline)
                EndModal(Views.cmCancel);
        }
    }

    [Fact]
    public void APostReachesAViewUnderneathARunningModalDialog()
    {
        var app = new TestApp();
        var payload = new Payload("while modal");
        var dialog = new ScriptedDialog(new TRect(5, 5, 45, 15), "Modal");

        using var release = new ManualResetEventSlim(false);
        Task? worker = null;
        bool posted = false;

        dialog.BeforeEachPass = () =>
        {
            if (!posted)
            {
                posted = true;
                // Post from a worker while the dialog owns the event loop.
                worker = Task.Factory.StartNew(
                    () =>
                    {
                        app.B.Post(205, payload);
                        release.Set();
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,   // its own thread, never inlined
                    TaskScheduler.Default);
            }
            else if (release.IsSet && app.B.Commands.Count == 1)
            {
                dialog.Finish = true;   // seen it; let the dialog close
            }
        };

        ushort result = app.DeskTop!.ExecView(dialog);
        worker?.Wait();

        // The dialog ran its own nested loop to completion...
        Assert.Equal(Views.cmCancel, result);

        // ...and the post went to the view underneath it, exactly once, not to the dialog.
        Assert.Equal(new ushort[] { 205 }, app.B.Commands);
        Assert.Same(payload, Assert.Single(app.B.Payloads));
        Assert.DoesNotContain((ushort)205, dialog.Commands);
        Assert.Empty(app.A.Commands);
        Assert.Equal(0, TEventQueue.PostedCount);
    }

    [Fact]
    public void APostReachesTheRunningModalDialogItself()
    {
        var app = new TestApp();
        var dialog = new ScriptedDialog(new TRect(5, 5, 45, 15), "Modal");
        bool posted = false;

        dialog.BeforeEachPass = () =>
        {
            if (!posted)
            {
                posted = true;
                dialog.Post(206);
            }
            else if (dialog.Commands.Contains((ushort)206))
            {
                dialog.Finish = true;
            }
        };

        app.DeskTop!.ExecView(dialog);

        Assert.Contains((ushort)206, dialog.Commands);
        Assert.Empty(app.B.Commands);
    }

    [Fact]
    public void APostReachesUnderneathTwoNestedModalDialogs()
    {
        var app = new TestApp();
        var payload = new Payload("nested");
        var outer = new ScriptedDialog(new TRect(2, 2, 50, 18), "Outer");
        var inner = new ScriptedDialog(new TRect(6, 6, 40, 14), "Inner");

        bool innerOpened = false;
        bool posted = false;

        inner.BeforeEachPass = () =>
        {
            if (!posted)
            {
                posted = true;
                OnWorker(() => app.B.Post(207, payload));
            }
            else if (app.B.Commands.Count == 1)
            {
                inner.Finish = true;
            }
        };

        outer.BeforeEachPass = () =>
        {
            if (!innerOpened)
            {
                innerOpened = true;
                app.DeskTop!.ExecView(inner);      // second level of nesting
            }
            else
            {
                outer.Finish = true;
            }
        };

        app.DeskTop!.ExecView(outer);

        Assert.Equal(new ushort[] { 207 }, app.B.Commands);
        Assert.Same(payload, Assert.Single(app.B.Payloads));
        Assert.DoesNotContain((ushort)207, inner.Commands);
        Assert.DoesNotContain((ushort)207, outer.Commands);
    }

    // ── target lifetime ──────────────────────────────────────────────────────

    [Fact]
    public void APostToAViewThatWasRemovedIsDiscarded()
    {
        var app = new TestApp();
        RecordingView target = app.B;

        target.Post(208);
        app.DeskTop!.Remove(target);

        Pump(app, passes: 10);

        Assert.Empty(target.Commands);
        Assert.Equal(0, TEventQueue.PostedCount);   // taken off the queue, not retained
    }

    [Fact]
    public void APostToAClosedWindowIsDiscarded()
    {
        var app = new TestApp();
        var window = new TWindow(new TRect(5, 5, 40, 15), "Doomed", 0);
        app.DeskTop!.Insert(window);

        window.Post(209);
        window.Close();

        Pump(app, passes: 10);

        Assert.Equal(0, TEventQueue.PostedCount);
        Assert.Null(window.owner);
    }

    [Fact]
    public void ShutdownDropsPendingPosts()
    {
        var app = new TestApp();

        app.B.Post(210);
        Assert.Equal(1, TEventQueue.PostedCount);

        app.ShutDown();

        Assert.Equal(0, TEventQueue.PostedCount);
        Assert.Empty(app.B.Commands);
    }

    [Fact]
    public void TheApplicationItselfIsAValidTarget()
    {
        var app = new TestApp();

        // TProgram has no owner, so it needs its own liveness rule.
        app.Post(211);
        Pump(app);

        // Delivered without throwing; TProgram handles what it recognises and ignores the rest.
        Assert.Equal(0, TEventQueue.PostedCount);
    }

    // ── ordering ─────────────────────────────────────────────────────────────

    [Fact]
    public void PostsFromOneThreadArriveInOrder()
    {
        var app = new TestApp();

        OnWorker(() =>
        {
            for (ushort command = 1; command <= 20; command++)
                app.B.Post(command);
        });

        PumpUntil(app, () => app.B.Commands.Count == 20);

        for (int i = 0; i < 20; i++)
            Assert.Equal((ushort)(i + 1), app.B.Commands[i]);
    }

    [Fact]
    public void PostsFromSeveralThreadsAllArriveAndEachThreadKeepsItsOwnOrder()
    {
        var app = new TestApp();
        const int workers = 4;
        const int perWorker = 25;

        var threads = Enumerable.Range(0, workers).Select(w => new Thread(() =>
        {
            for (int i = 0; i < perWorker; i++)
                app.B.Post((ushort)((w * 1000) + i));
        })).ToList();

        foreach (Thread t in threads) t.Start();
        foreach (Thread t in threads) Assert.True(t.Join(TimeSpan.FromSeconds(10)));

        PumpUntil(app, () => app.B.Commands.Count == workers * perWorker);

        // Interleaving between workers is whatever order they took the lock in; what is
        // guaranteed is that nothing is lost and each worker's own sequence is preserved.
        for (int w = 0; w < workers; w++)
        {
            var mine = app.B.Commands.Where(c => c / 1000 == w).ToArray();
            Assert.Equal(perWorker, mine.Length);
            for (int i = 0; i < perWorker; i++)
                Assert.Equal((ushort)((w * 1000) + i), mine[i]);
        }
    }

    // ── living alongside ordinary input ──────────────────────────────────────

    [Fact]
    public void PostsAndKeyboardEventsBothGetThrough()
    {
        var app = new TestApp();

        // Queue a burst of posts and a burst of keys together.
        for (ushort command = 1; command <= 5; command++)
            app.B.Post(command);

        for (int i = 0; i < 5; i++)
        {
            var key = new TEvent { What = Events.evKeyDown };
            key.keyDown.keyCode = Keys.kbDown;
            _driver.Driver.EnqueueKey(key);
        }

        var keysSeen = 0;
        for (int pass = 0; pass < 30; pass++)
        {
            TEvent ev = default;
            app.GetEvent(ref ev);
            if (ev.What == Events.evKeyDown && ev.keyDown.keyCode == Keys.kbDown)
                keysSeen++;
        }

        // Neither starves the other: one post per pass, input still flowing alongside.
        Assert.Equal(5, app.B.Commands.Count);
        Assert.Equal(5, keysSeen);
        Assert.Equal(0, TEventQueue.PostedCount);
    }

    [Fact]
    public void TheStatusLineKeyFilterStillWorksWhilePostsArePending()
    {
        var app = new TestApp();

        app.B.Post(212);

        var key = new TEvent { What = Events.evKeyDown };
        key.keyDown.keyCode = Keys.kbAltX;
        _driver.Driver.EnqueueKey(key);

        TEvent ev = default;
        app.GetEvent(ref ev);

        // TProgram's default status line maps Alt+X to cmQuit; delivering a post in the same
        // pass must not disturb that.
        Assert.Equal(Events.evCommand, ev.What);
        Assert.Equal(Views.cmQuit, ev.message.command);
        Assert.Single(app.B.Commands);
    }

    [Fact]
    public void AnEnqueuedBroadcastIsStillLostUnderAModalDialog()
    {
        // Documents the limitation Post exists to work around, and pins it so the two
        // mechanisms cannot quietly converge. An event taken from the ordinary queue is
        // handed to whichever group owns the loop; the modal dialog does not recognise this
        // broadcast, so TGroup.EventError drops it and the view underneath never sees it.
        var app = new TestApp();
        var dialog = new ScriptedDialog(new TRect(5, 5, 45, 15), "Modal");
        bool enqueued = false;
        int passes = 0;

        dialog.BeforeEachPass = () =>
        {
            if (!enqueued)
            {
                enqueued = true;
                var broadcast = new TEvent { What = Events.evBroadcast };
                broadcast.message.command = 214;
                TEventQueue.Enqueue(broadcast);
            }
            else if (++passes > 5)
            {
                dialog.Finish = true;
            }
        };

        app.DeskTop!.ExecView(dialog);

        Assert.Empty(app.B.Commands);
        Assert.Empty(app.A.Commands);
    }

    [Fact]
    public void OrdinaryEnqueuedEventsStillFlowThroughTheNormalPath()
    {
        var app = new TestApp();

        var broadcast = new TEvent { What = Events.evBroadcast };
        broadcast.message.command = 213;
        TEventQueue.Enqueue(broadcast);

        TEvent ev = default;
        app.GetEvent(ref ev);

        // Enqueue is untouched: it still yields the event to whoever is running the loop.
        Assert.Equal(Events.evBroadcast, ev.What);
        Assert.Equal(213, ev.message.command);
    }
}
