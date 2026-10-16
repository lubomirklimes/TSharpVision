using NativeSdl = global::SDL3.SDL;

namespace TSharpVision.Drivers.SDL;

// A managed signal is bridged to one persistent SDL user event. Registration never
// dispatches UI code. Cleanup joins callbacks before native resources may be released.
internal sealed class SdlIdleWait
{
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _finished = new(true);
    private bool _stopped;
    private bool _waiting;
    private uint _eventType;
    private string? _wakeError;

    internal void Initialize()
    {
        lock (_gate)
        {
            _stopped = false;
            _wakeError = null;
            _eventType = NativeSdl.RegisterEvents(1);
            if (_eventType == 0 || _eventType == uint.MaxValue) throw new InvalidOperationException(NativeSdl.GetError());
        }
    }

    private void PushWake()
    {
        var ev = new NativeSdl.Event { Type = _eventType };
        if (!NativeSdl.PushEvent(ref ev))
            Volatile.Write(ref _wakeError, $"SDL managed wake failed: {NativeSdl.GetError()}");
    }

    internal void Wait(int timeout, WaitHandle signal, Action<NativeSdl.Event> accept)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);
            _waiting = true;
            _finished.Reset();
        }
        RegisteredWaitHandle? registration = null;
        var callbackGate = new object();
        bool callbackActive = true;
        try
        {
            // Bounded native safety deadline detects a filtered/failed user-event wake.
            // Normal idle/input/posts interrupt this wait immediately.
            registration = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) =>
            {
                lock (callbackGate) { if (callbackActive) PushWake(); }
            }, null, -1, true);
            NativeSdl.ClearError();
            if (NativeSdl.WaitEventTimeout(out var ev, timeout < 0 ? 1000 : Math.Min(timeout, 1000)))
            {
                if (ev.Type != _eventType) accept(ev);
            }
            else if (NativeSdl.GetError() is { Length: > 0 } error)
                throw new InvalidOperationException($"SDL idle wait failed: {error}");
        }
        finally
        {
            if (registration != null)
            {
                registration.Unregister(null);
                lock (callbackGate) callbackActive = false;
            }
            lock (_gate) { _waiting = false; _finished.Set(); }
        }
        if (Volatile.Read(ref _wakeError) is string wakeError) throw new InvalidOperationException(wakeError);
        ObjectDisposedException.ThrowIf(_stopped, this);
    }

    internal void ThrowIfStopped() { lock (_gate) ObjectDisposedException.ThrowIf(_stopped, this); }

    internal void Stop()
    {
        bool waiting;
        lock (_gate) { _stopped = true; waiting = _waiting; }
        if (waiting) { PushWake(); _finished.Wait(); }
    }
}
