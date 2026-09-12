// Source: derived from tvision driver contract (no upstream equivalent).
//
// NullDriver — synthetic driver used by the demo and unit tests. It owns a
// scriptable input queue so callers can pre-load TEvents and let the
// regular event loop consume them. No real I/O happens.
using System.Collections.Generic;
using TSharpVision;

namespace TSharpVision.Drivers;

/// <summary>Headless driver with configurable cell dimensions and scripted input; rendering and host-device operations are no-ops.</summary>
[ScreenDriver(System = Platform.Windows, Driver = nameof(NullDriver), Priority = 0)]
[ScreenDriver(System = Platform.Linux,   Driver = nameof(NullDriver), Priority = 0)]
[ScreenDriver(System = Platform.MacOS,   Driver = nameof(NullDriver), Priority = 0)]
public sealed class NullDriver : IDriver
{
    private readonly Queue<TEvent> _scriptedKeys = new();
    private ushort _cursorType = 0;
    private ushort _cols;
    private ushort _rows;

    /// <summary>Creates a headless driver with an 80-by-25-cell display and empty input queue.</summary>
    public NullDriver() : this(80, 25) { }

    /// <summary>Creates a headless driver with the specified character-cell dimensions and empty input queue.</summary>
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

    /// <summary>Queues a command event on the shared application event queue.</summary>
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

    /// <inheritdoc /><remarks>Publishes the configured dimensions to shared screen state.</remarks>
    public void Initialize()
    {
        TScreen.ScreenWidth = _cols;
        TScreen.ScreenHeight = _rows;
    }
    /// <inheritdoc /><remarks>This headless implementation performs no host operation.</remarks>
    public void Suspend() { }
    /// <inheritdoc /><remarks>This headless implementation performs no host operation.</remarks>
    public void Resume() { }
    /// <inheritdoc /><remarks>This headless implementation performs no host operation.</remarks>
    public void Shutdown() { }

    /// <inheritdoc />
    public ushort GetCols() => _cols;
    /// <inheritdoc />
    public ushort GetRows() => _rows;
    /// <inheritdoc /><remarks>Always reports legacy color text mode CO80.</remarks>
    public TDisplay.SM GetScreenMode() => TDisplay.SM.CO80;
    /// <inheritdoc /><remarks>This headless implementation performs no host operation.</remarks>
    public void SetScreenMode(TDisplay.SM mode) { }
    /// <inheritdoc />
    public ScreenBuffer AllocateScreenBuffer() => new ScreenBuffer(_cols, _rows);
    /// <inheritdoc /><remarks>This headless implementation performs no host operation.</remarks>
    public void ClearScreen(ushort cols, ushort rows) { }

    /// <inheritdoc />
    public ushort GetCursorType() => _cursorType;
    /// <inheritdoc />
    public void SetCursorType(ushort cursorType) => _cursorType = cursorType;
    /// <inheritdoc /><remarks>This headless implementation performs no host operation.</remarks>
    public void SetCaretPosition(int x, int y) { }

    /// <inheritdoc /><remarks>This headless implementation performs no host operation.</remarks>
    public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf) { }
    /// <inheritdoc /><remarks>This headless implementation performs no host operation.</remarks>
    public void MakeBeep() { }

    /// <inheritdoc /><remarks>This headless implementation performs no host operation.</remarks>
    public void PumpMessages() { /* nothing — events are pushed manually */ }

    /// <inheritdoc /><remarks>Consumes the next scripted key FIFO; returns false and a default event when empty.</remarks>
    public bool ReadKeyEvent(out TEvent ev)
    {
        if (_scriptedKeys.Count > 0) { ev = _scriptedKeys.Dequeue(); return true; }
        ev = default;
        return false;
    }

    /// <inheritdoc />
    public bool SupportsMouse    => false;
    /// <inheritdoc />
    public bool SupportsTrueColor => false;
    /// <inheritdoc />
    public bool SupportsGraphics  => false;
}
