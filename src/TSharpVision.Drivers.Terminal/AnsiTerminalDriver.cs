// Source: tvision/linux/* (NB: not present in this checkout — the upstream
// Linux driver lives in `tvision/win32/winntcli.cc` for the redirected-stdin
// fallback path; the runtime sequences here are standard xterm/VT220 from
// `man console_codes`).
//
// Selected by the factory on Linux/macOS.
//
// Lifecycle expectations:
//  * Initialize/Shutdown switch the controlling TTY between cooked and
//    raw mode via termios (libc P/Invoke). On platforms without /dev/tty
//    the driver leaves itself unattached and short-circuits every call,
//    matching the Win32 driver's headless-CI behaviour.
//  * Output uses xterm escape sequences emitted on stdout via Console.Out
//    so we don't compete with .NET buffering.
//  * Input uses unistd `read(0, …)` through libc and feeds the byte stream
//    through AnsiKeyDecoder + AnsiMouseDecoder.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers;

namespace TSharpVision.Drivers.Terminal;

/// <summary>ANSI/VT terminal backend with POSIX raw-mode input; initialization remains inactive on Windows or without an attached terminal.</summary>
[ScreenDriver(System = Platform.Linux,   Driver = nameof(AnsiTerminalDriver), Priority = 50)]
[ScreenDriver(System = Platform.MacOS,   Driver = nameof(AnsiTerminalDriver), Priority = 50)]
// Priority = 10 on Windows: below Win32ConsoleDriver (50) so the native driver wins
// by default. TSHARPVISION_DRIVER="AnsiTerminalDriver" can override for decoder testing.
// Initialize() short-circuits on Windows, so no TTY side-effects occur.
[ScreenDriver(System = Platform.Windows, Driver = nameof(AnsiTerminalDriver), Priority = 10)]
public sealed class AnsiTerminalDriver : IDriver, IDisposable
{
    // ---- libc P/Invoke -------------------------------------------------
    // termios.h struct on Linux. macOS layout differs slightly but the
    // tcgetattr/tcsetattr pair only needs the raw bytes preserved across
    // the round-trip, so we marshal it as a 60-byte blob. (The size is
    // chosen large enough for both glibc and macOS libc.)
    private const int TermiosBytes = 128;

    private const int STDIN_FILENO  = 0;
    private const int STDOUT_FILENO = 1;
    private const int TCSANOW = 0;

    private const ushort TIOCGWINSZ_LINUX = 0x5413;
    private const ulong  TIOCGWINSZ_MAC   = 0x40087468;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel;
        public ushort ws_ypixel;
    }

    [DllImport("libc", EntryPoint = "tcgetattr", SetLastError = true)]
    private static extern int tcgetattr(int fd, IntPtr termios);

    [DllImport("libc", EntryPoint = "tcsetattr", SetLastError = true)]
    private static extern int tcsetattr(int fd, int optionalActions, IntPtr termios);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int ioctl(int fd, ulong req, ref WinSize ws);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern unsafe int read(int fd, byte* buf, int count);

    [DllImport("libc", EntryPoint = "isatty", SetLastError = true)]
    private static extern int isatty(int fd);

    [DllImport("libc", EntryPoint = "cfmakeraw", SetLastError = true)]
    private static extern void cfmakeraw(IntPtr termios);

    // poll() — POSIX-standard non-blocking readability check. Used in
    // PumpMessages to guard read() so it never blocks when no data is ready.
    // This is simpler and more portable than patching VMIN/VTIME byte offsets
    // in the opaque termios blob (which differ between Linux and macOS).
    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }

    private const short POLLIN = 0x0001;

    [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static extern int poll(ref PollFd fds, uint nfds, int timeout);

    // ---- driver state --------------------------------------------------
    private bool   _attached;
    private IntPtr _savedTermios = IntPtr.Zero;
    private ushort _cols = 80;
    private ushort _rows = 25;
    private ushort _cursorType;
    private int _caretX;
    private int _caretY;
    private bool _installedClipboardService;
    private readonly Queue<TEvent> _pendingKeys = new();
    private readonly TerminalInputDecoder _input = new();

    /// <inheritdoc />
    public bool SupportsMouse    => true;
    /// <inheritdoc />
    public bool SupportsTrueColor => true;
    /// <inheritdoc />
    public bool SupportsGraphics  => false;
    /// <summary>
    /// Gets keyboard features confirmed at runtime through Kitty protocol negotiation;
    /// legacy ANSI mode reports <see cref="KeyboardCapabilities.None"/>.
    /// </summary>
    public KeyboardCapabilities KeyboardCapabilities => _input.KeyboardCapabilities;

    /// <inheritdoc />
    public void Initialize()
    {
        if (OperatingSystem.IsWindows()) return;
        if (_attached) return;
        try
        {
            // Both stdin and stdout must be a real TTY.
            if (isatty(STDIN_FILENO) == 0 || isatty(STDOUT_FILENO) == 0) return;

            // Free any previously saved termios (double-call / Resume guard).
            if (_savedTermios != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_savedTermios);
                _savedTermios = IntPtr.Zero;
            }
            _savedTermios = Marshal.AllocHGlobal(TermiosBytes);
            if (tcgetattr(STDIN_FILENO, _savedTermios) != 0)
            {
                Marshal.FreeHGlobal(_savedTermios);
                _savedTermios = IntPtr.Zero;
                return;
            }

            // Apply a raw clone of the saved termios.
            IntPtr raw = Marshal.AllocHGlobal(TermiosBytes);
            try
            {
                unsafe
                {
                    Buffer.MemoryCopy(
                        (void*)_savedTermios, (void*)raw,
                        TermiosBytes, TermiosBytes);
                }
                cfmakeraw(raw);
                tcsetattr(STDIN_FILENO, TCSANOW, raw);
            }
            finally { Marshal.FreeHGlobal(raw); }

            // Enable alternate screen + xterm SGR button-motion mouse + hide cursor.
            Write("\x1b[?1049h");           // alt screen
            Write("\x1b[?25l");              // hide cursor
            Write("\x1b[?1002h\x1b[?1006h"); // mouse press/drag + SGR encoding

            // Query window size.
            var ws = default(WinSize);
            ulong req = OperatingSystem.IsMacOS() ? TIOCGWINSZ_MAC : (ulong)TIOCGWINSZ_LINUX;
            if (ioctl(STDOUT_FILENO, req, ref ws) == 0 && ws.ws_col > 0)
            {
                _cols = ws.ws_col;
                _rows = ws.ws_row;
                TScreen.ScreenWidth  = _cols;
                TScreen.ScreenHeight = _rows;
            }

            _attached = true;

            WriteControl(_input.BeginKeyboardNegotiation());

            if (ScreenDriverFactory.WindowTitle is { } title)
                Write($"\x1b]0;{title}\x07");

            ClipboardService.Current = new TerminalClipboardService();
            _installedClipboardService = true;
        }
        catch
        {
            _input.EndKeyboardMode();
            _pendingKeys.Clear();
            if (_savedTermios != IntPtr.Zero)
            {
                tcsetattr(STDIN_FILENO, TCSANOW, _savedTermios);
                Marshal.FreeHGlobal(_savedTermios);
                _savedTermios = IntPtr.Zero;
            }
            if (_installedClipboardService)
            {
                ClipboardService.Reset();
                _installedClipboardService = false;
            }
            _attached = false;
        }
    }

    /// <inheritdoc />
    public void Suspend()
    {
        if (!_attached) return;
        if (_input.EndKeyboardMode() is { } restoreKeyboard)
            WriteControl(restoreKeyboard);
        _pendingKeys.Clear();
        WriteControl("\x1b[?1006l\x1b[?1002l\x1b[?25h\x1b[?1049l");
        if (_savedTermios != IntPtr.Zero)
            tcsetattr(STDIN_FILENO, TCSANOW, _savedTermios);
    }

    /// <inheritdoc />
    public void Resume()
    {
        if (!_attached) return;
        // Re-apply raw mode without re-saving termios. _savedTermios still holds
        // the original cooked state captured by Initialize(). The OS may have
        // restored the saved (cooked) termios after SIGTSTP, so we must
        // re-apply cfmakeraw.
        if (_savedTermios != IntPtr.Zero)
        {
            IntPtr raw = Marshal.AllocHGlobal(TermiosBytes);
            try
            {
                unsafe
                {
                    Buffer.MemoryCopy(
                        (void*)_savedTermios, (void*)raw,
                        TermiosBytes, TermiosBytes);
                }
                cfmakeraw(raw);
                tcsetattr(STDIN_FILENO, TCSANOW, raw);
            }
            finally { Marshal.FreeHGlobal(raw); }
        }
        WriteControl("\x1b[?1049h\x1b[?25l\x1b[?1002h\x1b[?1006h"
            + _input.BeginKeyboardNegotiation());
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        if (!_attached) return;
        Suspend();
        if (_savedTermios != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_savedTermios);
            _savedTermios = IntPtr.Zero;
        }
        if (_installedClipboardService)
        {
            ClipboardService.Reset();
            _installedClipboardService = false;
        }
        _attached = false;
    }

    /// <inheritdoc />
    public ushort GetCols() => _cols;
    /// <inheritdoc />
    public ushort GetRows() => _rows;
    /// <inheritdoc />
    public TDisplay.SM GetScreenMode() => TDisplay.SM.CO80;
    /// <summary>Accepts a logical screen-mode request without changing this backend's display mode.</summary>
    public void SetScreenMode(TDisplay.SM mode) { /* TTY size is OS-driven */ }
    /// <inheritdoc />
    public ScreenBuffer AllocateScreenBuffer() => new ScreenBuffer(_cols, _rows);

    /// <inheritdoc />
    public void ClearScreen(ushort cols, ushort rows)
    {
        if (!_attached) return;
        Write("\x1b[2J\x1b[H");
        Console.Out.Flush();
    }

    /// <inheritdoc />
    public ushort GetCursorType() => _cursorType;

    /// <inheritdoc />
    public void SetCursorType(ushort cursorType)
    {
        _cursorType = cursorType;
        if (!_attached) return;
        Write(cursorType == 0 ? "\x1b[?25l" : "\x1b[?25h");
        Console.Out.Flush();
    }

    /// <inheritdoc />
    public void SetCaretPosition(int x, int y)
    {
        _caretX = x;
        _caretY = y;
        if (!_attached) return;
        // CSI rows are 1-based.
        Write($"\x1b[{y + 1};{x + 1}H");
        Console.Out.Flush();
    }

    /// <inheritdoc />
    public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf)
    {
        if (!_attached || w <= 0 || h <= 0) return;
        Write(FormatBuffer(x, y, w, h, buf, _caretX, _caretY));
        Console.Out.Flush();
    }

    // Each redraw owns its scratch buffer. Session output can cause a redraw on
    // a reader task while the UI thread is drawing a move or resize.
    internal static string FormatBuffer(int x, int y, int w, int h, Span<TScreenChar> buf,
        int caretX, int caretY)
    {
        var sb = new System.Text.StringBuilder();
        sb.EnsureCapacity(Math.Min(Math.Max(w * h + 32, 256), 64 * 1024));
        TColorAttr lastAttr = default;
        bool first = true;
        for (int row = 0; row < h; row++)
        {
            AppendCaretPosition(sb, x, y + row);
            for (int col = 0; col < w; col++)
            {
                var sc = buf[row * w + col];
                if (first || (byte)sc.Attr != (byte)lastAttr)
                {
                    sb.Append(AttrToSgr(sc.Attr));
                    lastAttr = sc.Attr;
                    first = false;
                }
                sb.Append(sc.Character == 0 ? ' ' : sc.Character);
            }
        }
        sb.Append("\x1b[0m");
        AppendCaretPosition(sb, caretX, caretY);
        return sb.ToString();
    }

    /// <inheritdoc />
    public void MakeBeep()
    {
        if (_attached) Write("\a");
    }

    /// <inheritdoc />
    public unsafe void PumpMessages()
    {
        if (!_attached) return;
        // Non-blocking input drain. poll() with timeout=0 checks whether stdin
        // has data before each read() call, preventing the blocking stall that
        // cfmakeraw VMIN=1 (its default) would otherwise cause on idle frames.
        var pfd = new PollFd { fd = STDIN_FILENO, events = POLLIN };
        Span<byte> tmp = stackalloc byte[256];
        fixed (byte* p = tmp)
        {
            while (poll(ref pfd, 1, 0) > 0)
            {
                int n = read(STDIN_FILENO, p, tmp.Length);
                if (n <= 0) break;
                _input.Feed(tmp[..n], WriteControl);
            }
        }
        PublishNextInputEvent();
        PollResize();
    }

    /// <summary>
    /// Poll the terminal size via ioctl(TIOCGWINSZ). When dimensions change,
    /// updates TScreen and enqueues cmScreenResized, mirroring the Win32
    /// WINDOW_BUFFER_SIZE_EVENT behaviour.
    /// </summary>
    private void PollResize()
    {
        if (!_attached) return;
        var ws = default(WinSize);
        ulong req = OperatingSystem.IsMacOS() ? TIOCGWINSZ_MAC : (ulong)TIOCGWINSZ_LINUX;
        if (ioctl(STDOUT_FILENO, req, ref ws) != 0) return;
        if (ws.ws_col == 0 || ws.ws_row == 0) return;
        if (ws.ws_col == _cols && ws.ws_row == _rows) return;
        _cols = ws.ws_col;
        _rows = ws.ws_row;
        TScreen.ScreenWidth  = _cols;
        TScreen.ScreenHeight = _rows;
        TScreen.ScreenBuffer = AllocateScreenBuffer();
        {
            TEvent resEv = default;
            resEv.What = Events.evCommand;
            resEv.message.command = Views.cmScreenResized;
            TEventQueue.Enqueue(resEv);
        }
    }

    private void PublishNextInputEvent()
    {
        // Publish one native event per framework pump. Holding later mouse input
        // behind a pending key preserves the original byte/event order despite
        // the framework's separate mouse and keyboard retrieval paths.
        if (_pendingKeys.Count != 0 || !_input.TryRead(out TEvent ev)) return;
        if ((ev.What & Events.evMouse) != 0)
            TEventQueue.Enqueue(ev);
        else
            _pendingKeys.Enqueue(ev);
    }

    /// <inheritdoc />
    public bool ReadKeyEvent(out TEvent ev)
    {
        if (_pendingKeys.Count > 0) { ev = _pendingKeys.Dequeue(); return true; }
        ev = default;
        return false;
    }

    /// <summary>Shuts down the backend and releases its display and input resources.</summary>
    public void Dispose() => Shutdown();

    internal string BeginKeyboardNegotiationForTesting() => _input.BeginKeyboardNegotiation();
    internal void FeedInputForTesting(ReadOnlySpan<byte> bytes, Action<string> output)
        => _input.Feed(bytes, output);

    // ---- helpers -------------------------------------------------------

    private static void Write(string s) => Console.Write(s);

    private static void WriteControl(string value)
    {
        try
        {
            Console.Write(value);
            Console.Out.Flush();
        }
        catch (IOException)
        {
            // Capability and restore writes are best-effort. A failed outer
            // terminal transport must not break the application input loop.
        }
    }

    private static void AppendCaretPosition(System.Text.StringBuilder sb, int x, int y)
    {
        // CSI rows/columns are 1-based.
        sb.Append("\x1b[");
        sb.Append(y + 1);
        sb.Append(';');
        sb.Append(x + 1);
        sb.Append('H');
    }

    /// <summary>
    /// Translate a tvision <see cref="TColorAttr"/> (FG nibble | BG nibble)
    /// into an SGR escape sequence using the same VGA palette as the SDL driver.
    /// </summary>
    internal static string AttrToSgr(TColorAttr attr)
        => SgrCache[(byte)attr];

    private static string[] BuildSgrCache()
    {
        var cache = new string[256];
        for (int raw = 0; raw < cache.Length; raw++)
        {
            int fg = raw & 0x0F;
            int bg = (raw >> 4) & 0x0F;
            cache[raw] = $"\x1b[0;{VgaToSgr(fg, background: false)};{VgaToSgr(bg, background: true)}m";
        }
        return cache;
    }

    private static string VgaToSgr(int color, bool background)
    {
        uint rgb = Vga16[color & 0x0F];
        byte r = (byte)((rgb >> 16) & 0xFF);
        byte g = (byte)((rgb >> 8) & 0xFF);
        byte b = (byte)(rgb & 0xFF);
        return background
            ? $"48;2;{r};{g};{b}"
            : $"38;2;{r};{g};{b}";
    }

    /// <summary>16-color VGA palette as 0xAARRGGBB, matching SdlPalette.Vga16.</summary>
    private static readonly uint[] Vga16 =
    {
        0xFF000000, // 0  Black
        0xFF0000AA, // 1  Blue
        0xFF00AA00, // 2  Green
        0xFF00AAAA, // 3  Cyan
        0xFFAA0000, // 4  Red
        0xFFAA00AA, // 5  Magenta
        0xFFAA5500, // 6  Brown
        0xFFAAAAAA, // 7  Light Gray
        0xFF555555, // 8  Dark Gray
        0xFF5555FF, // 9  Bright Blue
        0xFF55FF55, // 10 Bright Green
        0xFF55FFFF, // 11 Bright Cyan
        0xFFFF5555, // 12 Bright Red
        0xFFFF55FF, // 13 Bright Magenta
        0xFFFFFF55, // 14 Yellow
        0xFFFFFFFF, // 15 White
    };

    private static readonly string[] SgrCache = BuildSgrCache();
}
