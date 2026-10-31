// TV-1.1-PENDING-1. TProgram.Pending is one process-wide slot. An event put there belongs to the program whose
// PutEvent wrote it, and it must not outlive that program: a later program in the same process starts without it.
// Inside one program the slot keeps its classic behaviour, nested modal loops included. Nothing here clears the
// slot by hand; every assertion reads what the framework itself left behind.
using TSharpVision.Constants;
using TSharpVision.Tests.Infrastructure;
using Xunit;

namespace TSharpVision.Tests.Core;

[Collection("NonParallel")]
public sealed class ProgramPendingLifecycleTests : IDisposable
{
    private const ushort cmProbe = 1000;
    private const ushort TimedOut = 9999;

    private readonly DriverScope _driver = new();
    private readonly List<TProgram> _programs = new();

    public ProgramPendingLifecycleTests()
    {
        TEventQueue.Resume();
        TEventQueue.ClearPosted();
    }

    public void Dispose()
    {
        foreach (TProgram program in _programs)
            program.ShutDown();
        TEventQueue.ClearPosted();
        _driver.Dispose();
    }

    private T Track<T>(T program) where T : TProgram
    {
        _programs.Add(program);
        return program;
    }

    private static TEvent Key(ushort keyCode)
    {
        var ev = new TEvent { What = Events.evKeyDown };
        ev.keyDown.keyCode = keyCode;
        ev.keyDown.charScan = new CharScanType(keyCode);
        return ev;
    }

    private static TEvent Command(ushort command)
    {
        var ev = new TEvent { What = Events.evCommand };
        ev.message.command = command;
        return ev;
    }

    /// <summary>
    /// The H2 route: Esc reaches a dialog in the program's desktop, TDialog turns it into cmCancel and posts it up the
    /// owner chain into the program's pending slot, and nothing reads it before the program ends.
    /// </summary>
    private static TDialog LeaveCancelPending(TProgram program)
    {
        var dialog = new TDialog(new TRect(2, 2, 40, 12), "Stale");
        program.DeskTop!.Insert(dialog);
        TEvent esc = Key(Keys.kbEsc);
        dialog.HandleEvent(ref esc);
        Assert.Equal(Events.evNothing, esc.What);
        return dialog;
    }

    private static void AssertPending(ushort what, ushort command)
    {
        Assert.Equal(what, TestProgram.PendingEvent.What);
        Assert.Equal(command, TestProgram.PendingEvent.message.command);
    }

    private static TEvent ReadOne(TProgram program)
    {
        TEvent ev = default;
        program.GetEvent(ref ev);
        return ev;
    }

    /// <summary>A modal dialog that ends itself with <see cref="cmProbe"/> once its loop reads nothing.</summary>
    private sealed class IdleEndingDialog : TDialog
    {
        public readonly List<ushort> Commands = new();
        private int _idle;

        public IdleEndingDialog() : base(new TRect(4, 4, 44, 14), "Fresh") { }

        public override void GetEvent(ref TEvent ev)
        {
            base.GetEvent(ref ev);
            if (ev.What == Events.evCommand) Commands.Add(ev.message.command);
            if (ev.What != Events.evNothing) _idle = 0;
            else if (++_idle == 1) EndModal(cmProbe);
            else if (_idle > 200) EndModal(TimedOut);
        }
    }

    /// <summary>A modal dialog that, like a menu choosing an item, posts a command for its owner and then ends.</summary>
    private sealed class PostingDialog(ushort command) : TDialog(new TRect(4, 4, 44, 14), "Post")
    {
        private int _idle;

        public override void GetEvent(ref TEvent ev)
        {
            base.GetEvent(ref ev);
            if (ev.What != Events.evNothing) { _idle = 0; return; }
            if (++_idle == 1)
            {
                TEvent post = Command(command);
                PutEvent(ref post);
                EndModal(Views.cmOK);
            }
            else if (_idle > 200) EndModal(TimedOut);
        }
    }

    // ── baseline reproduction ─────────────────────────────────────

    [Fact]
    public void StaleCancel_FromProgramShutDown_DoesNotReachNextProgram()
    {
        var a = new TApplication();
        TDialog dialog = LeaveCancelPending(a);
        AssertPending(Events.evCommand, Views.cmCancel);
        dialog.ShutDown();
        a.ShutDown();

        var b = Track(new TApplication());
        TEvent first = ReadOne(b);

        Assert.Equal(Events.evNothing, first.What);
    }

    [Fact]
    public void StaleCancel_FromProgramDispose_DoesNotReachNextProgram()
    {
        // The exact H2 teardown: the fixture shut the dialog down and disposed the application, without ShutDown.
        var a = new TApplication();
        TDialog dialog = LeaveCancelPending(a);
        AssertPending(Events.evCommand, Views.cmCancel);
        dialog.ShutDown();
        a.Dispose();

        var b = Track(new TApplication());
        TEvent first = ReadOne(b);

        Assert.Equal(Events.evNothing, first.What);
    }

    [Fact]
    public void StaleCancel_DoesNotEndNextProgramsFirstModalDialog()
    {
        var a = new TApplication();
        LeaveCancelPending(a).ShutDown();
        a.Dispose();

        var b = Track(new TApplication());
        var dialog = new IdleEndingDialog();
        ushort result = b.DeskTop!.ExecView(dialog);
        dialog.ShutDown();

        Assert.Equal(cmProbe, result);
        Assert.DoesNotContain(Views.cmCancel, dialog.Commands);
    }

    [Fact]
    public void RepeatedProgramCycles_StayIsolated()
    {
        for (int cycle = 0; cycle < 6; cycle++)
        {
            var program = new TApplication();
            Assert.Equal(Events.evNothing, ReadOne(program).What);

            // Its own post still arrives, once.
            TEvent own = Command((ushort)(cmProbe + cycle));
            program.PutEvent(ref own);
            TEvent got = ReadOne(program);
            Assert.Equal(Events.evCommand, got.What);
            Assert.Equal((ushort)(cmProbe + cycle), got.message.command);
            Assert.Equal(Events.evNothing, ReadOne(program).What);

            // Then it ends with a cancel left over, alternating the two teardown routes.
            LeaveCancelPending(program).ShutDown();
            if (cycle % 2 == 0) program.ShutDown();
            else program.Dispose();
            Assert.Equal(Events.evNothing, TestProgram.PendingEvent.What);
        }

        var last = Track(new TApplication());
        Assert.Equal(Events.evNothing, ReadOne(last).What);
    }

    [Fact]
    public void ShutDownWithoutPendingEvent_LeavesNextProgramClean()
    {
        var a = new TApplication();
        Assert.Equal(Events.evNothing, TestProgram.PendingEvent.What);
        a.ShutDown();

        var b = Track(new TApplication());
        Assert.Equal(Events.evNothing, ReadOne(b).What);
    }

    [Fact]
    public void RepeatedShutDown_IsHarmless()
    {
        var a = new TApplication();
        LeaveCancelPending(a).ShutDown();
        a.ShutDown();
        a.ShutDown();
        a.Dispose();

        var b = Track(new TApplication());
        Assert.Equal(Events.evNothing, ReadOne(b).What);
    }

    [Fact]
    public void PutEventOnEndedProgram_DoesNotReachNextProgram()
    {
        var a = new TApplication();
        a.ShutDown();
        TEvent late = Command(Views.cmCancel);
        a.PutEvent(ref late);

        var b = Track(new TApplication());
        Assert.Equal(Events.evNothing, ReadOne(b).What);
    }

    // ── one program: unchanged behaviour ─────────────────────────

    [Fact]
    public void PendingEvent_DeliveredExactlyOnce()
    {
        var program = Track(new TApplication());
        TEvent post = Command(cmProbe);
        program.PutEvent(ref post);

        TEvent first = ReadOne(program);
        TEvent second = ReadOne(program);

        Assert.Equal(Events.evCommand, first.What);
        Assert.Equal(cmProbe, first.message.command);
        Assert.Equal(Events.evNothing, second.What);
    }

    [Fact]
    public void CancelPostedBeforeModal_EndsThatProgramsModalDialog()
    {
        // Same program, same slot: a cancel that is still pending is a legitimate event for the next loop that reads.
        var program = Track(new TApplication());
        TEvent cancel = Command(Views.cmCancel);
        program.PutEvent(ref cancel);

        var dialog = new IdleEndingDialog();
        ushort result = program.DeskTop!.ExecView(dialog);
        dialog.ShutDown();

        Assert.Equal(Views.cmCancel, result);
    }

    [Fact]
    public void EventPostedInNestedModalLoop_SurvivesModalExit()
    {
        var program = Track(new TApplication());
        var dialog = new PostingDialog(cmProbe);

        ushort result = program.DeskTop!.ExecView(dialog);
        dialog.ShutDown();

        Assert.Equal(Views.cmOK, result);
        TEvent after = ReadOne(program);
        Assert.Equal(Events.evCommand, after.What);
        Assert.Equal(cmProbe, after.message.command);
        Assert.Equal(Events.evNothing, ReadOne(program).What);
    }

    [Fact]
    public void EventPostedInDoublyNestedModalLoop_ReachesOuterModalLoop()
    {
        var program = Track(new TApplication());
        var inner = new PostingDialog(cmProbe);
        var outer = new OuterDialog(inner);

        ushort result = program.DeskTop!.ExecView(outer);
        outer.ShutDown();

        Assert.Equal(cmProbe, result);
        Assert.Equal(Events.evNothing, ReadOne(program).What);
    }

    /// <summary>Runs a nested dialog on its first idle pass and ends itself with the first command it then reads.</summary>
    private sealed class OuterDialog(TDialog inner) : TDialog(new TRect(2, 2, 60, 20), "Outer")
    {
        private bool _ranInner;
        private int _idle;

        public override void GetEvent(ref TEvent ev)
        {
            base.GetEvent(ref ev);
            if (ev.What == Events.evCommand && ev.message.command == cmProbe)
            {
                ClearEvent(ref ev);
                EndModal(cmProbe);
                return;
            }
            if (ev.What != Events.evNothing) return;
            if (!_ranInner)
            {
                _ranInner = true;
                ExecView(inner);
                inner.ShutDown();
            }
            else if (++_idle > 200) EndModal(TimedOut);
        }
    }

    // ── other live programs and the loop itself ──────────────────

    [Fact]
    public void EndingOneProgram_KeepsPendingEventOfAnotherLiveProgram()
    {
        // One application at a time is the documented model; this only shows the cleanup is owner-scoped and never
        // takes an event a different, still-live program wrote.
        var a = new TApplication();
        var b = Track(new TApplication());
        TEvent post = Command(cmProbe);
        b.PutEvent(ref post);

        a.ShutDown();
        a.Dispose();

        TEvent got = ReadOne(b);
        Assert.Equal(Events.evCommand, got.What);
        Assert.Equal(cmProbe, got.message.command);
    }

    /// <summary>A program that reports when its loop first reaches an idle pass.</summary>
    private sealed class SignalingProgram : TApplication
    {
        public readonly ManualResetEventSlim ReachedIdle = new(false);
        public override void Idle()
        {
            base.Idle();
            ReachedIdle.Set();
        }
    }

    [Fact]
    public void ReplacementProgram_LoopWakesForCrossThreadPost()
    {
        var a = new TApplication();
        LeaveCancelPending(a).ShutDown();
        a.ShutDown();

        var b = Track(new SignalingProgram());
        Exception? workerError = null;
        var worker = new Thread(() =>
        {
            try
            {
                if (!b.ReachedIdle.Wait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("Program B never reached an idle pass.");
                b.Post(Views.cmQuit);
            }
            catch (Exception ex) { workerError = ex; }
        }) { IsBackground = true };
        worker.Start();

        b.Run();
        Assert.True(worker.Join(TimeSpan.FromSeconds(30)));

        Assert.Null(workerError);
        Assert.Equal(Views.cmQuit, b.endState);
    }
}
