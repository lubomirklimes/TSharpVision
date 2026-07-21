// Lifetime of the shared event queue.
//
// TEventQueue holds only static state, so an instance owns nothing. Its finalizer used to run
// Dispose(false) -> Suspend(), and ~TApplication reached both that and TScreen.Suspend()
// through its managed fields. Finalizing an abandoned application therefore suspended the
// input queue — and the driver — belonging to whichever application was actually running,
// at an arbitrary moment on the finalizer thread.
//
// The contract now: finalizers touch no shared state; deterministic disposal owns it.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class EventQueueLifetimeTests : IDisposable
{
    private readonly DriverScope _driver = new DriverScope();

    public EventQueueLifetimeTests()
    {
        TEventQueue.ClearPosted();
        TEventQueue.Resume();
        DrainInputQueue();
    }

    public void Dispose()
    {
        TEventQueue.ClearPosted();
        DrainInputQueue();
        TEventQueue.Resume();
        _driver.Dispose();
    }

    private static void DrainInputQueue()
    {
        for (int i = 0; i < 1000; i++)
        {
            TEvent ev = default;
            TEventQueue.GetNextEvent(ref ev);
            if (ev.What == Events.evNothing) return;
        }
    }

    /// <summary>Round-trips one event through the shared queue.</summary>
    private static bool QueueDeliversEvents()
    {
        var probe = new TEvent { What = Events.evBroadcast };
        probe.message.command = 4242;
        TEventQueue.Enqueue(probe);

        TEvent got = default;
        TEventQueue.GetNextEvent(ref got);
        return got.What == Events.evBroadcast && got.message.command == 4242;
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// Builds and abandons an application in a method of its own, so no local in the caller's
    /// frame keeps it reachable once it returns.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbandonAnApplication()
    {
        var doomed = new TApplication();
        doomed.ShutDown();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbandonAnEventQueue() => _ = new TEventQueue();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbandonAScreen() => _ = new TScreen();

    // ── the reported bug ─────────────────────────────────────────────────────

    [Fact]
    public void FinalizingAnAbandonedApplicationLeavesTheSharedQueueWorking()
    {
        Assert.True(QueueDeliversEvents(), "precondition: the queue works to begin with");

        AbandonAnApplication();
        Collect();

        Assert.True(
            QueueDeliversEvents(),
            "finalizing an abandoned TApplication suspended the shared event queue");
    }

    [Fact]
    public void FinalizingAnAbandonedEventQueueLeavesTheSharedQueueWorking()
    {
        Assert.True(QueueDeliversEvents());

        AbandonAnEventQueue();
        Collect();

        Assert.True(QueueDeliversEvents());
    }

    [Fact]
    public void FinalizingAnAbandonedScreenLeavesTheDriverAlone()
    {
        Assert.True(QueueDeliversEvents());

        AbandonAScreen();
        Collect();

        // TScreen.Suspend drives the shared driver; the finalizer must not reach it.
        Assert.NotNull(TDisplay.driver);
        Assert.True(QueueDeliversEvents());
    }

    [Fact]
    public void RepeatedFinalizationRoundsDoNotDegradeTheQueue()
    {
        // The original flake needed an unlucky moment; hammering it makes the old behaviour
        // fail reliably rather than occasionally.
        for (int round = 0; round < 25; round++)
        {
            AbandonAnApplication();
            AbandonAnEventQueue();
            AbandonAScreen();
            Collect();

            Assert.True(QueueDeliversEvents(), $"queue stopped delivering after round {round}");
        }
    }

    // ── a live application is not disturbed by an unrelated finalization ─────

    private sealed class RecordingView : TView
    {
        public RecordingView(TRect bounds) : base(bounds)
        {
            options |= Views.ofSelectable;
            eventMask = 0xFFFF;
        }

        public List<ushort> Commands { get; } = new();

        public override void HandleEvent(ref TEvent ev)
        {
            if (ev.What == Events.evCommand)
            {
                Commands.Add(ev.message.command);
                ClearEvent(ref ev);
                return;
            }

            base.HandleEvent(ref ev);
        }
    }

    private sealed class LiveApp : TProgram
    {
        public RecordingView Target { get; private set; } = null!;

        public override TDeskTop InitDesktop(TRect r)
        {
            r.a.y += 1;
            r.b.y -= 1;

            var desktop = new TDeskTop(r);
            Target = new RecordingView(new TRect(0, 0, 10, 3));
            desktop.Insert(Target);
            return desktop;
        }
    }

    [Fact]
    public void ALiveApplicationKeepsReceivingEventsAcrossAnUnrelatedFinalization()
    {
        var live = new LiveApp();

        // Queued input, a keystroke and a targeted post are all in flight.
        var queued = new TEvent { What = Events.evBroadcast };
        queued.message.command = 4243;
        TEventQueue.Enqueue(queued);

        var key = new TEvent { What = Events.evKeyDown };
        key.keyDown.keyCode = Keys.kbDown;
        _driver.Driver.EnqueueKey(key);

        live.Target.Post(4244);

        AbandonAnApplication();
        Collect();

        // Ordinary queued event still arrives.
        TEvent first = default;
        live.GetEvent(ref first);
        Assert.Equal(Events.evBroadcast, first.What);
        Assert.Equal(4243, first.message.command);

        // Keyboard input still arrives.
        TEvent second = default;
        live.GetEvent(ref second);
        Assert.Equal(Events.evKeyDown, second.What);
        Assert.Equal(Keys.kbDown, second.keyDown.keyCode);

        // The targeted post was delivered along the way, exactly once.
        Assert.Equal(new ushort[] { 4244 }, live.Target.Commands);
        Assert.Equal(0, TEventQueue.PostedCount);
    }

    [Fact]
    public void UnrelatedFinalizationDoesNotClearPendingPostsOrTheUiThreadClaim()
    {
        var live = new LiveApp();
        int uiThreadBefore = TEventQueue.UiThreadId;

        live.Target.Post(4245);
        Assert.Equal(1, TEventQueue.PostedCount);

        AbandonAnApplication();
        Collect();

        // The abandoned instance owns none of this.
        Assert.Equal(1, TEventQueue.PostedCount);
        Assert.Equal(uiThreadBefore, TEventQueue.UiThreadId);
        Assert.True(TEventQueue.IsUiThread);

        Assert.True(TEventQueue.DeliverPostedEvent());
        Assert.Equal(new ushort[] { 4245 }, live.Target.Commands);
    }

    // ── deterministic disposal still does its job ────────────────────────────

    [Fact]
    public void DisposingAnApplicationExplicitlyStillSuspendsTheQueue()
    {
        var app = new TApplication();
        Assert.True(QueueDeliversEvents(), "a live application leaves the queue running");

        app.Dispose();

        // The contract kept from before: deterministic disposal stops input delivery.
        Assert.False(QueueDeliversEvents(), "explicit Dispose should have suspended the queue");

        TEventQueue.Resume();
        Assert.True(QueueDeliversEvents());
    }

    [Fact]
    public void DisposingAnEventQueueExplicitlyStillSuspendsIt()
    {
        var queue = new TEventQueue();
        Assert.True(QueueDeliversEvents());

        queue.Dispose();

        Assert.False(QueueDeliversEvents());

        TEventQueue.Resume();
    }

    [Fact]
    public void ProgramShutDownStillOwnsClearingPostedEvents()
    {
        var live = new LiveApp();
        live.Target.Post(4246);
        Assert.Equal(1, TEventQueue.PostedCount);

        live.ShutDown();

        // ShutDown remains the authoritative teardown for posted events.
        Assert.Equal(0, TEventQueue.PostedCount);
    }

    // ── repeated cleanup is safe ─────────────────────────────────────────────

    [Fact]
    public void RepeatedShutDownAndDisposeAreSafeInEveryCombination()
    {
        var a = new TApplication();
        a.ShutDown();
        Assert.Null(Record.Exception(a.ShutDown));

        var b = new TApplication();
        b.Dispose();
        Assert.Null(Record.Exception(b.Dispose));

        var c = new TApplication();
        c.ShutDown();
        Assert.Null(Record.Exception(c.Dispose));

        var d = new TApplication();
        d.Dispose();
        Assert.Null(Record.Exception(d.ShutDown));

        // Whatever the order, the shared queue can be brought back for the next application.
        TEventQueue.Resume();
        Assert.True(QueueDeliversEvents());
    }

    [Fact]
    public void DisposingSuppressesFinalizationSoNothingRunsLater()
    {
        var app = new TApplication();
        app.Dispose();
        TEventQueue.Resume();

        // Dispose() calls GC.SuppressFinalize, and the finalizers that used to touch shared
        // state are gone, so a later collection changes nothing.
        Collect();

        Assert.True(QueueDeliversEvents());
    }

    [Fact]
    public void AScreenDisposedTwiceOnlySuspendsOnce()
    {
        var screen = new TScreen();

        screen.Dispose();
        TEventQueue.Resume();

        Assert.Null(Record.Exception(screen.Dispose));
        Assert.NotNull(TDisplay.driver);
    }
}
