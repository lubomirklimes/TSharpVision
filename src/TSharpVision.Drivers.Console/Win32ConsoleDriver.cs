// Win32 console IDriver. Uses the Windows console API (kernel32) directly
// because System.Console doesn't deliver mouse / Ctrl-state / window-resize
// events the way upstream needs them.
//
// The driver is platform-guarded: Initialize() only takes over the console
// when running on Windows AND stdin/stdout are attached to a real console
// (NOT redirected). Otherwise it operates as a no-op so headless test
// harnesses keep working.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TSharpVision;
using TSharpVision.Constants;
using TSharpVision.Drivers;

namespace TSharpVision.Drivers.Console;

[ScreenDriver(System = Platform.Windows, Driver = nameof(Win32ConsoleDriver), Priority = 50)]
public sealed class Win32ConsoleDriver : IDriver, IDisposable
{
    // ---- kernel32 P/Invoke surface ------------------------------------
    private const int STD_INPUT_HANDLE  = -10;
    private const int STD_OUTPUT_HANDLE = -11;

    private const uint ENABLE_PROCESSED_INPUT          = 0x0001;
    private const uint ENABLE_LINE_INPUT               = 0x0002;
    private const uint ENABLE_ECHO_INPUT               = 0x0004;
    private const uint ENABLE_WINDOW_INPUT             = 0x0008;
    private const uint ENABLE_MOUSE_INPUT              = 0x0010;
    private const uint ENABLE_QUICK_EDIT_MODE          = 0x0040;
    private const uint ENABLE_EXTENDED_FLAGS           = 0x0080;
    private const uint ENABLE_VIRTUAL_TERMINAL_INPUT   = 0x0200;

    private const uint ENABLE_PROCESSED_OUTPUT             = 0x0001;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING  = 0x0004;

    private const ushort KEY_EVENT    = 0x0001;
    private const ushort MOUSE_EVENT  = 0x0002;
    private const ushort WINDOW_BUFFER_SIZE_EVENT = 0x0004;

    private const uint MOUSE_MOVED        = 0x0001;
    private const uint DOUBLE_CLICK       = 0x0002;
    private const uint MOUSE_WHEELED      = 0x0004;
    private const uint FROM_LEFT_1ST_BUTTON_PRESSED = 0x0001;
    private const uint RIGHTMOST_BUTTON_PRESSED     = 0x0002;
    private const uint FROM_LEFT_2ND_BUTTON_PRESSED = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD { public short X; public short Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SMALL_RECT
    {
        public short Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CONSOLE_SCREEN_BUFFER_INFO
    {
        public COORD dwSize;
        public COORD dwCursorPosition;
        public ushort wAttributes;
        public SMALL_RECT srWindow;
        public COORD dwMaximumWindowSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CONSOLE_CURSOR_INFO
    {
        public uint dwSize;
        public int  bVisible;
    }

    // CharSet.Unicode is required so that the 'char' field is marshaled as
    // WCHAR (2 bytes), matching Win32 CHAR_INFO.Char.UnicodeChar. Without it
    // the default CharSet.Ansi causes 'char' to be marshaled as 1-byte CHAR,
    // truncating every codepoint > U+00FF and corrupting all box-drawing
    // and shading glyphs (▒ ╔ ═ ║ ┌ ─ etc.).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CHAR_INFO
    {
        public char UnicodeChar;
        public ushort Attributes;
    }

    // INPUT_RECORD with a fixed-size payload sized to the largest member.
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_RECORD
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
        [FieldOffset(4)] public MOUSE_EVENT_RECORD MouseEvent;
        [FieldOffset(4)] public WINDOW_BUFFER_SIZE_RECORD WindowBufferSizeEvent;
    }

    // CharSet.Unicode ensures UnicodeChar is marshaled as WCHAR (2 bytes)
    // so that non-ASCII keyboard input (Alt+letter, AltGr etc.) is read correctly.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct KEY_EVENT_RECORD
    {
        public int    bKeyDown;
        public ushort wRepeatCount;
        public ushort wVirtualKeyCode;
        public ushort wVirtualScanCode;
        public char   UnicodeChar;
        public uint   dwControlKeyState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSE_EVENT_RECORD
    {
        public COORD dwMousePosition;
        public uint  dwButtonState;
        public uint  dwControlKeyState;
        public uint  dwEventFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOW_BUFFER_SIZE_RECORD { public COORD dwSize; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr h, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr h, uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleScreenBufferInfo(IntPtr h, out CONSOLE_SCREEN_BUFFER_INFO info);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCursorPosition(IntPtr h, COORD pos);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCursorInfo(IntPtr h, ref CONSOLE_CURSOR_INFO info);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FillConsoleOutputCharacterW(
        IntPtr h, char ch, uint length, COORD writeCoord, out uint written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FillConsoleOutputAttribute(
        IntPtr h, ushort attribute, uint length, COORD writeCoord, out uint written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteConsoleOutputW(
        IntPtr h, [In] CHAR_INFO[] buffer,
        COORD bufferSize, COORD bufferCoord, ref SMALL_RECT writeRegion);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PeekConsoleInputW(
        IntPtr h, [Out] INPUT_RECORD[] buffer, uint length, out uint read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadConsoleInputW(
        IntPtr h, [Out] INPUT_RECORD[] buffer, uint length, out uint read);

    [DllImport("kernel32.dll")]
    private static extern bool MessageBeep(uint type);

    // ---- driver state -------------------------------------------------
    private bool   _attached;
    private IntPtr _hIn  = IntPtr.Zero;
    private IntPtr _hOut = IntPtr.Zero;
    private uint   _savedInMode;
    private uint   _savedOutMode;
    private ushort _cols  = 80;
    private ushort _rows  = 25;
    private ushort _cursorType;
    private readonly Queue<TEvent> _pendingKeys = new();

    public bool SupportsMouse => true;
    public bool SupportsTrueColor => false;

    /// <summary>
    /// Exposed for smoke tests: returns the P/Invoke marshaled size of CHAR_INFO.
    /// With CharSet.Unicode, char = 2 bytes → struct = 4 bytes (matches Win32).
    /// With CharSet.Ansi  (wrong), char = 1 byte + 1 pad = 4 bytes but
    ///   high byte of UnicodeChar is lost, corrupting all non-ASCII glyphs.
    /// </summary>
    public static int CharInfoMarshaledSize =>
        System.Runtime.InteropServices.Marshal.SizeOf<CHAR_INFO>();

    /// <summary>
    /// Exposed for tests: feeds a synthetic key event through the same
    /// translation pipeline the real console uses.
    /// </summary>
    public bool TryTranslateKey(bool keyDown, ushort vk, char ch, uint ctrl, out TEvent ev)
        => Win32KeyTranslator.TryTranslate(keyDown, vk, ch, ctrl, out ev);

    public void Initialize()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Ensure Console.Write/Console.Error paths handle Unicode correctly.
        // This does not affect WriteConsoleOutputW (which is always Unicode),
        // but prevents mojibake if any diagnostic output uses Console.Write.
        System.Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            _hIn  = GetStdHandle(STD_INPUT_HANDLE);
            _hOut = GetStdHandle(STD_OUTPUT_HANDLE);
            if (_hIn == IntPtr.Zero || _hOut == IntPtr.Zero) return;

            // Only attach when both handles refer to a real console.
            if (!GetConsoleMode(_hIn, out _savedInMode))   return;
            if (!GetConsoleMode(_hOut, out _savedOutMode)) return;
            if (System.Console.IsInputRedirected || System.Console.IsOutputRedirected) return;

            uint inMode = (_savedInMode | ENABLE_WINDOW_INPUT | ENABLE_MOUSE_INPUT | ENABLE_EXTENDED_FLAGS)
                          & ~(ENABLE_PROCESSED_INPUT | ENABLE_LINE_INPUT | ENABLE_ECHO_INPUT | ENABLE_QUICK_EDIT_MODE);
            SetConsoleMode(_hIn, inMode);

            uint outMode = _savedOutMode | ENABLE_PROCESSED_OUTPUT | ENABLE_VIRTUAL_TERMINAL_PROCESSING;
            SetConsoleMode(_hOut, outMode);

            if (GetConsoleScreenBufferInfo(_hOut, out var info))
            {
                _cols = (ushort)(info.srWindow.Right - info.srWindow.Left + 1);
                _rows = (ushort)(info.srWindow.Bottom - info.srWindow.Top + 1);
                TScreen.ScreenWidth  = _cols;
                TScreen.ScreenHeight = _rows;
            }

            _attached = true;

            // Register the Windows clipboard service so that editor copy/paste
            // routes through the OS clipboard when this driver is active.
            ClipboardService.Current = new Win32ClipboardService();
        }
        catch
        {
            _attached = false;
        }
    }

    public void Suspend()
    {
        if (!_attached) return;
        SetConsoleMode(_hIn,  _savedInMode);
        SetConsoleMode(_hOut, _savedOutMode);
    }

    public void Resume()
    {
        if (!_attached) return;
        // Re-apply the modes we had after Initialize.
        Initialize();
    }

    public void Shutdown()
    {
        if (!_attached) return;

        // Clear the screen and restore the cursor so the terminal
        // returns to a tidy state after the TSharpVision UI exits.
        // 1. Fill screen with spaces (normal attribute 0x07).
        ClearScreen(_cols, _rows);
        // 2. Park the cursor at (0,0) so the next shell prompt appears at the top.
        SetConsoleCursorPosition(_hOut, new COORD { X = 0, Y = 0 });
        // 3. Restore cursor to the block-visible state.
        var ci = new CONSOLE_CURSOR_INFO { dwSize = 25, bVisible = 1 };
        SetConsoleCursorInfo(_hOut, ref ci);

        // 4. Restore console modes.
        // OR ENABLE_EXTENDED_FLAGS into the saved input mode so that Quick Edit
        // bits inside _savedInMode are honoured by the kernel (the flag is
        // required for ENABLE_QUICK_EDIT_MODE to take effect).
        SetConsoleMode(_hIn,  _savedInMode | ENABLE_EXTENDED_FLAGS);
        SetConsoleMode(_hOut, _savedOutMode);
        _attached = false;
        ClipboardService.Reset();
    }

    public ushort GetCols() => _cols;
    public ushort GetRows() => _rows;
    public TDisplay.SM GetScreenMode() => TDisplay.SM.CO80;
    public void SetScreenMode(TDisplay.SM mode) { /* console size is OS-driven */ }

    public ScreenBuffer AllocateScreenBuffer() => new ScreenBuffer(_cols, _rows);

    public void ClearScreen(ushort cols, ushort rows)
    {
        if (!_attached) return;
        var origin = new COORD { X = 0, Y = 0 };
        uint cells = (uint)cols * rows;
        bool charOk = FillConsoleOutputCharacterW(_hOut, ' ', cells, origin, out uint charWritten);
        bool attrOk = FillConsoleOutputAttribute (_hOut, 0x07, cells, origin, out _);
        if (InputTrace.Enabled)
            InputTrace.Log("ClearScreen",
                $"cols={cols} rows={rows} cells={cells} " +
                $"fillChar={charOk}(written={charWritten}) fillAttr={attrOk}" +
                (!charOk || !attrOk ? $" err={Marshal.GetLastWin32Error()}" : ""));
    }

    public ushort GetCursorType() => _cursorType;

    public void SetCursorType(ushort cursorType)
    {
        _cursorType = cursorType;
        if (!_attached) return;
        var ci = new CONSOLE_CURSOR_INFO
        {
            // tvision: 0 = hidden, 0x0607 = block, otherwise underline.
            dwSize  = (uint)((cursorType >= 100) ? 100 : (cursorType == 0 ? 1 : 25)),
            bVisible = (cursorType == 0) ? 0 : 1,
        };
        SetConsoleCursorInfo(_hOut, ref ci);
    }

    public void SetCaretPosition(int x, int y)
    {
        if (!_attached) return;
        SetConsoleCursorPosition(_hOut, new COORD { X = (short)x, Y = (short)y });
    }

    public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf)
    {
        if (!_attached || w <= 0 || h <= 0) return;
        // CHAR_INFO is laid out (UnicodeChar, Attributes); we marshal one
        // row at a time so the buffer-rect math stays simple.
        var line = new CHAR_INFO[w];
        for (int row = 0; row < h; row++)
        {
            for (int col = 0; col < w; col++)
            {
                var sc = buf[row * w + col];
                line[col].UnicodeChar = sc.Character == 0 ? ' ' : sc.Character;
                line[col].Attributes  = (ushort)sc.Attr;
            }
            var bufSize    = new COORD { X = (short)w, Y = 1 };
            var bufCoord   = new COORD { X = 0, Y = 0 };
            var writeRect  = new SMALL_RECT
            {
                Left   = (short)x,
                Top    = (short)(y + row),
                Right  = (short)(x + w - 1),
                Bottom = (short)(y + row),
            };
            bool writeOk = WriteConsoleOutputW(_hOut, line, bufSize, bufCoord, ref writeRect);
            if (InputTrace.Enabled && !writeOk)
                InputTrace.Log("WriteBuf",
                    $"WriteConsoleOutputW failed: " +
                    $"rect=[{writeRect.Left},{writeRect.Top},{writeRect.Right},{writeRect.Bottom}] " +
                    $"bufSize={w}x1 cols={_cols} rows={_rows} " +
                    $"err={Marshal.GetLastWin32Error()}");
        }
    }

    public void MakeBeep()
    {
        if (OperatingSystem.IsWindows()) MessageBeep(0xFFFFFFFF);
    }

    public void PumpMessages()
    {
        if (!_attached) return;

        // Drain at most 32 records per pump to keep latency bounded.
        var records = new INPUT_RECORD[32];
        if (!PeekConsoleInputW(_hIn, records, (uint)records.Length, out uint avail) || avail == 0)
            return;
        if (!ReadConsoleInputW(_hIn, records, avail, out uint read)) return;

        for (uint i = 0; i < read; i++)
        {
            ref var r = ref records[i];
            switch (r.EventType)
            {
                case KEY_EVENT:
                    if (InputTrace.Enabled)
                        InputTrace.Log("Stage1-NativeKey",
                            $"keyDown={r.KeyEvent.bKeyDown != 0} vk=0x{r.KeyEvent.wVirtualKeyCode:X2} scan=0x{r.KeyEvent.wVirtualScanCode:X2} ch=U+{(int)r.KeyEvent.UnicodeChar:X4} ctrl=0x{r.KeyEvent.dwControlKeyState:X4}");
                    if (Win32KeyTranslator.TryTranslate(
                            r.KeyEvent.bKeyDown != 0,
                            r.KeyEvent.wVirtualKeyCode,
                            r.KeyEvent.UnicodeChar,
                            r.KeyEvent.dwControlKeyState,
                            out var kev))
                    {
                        kev.keyDown.raw_scanCode = (byte)r.KeyEvent.wVirtualScanCode;
                        _pendingKeys.Enqueue(kev);
                        if (InputTrace.Enabled)
                            InputTrace.LogEvent("Stage2-TranslatedKey", kev);
                    }
                    else if (InputTrace.Enabled)
                        InputTrace.Log("Stage2-TranslatedKey", "skipped (key-up or dead modifier)");
                    break;

                case MOUSE_EVENT:
                    var mev = TranslateMouse(r.MouseEvent);
                    if (mev.What != Events.evNothing)
                    {
                        TEventQueue.Enqueue(mev);
                        if (InputTrace.Enabled)
                            InputTrace.LogEvent("Stage2-TranslatedMouse", mev);
                    }
                    break;

                case WINDOW_BUFFER_SIZE_EVENT:
                {
                    ushort prevCols = _cols;
                    ushort prevRows = _rows;
                    // Use GetConsoleScreenBufferInfo to read the VISIBLE window
                    // dimensions (srWindow).  The event's dwSize is the scroll
                    // buffer size, which can differ from the window in CMD.exe
                    // (e.g. buffer 80×9999, window 80×25).  Initialize() already
                    // uses srWindow; we keep the same convention on resize.
                    bool gotInfo = GetConsoleScreenBufferInfo(_hOut, out var resizeInfo);
                    if (gotInfo)
                    {
                        _cols = (ushort)(resizeInfo.srWindow.Right  - resizeInfo.srWindow.Left + 1);
                        _rows = (ushort)(resizeInfo.srWindow.Bottom - resizeInfo.srWindow.Top  + 1);
                    }
                    else
                    {
                        // Fallback: use event buffer size if the API call fails.
                        _cols = (ushort)r.WindowBufferSizeEvent.dwSize.X;
                        _rows = (ushort)r.WindowBufferSizeEvent.dwSize.Y;
                    }
                    if (InputTrace.Enabled)
                        InputTrace.Log("Resize",
                            $"WINDOW_BUFFER_SIZE_EVENT: " +
                            $"bufEvent={r.WindowBufferSizeEvent.dwSize.X}x{r.WindowBufferSizeEvent.dwSize.Y}" +
                            (gotInfo
                                ? $" srWindow=[{resizeInfo.srWindow.Left},{resizeInfo.srWindow.Top}," +
                                  $"{resizeInfo.srWindow.Right},{resizeInfo.srWindow.Bottom}]"
                                : $" (GetCSBI failed err={Marshal.GetLastWin32Error()})") +
                            $" prev={prevCols}x{prevRows} -> {_cols}x{_rows}");
                    TScreen.ScreenWidth  = _cols;
                    TScreen.ScreenHeight = _rows;
                    // Reallocate the screen buffer to match the new dimensions.
                    // The old buffer has the wrong size; keeping it would cause
                    // clipping or out-of-range writes on the next WriteBuf call.
                    TScreen.ScreenBuffer = AllocateScreenBuffer();
                    // Enqueue a resize notification so TProgram can relayout
                    // and force a full redraw on the next HandleEvent cycle.
                    {
                        TEvent resEv = default;
                        resEv.What = TSharpVision.Constants.Events.evCommand;
                        resEv.message.command = TSharpVision.Constants.Views.cmScreenResized;
                        TEventQueue.Enqueue(resEv);
                    }
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Translate a Windows mouse record into a tvision mouse event.
    /// Public for unit tests; the driver itself only uses it internally.
    /// </summary>
    public static TEvent TranslateMouse(uint buttonState, uint eventFlags, short x, short y)
    {
        TEvent ev = default;
        ev.mouse.where = new TPoint(x, y);
        ev.mouse.doubleClick = false;

        // Handle vertical wheel before the regular button logic.
        // Win32 places the signed wheel delta in the high 16 bits of
        // dwButtonState when MOUSE_WHEELED is set.
        // Positive delta = wheel rotated away from user = scroll up (mbButton4).
        // Negative delta = wheel rotated toward user  = scroll down (mbButton5).
        if ((eventFlags & MOUSE_WHEELED) != 0)
        {
            short wheelDelta = (short)((int)buttonState >> 16);
            ev.What = Events.evMouseWheel;
            ev.mouse.buttons = (byte)(wheelDelta > 0 ? Events.mbButton4 : Events.mbButton5);
            return ev;
        }

        // tvision button bitmask: 0x01 left, 0x02 right.
        byte buttons = 0;
        if ((buttonState & FROM_LEFT_1ST_BUTTON_PRESSED) != 0) buttons |= 0x01;
        if ((buttonState & RIGHTMOST_BUTTON_PRESSED)     != 0) buttons |= 0x02;
        ev.mouse.buttons = buttons;
        ev.mouse.doubleClick = (eventFlags & DOUBLE_CLICK) != 0;

        if ((eventFlags & MOUSE_MOVED) != 0)
            ev.What = Events.evMouseMove;
        else if (buttonState != 0)
            ev.What = Events.evMouseDown;
        else
            ev.What = Events.evMouseUp;
        return ev;
    }

    private static TEvent TranslateMouse(MOUSE_EVENT_RECORD m)
        => TranslateMouse(m.dwButtonState, m.dwEventFlags, m.dwMousePosition.X, m.dwMousePosition.Y);

    public bool ReadKeyEvent(out TEvent ev)
    {
        if (_pendingKeys.Count > 0)
        {
            ev = _pendingKeys.Dequeue();
            if (InputTrace.Enabled)
                InputTrace.LogEvent("Stage2b-ReadKeyEvent(dequeued)", ev);
            return true;
        }
        ev = default;
        return false;
    }

    public void Dispose() => Shutdown();
}

