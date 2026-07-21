using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Drivers.SDL;
using TSharpVision.Drivers.SDL.Config;
using System.Runtime.InteropServices;
using System.Text;

namespace TSharpVision.Drivers.SDL.Gpu;

[ScreenDriver(System = Platform.Windows, Driver = nameof(SDLGpuDriver), Priority = 200)]
[ScreenDriver(System = Platform.Linux,   Driver = nameof(SDLGpuDriver), Priority = 200)]
[ScreenDriver(System = Platform.MacOS,   Driver = nameof(SDLGpuDriver), Priority = 200)]
public class SDLGpuDriver : IDisposable, IDriver
{
    private const int IdleWaitMs = 8;

    private ushort _cols = 120;
    private ushort _rows = 37;
    private ushort _cursorType = 100;
    private int    _caretX;
    private int    _caretY;
    private int    _cellWidth  = 8;
    private int    _cellHeight = 16;

    private bool _attached;
    private bool _dirty = true;
    private bool _disposedValue;
    private bool _diagnosticsEnabled;

    // Dirty-rect tracking (cell coordinates, inclusive bounds).
    // When _hasDirtyRegion is false the next render composites the full screen.
    private bool _hasDirtyRegion;
    private int  _dirtyX1, _dirtyY1, _dirtyX2, _dirtyY2;

    private IntPtr _window = IntPtr.Zero;
    private SDLGpuRenderer? _gpuRenderer;
    private ScreenBuffer? _screenBuffer;
    private readonly Queue<TEvent> _pendingKeys = new();

    // Mouse state
    private byte   _heldButtons;
    private ushort _lastModState;
    private readonly SdlMotionCoalescer _coalescer = new();

    public bool SupportsMouse     => true;
    public bool SupportsTrueColor => false;
    public bool SupportsGraphics  => true;

    public Action<IRenderer>? MessageLoop { get; set; }

    public void Initialize()
    {
        if (Environment.GetEnvironmentVariable("TSharpVision_NO_SDL") == "1") return;

        bool sdlInitialized = false;
        try
        {
            try
            {
                if (!SDL3.SDL.Init(SDL3.SDL.InitFlags.Video))
                    return;
                sdlInitialized = true;
            }
            catch (DllNotFoundException) { return; }

            var windowFlags = SDL3.SDL.WindowFlags.Resizable;

            string windowTitle = ScreenDriverFactory.WindowTitle ?? "TSharpVision";
            _window = SDL3.SDL.CreateWindow(
                windowTitle,
                _cols * _cellWidth,
                _rows * _cellHeight,
                windowFlags);

            if (_window == IntPtr.Zero)
            {
                SDL3.SDL.Quit();
                return;
            }

            var sdl      = ScreenDriverFactory.RegisterConfigSection<SdlDriverConfiguration>();
            var graphics = ScreenDriverFactory.Configuration?.Graphics;
            var options  = SDLGpuOptions.FromEnvironment(sdl);
            _diagnosticsEnabled = options.DiagnosticsEnabled;
            _gpuRenderer = new SDLGpuRenderer(
                _window, options,
                graphics?.FontName,
                graphics?.FontSize);
            _gpuRenderer.Initialize();
            _gpuRenderer.LogDiagnostics(_window);
            _gpuRenderer.StartRenderThread();

            _cellWidth  = _gpuRenderer.CellWidth;
            _cellHeight = _gpuRenderer.CellHeight;
            SDL3.SDL.SetWindowSize(_window, _cols * _cellWidth, _rows * _cellHeight);

            SDL3.SDL.StartTextInput(_window);
            ClipboardService.Current = new SdlClipboardService();

            _attached = true;
            TScreen.ScreenWidth  = _cols;
            TScreen.ScreenHeight = _rows;
        }
        catch
        {
            try { _gpuRenderer?.Dispose(); } catch { }
            _gpuRenderer = null;
            if (_window != IntPtr.Zero) { try { SDL3.SDL.DestroyWindow(_window); } catch { } _window = IntPtr.Zero; }
            if (sdlInitialized) { try { SDL3.SDL.Quit(); } catch { } }
            _attached = false;
        }
    }

    public ScreenBuffer AllocateScreenBuffer()
    {
        _screenBuffer = new ScreenBuffer(TScreen.ScreenWidth, TScreen.ScreenHeight);
        MessageLoop = r => r.Render(_screenBuffer, 0, 0, TScreen.ScreenWidth, TScreen.ScreenHeight);
        _hasDirtyRegion = false; // full composite required after reallocation
        return _screenBuffer;
    }

    public ushort GetCols()       => _cols;
    public ushort GetRows()       => _rows;
    public ushort GetCursorType() => _cursorType;

    public TDisplay.SM GetScreenMode() => TDisplay.SM.CO80;

    public void PumpMessages()
    {
        if (!_attached) return;

        // Render first — WriteBuf is called by the framework between PumpMessages
        // calls, so _dirty=true here means the screen buffer is already fresh.
        // Rendering before the event drain gives minimum WriteBuf→pixel latency,
        // matching the SDLDriver (SDLRenderer) approach.
        bool rendered = false;
        if (_dirty)
        {
            _dirty    = false;
            rendered  = true;
            if (MessageLoop != null && _gpuRenderer != null)
            {
                _hasDirtyRegion = false;
                MessageLoop.Invoke(_gpuRenderer);
            }
            else
                _gpuRenderer?.RenderFrame();
        }

        // Drain all pending SDL events. Motion events are accumulated into the
        // coalescer; a single TEvent for the latest position is flushed after
        // the drain so a burst of motion events never piles up in TEventQueue.
        bool hadSdlEvent = false;
        while (SDL3.SDL.PollEvent(out var e))
        {
            hadSdlEvent = true;
            ProcessSdlEvent(e);
        }
        FlushPendingMotion();

        // Idle wait only when nothing was rendered, no events arrived, and
        // nothing became dirty during the drain above.
        if (!rendered && !hadSdlEvent && !_dirty)
        {
            if (SDL3.SDL.WaitEventTimeout(out var waited, IdleWaitMs))
            {
                ProcessSdlEvent(waited);
                while (SDL3.SDL.PollEvent(out var e))
                    ProcessSdlEvent(e);
                FlushPendingMotion();
            }
        }
    }

    private void ProcessSdlEvent(SDL3.SDL.Event e)
    {
        var type = (SDL3.SDL.EventType)e.Type;
        switch (type)
        {
            case SDL3.SDL.EventType.Quit:
            case SDL3.SDL.EventType.WindowCloseRequested:
                FlushPendingMotion();
                TEventQueue.Enqueue(MakeQuitEvent());
                _dirty = true;
                break;

            case SDL3.SDL.EventType.KeyDown:
            {
                uint   kc  = (uint)e.Key.Key;
                ushort mod = (ushort)e.Key.Mod;
                _lastModState = mod;

                // Printable keys without Ctrl/LAlt: let SDL_TEXTINPUT deliver
                // the layout-correct character instead.
                bool hasCtrl         = (mod & SdlKeyTranslator.SDL_KMOD_CTRL) != 0;
                bool hasLAlt         = (mod & SdlKeyTranslator.SDL_KMOD_LALT) != 0;
                bool isPrintableRange = (kc >= 0x20 && kc <= 0x7E) || (kc >= 'a' && kc <= 'z');
                if (isPrintableRange && !hasCtrl && !hasLAlt)
                    break;

                FlushPendingMotion();
                if (SdlKeyTranslator.TryTranslate(kc, mod, '\0', out var kev))
                    _pendingKeys.Enqueue(kev);
                _dirty = true;
                break;
            }

            case SDL3.SDL.EventType.TextInput:
            {
                string? text = Marshal.PtrToStringUTF8(e.Text.Text);
                if (string.IsNullOrEmpty(text)) break;

                Rune rune = Rune.GetRuneAt(text, 0);
                char ch   = rune.Value <= 0xFFFF ? (char)rune.Value : text[0];

                ushort shift = SdlKeyTranslator.ToShiftState(_lastModState);
                TEvent kev = default;
                kev.What                      = Events.evKeyDown;
                kev.keyDown.keyCode           = (ushort)ch;
                kev.keyDown.charScan.charCode = rune.Value <= 0x7F ? (byte)rune.Value : (byte)0;
                kev.keyDown.shiftState        = shift;
                kev.keyDown.text              = text;

                FlushPendingMotion();
                _pendingKeys.Enqueue(kev);
                _dirty = true;
                break;
            }

            case SDL3.SDL.EventType.MouseButtonDown:
            case SDL3.SDL.EventType.MouseButtonUp:
            {
                var kind = type == SDL3.SDL.EventType.MouseButtonDown
                    ? SdlMouseEventKind.Down : SdlMouseEventKind.Up;
                var cell = SdlMouseTranslator.PixelToCell(
                    (int)e.Button.X, (int)e.Button.Y, _cellWidth, _cellHeight);
                var mev = SdlMouseTranslator.MakeEvent(
                    kind, e.Button.Button, cell.x, cell.y, e.Button.Clicks);
                if (kind == SdlMouseEventKind.Down)
                    _heldButtons |= mev.mouse.buttons;
                else
                    _heldButtons = 0;

                // Flush any pending motion before the button event so that
                // the TEventQueue ordering is: ...motion → button.
                FlushPendingMotion();
                TEventQueue.Enqueue(mev);
                _dirty = true;
                break;
            }

            case SDL3.SDL.EventType.MouseMotion:
                // Accumulate into coalescer — do NOT enqueue yet.
                // FlushPendingMotion() emits exactly one TEvent (latest position)
                // after the full drain, so a burst of motion events never piles up
                // in TEventQueue as individual entries.
                // Do NOT set _dirty here: mouse position alone does not change screen
                // content. The framework will call WriteBuf after processing the motion
                // event, which sets _dirty and triggers the correct render.
                _coalescer.Accumulate(
                    (int)e.Motion.X, (int)e.Motion.Y, _heldButtons);
                break;

            case SDL3.SDL.EventType.MouseWheel:
            {
                var cell = SdlMouseTranslator.PixelToCell(
                    (int)e.Wheel.MouseX, (int)e.Wheel.MouseY, _cellWidth, _cellHeight);
                FlushPendingMotion();
                if (SdlMouseTranslator.MakeWheelEvent(e.Wheel.Y, cell.x, cell.y, out var wev))
                    TEventQueue.Enqueue(wev);
                _dirty = true;
                break;
            }

            case SDL3.SDL.EventType.WindowResized:
                FlushPendingMotion();
                SDL3.SDL.GetWindowSizeInPixels(_window, out int pw, out int ph);
                HandleWindowResize(pw, ph);
                break;

            case SDL3.SDL.EventType.WindowExposed:
            case SDL3.SDL.EventType.WindowShown:
            case SDL3.SDL.EventType.WindowRestored:
                _hasDirtyRegion = false; // OS may have damaged the window — full repaint
                _dirty = true;
                break;

            default:
                if (_diagnosticsEnabled)
                    Console.Error.WriteLine(
                        $"[SDLGpu] unhandled SDL event type=0x{e.Type:X8} ({type})");
                break;
        }
    }

    private void ExpandDirtyRegion(int x, int y, int w, int h)
    {
        int x2 = x + w - 1;
        int y2 = y + h - 1;
        if (!_hasDirtyRegion)
        {
            _dirtyX1 = x;  _dirtyY1 = y;
            _dirtyX2 = x2; _dirtyY2 = y2;
            _hasDirtyRegion = true;
        }
        else
        {
            if (x  < _dirtyX1) _dirtyX1 = x;
            if (y  < _dirtyY1) _dirtyY1 = y;
            if (x2 > _dirtyX2) _dirtyX2 = x2;
            if (y2 > _dirtyY2) _dirtyY2 = y2;
        }
    }

    private void FlushPendingMotion()
    {
        if (!_coalescer.TryFlush(out int px, out int py, out byte held))
            return;
        var cell = SdlMouseTranslator.PixelToCell(px, py, _cellWidth, _cellHeight);
        var mev  = SdlMouseTranslator.MakeEvent(
            SdlMouseEventKind.Move, 0, cell.x, cell.y, heldButtons: held);
        TEventQueue.Enqueue(mev);
    }

    private void HandleWindowResize(int pixelW, int pixelH)
    {
        if (!_attached || _cellWidth <= 0 || _cellHeight <= 0) return;

        ushort newCols = (ushort)Math.Max(2, pixelW / _cellWidth);
        ushort newRows = (ushort)Math.Max(1, pixelH / _cellHeight);

        if (newCols == _cols && newRows == _rows) return;

        _cols = newCols;
        _rows = newRows;
        TScreen.ScreenWidth  = _cols;
        TScreen.ScreenHeight = _rows;
        TScreen.ScreenBuffer = AllocateScreenBuffer();
        _dirty = true;

        TEvent resEv = default;
        resEv.What = Events.evCommand;
        resEv.message.command = Views.cmScreenResized;
        TEventQueue.Enqueue(resEv);
    }

    internal static TEvent MakeQuitEvent()
    {
        TEvent ev = default;
        ev.What = Events.evCommand;
        ev.message.command = Views.cmQuit;
        return ev;
    }

    public void SetCursorType(ushort cursorType)
    {
        ExpandDirtyRegion(_caretX, _caretY, 1, 1); // cursor cell changes appearance
        _cursorType = cursorType;
        _gpuRenderer?.SetCursor(_caretX, _caretY, _cursorType);
        _dirty = true;
    }

    public void SetCaretPosition(int x, int y)
    {
        ExpandDirtyRegion(_caretX, _caretY, 1, 1); // erase cursor at old position
        _caretX = x;
        _caretY = y;
        ExpandDirtyRegion(_caretX, _caretY, 1, 1); // draw cursor at new position
        _gpuRenderer?.SetCursor(_caretX, _caretY, _cursorType);
        _dirty = true;
    }

    public void Suspend() { }
    public void Resume()  { if (!_attached) Initialize(); }

    public void Shutdown()
    {
        if (!_attached) return;
        try { _gpuRenderer?.Dispose(); } catch { }
        _gpuRenderer = null;
        if (_window != IntPtr.Zero) SDL3.SDL.StopTextInput(_window);
        if (_window != IntPtr.Zero) { SDL3.SDL.DestroyWindow(_window); _window = IntPtr.Zero; }
        SDL3.SDL.Quit();
        _attached = false;
        ClipboardService.Reset();
    }

    public void SetScreenMode(TDisplay.SM mode) { }

    public void ClearScreen(ushort cols, ushort rows)
    {
        if (!_attached || _screenBuffer == null) return;
        var blank = new TScreenChar { Character = ' ', Attr = new TColorAttr(0x07) };
        for (uint y = 0; y < rows; y++)
            for (uint x = 0; x < cols; x++)
                _screenBuffer.SetChar(x, y, blank);
        _hasDirtyRegion = false; // entire screen cleared → full composite
        _dirty = true;
    }

    public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf)
    {
        if (!_attached || _screenBuffer == null) return;
        for (int row = 0; row < h; row++)
            for (int col = 0; col < w; col++)
                _screenBuffer.SetChar((uint)(x + col), (uint)(y + row), buf[row * w + col]);
        ExpandDirtyRegion(x, y, w, h);
        _dirty = true;
    }

    public void MakeBeep()
    {
        try { Console.Write('\a'); } catch { }
    }

    public bool ReadKeyEvent(out TEvent ev)
    {
        if (_pendingKeys.Count > 0) { ev = _pendingKeys.Dequeue(); return true; }
        ev = default;
        return false;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing) { /* managed state disposed via Shutdown */ }
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
