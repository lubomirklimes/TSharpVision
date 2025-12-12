// Source: derived from tvision driver contract (no upstream equivalent).
//
// NullDriver — synthetic driver used by the demo and unit tests. It owns a
// scriptable input queue so callers can pre-load TEvents and let the
// regular event loop consume them. No real I/O happens.
using System.Collections.Generic;
using TSharpVision;

namespace TSharpVision.Drivers;

[ScreenDriver(System = Platform.Windows, Driver = nameof(NullDriver), Priority = 0)]
[ScreenDriver(System = Platform.Linux,   Driver = nameof(NullDriver), Priority = 0)]
[ScreenDriver(System = Platform.MacOS,   Driver = nameof(NullDriver), Priority = 0)]
public sealed class NullDriver : IDriver
{
    private readonly Queue<TEvent> _scriptedKeys = new();
    private ushort _cursorType = 0;
    private ushort _cols;
    private ushort _rows;

    public NullDriver() : this(80, 25) { }

    public NullDriver(ushort cols, ushort rows)
    {
        _cols = cols;
        _rows = rows;
    }

    /// <summary>
    /// Push a synthetic keyboard event onto the queue; consumed FIFO by
    /// <see cref="ReadKeyEvent"/>.
    /// </summary>
    public void EnqueueKey(TEvent ev) => _scriptedKeys.Enqueue(ev);

    public void EnqueueCommand(ushort command)
    {
        var ev = new TEvent { What = TSharpVision.Constants.Events.evCommand };
        ev.message.command = command;
        TEventQueue.Enqueue(ev);
    }

    /// <summary>
    /// Simulates a host console resize event (mirrors what Win32ConsoleDriver
    /// does when it processes a WINDOW_BUFFER_SIZE_EVENT):
    /// updates driver dimensions, refreshes TScreen, reallocates
    /// TScreen.ScreenBuffer, and enqueues a cmScreenResized command.
    /// </summary>
    public void SimulateResize(ushort cols, ushort rows)
    {
        _cols = cols;
        _rows = rows;
        TScreen.ScreenWidth  = _cols;
        TScreen.ScreenHeight = _rows;
        TScreen.ScreenBuffer = AllocateScreenBuffer();
        TEvent resEv = default;
        resEv.What = TSharpVision.Constants.Events.evCommand;
        resEv.message.command = TSharpVision.Constants.Views.cmScreenResized;
        TEventQueue.Enqueue(resEv);
    }

    public void Initialize()
    {
        TScreen.ScreenWidth = _cols;
        TScreen.ScreenHeight = _rows;
    }
    public void Suspend() { }
    public void Resume() { }
    public void Shutdown() { }

    public ushort GetCols() => _cols;
    public ushort GetRows() => _rows;
    public TDisplay.SM GetScreenMode() => TDisplay.SM.CO80;
    public void SetScreenMode(TDisplay.SM mode) { }
    public ScreenBuffer AllocateScreenBuffer() => new ScreenBuffer(_cols, _rows);
    public void ClearScreen(ushort cols, ushort rows) { }

    public ushort GetCursorType() => _cursorType;
    public void SetCursorType(ushort cursorType) => _cursorType = cursorType;
    public void SetCaretPosition(int x, int y) { }

    public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf) { }
    public void MakeBeep() { }

    public void PumpMessages() { /* nothing — events are pushed manually */ }

    public bool ReadKeyEvent(out TEvent ev)
    {
        if (_scriptedKeys.Count > 0) { ev = _scriptedKeys.Dequeue(); return true; }
        ev = default;
        return false;
    }

    public bool SupportsMouse => false;
    public bool SupportsTrueColor => false;
}
