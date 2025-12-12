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

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            Suspend();

            disposedValue = true;
        }
    }

    // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    ~TEventQueue()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}