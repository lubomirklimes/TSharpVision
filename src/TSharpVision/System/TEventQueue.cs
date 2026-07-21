using TSharpVision.Constants;

namespace TSharpVision;

public class TEventQueue : IDisposable
{
    // dblclick 500 ms (C++ had doubleDelay = 8 ticks ≈ 8*55ms)
    public static int DoubleDelay = 500;
    public static bool MouseReverse = false;

    public static int RepeatDelay = 500;
    public static int AutoDelay = 0;

    private static readonly object _lock = new object();
    private static readonly Queue<TEvent> _queue = new Queue<TEvent>();

    // Posted events live in their own queue rather than in _queue. An event taken from
    // _queue is handed to whichever group currently owns the event loop, so a modal view
    // running through TGroup.ExecView would receive — and drop — anything meant for a view
    // underneath it. Keeping posts separate lets DeliverPostedEvent hand them straight to
    // their target and leaves ordinary event semantics untouched.
    private static readonly object _postLock = new object();
    private static readonly Queue<TPostedEvent> _posts = new Queue<TPostedEvent>();

    private static MouseEventType _lastMouse;
    private static MouseEventType _downMouse;
    private static int _downTime;
    private static int _autoTime;

    private static bool _mouseEvents = false;
    private bool disposedValue;

    private static int CurrentTick => Environment.TickCount & Int32.MaxValue;

    public TEventQueue()
    {
        Resume();
    }

    public static void Resume()
    {
        _mouseEvents = true;
        // Inicializovat stav
        _lastMouse = default;
        _downMouse = default;
        _downTime = CurrentTick;
        _autoTime = CurrentTick;
    }

    public static void Suspend()
    {
        _mouseEvents = false;
    }

    public static void Enqueue(TEvent ev)
    {
        lock (_lock)
        {
            _queue.Enqueue(ev);
        }
    }

    // ── posted events ────────────────────────────────────────────────────────

    /// <summary>
    /// Managed id of the thread that runs the event loop, or 0 before one is known.
    ///
    /// Established by <see cref="TProgram"/> when it is constructed and when it starts
    /// running, and otherwise by the first <see cref="DeliverPostedEvent"/> call. Posted
    /// events are only ever delivered on this thread.
    /// </summary>
    public static int UiThreadId { get; private set; }

    /// <summary>True when the caller is running on the event-loop thread.</summary>
    public static bool IsUiThread => UiThreadId == Environment.CurrentManagedThreadId;

    /// <summary>Number of posted events still waiting to be delivered.</summary>
    public static int PostedCount
    {
        get { lock (_postLock) { return _posts.Count; } }
    }

    /// <summary>
    /// Records the calling thread as the event-loop thread.
    ///
    /// <see cref="TProgram"/> does this when it is constructed and when it starts running, so
    /// applications never need to call it. It is public for a host that drives the loop
    /// itself from a thread TSharpVision has not seen; call it from that thread, and only
    /// from it, before pumping <see cref="DeliverPostedEvent"/>.
    /// </summary>
    public static void ClaimUiThread() => UiThreadId = Environment.CurrentManagedThreadId;

    /// <summary>
    /// Queues <paramref name="command"/> for delivery to <paramref name="target"/>.
    /// Safe to call from any thread. Use <see cref="TView.Post"/> rather than this directly.
    /// </summary>
    internal static void Post(TView target, ushort command, IInfo? info)
    {
        ArgumentNullException.ThrowIfNull(target);

        lock (_postLock)
        {
            _posts.Enqueue(new TPostedEvent(target, command, info));
        }
    }

    /// <summary>
    /// Drops the pending posts whose target has left the view tree, releasing those
    /// references, and leaves every still-deliverable post alone.
    ///
    /// This is what an application runs as it shuts down: its own views are detached by then,
    /// so its posts go, while posts belonging to any other live application are untouched.
    /// Undeliverable posts would be discarded on their turn anyway; this just stops them
    /// holding their targets alive until a loop next runs.
    /// </summary>
    internal static void DropUndeliverablePosts()
    {
        lock (_postLock)
        {
            int count = _posts.Count;
            for (int i = 0; i < count; i++)
            {
                TPostedEvent post = _posts.Dequeue();
                if (post.Target.CanReceivePostedEvents)
                    _posts.Enqueue(post);
            }
        }
    }

    /// <summary>
    /// Drops every pending post without delivering it, releasing the target references —
    /// including posts belonging to other live applications, so this is a whole-process
    /// reset rather than per-application cleanup. Shutdown uses
    /// <see cref="DropUndeliverablePosts"/> instead.
    /// </summary>
    public static void ClearPosted()
    {
        lock (_postLock)
        {
            _posts.Clear();
        }
    }

    /// <summary>
    /// Delivers at most one posted event, as an <c>evCommand</c> handed straight to its
    /// target's <see cref="TView.HandleEvent"/>.
    ///
    /// <see cref="TProgram.GetEvent"/> calls this once per pass, so posts are delivered from
    /// every event loop — including the nested one a modal view runs through
    /// <see cref="TGroup.ExecView"/> — without that modal view ever seeing them.
    ///
    /// A host driving its own loop may call this directly. It refuses to do anything on any
    /// thread other than the event-loop thread, so view code never runs on a worker; the post
    /// simply stays queued until the loop picks it up.
    ///
    /// A post whose target has left the view tree is discarded rather than delivered.
    /// </summary>
    /// <returns><c>true</c> when a post was taken off the queue.</returns>
    public static bool DeliverPostedEvent()
    {
        if (UiThreadId == 0)
            ClaimUiThread();       // first thread to pump is the event-loop thread
        else if (!IsUiThread)
            return false;          // never run view code on a worker

        TPostedEvent post;
        lock (_postLock)
        {
            if (_posts.Count == 0) return false;
            post = _posts.Dequeue();
        }

        // Dispatch outside the lock: the handler may post again, or open a modal view whose
        // nested loop re-enters this method.
        if (!post.Target.CanReceivePostedEvents)
            return true;           // target is gone — taken off the queue and dropped

        TEvent ev = default;
        ev.What = Constants.Events.evCommand;
        ev.message.command = post.Command;
        ev.message.infoPtr = post.Info;
        post.Target.HandleEvent(ref ev);

        return true;
    }

    /// <summary>
    /// Pulls the next queued event into <paramref name="ev"/>. Mirrors
    /// upstream <c>TEventQueue::getMouseEvent</c> (mis-named upstream — it
    /// actually serves all queued events: mouse, broadcast and command).
    /// Returns <c>evNothing</c> when the queue is empty.
    /// </summary>
    public static void GetMouseEvent(ref TEvent ev)
    {
        GetNextEvent(ref ev);
    }

    public static void GetNextEvent(ref TEvent ev)
    {
        if (!_mouseEvents)
        {
            ev.What = Events.evNothing;
            return;
        }

        lock (_lock)
        {
            if (_queue.Count > 0)
            {
                ev = _queue.Dequeue();

                // For evMouseDown: run full click-detection to add doubleClick
                // and update _lastMouse, _downMouse, _downTime.
                // For evMouseUp / evMouseMove: only update _lastMouse so that
                // the next evMouseDown sees buttons==0 and correctly
                // recognises the transition (otherwise a second drag never
                // starts because _lastMouse.buttons stays non-zero after the
                // first drag ends).
                if (ev.What == Events.evMouseDown)
                    HandleClickDetection(ref ev);
                else if (ev.What == Events.evMouseWheel)
                {
                    // wheel events carry wheel direction in
                    // mouse.buttons (mbButton4/mbButton5), NOT real button
                    // state. Do NOT update _lastMouse or the next real
                    // evMouseDown would see non-zero buttons and fail to
                    // detect the DOWN transition correctly.
                }
                else if ((ev.What & Events.evMouse) != 0)
                    _lastMouse = ev.mouse;  // keep state tracking current

                return;
            }
        }

        ev.What = Events.evNothing;
    }

    private static void HandleClickDetection(ref TEvent ev)
    {
        var m = ev.mouse;
        int now = CurrentTick;

        if (m.buttons == 0 && _lastMouse.buttons != 0)
        {
            ev.What = Events.evMouseUp;
            _lastMouse = m;
            return;
        }

        if (m.buttons != 0 && _lastMouse.buttons == 0)
        {
            // double‑click?
            if (m.buttons == _downMouse.buttons
                && m.where == _downMouse.where
                && now - _downTime <= DoubleDelay)
            {
                m.doubleClick = true;
                ev.mouse.doubleClick = true;
            }

            _downMouse = m;
            _downTime = now;
            _autoTime = now + RepeatDelay;
            ev.What = Events.evMouseDown;
            _lastMouse = m;
            return;
        }

        if (m.where != _lastMouse.where)
        {
            ev.What = Events.evMouseMove;
            _lastMouse = m;
            return;
        }

        if (m.buttons != 0 && now >= _autoTime)
        {
            _autoTime = now + AutoDelay;
            ev.What = Events.evMouseAuto;
            _lastMouse = m;
            return;
        }

        ev.What = Events.evNothing;
    }

    /// <summary>
    /// Every field of the queue except <c>disposedValue</c> is static, so an instance owns
    /// nothing: disposal exists only to let an application deterministically stop input
    /// delivery when it is finished with the queue.
    ///
    /// <paramref name="disposing"/> gates that. Suspending is a change to process-wide state
    /// shared with every other live application, so it must only happen on a deterministic
    /// call — never from a finalizer, which runs at an arbitrary time on the finalizer thread
    /// and would suspend the queue out from under whoever is using it now.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposedValue) return;
        disposedValue = true;

        if (disposing)
            Suspend();
    }

    // No finalizer: there is no unmanaged resource to release, and the only thing
    // Dispose does is mutate shared static state, which a finalizer must not touch.

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}