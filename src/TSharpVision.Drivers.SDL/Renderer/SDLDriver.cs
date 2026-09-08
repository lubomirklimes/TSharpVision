using TSharpVision.Constants;
using System.Runtime.InteropServices;
using System.Text;

namespace TSharpVision.Drivers.SDL;

// NOTE: priority intentionally lower than the platform-native console
// drivers so headless CI keeps using NullDriver / Win32 console / ANSI.
// Set TSHARPVISION_DRIVER=SDLDriver to force the SDL window.
[ScreenDriver(System = Platform.Windows, Driver = nameof(SDLDriver), Priority = 300)]
[ScreenDriver(System = Platform.Linux,   Driver = nameof(SDLDriver), Priority = 300)]
[ScreenDriver(System = Platform.MacOS,   Driver = nameof(SDLDriver), Priority = 300)]
public class SDLDriver : IDisposable, IDriver
{
    // Cell metrics are derived from the loaded font at runtime.
    // These defaults are used only if Initialize() is never called (headless).
    private int _cellWidth  = 12;
    private int _cellHeight = 26;

    // ── Dirty rendering ──────────────────────────────────────────────────────
    // Only call Render/Present when the screen buffer or cursor state changed.
    // _dirty starts true so the very first frame is always drawn.

    [Flags]
    internal enum SdlDirtyReason
    {
        None         = 0,
        Initial      = 1 << 0,
        KeyInput     = 1 << 1,
        MouseButton  = 1 << 2,
        MouseMotion  = 1 << 3,
        WindowEvent  = 1 << 4,
        WriteBuf     = 1 << 5,
        CursorChange = 1 << 6,
    }

    private bool          _dirty          = true;
    private SdlDirtyReason _pendingReasons = SdlDirtyReason.Initial;
    private int           _dirtyRenderCount;

    // ── Motion coalescing ────────────────────────────────────────────────────
    // Accumulate SDL mouse-motion events across the drain loop; emit at most one
    // TEvent per drain cycle so the framework jumps to the latest cursor position.

    private readonly SdlMotionCoalescer _coalescer = new();

    // Per-drain-cycle counters (reset each drain, reported once per render).
    private int _drainMotionReceived;  // motion events from SDL this drain
    private int _drainMotionCoalesced; // motion events dropped by coalescing
    private int _drainMotionDuringDrag; // motion events while button held
    private int _drainEventCount;      // total events processed this drain

    // ── Idle wait ────────────────────────────────────────────────────────────
    // Use SDL_WaitEventTimeout rather than busy-spinning when the UI is idle.
    private const int IdleWaitMs = 8;

    // Character grid dimensions — updated on window resize.
    private ushort _cols = 120;
    private ushort _rows = 37;

    private bool disposedValue;

    private IntPtr window;
    private IntPtr renderer;
    private ScreenBuffer? screenBuffer;
    private SDLRenderer? sdlRenderer;
    private bool _attached;
    // 0 = hidden; 100 = block; other non-zero = underline.
    // Initialised to 100 (block) so TScreen.SetCrtData() saves a non-zero
    // CursorLines before hiding the cursor for the first draw pass.
    private ushort _cursorType = 100;
    private int _caretX;
    private int _caretY;
    private readonly Queue<TEvent> _pendingKeys = new();

    // Held button mask for motion events, and last-seen modifier
    // state for TextInput shift-state reconstruction.
    private byte _heldButtons;
    private ushort _lastModState;

    // SupportsTrueColor is false: the SDL driver uses the 16-color VGA palette.
    public bool SupportsMouse    => true;
    public bool SupportsTrueColor => false;
    public bool SupportsGraphics  => true;

    public Action<IRenderer>? MessageLoop { get; set; }

    public void Initialize() => Initialize(() =>
    {
        if (!SDL3.SDL.Init(SDL3.SDL.InitFlags.Video))
            throw new InvalidOperationException($"SDL_Init failed: {SDL3.SDL.GetError()}");
    });

    // Injectable first native operation for headless failure regression tests.
    internal void Initialize(Action initializeVideo)
    {
        // Headless guard: skip SDL when explicitly disabled (e.g., during
        // smoke tests). Actual startup failures propagate after cleanup.
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

            int w = _cellWidth  * GetCols();
            int h = _cellHeight * GetRows();

            string windowTitle = ScreenDriverFactory.WindowTitle ?? "TSharpVision";
            if (!SDL3.SDL.CreateWindowAndRenderer(windowTitle, w, h, SDL3.SDL.WindowFlags.Resizable, out window, out renderer))
            {
                SDL3.SDL.LogError(SDL3.SDL.LogCategory.Application,
                    $"Error creating window and renderer: {SDL3.SDL.GetError()}");
                throw new InvalidOperationException($"SDL window creation failed: {SDL3.SDL.GetError()}");
            }

            // SDLRenderer construction may throw (font not found).
            // Clean up window, renderer, and SDL state on failure so nothing leaks.
            var graphics = ScreenDriverFactory.Configuration?.Graphics;
            sdlRenderer = new SDLRenderer(renderer, graphics?.FontName, graphics?.FontSize);

            // Adopt font-derived cell dimensions from the renderer,
            // then resize the window so the grid fills the screen correctly.
            _cellWidth  = sdlRenderer.CellWidth;
            _cellHeight = sdlRenderer.CellHeight;
            int finalW = _cellWidth  * GetCols();
            int finalH = _cellHeight * GetRows();
            SDL3.SDL.SetWindowSize(window, finalW, finalH);

            // Log SDL window / display diagnostics and apply VSync configuration.
            sdlRenderer.LogWindowDiagnostics(window, renderer);

            _attached = true;
            TScreen.ScreenWidth  = GetCols();
            TScreen.ScreenHeight = GetRows();

            // Enable SDL text-input mode so SDL_EVENT_TEXT_INPUT events are
            // delivered for printable keystrokes.  Without this call the
            // KeyDown handler skips printable keys (deferring to TextInput)
            // but TextInput events never arrive, so all typing is lost.
            SDL3.SDL.StartTextInput(window);

            // Register SDL clipboard so that editor copy/paste routes through SDL3.
            ClipboardService.Current = new SdlClipboardService();
        }
        catch (Exception ex)
        {
            // Clean up any partially-constructed SDL resources.
            try { sdlRenderer?.Dispose(); } catch { }
            sdlRenderer = null;
            if (renderer != IntPtr.Zero) { try { SDL3.SDL.DestroyRenderer(renderer); } catch { } renderer = IntPtr.Zero; }
            if (window   != IntPtr.Zero) { try { SDL3.SDL.DestroyWindow(window);     } catch { } window   = IntPtr.Zero; }
            if (sdlInitialized) { try { SDL3.SDL.Quit(); } catch { } }
            _attached = false;
            throw new InvalidOperationException($"{GetType().Name} initialization failed: {ex.Message}", ex);
        }
    }

    public ScreenBuffer AllocateScreenBuffer()
    {
        screenBuffer = new ScreenBuffer(TScreen.ScreenWidth, TScreen.ScreenHeight);
        MessageLoop = (r) =>
        {
            r.Render(screenBuffer, 0, 0, TScreen.ScreenWidth, TScreen.ScreenHeight);
        };
        return screenBuffer;
    }

    public ushort GetCols()  => _cols;
    public ushort GetRows()  => _rows;
    public ushort GetCursorType() => _cursorType;

    public TDisplay.SM GetScreenMode() => TDisplay.SM.CO80;

    public void PumpMessages()
    {
        if (!_attached) return;

        // ── Render (if dirty) ──────────────────────────────────────────────
        // Render at the top so we always show the buffer that WriteBuf() wrote
        // in the *previous* iteration — WriteBuf is called by the framework
        // after PumpMessages returns, so dirty=true here means the buffer is fresh.
        bool rendered = false;
        if (_dirty)
        {
            var reasons = _pendingReasons;
            _dirty          = false;
            _pendingReasons = SdlDirtyReason.None;

            if (sdlRenderer != null) MessageLoop?.Invoke(sdlRenderer);
            sdlRenderer?.RecordRenderWithReasons((int)reasons);

            rendered = true;
            _dirtyRenderCount++;
        }

        // ── Drain all pending SDL events ───────────────────────────────────
        // Motion events are accumulated into the coalescer; a single TEvent
        // for the latest position is flushed after all events are drained.
        // Non-motion events flush the coalescer first to preserve ordering.
        ResetDrainCounters();
        bool hadSdlEvent = false;
        while (SDL3.SDL.PollEvent(out var e))
        {
            hadSdlEvent = true;
            _drainEventCount++;
            ProcessSdlEvent(e);
        }
        FlushPendingMotion();
        ReportDrainStats();

        // ── Idle wait ──────────────────────────────────────────────────────
        // When nothing is dirty and no events were pending, wait for the next
        // SDL event rather than busy-spinning.
        if (!rendered && !hadSdlEvent && !_dirty)
        {
            sdlRenderer?.RecordIdleWait();
            if (SDL3.SDL.WaitEventTimeout(out var waited, IdleWaitMs))
            {
                ResetDrainCounters();
                _drainEventCount++;
                ProcessSdlEvent(waited);
                // Drain any further events that arrived during the wait.
                while (SDL3.SDL.PollEvent(out var e))
                {
                    _drainEventCount++;
                    ProcessSdlEvent(e);
                }
                FlushPendingMotion();
                ReportDrainStats();
            }
        }
    }

    // ── SDL event processing ───────────────────────────────────────────────

    private void ProcessSdlEvent(SDL3.SDL.Event e)
    {
        var eventType = (SDL3.SDL.EventType)e.Type;
        switch (eventType)
        {
            case SDL3.SDL.EventType.WindowCloseRequested:
            case SDL3.SDL.EventType.Quit:
                FlushPendingMotion();
                TEventQueue.Enqueue(MakeQuitEvent());
                MarkDirty(SdlDirtyReason.WindowEvent);
                break;

            case SDL3.SDL.EventType.KeyDown:
            {
                uint kc    = (uint)e.Key.Key;
                ushort mod = (ushort)e.Key.Mod;
                _lastModState = mod;

                // SDL_TEXTINPUT will follow for printable keys that are not
                // Ctrl/LALT modified.  Skip KeyDown for those so TextInput
                // provides the layout-correct character.
                bool hasCtrl          = (mod & SdlKeyTranslator.SDL_KMOD_CTRL) != 0;
                bool hasLAlt          = (mod & SdlKeyTranslator.SDL_KMOD_LALT) != 0;
                bool isPrintableRange = (kc >= 0x20 && kc <= 0x7E) || (kc >= 'a' && kc <= 'z');
                if (isPrintableRange && !hasCtrl && !hasLAlt)
                    break; // TextInput will handle it.

                FlushPendingMotion();
                if (SdlKeyTranslator.TryTranslate(kc, mod, '\0', out var kev))
                    _pendingKeys.Enqueue(kev);
                MarkDirty(SdlDirtyReason.KeyInput);
                break;
            }

            // SDL_TEXTINPUT: printable text produced by the OS keyboard layout
            // (handles AltGr, dead keys, non-US layouts).
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
                MarkDirty(SdlDirtyReason.KeyInput);
                break;
            }

            case SDL3.SDL.EventType.MouseButtonDown:
            case SDL3.SDL.EventType.MouseButtonUp:
            {
                var kind = eventType == SDL3.SDL.EventType.MouseButtonDown
                    ? SdlMouseEventKind.Down : SdlMouseEventKind.Up;
                var cell = SdlMouseTranslator.PixelToCell(
                    (int)e.Button.X, (int)e.Button.Y, _cellWidth, _cellHeight);
                var mev = SdlMouseTranslator.MakeEvent(
                    kind, e.Button.Button, cell.x, cell.y, e.Button.Clicks);
                if (kind == SdlMouseEventKind.Down)
                    _heldButtons |= mev.mouse.buttons;
                else
                    _heldButtons = 0;

                // Flush pending motion first so button events appear after any
                // preceding motion in the TEventQueue (correct ordering).
                FlushPendingMotion();
                TEventQueue.Enqueue(mev);
                MarkDirty(SdlDirtyReason.MouseButton);
                break;
            }

            case SDL3.SDL.EventType.MouseMotion:
            {
                // Accumulate — do NOT enqueue to TEventQueue yet.
                // FlushPendingMotion() emits at most one TEvent after the whole
                // queue is drained, so the framework only processes the latest
                // cursor position per drain cycle.
                bool overwritten = _coalescer.Accumulate(
                    (int)e.Motion.X, (int)e.Motion.Y, _heldButtons);

                _drainMotionReceived++;
                if (_heldButtons != 0) _drainMotionDuringDrag++;
                if (overwritten)       _drainMotionCoalesced++;

                MarkDirty(SdlDirtyReason.MouseMotion);
                break;
            }

            case SDL3.SDL.EventType.MouseWheel:
            {
                var cell = SdlMouseTranslator.PixelToCell(
                    (int)e.Wheel.MouseX, (int)e.Wheel.MouseY, _cellWidth, _cellHeight);
                FlushPendingMotion();
                if (SdlMouseTranslator.MakeWheelEvent(e.Wheel.Y, cell.x, cell.y, out var wev))
                    TEventQueue.Enqueue(wev);
                MarkDirty(SdlDirtyReason.MouseButton);
                break;
            }

            case SDL3.SDL.EventType.WindowResized:
            {
                FlushPendingMotion();
                if (SDL3.SDL.GetCurrentRenderOutputSize(renderer, out int pw, out int ph))
                    HandleWindowResize(pw, ph);
                // _dirty / MarkDirty called inside HandleWindowResize
                break;
            }

            // Expose / restore: redraw even if the buffer hasn't changed.
            case SDL3.SDL.EventType.WindowExposed:
            case SDL3.SDL.EventType.WindowShown:
            case SDL3.SDL.EventType.WindowRestored:
                MarkDirty(SdlDirtyReason.WindowEvent);
                break;
        }
    }

    // ── Motion coalescing helpers ──────────────────────────────────────────

    /// <summary>
    /// Emits at most one TEvent for the latest accumulated mouse position and
    /// resets the coalescer.  Safe to call multiple times; no-op if nothing
    /// was accumulated.
    /// </summary>
    private void FlushPendingMotion()
    {
        if (!_coalescer.TryFlush(out int px, out int py, out byte held))
            return;

        var cell = SdlMouseTranslator.PixelToCell(px, py, _cellWidth, _cellHeight);
        var mev  = SdlMouseTranslator.MakeEvent(
            SdlMouseEventKind.Move, 0, cell.x, cell.y, heldButtons: held);
        TEventQueue.Enqueue(mev);
    }

    // ── Drain-cycle stats helpers ──────────────────────────────────────────

    private void ResetDrainCounters()
    {
        _drainMotionReceived   = 0;
        _drainMotionCoalesced  = 0;
        _drainMotionDuringDrag = 0;
        _drainEventCount       = 0;
    }

    private void ReportDrainStats()
    {
        if (_drainEventCount == 0) return;
        sdlRenderer?.RecordDrainCycle(
            _drainEventCount,
            _drainMotionReceived,
            _drainMotionCoalesced,
            _drainMotionDuringDrag);
    }

    // ── Dirty-flag helpers ────────────────────────────────────────────────

    private void MarkDirty(SdlDirtyReason reason)
    {
        _dirty          = true;
        _pendingReasons |= reason;
    }

    /// <summary>
    /// Called when the SDL window is resized. Recalculates the character grid
    /// from the new pixel dimensions, updates TScreen, reallocates the screen
    /// buffer, forces an immediate redraw, and enqueues cmScreenResized.
    /// </summary>
    private void HandleWindowResize(int pixelW, int pixelH)
    {
        if (!_attached || _cellWidth <= 0 || _cellHeight <= 0) return;

        ushort newCols = (ushort)Math.Max(2, pixelW / _cellWidth);
        ushort newRows = (ushort)Math.Max(1, pixelH / _cellHeight);

        if (newCols == _cols && newRows == _rows) return; // no change

        _cols = newCols;
        _rows = newRows;
        TScreen.ScreenWidth  = _cols;
        TScreen.ScreenHeight = _rows;
        TScreen.ScreenBuffer = AllocateScreenBuffer();
        MarkDirty(SdlDirtyReason.WindowEvent);

        TEvent resEv = default;
        resEv.What = Constants.Events.evCommand;
        resEv.message.command = Constants.Views.cmScreenResized;
        TEventQueue.Enqueue(resEv);
    }

    /// <summary>
    /// Builds the <see cref="TEvent"/> that represents a user-requested application
    /// quit (e.g. the OS window-close button). Posts <c>cmQuit</c> through the
    /// normal command pipeline so the application shuts down cleanly.
    /// Internal so unit tests can call it without native SDL.
    /// </summary>
    internal static TEvent MakeQuitEvent()
    {
        TEvent ev = default;
        ev.What = Events.evCommand;
        ev.message.command = Views.cmQuit;
        return ev;
    }

    /// <summary>
    /// Calculates the character grid size (columns, rows) from pixel dimensions
    /// and cell metrics. Minimum grid is 2 columns × 1 row.
    /// Internal so unit tests can call it without native SDL.
    /// </summary>
    internal static (ushort cols, ushort rows) CalculateGridSize(
        int pixelW, int pixelH, int cellW, int cellH)
    {
        ushort cols = (ushort)Math.Max(2, pixelW / cellW);
        ushort rows = (ushort)Math.Max(1, pixelH / cellH);
        return (cols, rows);
    }

    public void SetCursorType(ushort cursorType)
    {
        _cursorType = cursorType;
        sdlRenderer?.SetCursor(_caretX, _caretY, _cursorType);
        MarkDirty(SdlDirtyReason.CursorChange);
    }

    public void Suspend() { /* SDL has no cooked-mode equivalent */ }
    public void Resume()  { if (!_attached) Initialize(); }
    public void Shutdown()
    {
        if (!_attached) return;
        try { sdlRenderer?.Dispose(); } catch { }
        sdlRenderer = null;
        // Stop text input before destroying the window so SDL cleans up its
        // internal text-input state for this window.
        if (window != IntPtr.Zero) SDL3.SDL.StopTextInput(window);
        if (renderer != IntPtr.Zero) { SDL3.SDL.DestroyRenderer(renderer); renderer = IntPtr.Zero; }
        if (window   != IntPtr.Zero) { SDL3.SDL.DestroyWindow(window);     window   = IntPtr.Zero; }
        SDL3.SDL.Quit();
        _attached = false;
        ClipboardService.Reset();
    }

    public void SetScreenMode(TDisplay.SM mode) { /* fixed cell grid */ }

    public void ClearScreen(ushort cols, ushort rows)
    {
        if (!_attached || screenBuffer == null) return;
        var blank = new TScreenChar { Character = ' ', Attr = new TColorAttr(0x07) };
        for (uint y = 0; y < rows; y++)
            for (uint x = 0; x < cols; x++)
                screenBuffer.SetChar(x, y, blank);
    }

    public void SetCaretPosition(int x, int y)
    {
        _caretX = x;
        _caretY = y;
        sdlRenderer?.SetCursor(_caretX, _caretY, _cursorType);
        MarkDirty(SdlDirtyReason.CursorChange);
    }

    public void MakeBeep()
    {
        // SDL3 has no portable beep; fall back to the BEL byte if a console
        // is attached, otherwise no-op.
        try { Console.Write('\a'); } catch { }
    }

    public bool ReadKeyEvent(out TEvent ev)
    {
        if (_pendingKeys.Count > 0) { ev = _pendingKeys.Dequeue(); return true; }
        ev = default;
        return false;
    }

    public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf)
    {
        if (!_attached || screenBuffer == null) return;
        for (int row = 0; row < h; row++)
            for (int col = 0; col < w; col++)
                screenBuffer.SetChar((uint)(x + col), (uint)(y + row), buf[row * w + col]);
        MarkDirty(SdlDirtyReason.WriteBuf);
    }

    // ── Internal helpers for unit tests (no SDL required) ───────────────────

    /// <summary>Current dirty state — true means a render is pending.</summary>
    internal bool IsDirty => _dirty;

    /// <summary>Total frames actually rendered (dirty-triggered).</summary>
    internal int DirtyRenderCount => _dirtyRenderCount;

    /// <summary>
    /// Clears the dirty flag and pending reasons without rendering. Used only by
    /// unit tests to establish a known baseline before verifying that a specific
    /// call sets dirty.
    /// </summary>
    internal void ClearDirtyForTest() { _dirty = false; _pendingReasons = SdlDirtyReason.None; }

    /// <summary>Current dirty reason bitmask — for unit tests.</summary>
    internal SdlDirtyReason PendingReasons => _pendingReasons;

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing) { /* managed state disposed via Shutdown */ }
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

}
