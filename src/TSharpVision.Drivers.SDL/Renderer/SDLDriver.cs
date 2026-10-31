using TSharpVision.Config;
using TSharpVision.Constants;
using System.Runtime.InteropServices;
using TSharpVision.Drivers.SDL.Rendering.Fonts;

namespace TSharpVision.Drivers.SDL;

// The SDL_Renderer compatibility path: below SDLGpuDriver (300), above the native
// text drivers (50). Set TSHARPVISION_DRIVER=SDLDriver to select it explicitly.
/// <summary>Windowed character-cell backend using the SDL renderer and font rasterization.</summary>
[ScreenDriver(System = Platform.Windows, Driver = nameof(SDLDriver), Priority = 200)]
[ScreenDriver(System = Platform.Linux,   Driver = nameof(SDLDriver), Priority = 200)]
[ScreenDriver(System = Platform.MacOS,   Driver = nameof(SDLDriver), Priority = 200)]
public partial class SDLDriver : IDisposable, IDriver, IIdleWaitDriver, IGridSizeDriver
{
    // Cell metrics are derived from the loaded font at runtime, in logical (1×) and drawable
    // pixels; the defaults are used only if Initialize() is never called (headless). Rendering,
    // the grid and mouse hit-testing all work in drawable pixels.
    private SdlCellMetrics  _metrics = SdlCellMetrics.ForDensity(12, 26, 1f);
    private SdlDisplayScale _scale   = SdlDisplayScale.Identity;

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
    private readonly SdlIdleWait _idleWait = new();

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
    private readonly SdlModifierTranslator _modifierTranslator = new();
    private readonly SdlHeldKeyTracker _heldKeys = new();

    // Held button mask for motion events, and last-seen modifier
    // state for TextInput shift-state reconstruction.
    private byte _heldButtons;
    private ushort _lastModState;

    // SupportsTrueColor is false: the SDL driver uses the 16-color VGA palette.
    /// <inheritdoc />
    public bool SupportsMouse    => true;
    /// <inheritdoc />
    public bool SupportsTrueColor => false;
    /// <inheritdoc />
    public bool SupportsGraphics  => true;
    /// <inheritdoc />
    public KeyboardCapabilities KeyboardCapabilities =>
        KeyboardCapabilities.KeyReleaseEvents | KeyboardCapabilities.StandaloneModifierTransitions
        | KeyboardCapabilities.DistinctNumericKeypad | KeyboardCapabilities.StandaloneMetaTransitions;

    /// <summary>Optional rendering callback invoked while pumping messages; initialization installs a callback that renders the screen buffer.</summary>
    public Action<IRenderer>? MessageLoop { get; set; }

    /// <summary>The typed font fitting mode resolved during initialization.</summary>
    internal FontFitMode FontFit { get; private set; }

    /// <inheritdoc />
    public void Initialize() => Initialize(() =>
    {
        // One native close must reach the application as one cmQuit: by default SDL follows
        // SDL_EVENT_WINDOW_CLOSE_REQUESTED on the last window with a synthesized SDL_EVENT_QUIT, and the
        // second request would bypass an application's veto or quit confirmation. Genuine OS/signal
        // quits still arrive as SDL_EVENT_QUIT.
        SDL3.SDL.SetHint(SDL3.SDL.Hints.QuitOnLastWindowClose, "0");
        if (!SDL3.SDL.Init(SDL3.SDL.InitFlags.Video))
            throw new InvalidOperationException($"SDL_Init failed: {SDL3.SDL.GetError()}");
    });

    // Injectable first native operation for headless failure regression tests.
    internal void Initialize(Action initializeVideo)
    {
        // Headless guard: skip SDL when explicitly disabled (e.g., during
        // smoke tests). Actual startup failures propagate after cleanup.
        if (_attached) return;
        if (SdlHeadlessOptOut.IsRequested())
        {
            Console.Error.WriteLine($"[SDL] Initialization explicitly disabled by {SdlHeadlessOptOut.EnvironmentVariable}=1; driver remains detached.");
            return;
        }

        bool sdlInitialized = false;
        try
        {
            // The typed [graphics] fontFit; the configuration loader already rejected invalid values.
            FontFit = ScreenDriverFactory.Configuration?.Graphics.FontFit ?? FontFitMode.Condense;

            initializeVideo();
            sdlInitialized = true;

            int w = _metrics.LogicalWidth  * GetCols();
            int h = _metrics.LogicalHeight * GetRows();

            // HighPixelDensity: on a Retina / HiDPI display the renderer output gets the native
            // backing resolution instead of a 1× surface the compositor would magnify.
            string windowTitle = ScreenDriverFactory.WindowTitle ?? "TSharpVision";
            var windowFlags = SDL3.SDL.WindowFlags.Resizable | SDL3.SDL.WindowFlags.HighPixelDensity
                | ExtraWindowFlagsForTesting;
            if (!SDL3.SDL.CreateWindowAndRenderer(windowTitle, w, h, windowFlags, out window, out renderer))
            {
                SDL3.SDL.LogError(SDL3.SDL.LogCategory.Application,
                    $"Error creating window and renderer: {SDL3.SDL.GetError()}");
                throw new InvalidOperationException($"SDL window creation failed: {SDL3.SDL.GetError()}");
            }

            // SDLRenderer construction may throw (font not found).
            // Clean up window, renderer, and SDL state on failure so nothing leaks.
            var graphics = ScreenDriverFactory.Configuration?.Graphics;
            _scale = SdlDisplayScale.Query(window);
            sdlRenderer = new SDLRenderer(renderer, graphics?.FontName, graphics?.FontSize, FontFit, _scale.Density);

            // Adopt font-derived cell dimensions from the renderer, then resize the window so
            // its drawable holds exactly the grid of drawable cells.
            _metrics = sdlRenderer.Metrics;
            var (finalW, finalH) = _metrics.WindowSizeForGrid(GetCols(), GetRows(), _scale);
            SDL3.SDL.SetWindowSize(window, finalW, finalH);
            _scale = SdlDisplayScale.Query(window);

            // Log SDL window / display diagnostics and apply VSync configuration.
            sdlRenderer.LogWindowDiagnostics(window, renderer);

            _idleWait.Initialize();
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

    /// <inheritdoc />
    public ScreenBuffer AllocateScreenBuffer()
    {
        screenBuffer = new ScreenBuffer(TScreen.ScreenWidth, TScreen.ScreenHeight);
        MessageLoop = (r) =>
        {
            r.Render(screenBuffer, 0, 0, TScreen.ScreenWidth, TScreen.ScreenHeight);
        };
        return screenBuffer;
    }

    /// <inheritdoc />
    public void SetGridSize(ushort columns, ushort rows)
    {
        if (columns < 2 || rows < 1) throw new ArgumentOutOfRangeException(nameof(columns));
        if (!_attached) { _cols = columns; _rows = rows; return; }
        var (w, h) = _metrics.WindowSizeForGrid(columns, rows, _scale);
        if (!SDL3.SDL.SetWindowSize(window, w, h) || !SDL3.SDL.SyncWindow(window))
            throw new NotSupportedException("SDL window resize rejected: " + SDL3.SDL.GetError());
        RefreshDisplayScale();
        if (_cols != columns || _rows != rows)
            throw new NotSupportedException("SDL host did not adopt the requested grid.");
    }
    /// <inheritdoc />
    public ushort GetCols()  => _cols;
    /// <inheritdoc />
    public ushort GetRows()  => _rows;
    /// <inheritdoc />
    public ushort GetCursorType() => _cursorType;

    /// <inheritdoc />
    public TDisplay.SM GetScreenMode() => TDisplay.SM.CO80;

    /// <inheritdoc />
    public void WaitForEvent(int timeoutMilliseconds, WaitHandle wakeSignal)
    {
        _idleWait.ThrowIfStopped();
        if (!_attached)
        {
            wakeSignal.WaitOne(timeoutMilliseconds < 0 ? 100 : Math.Min(timeoutMilliseconds, 100));
            return;
        }
        if (_dirty || _pendingKeys.Count != 0 || _heldKeys.HasPendingKeypadPress) return;
        sdlRenderer?.RecordIdleWait();
        _idleWait.Wait(timeoutMilliseconds, wakeSignal, ProcessSdlEvent);
        FlushPendingMotion();
    }

    /// <inheritdoc />
    public void PumpMessages()
    {
        if (!_attached) return;

        // ── Render (if dirty) ──────────────────────────────────────────────
        // Render at the top so we always show the buffer that WriteBuf() wrote
        // in the *previous* iteration — WriteBuf is called by the framework
        // after PumpMessages returns, so dirty=true here means the buffer is fresh.
        if (_dirty)
        {
            var reasons = _pendingReasons;
            _dirty          = false;
            _pendingReasons = SdlDirtyReason.None;

            if (sdlRenderer != null) MessageLoop?.Invoke(sdlRenderer);
            sdlRenderer?.RecordRenderWithReasons((int)reasons);

            _dirtyRenderCount++;
        }

        // ── Drain all pending SDL events ───────────────────────────────────
        // Motion events are accumulated into the coalescer; a single TEvent
        // for the latest position is flushed after all events are drained.
        // Non-motion events flush the coalescer first to preserve ordering.
        ResetDrainCounters();
        while (SDL3.SDL.PollEvent(out var e))
        {
            _drainEventCount++;
            ProcessSdlEvent(e);
        }
        CompleteDrain();
        ReportDrainStats();
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
            case SDL3.SDL.EventType.KeyUp:
            {
                uint kc    = (uint)e.Key.Key;
                ushort mod = (ushort)e.Key.Mod;
                _lastModState = mod;

                bool keyDown = eventType == SDL3.SDL.EventType.KeyDown;
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

            // SDL_TEXTINPUT: printable text produced by the OS keyboard layout
            // (handles AltGr, dead keys, non-US layouts).
            case SDL3.SDL.EventType.TextInput:
            {
                string? text = Marshal.PtrToStringUTF8(e.Text.Text);
                if (string.IsNullOrEmpty(text)) break;

                ProcessTextInput(text, _lastModState);
                break;
            }

            case SDL3.SDL.EventType.MouseButtonDown:
            case SDL3.SDL.EventType.MouseButtonUp:
            {
                var kind = eventType == SDL3.SDL.EventType.MouseButtonDown
                    ? SdlMouseEventKind.Down : SdlMouseEventKind.Up;
                var cell = WindowToCell(e.Button.X, e.Button.Y);
                var mev = SdlMouseTranslator.MakeEvent(
                    kind, e.Button.Button, cell.x, cell.y, e.Button.Clicks,
                    controlKeyState: _modifierTranslator.LogicalState);
                byte changedButton = SdlMouseTranslator.TranslateButton(e.Button.Button);
                if (kind == SdlMouseEventKind.Down)
                    _heldButtons |= changedButton;
                else
                    _heldButtons &= (byte)~changedButton;

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
                // The coalescer holds drawable pixels; SDL reports window coordinates.
                var (px, py) = _scale.WindowToPixel(e.Motion.X, e.Motion.Y);
                bool overwritten = _coalescer.Accumulate(
                    px, py, _heldButtons, _modifierTranslator.LogicalState);

                _drainMotionReceived++;
                if (_heldButtons != 0) _drainMotionDuringDrag++;
                if (overwritten)       _drainMotionCoalesced++;

                MarkDirty(SdlDirtyReason.MouseMotion);
                break;
            }

            case SDL3.SDL.EventType.MouseWheel:
            {
                FlushPendingMotion();
                foreach (TEvent wev in TranslateWheel(e.Wheel))
                    TEventQueue.Enqueue(wev);
                MarkDirty(SdlDirtyReason.MouseButton);
                break;
            }

            // Size or pixel density changed: a resize, or the window moved to a display with
            // another scale. Both re-derive the grid from drawable pixels.
            case SDL3.SDL.EventType.WindowResized:
            case SDL3.SDL.EventType.WindowPixelSizeChanged:
            case SDL3.SDL.EventType.WindowDisplayScaleChanged:
            case SDL3.SDL.EventType.WindowDisplayChanged:
            {
                FlushPendingMotion();
                RefreshDisplayScale();
                // _dirty / MarkDirty called inside ApplyDisplayScale / HandleWindowResize
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
        if (!_coalescer.TryFlush(
                out int px, out int py, out byte held, out uint controlKeyState))
            return;

        var cell = SdlMouseTranslator.PixelToCell(px, py, _metrics.PixelWidth, _metrics.PixelHeight);
        var mev  = SdlMouseTranslator.MakeEvent(
            SdlMouseEventKind.Move, 0, cell.x, cell.y,
            heldButtons: held, controlKeyState: controlKeyState);
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

    /// <summary>Translates an SDL wheel event at its window position into framework wheel events.</summary>
    internal TEvent[] TranslateWheel(in SDL3.SDL.MouseWheelEvent wheel)
    {
        var cell = WindowToCell(wheel.MouseX, wheel.MouseY);
        return SdlMouseTranslator.MakeWheelEvents(
            wheel, cell.x, cell.y, _heldButtons, _modifierTranslator.LogicalState);
    }

    /// <summary>Cell under an SDL mouse position given in window coordinates.</summary>
    private TPoint WindowToCell(float x, float y)
    {
        var (px, py) = _scale.WindowToPixel(x, y);
        return SdlMouseTranslator.PixelToCell(px, py, _metrics.PixelWidth, _metrics.PixelHeight);
    }

    /// <summary>
    /// Re-reads the window's size and pixel density and the renderer's output size, then applies
    /// them (<see cref="ApplyDisplayScale"/>).
    /// </summary>
    private void RefreshDisplayScale()
    {
        if (!_attached || window == IntPtr.Zero) return;
        SdlDisplayScale scale = SdlDisplayScale.Query(window);
        // The renderer output is the drawable SDLRenderer actually draws into.
        if (renderer != IntPtr.Zero && SDL3.SDL.GetCurrentRenderOutputSize(renderer, out int pw, out int ph))
            scale = scale with { PixelWidth = pw, PixelHeight = ph };
        ApplyDisplayScale(scale);
    }

    /// <summary>
    /// A density change re-rasterises the font and glyph textures for the new drawable cell and
    /// forces a redraw; then the grid is recomputed from the drawable size. Internal so unit
    /// tests can drive it with synthetic scales.
    /// </summary>
    internal void ApplyDisplayScale(SdlDisplayScale scale)
    {
        _scale = scale;
        if (scale.Density != _metrics.Density)
        {
            _metrics = sdlRenderer?.SetPixelDensity(scale.Density)
                ?? SdlCellMetrics.ForDensity(_metrics.LogicalWidth, _metrics.LogicalHeight, scale.Density);
            MarkDirty(SdlDirtyReason.WindowEvent);
            if (Renderer.SdlRendererPerformanceDiagnostics.Enabled)
                Console.Error.WriteLine($"[SDLRenderer] displayScale change {scale.Describe()} → {_metrics.Describe()}");
        }
        HandleWindowResize(scale.PixelWidth, scale.PixelHeight);
    }

    /// <summary>
    /// Called when the SDL window is resized. Recalculates the character grid
    /// from the new drawable pixel dimensions, updates TScreen, reallocates the screen
    /// buffer, forces an immediate redraw, and enqueues cmScreenResized.
    /// </summary>
    private void HandleWindowResize(int pixelW, int pixelH)
    {
        if (!_attached || pixelW <= 0 || pixelH <= 0) return;

        var (newCols, newRows) = _metrics.GridForDrawable(pixelW, pixelH);

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

    /// <inheritdoc />
    public void SetCursorType(ushort cursorType)
    {
        _cursorType = cursorType;
        sdlRenderer?.SetCursor(_caretX, _caretY, _cursorType);
        MarkDirty(SdlDirtyReason.CursorChange);
    }

    /// <inheritdoc />
    public void Suspend() { /* SDL has no cooked-mode equivalent */ }
    /// <inheritdoc />
    public void Resume()  { if (!_attached) Initialize(); }
    /// <inheritdoc />
    public void Shutdown()
    {
        _idleWait.Stop();
        TEventQueue.Wake();
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

    /// <summary>Accepts a logical screen-mode request without changing this backend's display mode.</summary>
    public void SetScreenMode(TDisplay.SM mode) { /* fixed cell grid */ }

    /// <inheritdoc />
    public void ClearScreen(ushort cols, ushort rows)
    {
        if (!_attached || screenBuffer == null) return;
        var blank = new TScreenChar { Character = ' ', Attr = new TColorAttr(0x07) };
        for (uint y = 0; y < rows; y++)
            for (uint x = 0; x < cols; x++)
                screenBuffer.SetChar(x, y, blank);
    }

    /// <inheritdoc />
    public void SetCaretPosition(int x, int y)
    {
        _caretX = x;
        _caretY = y;
        sdlRenderer?.SetCursor(_caretX, _caretY, _cursorType);
        MarkDirty(SdlDirtyReason.CursorChange);
    }

    /// <inheritdoc />
    public void MakeBeep()
    {
        // SDL3 has no portable beep; fall back to the BEL byte if a console
        // is attached, otherwise no-op.
        try { Console.Write('\a'); } catch { }
    }

    /// <inheritdoc />
    public bool ReadKeyEvent(out TEvent ev)
    {
        if (_pendingKeys.Count == 0) PublishPendingKeypadPress();
        if (_pendingKeys.Count > 0) { ev = _pendingKeys.Dequeue(); return true; }
        ev = default;
        return false;
    }

    internal bool ProcessModifierKey(uint keycode, bool down)
    {
        PublishPendingKeypadPress();
        if (!_modifierTranslator.TryTranslate(keycode, down, out TEvent ev)) return false;
        FlushPendingMotion();
        _pendingKeys.Enqueue(ev);
        MarkDirty(SdlDirtyReason.KeyInput);
        return true;
    }

    internal bool ProcessModifierFocusLost()
    {
        bool changed = PublishPendingKeypadPress();
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
        if (changed) MarkDirty(SdlDirtyReason.WindowEvent);
        return changed;
    }

    internal void ProcessTextInput(string text, ushort modifierState)
    {
        if (string.IsNullOrEmpty(text)) return;
        FlushPendingMotion();
        TEvent ev = SdlTextInput.CreateEvent(text, SdlKeyTranslator.ToShiftState(modifierState));
        ev.keyDown = _heldKeys.TextCommitted(ev.keyDown);
        _pendingKeys.Enqueue(ev);
        MarkDirty(SdlDirtyReason.KeyInput);
    }

    internal bool ProcessOrdinaryKeyUp(uint keycode, ushort modifierState)
    {
        PublishPendingKeypadPress();
        if (!_heldKeys.TryKeyUp(keycode, modifierState, out TEvent deferredPress, out TEvent ev)) return false;
        FlushPendingMotion();
        // A Right Alt chord the layout turned into no text is published as the shortcut it was.
        // (A waiting keypad press of this same key was already published above.)
        if (deferredPress.What != Events.evNothing) _pendingKeys.Enqueue(deferredPress);
        _pendingKeys.Enqueue(ev);
        MarkDirty(SdlDirtyReason.KeyInput);
        return true;
    }

    internal bool ProcessOrdinaryKeyDown(uint keycode, ushort modifierState)
    {
        // A keypad press still waiting for text is followed by no commit once another key arrives.
        PublishPendingKeypadPress();
        // Layout-correct printable input arrives later through SDL_TEXTINPUT.
        if (!_heldKeys.KeyDown(keycode, modifierState, out TEvent ev)) return false;
        FlushPendingMotion();
        _pendingKeys.Enqueue(ev);
        MarkDirty(SdlDirtyReason.KeyInput);
        return true;
    }

    // Everything a drain of SDL events leaves pending is published once the queue is empty.
    private void CompleteDrain()
    {
        // A keypad press whose drain brought no text commit types nothing.
        PublishPendingKeypadPress();
        FlushPendingMotion();
    }

    /// <summary>
    /// Processes <paramref name="events"/> as one drain of the SDL queue, exactly as <see cref="PumpMessages"/>
    /// does, without SDL. For input qualification with native event structures.
    /// </summary>
    internal void DrainForTesting(params SDL3.SDL.Event[] events)
    {
        foreach (SDL3.SDL.Event e in events) ProcessSdlEvent(e);
        CompleteDrain();
    }

    // A keypad digit or decimal press waits for the text commit SDL sends after it in the same drain, so a key that
    // types is one press carrying its text, never a cursor key followed by the text. Without a commit it is published
    // with its identity when the next key event arrives or the drain ends; it never outlives the drain.
    private bool PublishPendingKeypadPress()
    {
        if (!_heldKeys.TryTakePendingKeypadPress(out TEvent ev)) return false;
        FlushPendingMotion();
        _pendingKeys.Enqueue(ev);
        MarkDirty(SdlDirtyReason.KeyInput);
        return true;
    }

    /// <inheritdoc />
    public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf)
    {
        if (!_attached || screenBuffer == null) return;
        for (int row = 0; row < h; row++)
            for (int col = 0; col < w; col++)
                screenBuffer.SetChar((uint)(x + col), (uint)(y + row), buf[row * w + col]);
        MarkDirty(SdlDirtyReason.WriteBuf);
    }

    // ── Internal helpers for unit tests (no SDL required) ───────────────────

    /// <summary>Current cell metrics (logical and drawable).</summary>
    internal SdlCellMetrics CellMetrics => _metrics;

    /// <summary>Current window/drawable coordinate scale.</summary>
    internal SdlDisplayScale DisplayScale => _scale;

    /// <summary>Puts the driver in the attached state with the given metrics, without SDL.</summary>
    internal void AttachForTest(SdlCellMetrics metrics, SdlDisplayScale scale, ushort cols, ushort rows)
    {
        _metrics  = metrics;
        _scale    = scale;
        _cols     = cols;
        _rows     = rows;
        _attached = true;
        TScreen.ScreenWidth  = cols;
        TScreen.ScreenHeight = rows;
    }

    /// <summary>Cell for a window-coordinate mouse position, as button events compute it.</summary>
    internal TPoint WindowToCellForTest(float x, float y) => WindowToCell(x, y);

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

    /// <summary>The renderer created by <see cref="Initialize()"/>, or null while detached. For driver-output qualification.</summary>
    internal SDLRenderer? RendererForTesting => sdlRenderer;

    /// <summary>The SDL renderer handle the driver draws through, or zero while detached. For driver-output qualification.</summary>
    internal IntPtr RendererHandleForTesting => renderer;

    /// <summary>The SDL window, or zero while detached. For native-pixel qualification.</summary>
    internal IntPtr WindowForTesting => window;

    /// <summary>
    /// Window flags added to the driver's own at creation (e.g. <c>Hidden</c>, so a hardware
    /// renderer can be qualified without showing a window). None in normal operation.
    /// </summary>
    internal SDL3.SDL.WindowFlags ExtraWindowFlagsForTesting { get; set; }

    /// <summary>Shuts down the backend during resource disposal.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing) { /* managed state disposed via Shutdown */ }
            disposedValue = true;
        }
    }

    /// <summary>Shuts down the backend and releases its display and input resources.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

}
