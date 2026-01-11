using TSharpVision.Constants;
using System.Runtime.InteropServices;
using System.Text;

namespace TSharpVision.Drivers.SDL;

// NOTE: priority intentionally lower than the platform-native console
// drivers so headless CI keeps using NullDriver / Win32 console / ANSI.
// Set TSHARPVISION_DRIVER=SDLDriver to force the SDL window.
[ScreenDriver(System = Platform.Windows, Driver = nameof(SDLDriver), Priority = 30)]
[ScreenDriver(System = Platform.Linux,   Driver = nameof(SDLDriver), Priority = 30)]
[ScreenDriver(System = Platform.MacOS,   Driver = nameof(SDLDriver), Priority = 30)]
public class SDLDriver : IDisposable, IDriver
{
    // Cell metrics are derived from the loaded font at runtime.
    // These defaults are used only if Initialize() is never called (headless).
    private int _cellWidth  = 12;
    private int _cellHeight = 26;
    private const int FrameMs = 16; // ~60 Hz pacing

    // Character grid dimensions — updated on window resize.
    private ushort _cols = 120;
    private ushort _rows = 37;

    private bool disposedValue;

    private IntPtr window;
    private IntPtr renderer;
    private ScreenBuffer? screenBuffer;
    private SDLRenderer? sdlRenderer;
    private bool _attached;
    private long _lastFrameTicks;
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

    // SupportsTrueColor is false: SDL v1 uses 16-color VGA palette.
    // Truecolor (24-bit attribute rendering) is deferred to SDL v2.
    public bool SupportsMouse => true;
    public bool SupportsTrueColor => false;

    public Action<IRenderer>? MessageLoop { get; set; }

    public void Initialize()
    {
        // Headless guard: skip SDL when explicitly disabled (e.g., during
        // smoke tests) or when the process can't open a display.
        if (Environment.GetEnvironmentVariable("TSharpVision_NO_SDL") == "1") return;

        bool sdlInitialized = false;
        try
        {
            try
            {
                if (!SDL3.SDL.Init(SDL3.SDL.InitFlags.Video))
                    return; // No display available; stay detached.
                sdlInitialized = true;
            }
            catch (DllNotFoundException) { return; }

            int w = _cellWidth  * GetCols();
            int h = _cellHeight * GetRows();

            if (!SDL3.SDL.CreateWindowAndRenderer("Sharp Vision", w, h, SDL3.SDL.WindowFlags.Resizable, out window, out renderer))
            {
                SDL3.SDL.LogError(SDL3.SDL.LogCategory.Application,
                    $"Error creating window and renderer: {SDL3.SDL.GetError()}");
                SDL3.SDL.Quit();
                return;
            }

            // SDLRenderer construction may throw (font not found).
            // Clean up window, renderer, and SDL state on failure so nothing leaks.
            sdlRenderer = new SDLRenderer(renderer, ScreenDriverFactory.ConfiguredSdlFontName);

            // Adopt font-derived cell dimensions from the renderer,
            // then resize the window so the grid fills the screen correctly.
            _cellWidth  = sdlRenderer.CellWidth;
            _cellHeight = sdlRenderer.CellHeight;
            int finalW = _cellWidth  * GetCols();
            int finalH = _cellHeight * GetRows();
            SDL3.SDL.SetWindowSize(window, finalW, finalH);

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
        catch
        {
            // Clean up any partially-constructed SDL resources.
            try { sdlRenderer?.Dispose(); } catch { }
            sdlRenderer = null;
            if (renderer != IntPtr.Zero) { try { SDL3.SDL.DestroyRenderer(renderer); } catch { } renderer = IntPtr.Zero; }
            if (window   != IntPtr.Zero) { try { SDL3.SDL.DestroyWindow(window);     } catch { } window   = IntPtr.Zero; }
            if (sdlInitialized) { try { SDL3.SDL.Quit(); } catch { } }
            _attached = false;
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

        while (SDL3.SDL.PollEvent(out var e))
        {
            var eventType = (SDL3.SDL.EventType)e.Type;
            switch (eventType)
            {
                case SDL3.SDL.EventType.WindowCloseRequested:
                case SDL3.SDL.EventType.Quit:
                    TEventQueue.Enqueue(MakeQuitEvent());
                    break;

                case SDL3.SDL.EventType.KeyDown:
                {
                    uint kc  = (uint)e.Key.Key;
                    ushort mod = (ushort)e.Key.Mod;
                    _lastModState = mod;

                    // SDL_TEXTINPUT will follow for printable keys
                    // that are not Ctrl/LALT modified. Skip KeyDown for those so
                    // TextInput provides the layout-correct character.
                    bool hasCtrl = (mod & SdlKeyTranslator.SDL_KMOD_CTRL) != 0;
                    bool hasLAlt = (mod & SdlKeyTranslator.SDL_KMOD_LALT) != 0;
                    bool isPrintableRange = (kc >= 0x20 && kc <= 0x7E) ||
                                           (kc >= 'a'  && kc <= 'z');
                    if (isPrintableRange && !hasCtrl && !hasLAlt)
                        break; // TextInput will handle it.

                    if (SdlKeyTranslator.TryTranslate(kc, mod, '\0', out var kev))
                        _pendingKeys.Enqueue(kev);
                    break;
                }

                // SDL_TEXTINPUT: printable text produced by the OS
                // keyboard layout (handles AltGr, dead keys, non-US layouts).
                case SDL3.SDL.EventType.TextInput:
                {
                    // e.Text.Text is an IntPtr to a null-terminated UTF-8 string.
                    string? text = Marshal.PtrToStringUTF8(e.Text.Text);
                    if (string.IsNullOrEmpty(text)) break;

                    // Take the first Unicode scalar value from the input string.
                    // Typical text input is one composed character.
                    Rune rune = Rune.GetRuneAt(text, 0);
                    char ch = rune.Value <= 0xFFFF ? (char)rune.Value : text[0];

                    // Build a key event for the character using the last-seen
                    // modifier state (TextInput doesn't carry its own mod state).
                    ushort shift = SdlKeyTranslator.ToShiftState(_lastModState);
                    TEvent kev = default;
                    kev.What = Events.evKeyDown;
                    kev.keyDown.keyCode = (ushort)ch;
                    kev.keyDown.charScan.charCode = rune.Value <= 0x7F ? (byte)rune.Value : (byte)0;
                    kev.keyDown.shiftState = shift;
                    kev.keyDown.text = text;
                    _pendingKeys.Enqueue(kev);
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
                    // Keep held-button mask in sync for motion events.
                    if (kind == SdlMouseEventKind.Down)
                        _heldButtons |= mev.mouse.buttons;
                    else
                        _heldButtons = 0; // clear on any button release (simple v1 strategy)
                    TEventQueue.Enqueue(mev);
                    break;
                }

                case SDL3.SDL.EventType.MouseMotion:
                {
                    var cell = SdlMouseTranslator.PixelToCell(
                        (int)e.Motion.X, (int)e.Motion.Y, _cellWidth, _cellHeight);
                    // Pass held mask so DragView / TFrame receive it.
                    var mev = SdlMouseTranslator.MakeEvent(
                        SdlMouseEventKind.Move, 0, cell.x, cell.y,
                        heldButtons: _heldButtons);
                    TEventQueue.Enqueue(mev);
                    break;
                }

                // Mouse wheel events.
                case SDL3.SDL.EventType.MouseWheel:
                {
                    // SDL Wheel.MouseX/Y gives the pointer position in pixels.
                    var cell = SdlMouseTranslator.PixelToCell(
                        (int)e.Wheel.MouseX, (int)e.Wheel.MouseY, _cellWidth, _cellHeight);
                    if (SdlMouseTranslator.MakeWheelEvent(e.Wheel.Y, cell.x, cell.y, out var wev))
                        TEventQueue.Enqueue(wev);
                    break;
                }

                case SDL3.SDL.EventType.WindowResized:
                {
                    // Use the renderer's output size so we work correctly on
                    // HiDPI displays where window logical size != pixel size.
                    if (SDL3.SDL.GetCurrentRenderOutputSize(renderer, out int pw, out int ph))
                        HandleWindowResize(pw, ph);
                    break;
                }
            }
        }

        // Frame pacing — throttle the user-supplied render callback to ~60 Hz.
        long now = Environment.TickCount64;
        if (now - _lastFrameTicks >= FrameMs)
        {
            _lastFrameTicks = now;
            if (sdlRenderer != null) MessageLoop?.Invoke(sdlRenderer);
        }
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

        // Force an immediate render on the next PumpMessages call.
        _lastFrameTicks = 0;

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
    }

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
