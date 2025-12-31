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
    private readonly List<byte> _pendingBytes = new(64);

    public bool SupportsMouse    => true;
    public bool SupportsTrueColor => true;

    public void Initialize()
    {
        if (OperatingSystem.IsWindows()) return;
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
            ClipboardService.Current = new TerminalClipboardService();
            _installedClipboardService = true;
        }
        catch
        {
            _attached = false;
        }
    }

    public void Suspend()
    {
        if (!_attached) return;
        Write("\x1b[?1006l\x1b[?1002l"); // mouse off
        Write("\x1b[?25h");                // cursor on
        Write("\x1b[?1049l");             // primary screen
        if (_savedTermios != IntPtr.Zero)
            tcsetattr(STDIN_FILENO, TCSANOW, _savedTermios);
    }

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
        Write("\x1b[?1049h");
        Write("\x1b[?25l");
        Write("\x1b[?1002h\x1b[?1006h");
        Console.Out.Flush();
    }

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

    public ushort GetCols() => _cols;
    public ushort GetRows() => _rows;
    public TDisplay.SM GetScreenMode() => TDisplay.SM.CO80;
    public void SetScreenMode(TDisplay.SM mode) { /* TTY size is OS-driven */ }
    public ScreenBuffer AllocateScreenBuffer() => new ScreenBuffer(_cols, _rows);

    public void ClearScreen(ushort cols, ushort rows)
    {
        if (!_attached) return;
        Write("\x1b[2J\x1b[H");
        Console.Out.Flush();
    }

    public ushort GetCursorType() => _cursorType;

    public void SetCursorType(ushort cursorType)
    {
        _cursorType = cursorType;
        if (!_attached) return;
        Write(cursorType == 0 ? "\x1b[?25l" : "\x1b[?25h");
        Console.Out.Flush();
    }

    public void SetCaretPosition(int x, int y)
    {
        _caretX = x;
        _caretY = y;
        if (!_attached) return;
        // CSI rows are 1-based.
        Write($"\x1b[{y + 1};{x + 1}H");
        Console.Out.Flush();
    }

    public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf)
    {
        if (!_attached || w <= 0 || h <= 0) return;
        var sb = new System.Text.StringBuilder(w * h + 32);
        TColorAttr lastAttr = default;
        bool first = true;
        for (int row = 0; row < h; row++)
        {
            sb.Append($"\x1b[{y + row + 1};{x + 1}H");
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
        AppendCaretPosition(sb);
        Write(sb.ToString());
        Console.Out.Flush();
    }

    public void MakeBeep()
    {
        if (_attached) Write("\a");
    }

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
                for (int i = 0; i < n; i++) _pendingBytes.Add(tmp[i]);
            }
        }
        DrainBuffer();
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

    private void DrainBuffer()
    {
        while (_pendingBytes.Count > 0)
        {
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pendingBytes);

            // Try mouse first (its prefix ESC[< is unambiguous).
            int consumed = AnsiMouseDecoder.TryDecode(span, out var mev, out bool mc);
            if (consumed > 0)
            {
                if (mev.What != Events.evNothing) TEventQueue.Enqueue(mev);
                _pendingBytes.RemoveRange(0, consumed);
                continue;
            }
            if (!mc && consumed == 0) return; // need more bytes

            consumed = AnsiKeyDecoder.TryDecode(span, out var kev, out bool kc);
            if (!kc && consumed == 0) return;
            if (consumed == 0) return; // safety: shouldn't happen
            if (kev.What == Events.evKeyDown) _pendingKeys.Enqueue(kev);
            _pendingBytes.RemoveRange(0, consumed);
        }
    }

    public bool ReadKeyEvent(out TEvent ev)
    {
        if (_pendingKeys.Count > 0) { ev = _pendingKeys.Dequeue(); return true; }
        ev = default;
        return false;
    }

    public void Dispose() => Shutdown();

    // ---- helpers -------------------------------------------------------

    private static void Write(string s) => Console.Write(s);

    private void AppendCaretPosition(System.Text.StringBuilder sb)
        => sb.Append($"\x1b[{_caretY + 1};{_caretX + 1}H");

    /// <summary>
    /// Translate a tvision <see cref="TColorAttr"/> (FG nibble | BG nibble)
    /// into an SGR escape sequence using the same VGA palette as the SDL driver.
    /// </summary>
    public static string AttrToSgr(TColorAttr attr)
    {
        byte raw = (byte)attr;
        int fg = raw & 0x0F;
        int bg = (raw >> 4) & 0x0F;
        return $"\x1b[0;{VgaToSgr(fg, background: false)};{VgaToSgr(bg, background: true)}m";
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
}
