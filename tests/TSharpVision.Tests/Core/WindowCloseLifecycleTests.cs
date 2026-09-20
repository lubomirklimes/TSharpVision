using TSharpVision;
using TSharpVision.Constants;
using Xunit;

namespace TSharpVision.Tests.Core;

// Original: Borland TWINDOW.CPP Close reaches shutDown through TVOBJS.H TObject::destroy.
// Corrected: managed Close validates, notifies, then performs idempotent recursive shutdown/detach.
public sealed class WindowCloseLifecycleTests
{
    [Fact]
    public void SuccessfulCloseNotifiesThenShutsDownChildrenAndDetaches()
    {
        var owner = new RecordingGroup();
        var window = new ValidatingWindow();
        var child = new LifecycleView();
        window.Insert(child);
        owner.Insert(window);
        owner.OnPutEvent = () =>
        {
            Assert.Same(owner, window.owner);
            Assert.NotNull(window.frame);
            Assert.Equal(0, child.ShutDownCalls);
        };

        window.Close();

        TEvent notification = Assert.Single(owner.Events);
        Assert.Equal(Events.evBroadcast, notification.What);
        Assert.Equal(Views.cmClosingWindow, notification.message.command);
        Assert.Same(window, notification.message.infoPtr);
        Assert.Null(window.owner);
        Assert.Null(window.last);
        Assert.Null(window.frame);
        Assert.Null(child.owner);
        Assert.Equal(1, child.ShutDownCalls);
        Assert.Equal(0, child.DisposeCalls);
    }

    [Fact]
    public void RejectedValidationLeavesWindowAndChildrenFullyAlive()
    {
        var owner = new RecordingGroup();
        var window = new ValidatingWindow { AllowClose = false };
        var child = new LifecycleView();
        window.Insert(child);
        owner.Insert(window);

        window.Close();

        Assert.Same(owner, window.owner);
        Assert.Same(window, child.owner);
        Assert.NotNull(window.frame);
        Assert.Equal(0, child.ShutDownCalls);
        Assert.Equal(0, child.DisposeCalls);
        Assert.Empty(owner.Events);
        Assert.Equal(1, window.Validations);
    }

    [Fact]
    public void CloseTwiceShutsDownAndNotifiesOnlyOnce()
    {
        var owner = new RecordingGroup();
        var window = new ValidatingWindow();
        var child = new LifecycleView();
        window.Insert(child);
        owner.Insert(window);

        window.Close();
        window.Close();

        Assert.Single(owner.Events);
        Assert.Equal(1, child.ShutDownCalls);
        Assert.Equal(1, window.Validations);
    }

    [Fact]
    public void NestedOwnedChildrenShutDownExactlyOnceWithoutDispose()
    {
        var owner = new RecordingGroup();
        var window = new ValidatingWindow();
        var nested = new LifecycleGroup();
        var leaf = new LifecycleView();
        nested.Insert(leaf);
        window.Insert(nested);
        owner.Insert(window);

        window.Close();
        window.ShutDown();

        Assert.Equal(1, nested.ShutDownCalls);
        Assert.Equal(1, leaf.ShutDownCalls);
        Assert.Equal(0, nested.DisposeCalls);
        Assert.Equal(0, leaf.DisposeCalls);
    }

    [Fact]
    public void ClosingOneWindowLeavesSiblingOwnedAndOperational()
    {
        var owner = new RecordingGroup();
        var closing = new ValidatingWindow();
        var sibling = new ValidatingWindow();
        owner.Insert(closing);
        owner.Insert(sibling);

        closing.Close();

        Assert.Null(closing.owner);
        Assert.Same(owner, sibling.owner);
        Assert.Same(sibling, owner.FirstThat((view, sought) => ReferenceEquals(view, sought), sibling));
    }

    [Fact]
    public void ClosedWindowIsAbsentFromLaterEventTraversal()
    {
        var owner = new RecordingGroup();
        var window = new EventTrackingWindow();
        owner.Insert(window);
        window.Close();
        TEvent broadcast = new() { What = Events.evBroadcast };
        broadcast.message.command = 0x7FFE;

        owner.HandleEvent(ref broadcast);

        Assert.Equal(0, window.EventsAfterClose);
    }

    [Fact]
    public void RemoveOnlyDetachesAndAllowsReinsertionWithoutLifecycleCleanup()
    {
        var firstOwner = new RecordingGroup();
        var secondOwner = new RecordingGroup();
        var window = new ValidatingWindow();
        var child = new LifecycleView();
        window.Insert(child);
        firstOwner.Insert(window);

        firstOwner.Remove(window);
        secondOwner.Insert(window);

        Assert.Same(secondOwner, window.owner);
        Assert.Same(window, child.owner);
        Assert.NotNull(window.frame);
        Assert.Equal(0, child.ShutDownCalls);
        Assert.Equal(0, child.DisposeCalls);
    }

    [Fact]
    public void ModalCloseCommandEndsThroughCancelWithoutDestroyingWindow()
    {
        var owner = new RecordingGroup();
        var window = new ValidatingWindow();
        var child = new LifecycleView();
        window.Insert(child);
        owner.Insert(window);
        window.SetState(Views.sfModal, true);
        TEvent close = new() { What = Events.evCommand };
        close.message.command = Views.cmClose;
        close.message.infoPtr = window;

        window.HandleEvent(ref close);

        TEvent cancel = Assert.Single(owner.Events);
        Assert.Equal(Events.evCommand, cancel.What);
        Assert.Equal(Views.cmCancel, cancel.message.command);
        Assert.Same(owner, window.owner);
        Assert.NotNull(window.frame);
        Assert.Equal(0, child.ShutDownCalls);
    }

    [Fact]
    public void OwnerShutdownAfterPriorCloseDoesNotCleanClosedChildrenAgain()
    {
        var owner = new RecordingGroup();
        var window = new ValidatingWindow();
        var child = new LifecycleView();
        window.Insert(child);
        owner.Insert(window);
        window.Close();

        owner.ShutDown();

        Assert.Equal(1, child.ShutDownCalls);
        Assert.Equal(0, child.DisposeCalls);
    }

    [Fact]
    public void ReentrantCloseDuringNotificationHasNoDuplicateEffects()
    {
        var owner = new RecordingGroup();
        var window = new ValidatingWindow();
        var child = new LifecycleView();
        window.Insert(child);
        owner.Insert(window);
        owner.OnPutEvent = window.Close;

        window.Close();

        Assert.Single(owner.Events);
        Assert.Equal(1, child.ShutDownCalls);
        Assert.Equal(1, window.Validations);
    }

    private class RecordingGroup : TGroup
    {
        internal RecordingGroup() : base(new TRect(0, 0, 80, 25)) { }

        internal List<TEvent> Events { get; } = [];
        internal Action? OnPutEvent { get; set; }

        public override void PutEvent(ref TEvent ev)
        {
            Events.Add(ev);
            OnPutEvent?.Invoke();
        }
    }

    private class ValidatingWindow : TWindow
    {
        internal ValidatingWindow() : base(new TRect(1, 1, 40, 15), "Test", 0) { }

        internal bool AllowClose { get; set; } = true;
        internal int Validations { get; private set; }

        public override bool Valid(ushort command)
        {
            if (command == Views.cmClose)
            {
                Validations++;
                return AllowClose;
            }
            return base.Valid(command);
        }
    }

    private sealed class EventTrackingWindow : ValidatingWindow
    {
        private bool _closed;
        internal int EventsAfterClose { get; private set; }

        public override void Close()
        {
            base.Close();
            _closed = true;
        }

        public override void HandleEvent(ref TEvent @event)
        {
            if (_closed) EventsAfterClose++;
            base.HandleEvent(ref @event);
        }
    }

    private class LifecycleView : TView
    {
        internal LifecycleView() : base(new TRect(0, 0, 1, 1)) { }

        internal int ShutDownCalls { get; private set; }
        internal int DisposeCalls { get; private set; }

        public override void ShutDown()
        {
            ShutDownCalls++;
            base.ShutDown();
        }

        protected override void Dispose(bool disposing)
        {
            DisposeCalls++;
            base.Dispose(disposing);
        }
    }

    private sealed class LifecycleGroup : TGroup
    {
        internal LifecycleGroup() : base(new TRect(0, 0, 10, 5)) { }

        internal int ShutDownCalls { get; private set; }
        internal int DisposeCalls { get; private set; }

        public override void ShutDown()
        {
            ShutDownCalls++;
            base.ShutDown();
        }

        protected override void Dispose(bool disposing)
        {
            DisposeCalls++;
            base.Dispose(disposing);
        }
    }
}
