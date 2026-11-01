using TSharpVision.Config;
using TSharpVision.Constants;
using TSharpVision.Drivers;
using TSharpVision.Drivers.SDL;
using TSharpVision.Drivers.SDL.Config;
using TSharpVision.Drivers.SDL.Rendering.Fonts;
using System.Runtime.InteropServices;

namespace TSharpVision.Drivers.SDL.Gpu;

// The preferred SDL path: highest automatic priority, and what the `sdl` alias selects.
/// <summary>Windowed character-cell backend using SDL GPU rendering.</summary>
[ScreenDriver(System = Platform.Windows, Driver = nameof(SDLGpuDriver), Priority = 300)]
[ScreenDriver(System = Platform.Linux,   Driver = nameof(SDLGpuDriver), Priority = 300)]
[ScreenDriver(System = Platform.MacOS,   Driver = nameof(SDLGpuDriver), Priority = 300)]
public partial class SDLGpuDriver : IDisposable, IDriver, IIdleWaitDriver, IGridSizeDriver,
    IPhysicalPaletteDriver, IDisplayEffectDriver
{
    private readonly SdlIdleWait _idleWait = new();

    private ushort _cols = 120;
    private ushort _rows = 37;
    private ushort _cursorType = 100;
    private int    _caretX;
    private int    _caretY;

    // Cell size in logical (1×) and drawable pixels, and the window's current coordinate scale.
    // Rendering, the grid and mouse hit-testing all work in drawable pixels.
    private SdlCellMetrics  _metrics = SdlCellMetrics.ForDensity(8, 16, 1f);
    private SdlDisplayScale _scale   = SdlDisplayScale.Identity;

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
        KeyboardCapabilities.KeyReleaseEvents | KeyboardCapabilities.StandaloneModifierTransitions
        | KeyboardCapabilities.DistinctNumericKeypad | KeyboardCapabilities.StandaloneMetaTransitions;

    /// <summary>Optional rendering callback invoked while pumping messages; initialization installs a callback that renders the screen buffer.</summary>
    public Action<IRenderer>? MessageLoop { get; set; }

    /// <summary>The typed font fitting mode resolved during initialization.</summary>
    internal FontFitMode FontFit { get; private set; }

    // Physical palette and display effect. [graphics] palette/effect seed them once, at the first
    // Initialize, unless the application already selected one; Resume keeps the running choice.
    private static readonly string[] PaletteIds = GpuPhysicalPalette.All.Select(p => p.Id).ToArray();
    private GpuPhysicalPalette _physicalPalette = GpuPhysicalPalette.Default;
    private GpuDisplayEffect   _displayEffect   = GpuDisplayEffect.Default;
    private bool _paletteSelected;
    private bool _effectSelected;

    /// <inheritdoc />
    public IReadOnlyList<string> PhysicalPalettes => PaletteIds;
    /// <inheritdoc />
    public string PhysicalPalette => _physicalPalette.Id;

    /// <inheritdoc />
    public void SetPhysicalPalette(string palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (!GpuPhysicalPalette.TryFind(palette, out GpuPhysicalPalette selected))
            throw new ArgumentException(
                $"Unknown physical palette '{palette}'. Supported: {string.Join(", ", PaletteIds)}.", nameof(palette));
        _paletteSelected = true;
        if (ReferenceEquals(selected, _physicalPalette)) return;
        _physicalPalette = selected;
        if (_gpuRenderer != null) _gpuRenderer.PhysicalPalette = selected;
        RequestFullRepaint();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> DisplayEffects => GpuDisplayEffects.All;
    /// <inheritdoc />
    public string DisplayEffect => GpuDisplayEffects.Id(_displayEffect);

    /// <inheritdoc />
    public void SetDisplayEffect(string effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (!GpuDisplayEffects.TryParse(effect, out GpuDisplayEffect selected))
            throw new ArgumentException(
                $"Unknown display effect '{effect}'. Supported: {string.Join(", ", GpuDisplayEffects.All)}.", nameof(effect));
        _effectSelected = true;
        if (selected == _displayEffect) return;
        _displayEffect = selected;
        if (_gpuRenderer != null) _gpuRenderer.DisplayEffect = selected;
        RequestFullRepaint();
    }

    // The cell buffer is unchanged; only its presentation is. One more frame of the same buffer.
    private void RequestFullRepaint()
    {
        _hasDirtyRegion = false;
        _dirty = true;
    }

    // [graphics] palette/effect. An unknown id warns and keeps Default: a config written for a newer
    // driver must not stop an older one from starting.
    internal void ApplyConfiguredPresentation(GraphicsDriverConfiguration? graphics)
    {
        if (!_paletteSelected && graphics?.Palette is { } paletteId)
        {
            if (GpuPhysicalPalette.TryFind(paletteId, out GpuPhysicalPalette palette)) _physicalPalette = palette;
            else Console.Error.WriteLine(
                $"[SDLGpu] Unknown [graphics] palette '{paletteId}'; using Default. Supported: {string.Join(", ", PaletteIds)}.");
        }
        if (!_effectSelected && graphics?.Effect is { } effectId)
        {
            if (GpuDisplayEffects.TryParse(effectId, out GpuDisplayEffect effect)) _displayEffect = effect;
            else Console.Error.WriteLine(
                $"[SDLGpu] Unknown [graphics] effect '{effectId}'; using Default. Supported: {string.Join(", ", GpuDisplayEffects.All)}.");
        }
        _paletteSelected = _effectSelected = true;
    }

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

            // HighPixelDensity: on a Retina / HiDPI display the swapchain gets the native backing
            // resolution instead of a 1× surface the compositor would magnify.
            var windowFlags = SDL3.SDL.WindowFlags.Resizable | SDL3.SDL.WindowFlags.HighPixelDensity
                | ExtraWindowFlagsForTesting;

            string windowTitle = ScreenDriverFactory.WindowTitle ?? "TSharpVision";
            _window = SDL3.SDL.CreateWindow(
                windowTitle,
                _cols * _metrics.LogicalWidth,
                _rows * _metrics.LogicalHeight,
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
                graphics?.FontSize,
                FontFit)
            {
                SimulatePipelineFailureForTesting = SimulateShaderPipelineFailureForTesting,
            };
            ApplyConfiguredPresentation(graphics);
            _gpuRenderer.PhysicalPalette = _physicalPalette;
            _gpuRenderer.DisplayEffect   = _displayEffect;
            _gpuRenderer.GraphicsFrame   = _graphicsLayers.Frame;
            _gpuRenderer.Initialize();

            // Size the window so its drawable holds exactly the default grid of drawable cells.
            _metrics = _gpuRenderer.Metrics;
            _scale   = SdlDisplayScale.Query(_window);
            var (windowW, windowH) = _metrics.WindowSizeForGrid(_cols, _rows, _scale);
            SDL3.SDL.SetWindowSize(_window, windowW, windowH);
            _scale   = SdlDisplayScale.Query(_window);

            _gpuRenderer.LogDiagnostics(_window);
            if (_diagnosticsEnabled)
                Console.Error.WriteLine($"[SDLGpu] displayScale {_scale.Describe()} grid={_cols}x{_rows}");
            _gpuRenderer.StartRenderThread();

            SDL3.SDL.StartTextInput(_window);
            ClipboardService.Current = new SdlClipboardService();

            _idleWait.Initialize();
            _attached = true;
            TScreen.ScreenWidth  = _cols;
            TScreen.ScreenHeight = _rows;
        }
        catch (Exception ex)
        {
            // A render-worker timeout must also preserve its window and the SDL runtime.
            _gpuRenderer?.Dispose();
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
    public void SetGridSize(ushort columns, ushort rows)
    {
        if (columns < 2 || rows < 1) throw new ArgumentOutOfRangeException(nameof(columns));
        if (!_attached) { _cols = columns; _rows = rows; return; }
        var (w, h) = _metrics.WindowSizeForGrid(columns, rows, _scale);
        if (!SDL3.SDL.SetWindowSize(_window, w, h) || !SDL3.SDL.SyncWindow(_window))
            throw new NotSupportedException("SDL window resize rejected: " + SDL3.SDL.GetError());
        RefreshDisplayScale();
        if (_cols != columns || _rows != rows)
            throw new NotSupportedException("SDL host did not adopt the requested grid.");
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
    public void WaitForEvent(int timeoutMilliseconds, WaitHandle wakeSignal)
    {
        _idleWait.ThrowIfStopped();
        if (!_attached)
        {
            wakeSignal.WaitOne(timeoutMilliseconds < 0 ? 100 : Math.Min(timeoutMilliseconds, 100));
            return;
        }
        if (_dirty || _pendingKeys.Count != 0 || _heldKeys.HasPendingKeypadPress) return;
        _idleWait.Wait(timeoutMilliseconds, wakeSignal, ProcessSdlEvent);
        FlushPendingMotion();
    }

    /// <inheritdoc />
    public void PumpMessages()
    {
        if (!_attached) return;

        // Render first — WriteBuf is called by the framework between PumpMessages
        // calls, so _dirty=true here means the screen buffer is already fresh.
        // Rendering before the event drain gives minimum WriteBuf→pixel latency,
        // matching the SDLDriver (SDLRenderer) approach.
        if (_dirty)
        {
            _dirty    = false;
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
        while (SDL3.SDL.PollEvent(out var e))
        {
            ProcessSdlEvent(e);
        }
        CompleteDrain();
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

                ProcessTextInput(text, _lastModState);
                break;
            }

            case SDL3.SDL.EventType.MouseButtonDown:
            case SDL3.SDL.EventType.MouseButtonUp:
            {
                var kind = type == SDL3.SDL.EventType.MouseButtonDown
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

                // Flush any pending motion before the button event so that
                // the TEventQueue ordering is: ...motion → button.
                FlushPendingMotion();
                TEventQueue.Enqueue(mev);
                _dirty = true;
                break;
            }

            case SDL3.SDL.EventType.MouseMotion:
            {
                // Accumulate into coalescer — do NOT enqueue yet.
                // FlushPendingMotion() emits exactly one TEvent (latest position)
                // after the full drain, so a burst of motion events never piles up
                // in TEventQueue as individual entries.
                // Do NOT set _dirty here: mouse position alone does not change screen
                // content. The framework will call WriteBuf after processing the motion
                // event, which sets _dirty and triggers the correct render.
                // The coalescer holds drawable pixels; SDL reports window coordinates.
                var (px, py) = _scale.WindowToPixel(e.Motion.X, e.Motion.Y);
                _coalescer.Accumulate(px, py, _heldButtons, _modifierTranslator.LogicalState);
                break;
            }

            case SDL3.SDL.EventType.MouseWheel:
            {
                FlushPendingMotion();
                foreach (TEvent wev in TranslateWheel(e.Wheel))
                    TEventQueue.Enqueue(wev);
                _dirty = true;
                break;
            }

            // Size or pixel density changed: a resize, or the window moved to a display with
            // another scale. Both re-derive the grid from drawable pixels.
            case SDL3.SDL.EventType.WindowResized:
            case SDL3.SDL.EventType.WindowPixelSizeChanged:
            case SDL3.SDL.EventType.WindowDisplayScaleChanged:
            case SDL3.SDL.EventType.WindowDisplayChanged:
                FlushPendingMotion();
                RefreshDisplayScale();
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
        var cell = DrawablePixelToCell(px, py);
        var mev  = SdlMouseTranslator.MakeEvent(
            SdlMouseEventKind.Move, 0, cell.x, cell.y,
            heldButtons: held, controlKeyState: controlKeyState);
        TEventQueue.Enqueue(mev);
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
        return DrawablePixelToCell(px, py);
    }

    /// <summary>
    /// Cell under a drawable-pixel position. While Curvature is on screen a pixel shows the grid at
    /// <see cref="GpuCurvature.MapPixelToSourcePixel"/> (the shader's own destination-to-source formula),
    /// so hit-testing follows the warp; points in the black border clamp to the nearest edge cell.
    /// Every other effect, and a frame that fell back to Default, keeps the direct mapping.
    /// </summary>
    private TPoint DrawablePixelToCell(int px, int py)
    {
        if (PresentedEffect == GpuDisplayEffect.Curvature)
        {
            int gridW = _cols * _metrics.PixelWidth, gridH = _rows * _metrics.PixelHeight;
            GpuCurvature.MapPixelToSourcePixel(px, py, gridW, gridH, out px, out py);
        }
        return SdlMouseTranslator.PixelToCell(px, py, _metrics.PixelWidth, _metrics.PixelHeight);
    }

    /// <summary>The effect the last presented frame actually drew (Default while detached).</summary>
    internal GpuDisplayEffect PresentedEffect =>
        PresentedEffectOverrideForTesting ?? _gpuRenderer?.PresentedEffect ?? GpuDisplayEffect.Default;

    /// <summary>Replaces the renderer's presented effect for pointer-mapping tests without SDL. Null in normal operation.</summary>
    internal GpuDisplayEffect? PresentedEffectOverrideForTesting { get; set; }

    /// <summary>
    /// Re-reads the window's size and pixel density. A density change asks the renderer to
    /// re-rasterise at the new density (on its own thread, before the next frame) and forces a
    /// full repaint; the grid is then recomputed from the drawable size.
    /// </summary>
    private void RefreshDisplayScale()
    {
        if (!_attached || _window == IntPtr.Zero) return;
        ApplyDisplayScale(SdlDisplayScale.Query(_window));
    }

    // Split from RefreshDisplayScale so unit tests can drive it with synthetic scales.
    internal void ApplyDisplayScale(SdlDisplayScale scale)
    {
        _scale = scale;
        if (scale.Density != _metrics.Density)
        {
            _metrics = _gpuRenderer?.RequestPixelDensity(scale.Density)
                ?? SdlCellMetrics.ForDensity(_metrics.LogicalWidth, _metrics.LogicalHeight, scale.Density);
            _hasDirtyRegion = false;
            _dirty = true;
            if (_diagnosticsEnabled)
                Console.Error.WriteLine($"[SDLGpu] displayScale change {scale.Describe()} → {_metrics.Describe()}");
        }
        HandleWindowResize(scale.PixelWidth, scale.PixelHeight);
    }

    private void HandleWindowResize(int pixelW, int pixelH)
    {
        if (!_attached || pixelW <= 0 || pixelH <= 0) return;

        var (newCols, newRows) = _metrics.GridForDrawable(pixelW, pixelH);

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

    internal static TEvent MakeQuitEvent()
    {
        TEvent ev = default;
        ev.What = Events.evCommand;
        ev.message.command = Views.cmQuit;
        return ev;
    }

    // ── Native-pixel qualification seams (inert in normal operation) ─────────

    /// <summary>The GPU renderer created by <see cref="Initialize()"/>, or null while detached.</summary>
    internal SDLGpuRenderer? RendererForTesting => _gpuRenderer;

    /// <summary>The SDL window, or zero while detached.</summary>
    internal IntPtr WindowForTesting => _window;

    /// <summary>Window flags added to the driver's own at creation (e.g. <c>Hidden</c>). None in normal operation.</summary>
    internal SDL3.SDL.WindowFlags ExtraWindowFlagsForTesting { get; set; }

    /// <summary>
    /// Makes the renderer's shader-pipeline construction fail inside its own try block, so the real
    /// production fallback (CPU compositing) is selected. False in normal operation.
    /// </summary>
    internal bool SimulateShaderPipelineFailureForTesting { get; set; }

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
        _idleWait.Stop();
        TEventQueue.Wake();
        _graphicsLayers.Clear();
        if (!_attached) return;
        // Do not destroy the window or quit SDL if the worker is still accessing them.
        _gpuRenderer?.Dispose();
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
        _dirty = true;
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
        if (changed) _dirty = true;
        return changed;
    }

    internal void ProcessTextInput(string text, ushort modifierState)
    {
        if (string.IsNullOrEmpty(text)) return;
        FlushPendingMotion();
        TEvent ev = SdlTextInput.CreateEvent(text, SdlKeyTranslator.ToShiftState(modifierState));
        ev.keyDown = _heldKeys.TextCommitted(ev.keyDown);
        _pendingKeys.Enqueue(ev);
        _dirty = true;
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
        _dirty = true;
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
        _dirty = true;
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
