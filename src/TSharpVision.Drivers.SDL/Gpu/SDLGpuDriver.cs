using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Drivers.SDL;
using TSharpVision.Drivers.SDL.Config;
using System.Runtime.InteropServices;
using System.Text;

namespace TSharpVision.Drivers.SDL.Gpu;

/// <summary>Windowed character-cell backend using SDL GPU rendering.</summary>
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
    private readonly SdlModifierTranslator _modifierTranslator = new();
    private readonly SdlHeldKeyTracker _heldKeys = new();

    // Mouse state
    private byte   _heldButtons;
    private ushort _lastModState;
    private readonly SdlMotionCoalescer _coalescer = new();

    /// <inheritdoc />
    public bool SupportsMouse     => true;
    /// <inheritdoc />
    public bool SupportsTrueColor => false;
    /// <inheritdoc />
    public bool SupportsGraphics  => true;
    /// <inheritdoc />
    public KeyboardCapabilities KeyboardCapabilities =>
        KeyboardCapabilities.KeyReleaseEvents | KeyboardCapabilities.StandaloneModifierTransitions;

    /// <summary>Optional rendering callback invoked while pumping messages; initialization installs a callback that renders the screen buffer.</summary>
    public Action<IRenderer>? MessageLoop { get; set; }

    /// <inheritdoc />
    public void Initialize() => Initialize(() =>
    {
        if (!SDL3.SDL.Init(SDL3.SDL.InitFlags.Video))
            throw new InvalidOperationException($"SDL_Init failed: {SDL3.SDL.GetError()}");
    });

    // Injectable first native operation for headless failure regression tests.
    internal void Initialize(Action initializeVideo)
    {
        if (_attached) return;
        if (Environment.GetEnvironmentVariable("TSharpVision_NO_SDL") == "1")
        {
            Console.Error.WriteLine("[SDL] Initialization explicitly disabled by TSharpVision_NO_SDL=1; driver remains detached.");
            return;
        }

        bool sdlInitialized = false;
        try
        {
            initializeVideo();
            sdlInitialized = true;

            var windowFlags = SDL3.SDL.WindowFlags.Resizable;

            string windowTitle = ScreenDriverFactory.WindowTitle ?? "TSharpVision";
            _window = SDL3.SDL.CreateWindow(
                windowTitle,
                _cols * _cellWidth,
                _rows * _cellHeight,
                windowFlags);

            if (_window == IntPtr.Zero)
            {
                throw new InvalidOperationException($"SDL window creation failed: {SDL3.SDL.GetError()}");
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
        catch (Exception ex)
        {
            try { _gpuRenderer?.Dispose(); } catch { }
            _gpuRenderer = null;
            if (_window != IntPtr.Zero) { try { SDL3.SDL.DestroyWindow(_window); } catch { } _window = IntPtr.Zero; }
            if (sdlInitialized) { try { SDL3.SDL.Quit(); } catch { } }
            _attached = false;
            throw new InvalidOperationException($"{GetType().Name} initialization failed: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public ScreenBuffer AllocateScreenBuffer()
    {
        _screenBuffer = new ScreenBuffer(TScreen.ScreenWidth, TScreen.ScreenHeight);
        MessageLoop = r => r.Render(_screenBuffer, 0, 0, TScreen.ScreenWidth, TScreen.ScreenHeight);
        _hasDirtyRegion = false; // full composite required after reallocation
        return _screenBuffer;
    }

    /// <inheritdoc />
    public ushort GetCols()       => _cols;
    /// <inheritdoc />
    public ushort GetRows()       => _rows;
    /// <inheritdoc />
    public ushort GetCursorType() => _cursorType;

    /// <inheritdoc />
    public TDisplay.SM GetScreenMode() => TDisplay.SM.CO80;

    /// <inheritdoc />
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
            case SDL3.SDL.EventType.KeyUp:
            {
                uint   kc  = (uint)e.Key.Key;
                ushort mod = (ushort)e.Key.Mod;
                _lastModState = mod;

                bool keyDown = type == SDL3.SDL.EventType.KeyDown;
                if (SdlModifierTranslator.IsModifierKey(kc))
                {
                    ProcessModifierKey(kc, keyDown);
                    break;
                }
                if (!keyDown)
                {
                    ProcessOrdinaryKeyUp(kc, mod);
                    break;
                }
                ProcessOrdinaryKeyDown(kc, mod);
                break;
            }

            case SDL3.SDL.EventType.WindowFocusLost:
                _lastModState = 0;
                ProcessModifierFocusLost();
                break;

            case SDL3.SDL.EventType.TextInput:
            {
                string? text = Marshal.PtrToStringUTF8(e.Text.Text);
                if (string.IsNullOrEmpty(text)) break;

                Rune rune = Rune.GetRuneAt(text, 0);
                char ch   = rune.Value <= 0xFFFF ? (char)rune.Value : text[0];

                uint shift = SdlKeyTranslator.ToShiftState(_lastModState);
                TEvent kev = default;
                kev.What                      = Events.evKeyDown;
                kev.keyDown.keyCode           = (ushort)ch;
                kev.keyDown.charScan.charCode = rune.Value <= 0x7F ? (byte)rune.Value : (byte)0;
                kev.keyDown.controlKeyState   = shift;
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
                    kind, e.Button.Button, cell.x, cell.y, e.Button.Clicks,
                    controlKeyState: _modifierTranslator.LogicalState);
                byte changedButton = SdlMouseTranslator.TranslateButton(e.Button.Button);
                if (kind == SdlMouseEventKind.Down)
                    _heldButtons |= changedButton;
                else
                    _heldButtons &= (byte)~changedButton;

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
                    (int)e.Motion.X, (int)e.Motion.Y, _heldButtons,
                    _modifierTranslator.LogicalState);
                break;

            case SDL3.SDL.EventType.MouseWheel:
            {
                var cell = SdlMouseTranslator.PixelToCell(
                    (int)e.Wheel.MouseX, (int)e.Wheel.MouseY, _cellWidth, _cellHeight);
                FlushPendingMotion();
                foreach (TEvent wev in SdlMouseTranslator.MakeWheelEvents(
                    e.Wheel.X, e.Wheel.Y,
                    e.Wheel.Direction == SDL3.SDL.MouseWheelDirection.Flipped,
                    cell.x, cell.y, _heldButtons, _modifierTranslator.LogicalState))
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
        if (!_coalescer.TryFlush(
                out int px, out int py, out byte held, out uint controlKeyState))
            return;
        var cell = SdlMouseTranslator.PixelToCell(px, py, _cellWidth, _cellHeight);
        var mev  = SdlMouseTranslator.MakeEvent(
            SdlMouseEventKind.Move, 0, cell.x, cell.y,
            heldButtons: held, controlKeyState: controlKeyState);
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

    /// <inheritdoc />
    public void SetCursorType(ushort cursorType)
    {
        ExpandDirtyRegion(_caretX, _caretY, 1, 1); // cursor cell changes appearance
        _cursorType = cursorType;
        _gpuRenderer?.SetCursor(_caretX, _caretY, _cursorType);
        _dirty = true;
    }

    /// <inheritdoc />
    public void SetCaretPosition(int x, int y)
    {
        ExpandDirtyRegion(_caretX, _caretY, 1, 1); // erase cursor at old position
        _caretX = x;
        _caretY = y;
        ExpandDirtyRegion(_caretX, _caretY, 1, 1); // draw cursor at new position
        _gpuRenderer?.SetCursor(_caretX, _caretY, _cursorType);
        _dirty = true;
    }

    /// <inheritdoc />
    public void Suspend() { }
    /// <inheritdoc />
    public void Resume()  { if (!_attached) Initialize(); }

    /// <inheritdoc />
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

    /// <summary>Accepts a logical screen-mode request without changing this backend's display mode.</summary>
    public void SetScreenMode(TDisplay.SM mode) { }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf)
    {
        if (!_attached || _screenBuffer == null) return;
        for (int row = 0; row < h; row++)
            for (int col = 0; col < w; col++)
                _screenBuffer.SetChar((uint)(x + col), (uint)(y + row), buf[row * w + col]);
        ExpandDirtyRegion(x, y, w, h);
        _dirty = true;
    }

    /// <inheritdoc />
    public void MakeBeep()
    {
        try { Console.Write('\a'); } catch { }
    }

    /// <inheritdoc />
    public bool ReadKeyEvent(out TEvent ev)
    {
        if (_pendingKeys.Count > 0) { ev = _pendingKeys.Dequeue(); return true; }
        ev = default;
        return false;
    }

    internal bool ProcessModifierKey(uint keycode, bool down)
    {
        if (!_modifierTranslator.TryTranslate(keycode, down, out TEvent ev)) return false;
        FlushPendingMotion();
        _pendingKeys.Enqueue(ev);
        _dirty = true;
        return true;
    }

    internal bool ProcessModifierFocusLost()
    {
        bool changed = false;
        foreach (TEvent release in _heldKeys.ReleaseAll(_modifierTranslator.LogicalState))
        {
            FlushPendingMotion();
            _pendingKeys.Enqueue(release);
            changed = true;
        }
        if (_modifierTranslator.TryReset(out TEvent modifierReset))
        {
            FlushPendingMotion();
            _pendingKeys.Enqueue(modifierReset);
            changed = true;
        }
        if (changed) _dirty = true;
        return changed;
    }

    internal bool ProcessOrdinaryKeyUp(uint keycode, ushort modifierState)
    {
        if (!_heldKeys.TryKeyUp(keycode, modifierState, out TEvent ev)) return false;
        FlushPendingMotion();
        _pendingKeys.Enqueue(ev);
        _dirty = true;
        return true;
    }

    internal bool ProcessOrdinaryKeyDown(uint keycode, ushort modifierState)
    {
        _heldKeys.KeyDown(keycode, modifierState);

        bool hasCtrl = (modifierState & SdlKeyTranslator.SDL_KMOD_CTRL) != 0;
        bool hasLAlt = (modifierState & SdlKeyTranslator.SDL_KMOD_LALT) != 0;
        bool printable = (keycode >= 0x20 && keycode <= 0x7E)
            || (keycode >= 'a' && keycode <= 'z');
        if (printable && !hasCtrl && !hasLAlt) return false;

        if (!SdlKeyTranslator.TryTranslate(keycode, modifierState, '\0', out TEvent ev))
            return false;
        FlushPendingMotion();
        _pendingKeys.Enqueue(ev);
        _dirty = true;
        return true;
    }

    /// <summary>Shuts down the backend during resource disposal.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing) { /* managed state disposed via Shutdown */ }
            _disposedValue = true;
        }
    }

    /// <summary>Shuts down the backend and releases its display and input resources.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
