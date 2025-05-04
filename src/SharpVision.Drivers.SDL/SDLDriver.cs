using SharpVision.Constants;
using System.Runtime.InteropServices;
using System.Text;

namespace SharpVision.Drivers.SDL;

// NOTE: priority intentionally lower than the platform-native console
// drivers so headless CI keeps using NullDriver / Win32 console / ANSI.
// Set SHARPVISION_DRIVER=SDLDriver to force the SDL window.
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

    private bool disposedValue;

    private IntPtr window;
    private IntPtr renderer;
    private ScreenBuffer? screenBuffer;
    private SDLRenderer? sdlRenderer;
    private bool _attached;
    private long _lastFrameTicks;
    private ushort _cursorType;
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
        if (Environment.GetEnvironmentVariable("SHARPVISION_NO_SDL") == "1") return;

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

            if (!SDL3.SDL.CreateWindowAndRenderer("Sharp Vision", w, h, 0, out window, out renderer))
            {
                SDL3.SDL.LogError(SDL3.SDL.LogCategory.Application,
                    $"Error creating window and renderer: {SDL3.SDL.GetError()}");
                SDL3.SDL.Quit();
                return;
            }

            // SDLRenderer construction may throw (font not found).
            // Clean up window, renderer, and SDL state on failure so nothing leaks.
            sdlRenderer = new SDLRenderer(renderer);

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

    public ushort GetCols()  => 120;
    public ushort GetRows()  => 37;
    public ushort GetCursorType() => 100;

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
                    TEventQueue.Enqueue(new TEvent { What = Events.evCommand,
                                                     message = { command = 0xFFFF /* cmQuit */ } });
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

    public void SetCursorType(ushort cursorType) { _cursorType = cursorType; }

    public void Suspend() { /* SDL has no cooked-mode equivalent */ }
    public void Resume()  { if (!_attached) Initialize(); }
    public void Shutdown()
    {
        if (!_attached) return;
        try { sdlRenderer?.Dispose(); } catch { }
        sdlRenderer = null;
        if (renderer != IntPtr.Zero) { SDL3.SDL.DestroyRenderer(renderer); renderer = IntPtr.Zero; }
        if (window   != IntPtr.Zero) { SDL3.SDL.DestroyWindow(window);     window   = IntPtr.Zero; }
        SDL3.SDL.Quit();
        _attached = false;
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

    public void SetCaretPosition(int x, int y) { _caretX = x; _caretY = y; }

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
