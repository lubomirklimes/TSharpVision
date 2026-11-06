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

/// <summary>How <see cref="Win32ConsoleDriver"/> writes cells to the console host.</summary>
internal enum Win32TextTier
{
    /// <summary>T1: console cells through <c>WriteConsoleOutputW</c> (conhost, unknown hosts).</summary>
    Cells,
    /// <summary>T2: rows with clusters as VT text through <c>WriteConsoleW</c> (Windows Terminal).</summary>
    Vt,
}

/// <summary>Windows console backend for cell output and native keyboard, mouse, and resize events; remains inactive for redirected input or output.</summary>
[ScreenDriver(System = Platform.Windows, Driver = nameof(Win32ConsoleDriver), Priority = 50)]
public sealed class Win32ConsoleDriver : IDriver, IIdleWaitDriver, IDisposable
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
    private const uint MOUSE_HWHEELED     = 0x0008;
    private const uint FROM_LEFT_1ST_BUTTON_PRESSED = 0x0001;
    private const uint RIGHTMOST_BUTTON_PRESSED     = 0x0002;
    private const uint FROM_LEFT_2ND_BUTTON_PRESSED = 0x0004;
    private const uint FROM_LEFT_3RD_BUTTON_PRESSED = 0x0008;
    private const uint FROM_LEFT_4TH_BUTTON_PRESSED = 0x0010;

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
    internal struct CHAR_INFO
    {
        public char UnicodeChar;
        public ushort Attributes;
    }

    // CHAR_INFO.Attributes flags marking the two halves of a double-width character.
    internal const ushort COMMON_LVB_LEADING_BYTE  = 0x0100;
    internal const ushort COMMON_LVB_TRAILING_BYTE = 0x0200;

    private const uint ENABLE_WRAP_AT_EOL_OUTPUT = 0x0002;
    private const uint GW_OWNER = 4;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WriteConsoleW(
        IntPtr h, string buffer, uint length, out uint written, IntPtr reserved);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern unsafe int GetClassNameW(IntPtr hwnd, char* name, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern unsafe bool QueryFullProcessImageNameW(IntPtr process, uint flags, char* name, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool MessageBeep(uint type);

    // ---- driver state -------------------------------------------------
    private bool   _attached;
    private volatile bool _stopped;
    private IntPtr _hIn  = IntPtr.Zero;
    private IntPtr _hOut = IntPtr.Zero;
    private uint   _savedInMode;
    private uint   _savedOutMode;
    private ushort _cols  = 80;
    private ushort _rows  = 25;
    private ushort _cursorType;
    private readonly Queue<TEvent> _pendingKeys = new();
    private readonly Win32InputTranslator _inputTranslator = new();
    private CHAR_INFO[] _writeLine = Array.Empty<CHAR_INFO>();
    private readonly INPUT_RECORD[] _inputRecords = new INPUT_RECORD[32];
    private Win32TextTier _textTier = Win32TextTier.Cells;
    private string _hostDescription = "unknown";
    private ushort _defaultAttributes = 0x07;
    private int _caretX;
    private int _caretY;
    private readonly System.Text.StringBuilder _vtOutput = new();

    /// <summary>Environment variable that overrides host detection: <c>vt</c> or <c>cells</c>.</summary>
    internal const string TextTierVariable = "TSHARPVISION_WIN32_OUTPUT";

    /// <inheritdoc />
    public bool SupportsMouse    => true;
    /// <inheritdoc />
    public bool SupportsTrueColor => false;
    /// <inheritdoc />
    public bool SupportsGraphics  => false;
    /// <inheritdoc />
    public KeyboardCapabilities KeyboardCapabilities =>
        KeyboardCapabilities.KeyReleaseEvents | KeyboardCapabilities.StandaloneModifierTransitions
        | KeyboardCapabilities.DistinctNumericKeypad;

    /// <summary>
    /// Gets how grapheme clusters are drawn: double-width BMP characters as console lead/trail cell pairs on every
    /// console host; whole clusters, including supplementary characters and colour emoji, only when the host was
    /// identified as Windows Terminal and output goes through VT sequences. Inactive drivers report none.
    /// </summary>
    public TextCapabilities TextCapabilities => !_attached ? TextCapabilities.None
        : _textTier == Win32TextTier.Vt
            ? TextCapabilities.WideCells | TextCapabilities.ClusterText | TextCapabilities.ColorGlyphs
            : TextCapabilities.WideCells;

    /// <summary>The output tier chosen for the attached console host.</summary>
    internal Win32TextTier TextTier => _textTier;

    /// <summary>What host detection saw (console window class, owner window class, owner process).</summary>
    internal string HostDescription => _hostDescription;

    /// <summary>
    /// Exposed for smoke tests: returns the P/Invoke marshaled size of CHAR_INFO.
    /// With CharSet.Unicode, char = 2 bytes → struct = 4 bytes (matches Win32).
    /// With CharSet.Ansi  (wrong), char = 1 byte + 1 pad = 4 bytes but
    ///   high byte of UnicodeChar is lost, corrupting all non-ASCII glyphs.
    /// </summary>
    internal static int CharInfoMarshaledSize =>
        System.Runtime.InteropServices.Marshal.SizeOf<CHAR_INFO>();

    /// <summary>
    /// Exposed for tests: feeds a synthetic key event through the same
    /// translation pipeline the real console uses.
    /// </summary>
    internal bool TryTranslateKey(bool keyDown, ushort vk, char ch, uint ctrl, out TEvent ev)
        => Win32KeyTranslator.TryTranslate(keyDown, vk, ch, ctrl, out ev);

    /// <inheritdoc />
    public void Initialize()
    {
        _stopped = false;
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

            _textTier = DetectTextTier(out _hostDescription);

            uint outMode = _savedOutMode | ENABLE_PROCESSED_OUTPUT | ENABLE_VIRTUAL_TERMINAL_PROCESSING;
            // The VT tier writes text at the cursor: autowrap off, so nothing written can wrap or scroll.
            if (_textTier == Win32TextTier.Vt) outMode &= ~ENABLE_WRAP_AT_EOL_OUTPUT;
            SetConsoleMode(_hOut, outMode);

            if (GetConsoleScreenBufferInfo(_hOut, out var info))
            {
                _cols = (ushort)(info.srWindow.Right - info.srWindow.Left + 1);
                _rows = (ushort)(info.srWindow.Bottom - info.srWindow.Top + 1);
                _defaultAttributes = info.wAttributes;
                TScreen.ScreenWidth  = _cols;
                TScreen.ScreenHeight = _rows;
            }

            _attached = true;
            if (_textTier == Win32TextTier.Vt)
            {
                WriteVt("\x1b[?7l");
                // Autowrap belongs to the console session the shell keeps using: restore it even when the
                // process ends without Shutdown.
                AppDomain.CurrentDomain.ProcessExit -= OnProcessEnding;
                AppDomain.CurrentDomain.ProcessExit += OnProcessEnding;
                AppDomain.CurrentDomain.UnhandledException -= OnProcessEnding;
                AppDomain.CurrentDomain.UnhandledException += OnProcessEnding;
            }
            if (InputTrace.Enabled)
                InputTrace.Log("TextTier", $"tier={_textTier} host={_hostDescription}");

            if (ScreenDriverFactory.WindowTitle is { } title)
                System.Console.Title = title;

            // Register the Windows clipboard service so that editor copy/paste
            // routes through the OS clipboard when this driver is active.
            ClipboardService.Current = new Win32ClipboardService();
        }
        catch
        {
            _attached = false;
        }
    }

    /// <inheritdoc />
    public void Suspend()
    {
        if (!_attached) return;
        RestoreAutowrap();
        SetConsoleMode(_hIn,  _savedInMode);
        SetConsoleMode(_hOut, _savedOutMode);
    }

    // Undo the VT tier's DECAWM reset while VT processing is still on; the saved output mode restores
    // ENABLE_WRAP_AT_EOL_OUTPUT right after.
    private void RestoreAutowrap()
    {
        if (_textTier == Win32TextTier.Vt) WriteVt("\x1b[?7h");
    }

    private void OnProcessEnding(object? sender, EventArgs e)
    {
        try { if (_attached) RestoreAutowrap(); }
        catch { /* best effort while the process ends */ }
    }

    /// <inheritdoc />
    public void Resume()
    {
        if (!_attached) return;
        // Re-apply the modes we had after Initialize.
        Initialize();
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        _stopped = true;
        TEventQueue.Wake();
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

        // 4. Restore console modes (and autowrap, which the VT tier turned off).
        RestoreAutowrap();
        AppDomain.CurrentDomain.ProcessExit -= OnProcessEnding;
        AppDomain.CurrentDomain.UnhandledException -= OnProcessEnding;
        // OR ENABLE_EXTENDED_FLAGS into the saved input mode so that Quick Edit
        // bits inside _savedInMode are honoured by the kernel (the flag is
        // required for ENABLE_QUICK_EDIT_MODE to take effect).
        SetConsoleMode(_hIn,  _savedInMode | ENABLE_EXTENDED_FLAGS);
        SetConsoleMode(_hOut, _savedOutMode);
        _attached = false;
        ClipboardService.Reset();
    }

    /// <inheritdoc />
    public ushort GetCols() => _cols;
    /// <inheritdoc />
    public ushort GetRows() => _rows;
    /// <inheritdoc />
    public TDisplay.SM GetScreenMode() => TDisplay.SM.CO80;
    /// <summary>Accepts a logical screen-mode request without changing this backend's display mode.</summary>
    public void SetScreenMode(TDisplay.SM mode) { /* console size is OS-driven */ }

    /// <inheritdoc />
    public ScreenBuffer AllocateScreenBuffer() => new ScreenBuffer(_cols, _rows);

    /// <inheritdoc />
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

    /// <inheritdoc />
    public ushort GetCursorType() => _cursorType;

    /// <inheritdoc />
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

    /// <inheritdoc />
    public void SetCaretPosition(int x, int y)
    {
        _caretX = x;
        _caretY = y;
        if (!_attached) return;
        SetConsoleCursorPosition(_hOut, new COORD { X = (short)x, Y = (short)y });
    }

    /// <inheritdoc />
    public void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> buf)
    {
        if (!_attached || w <= 0 || h <= 0) return;
        bool legacy = TSharpVision.Text.TextLayout.DefaultProfile == TSharpVision.Text.TextWidthProfile.Legacy;
        // CHAR_INFO is laid out (UnicodeChar, Attributes); we marshal one
        // row at a time so the buffer-rect math stays simple.
        var line = EnsureWriteLineCapacity(w);
        System.Text.StringBuilder? vt = null;
        for (int row = 0; row < h; row++)
        {
            ReadOnlySpan<TScreenChar> cells = buf.Slice(row * w, w);
            // Windows Terminal: rows holding text that console cells cannot carry go out as VT text.
            if (!legacy && _textTier == Win32TextTier.Vt && NeedsVtOutput(cells))
            {
                if (vt is null) { vt = _vtOutput; vt.Clear(); }
                AppendVtRow(vt, x, y + row, cells, _cols, _defaultAttributes);
                continue;
            }
            FillCharInfoRow(cells, line, x, _cols, legacy);
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
        if (vt is not null)
        {
            vt.Append("\x1b[0m");
            VtCellFormatter.AppendCursorPosition(vt, _caretX, _caretY);
            WriteVt(vt.ToString());
        }
    }

    private void WriteVt(string text)
    {
        if (_hOut == IntPtr.Zero || text.Length == 0) return;
        bool ok = WriteConsoleW(_hOut, text, (uint)text.Length, out _, IntPtr.Zero);
        if (InputTrace.Enabled && !ok)
            InputTrace.Log("WriteBuf", $"WriteConsoleW failed: length={text.Length} err={Marshal.GetLastWin32Error()}");
    }

    // ---- text tiers ------------------------------------------------------

    /// <summary>
    /// Fills <paramref name="line"/> with the console cells for one row segment starting at screen column
    /// <paramref name="x"/> (tier T1, <c>WriteConsoleOutputW</c>).
    /// </summary>
    /// <remarks>
    /// <para>Single cells keep their historical output. Under the legacy cell-width profile every cell is written
    /// as its <see cref="TScreenChar.Character"/>, exactly as before clusters existed.</para>
    /// <para>Clusters, from the TV13j U0-B host evidence (conhost and Windows Terminal, code pages 437, 65001,
    /// 932 and 852):</para>
    /// <list type="bullet">
    /// <item>A double-width BMP character is a <c>COMMON_LVB_LEADING_BYTE</c> cell plus a
    /// <c>COMMON_LVB_TRAILING_BYTE</c> cell holding the same character, each with its own colours. Without the
    /// flags conhost overlaps glyphs and Windows Terminal shifts the rest of the row.</item>
    /// <item>A two-cell cluster that starts with a supplementary character is written as that character's surrogate
    /// halves in its two cells, unflagged: conhost draws one monochrome glyph over both cells, Windows Terminal
    /// draws two U+FFFD; both keep the row aligned. Flagged surrogate halves are never written (conhost loses the
    /// low half and draws a box). Further scalars of the cluster (skin tone, ZWJ sequence, second regional
    /// indicator) are dropped.</item>
    /// <item>Any other cluster draws its first character, or its precomposed form when it has one
    /// (e + U+0301 becomes U+00E9), in the lead cell, and blanks over the remaining cells. A one-cell cluster that
    /// starts with a supplementary character draws U+FFFD.</item>
    /// <item>A cluster whose trails are outside the segment, or that would cross the last screen column, and
    /// orphan trails become blanks.</item>
    /// </list>
    /// </remarks>
    internal static void FillCharInfoRow(ReadOnlySpan<TScreenChar> cells, Span<CHAR_INFO> line, int x, int screenCols,
        bool legacy)
    {
        if (legacy)
        {
            for (int col = 0; col < cells.Length; col++)
            {
                var sc = cells[col];
                line[col].UnicodeChar = sc.Character == 0 ? ' ' : sc.Character;
                line[col].Attributes  = (ushort)sc.Attr;
            }
            return;
        }

        Span<char> scratch = stackalloc char[2];
        Span<char> pair = stackalloc char[2];
        int i = 0;
        while (i < cells.Length)
        {
            var sc = cells[i];
            switch (sc.Kind)
            {
                case TScreenCellKind.Single:
                    line[i].UnicodeChar = sc.Character == 0 ? ' ' : sc.Character;
                    line[i].Attributes  = (ushort)sc.Attr;
                    i++;
                    continue;
                case TScreenCellKind.Trail:
                    Blank(line, i, sc);
                    i++;
                    continue;
            }

            int width = sc.CellWidth;
            int end = Math.Min(cells.Length, i + width);
            bool fits = i + width <= cells.Length && (screenCols <= 0 || x + i + width <= screenCols);
            for (int k = i + 1; fits && k < i + width; k++)
                if (cells[k].Kind != TScreenCellKind.Trail) fits = false;
            for (int k = i; k < end; k++) Blank(line, k, cells[k]);
            if (!fits)
            {
                i = end;
                continue;
            }

            ReadOnlySpan<char> text = sc.GetText(scratch);
            if (System.Text.Rune.DecodeFromUtf16(text, out var rune, out int used) != System.Buffers.OperationStatus.Done)
                rune = System.Text.Rune.ReplacementChar;
            if (rune.IsBmp)
            {
                char ch = (char)rune.Value;
                if (used < text.Length && TryPrecompose(text, out char composed)) ch = composed;
                if (char.IsControl(ch)) ch = ' ';
                line[i].UnicodeChar = ch;
                if (width >= 2 && (TSharpVision.Text.UnicodeWidthData.Get(ch) & TSharpVision.Text.UnicodeWidthData.EawWide) != 0)
                {
                    line[i].Attributes     |= COMMON_LVB_LEADING_BYTE;
                    line[i + 1].UnicodeChar = ch;
                    line[i + 1].Attributes |= COMMON_LVB_TRAILING_BYTE;
                }
            }
            else if (width >= 2)
            {
                rune.EncodeToUtf16(pair);
                line[i].UnicodeChar     = pair[0];
                line[i + 1].UnicodeChar = pair[1];
            }
            else
            {
                line[i].UnicodeChar = (char)0xFFFD;
            }
            i += width;
        }

        static void Blank(Span<CHAR_INFO> line, int k, TScreenChar cell)
        {
            line[k].UnicodeChar = ' ';
            line[k].Attributes  = (ushort)cell.Attr;
        }
    }

    // A combining sequence that has a single precomposed character (NFC), such as e + U+0301.
    private static bool TryPrecompose(ReadOnlySpan<char> text, out char composed)
    {
        composed = '\0';
        if (text.Length > 8) return false;
        Span<char> buffer = stackalloc char[8];
        if (!text.TryNormalize(buffer, out int written, System.Text.NormalizationForm.FormC) || written != 1
            || char.IsSurrogate(buffer[0]))
            return false;
        composed = buffer[0];
        return true;
    }

    /// <summary>
    /// True when a row segment holds a cluster that console cells cannot carry under Windows Terminal: anything
    /// but a single BMP character (supplementary characters arrive there as U+FFFD).
    /// </summary>
    internal static bool NeedsVtOutput(ReadOnlySpan<TScreenChar> cells)
    {
        Span<char> scratch = stackalloc char[2];
        foreach (ref readonly TScreenChar cell in cells)
            if (cell.Kind == TScreenCellKind.Lead && cell.GetText(scratch).Length != 1) return true;
        return false;
    }

    /// <summary>Appends one row segment as VT text for the Windows Terminal tier (T2).</summary>
    internal static void AppendVtRow(System.Text.StringBuilder sb, int x, int y, ReadOnlySpan<TScreenChar> cells,
        int screenCols, ushort defaultAttributes)
    {
        TColorAttr last = default;
        bool have = false;
        string[] sgr = SgrFor(defaultAttributes);
        VtCellFormatter.AppendRow(sb, x, y, cells, screenCols, attr => sgr[(byte)attr], ref last, ref have);
    }

    private static string[]? s_sgr;
    private static ushort s_sgrDefault;

    private static string[] SgrFor(ushort defaultAttributes)
    {
        string[]? cache = s_sgr;
        if (cache is not null && s_sgrDefault == defaultAttributes) return cache;
        cache = new string[256];
        for (int a = 0; a < 256; a++) cache[a] = AttributeToSgr((byte)a, defaultAttributes);
        s_sgrDefault = defaultAttributes;
        s_sgr = cache;
        return cache;
    }

    /// <summary>
    /// SGR for a console attribute byte (FOREGROUND_BLUE = 1, GREEN = 2, RED = 4, INTENSITY = 8; background in the
    /// high nibble). A colour equal to the console's default attribute maps to the default colour (39/49), as the
    /// console host itself presents default-attribute cells, so VT rows match rows written as console cells.
    /// </summary>
    internal static string AttributeToSgr(byte attribute, ushort defaultAttributes)
    {
        int fg = attribute & 0x0F, bg = (attribute >> 4) & 0x0F;
        string fgCode = fg == (defaultAttributes & 0x0F) ? "39" : ((fg & 8) != 0 ? 90 : 30) + Ansi(fg) + "";
        string bgCode = bg == ((defaultAttributes >> 4) & 0x0F) ? "49" : ((bg & 8) != 0 ? 100 : 40) + Ansi(bg) + "";
        return $"\x1b[0;{fgCode};{bgCode}m";

        static int Ansi(int c) => ((c & 4) != 0 ? 1 : 0) | (c & 2) | ((c & 1) != 0 ? 4 : 0);
    }

    // ---- host detection --------------------------------------------------

    /// <summary>
    /// Chooses the output tier from what the console window reveals about its host. Windows Terminal is identified
    /// only positively: a ConPTY console window (<c>PseudoConsoleWindow</c>) owned by a Windows Terminal window
    /// (<c>CASCADIA_HOSTING_WINDOW_CLASS</c>) or by the <c>WindowsTerminal.exe</c> process. Everything else, including
    /// other ConPTY terminals and an unknown host, keeps console cells. <c>WT_SESSION</c> is not consulted: it is
    /// inherited by every process started from a Windows Terminal tab, also by a conhost window opened from it.
    /// <paramref name="overrideValue"/> (<see cref="TextTierVariable"/>) forces <c>vt</c> or <c>cells</c>.
    /// </summary>
    internal static Win32TextTier SelectTextTier(string? consoleWindowClass, string? ownerWindowClass,
        string? ownerProcessPath, string? overrideValue)
    {
        if (string.Equals(overrideValue, "vt", StringComparison.OrdinalIgnoreCase)) return Win32TextTier.Vt;
        if (string.Equals(overrideValue, "cells", StringComparison.OrdinalIgnoreCase)) return Win32TextTier.Cells;
        if (!string.Equals(consoleWindowClass, "PseudoConsoleWindow", StringComparison.Ordinal))
            return Win32TextTier.Cells;
        if (string.Equals(ownerWindowClass, "CASCADIA_HOSTING_WINDOW_CLASS", StringComparison.Ordinal))
            return Win32TextTier.Vt;
        // This is Windows process metadata even when the classifier runs on another OS.
        ReadOnlySpan<char> path = ownerProcessPath.AsSpan();
        ReadOnlySpan<char> exe = path[(path.LastIndexOfAny('\\', '/') + 1)..];
        return exe.Equals("WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase)
            ? Win32TextTier.Vt
            : Win32TextTier.Cells;
    }

    private static unsafe Win32TextTier DetectTextTier(out string description)
    {
        string? consoleClass = null, ownerClass = null, ownerProcess = null;
        try
        {
            IntPtr console = GetConsoleWindow();
            consoleClass = ClassName(console);
            IntPtr owner = console == IntPtr.Zero ? IntPtr.Zero : GetWindow(console, GW_OWNER);
            if (owner != IntPtr.Zero)
            {
                ownerClass = ClassName(owner);
                GetWindowThreadProcessId(owner, out uint pid);
                ownerProcess = ProcessPath(pid);
            }
        }
        catch
        {
            // Detection failure keeps the safe tier.
        }
        string? overrideValue = Environment.GetEnvironmentVariable(TextTierVariable);
        description = $"console={consoleClass ?? "-"} owner={ownerClass ?? "-"} ownerProcess={ownerProcess ?? "-"}"
                      + (string.IsNullOrEmpty(overrideValue) ? "" : $" override={overrideValue}");
        return SelectTextTier(consoleClass, ownerClass, ownerProcess, overrideValue);

        static string? ClassName(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;
            char* name = stackalloc char[256];
            int length = GetClassNameW(hwnd, name, 256);
            return length > 0 ? new string(name, 0, length) : null;
        }

        static string? ProcessPath(uint pid)
        {
            if (pid == 0) return null;
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                char* path = stackalloc char[1024];
                uint size = 1024;
                return QueryFullProcessImageNameW(process, 0, path, ref size) ? new string(path, 0, (int)size) : null;
            }
            finally { CloseHandle(process); }
        }
    }

    /// <summary>Selects the output tier as host detection would have. For tests and driver-output qualification.</summary>
    internal void SetTextTierForTesting(Win32TextTier tier, ushort defaultAttributes = 0x07)
    {
        _textTier = tier;
        _defaultAttributes = defaultAttributes;
    }

    private CHAR_INFO[] EnsureWriteLineCapacity(int width)
    {
        if (_writeLine.Length < width)
            _writeLine = new CHAR_INFO[width];
        return _writeLine;
    }

    /// <inheritdoc />
    public void MakeBeep()
    {
        if (OperatingSystem.IsWindows()) MessageBeep(0xFFFFFFFF);
    }

    /// <inheritdoc />
    public void WaitForEvent(int timeoutMilliseconds, WaitHandle wakeSignal)
    {
        ObjectDisposedException.ThrowIf(_stopped, this);
        if (_pendingKeys.Count != 0) return;
        if (!_attached)
            wakeSignal.WaitOne(timeoutMilliseconds < 0 ? 100 : Math.Min(timeoutMilliseconds, 100));
        else
            WaitOnHandles(_hIn, wakeSignal, timeoutMilliseconds);
        ObjectDisposedException.ThrowIf(_stopped, this);
    }

    internal static void WaitOnHandles(IntPtr inputHandle, WaitHandle wakeSignal, int timeoutMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMilliseconds, -1);
        bool retained = false;
        try
        {
            wakeSignal.SafeWaitHandle.DangerousAddRef(ref retained);
            uint result = WaitForMultipleObjects(2,
                new[] { inputHandle, wakeSignal.SafeWaitHandle.DangerousGetHandle() }, false,
                timeoutMilliseconds < 0 ? uint.MaxValue : (uint)timeoutMilliseconds);
            if (result == uint.MaxValue)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Console idle wait failed.");
            if (result != 0 && result != 1 && result != 258)
                throw new InvalidOperationException($"Unexpected console wait result: {result}.");
        }
        finally { if (retained) wakeSignal.SafeWaitHandle.DangerousRelease(); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint milliseconds);

    /// <inheritdoc />
    public void PumpMessages()
    {
        if (!_attached) return;

        // Drain at most 32 records per pump to keep latency bounded.
        var records = _inputRecords;
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
                    if (ProcessKeyRecord(
                            r.KeyEvent.bKeyDown != 0, r.KeyEvent.wVirtualKeyCode,
                            r.KeyEvent.wVirtualScanCode, r.KeyEvent.UnicodeChar,
                            r.KeyEvent.dwControlKeyState, r.KeyEvent.wRepeatCount))
                    {
                        if (InputTrace.Enabled)
                            InputTrace.LogEvent("Stage2-TranslatedKey", _pendingKeys.Last());
                    }
                    else if (InputTrace.Enabled)
                        InputTrace.Log("Stage2-TranslatedKey", "skipped (duplicate modifier transition or unmapped key)");
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
                    ApplyWindowSize(prevCols, prevRows);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Publishes the window size just read into <c>_cols</c> / <c>_rows</c>, if it differs from the one
    /// the application was last told about.
    /// </summary>
    /// <remarks>
    /// A record that leaves the visible window as it was is not a resize. The record of a change made
    /// before <see cref="Initialize"/> read the size is still in the queue when the first pump runs,
    /// and one drag can queue several records that all read the same final size. Reporting such a
    /// record would replace the screen buffer with an empty one and have the application clear and
    /// repaint a screen that is already right.
    /// </remarks>
    /// <returns>Whether a resize was reported.</returns>
    internal bool ApplyWindowSize(ushort previousCols, ushort previousRows)
    {
        if (_cols == previousCols && _rows == previousRows)
            return false;

        TScreen.ScreenWidth  = _cols;
        TScreen.ScreenHeight = _rows;
        // Reallocate the screen buffer to match the new dimensions.
        // The old buffer has the wrong size; keeping it would cause
        // clipping or out-of-range writes on the next WriteBuf call.
        TScreen.ScreenBuffer = AllocateScreenBuffer();
        // Enqueue a resize notification so TProgram can relayout
        // and force a full redraw on the next HandleEvent cycle.
        TEvent resEv = default;
        resEv.What = TSharpVision.Constants.Events.evCommand;
        resEv.message.command = TSharpVision.Constants.Views.cmScreenResized;
        TEventQueue.Enqueue(resEv);
        return true;
    }

    /// <summary>Sets the window size as a console record would have it read. For tests.</summary>
    internal void SetWindowSizeForTests(ushort cols, ushort rows)
    {
        _cols = cols;
        _rows = rows;
    }

    /// <summary>
    /// Attaches the output side to a caller-owned console screen buffer, as <see cref="Initialize"/>
    /// would have attached it to the real one. Output then goes through the same kernel32 calls,
    /// so driver-output qualification can read the console cells back. No input handle is taken.
    /// </summary>
    internal void AttachOutputForTesting(IntPtr output, ushort cols, ushort rows)
    {
        _hOut = output;
        _cols = cols;
        _rows = rows;
        _attached = true;
    }

    /// <summary>Releases the attachment made by <see cref="AttachOutputForTesting"/> without restoring console modes.</summary>
    internal void DetachOutputForTesting()
    {
        _attached = false;
        _hOut = IntPtr.Zero;
    }

    /// <summary>
    /// Translate a Windows mouse record into a tvision mouse event.
    /// Public for unit tests; the driver itself only uses it internally.
    /// </summary>
    internal static TEvent TranslateMouse(
        uint buttonState, uint eventFlags, short x, short y, uint controlKeyState = 0)
    {
        TEvent ev = default;
        ev.mouse.where = new TPoint(x, y);
        ev.mouse.controlKeyState = Win32KeyTranslator.ToControlKeyState(controlKeyState);
        if ((eventFlags & MOUSE_MOVED) != 0)
            ev.mouse.eventFlags |= Events.meMouseMoved;
        if ((eventFlags & DOUBLE_CLICK) != 0)
            ev.mouse.eventFlags |= Events.meDoubleClick;

        byte buttons = TranslatePhysicalButtons(buttonState);
        ev.mouse.buttons = buttons;

        // Win32 places the signed wheel delta in the high 16 bits of
        // dwButtonState. Positive vertical is away/up; positive horizontal is right.
        if ((eventFlags & (MOUSE_WHEELED | MOUSE_HWHEELED)) != 0)
        {
            short wheelDelta = (short)((int)buttonState >> 16);
            ev.What = Events.evMouseWheel;
            if ((eventFlags & MOUSE_HWHEELED) != 0)
                ev.mouse.eventFlags |= wheelDelta > 0 ? Events.meWheelRight : Events.meWheelLeft;
            else
                ev.mouse.eventFlags |= wheelDelta > 0 ? Events.meWheelUp : Events.meWheelDown;
            return ev;
        }

        if ((eventFlags & MOUSE_MOVED) != 0)
            ev.What = Events.evMouseMove;
        else if (buttons != 0)
            ev.What = Events.evMouseDown;
        else
            ev.What = Events.evMouseUp;
        return ev;
    }

    private static byte TranslatePhysicalButtons(uint buttonState)
    {
        byte buttons = 0;
        if ((buttonState & FROM_LEFT_1ST_BUTTON_PRESSED) != 0) buttons |= (byte)Events.mbLeftButton;
        if ((buttonState & RIGHTMOST_BUTTON_PRESSED) != 0) buttons |= (byte)Events.mbRightButton;
        if ((buttonState & FROM_LEFT_2ND_BUTTON_PRESSED) != 0) buttons |= (byte)Events.mbMiddleButton;
        if ((buttonState & FROM_LEFT_3RD_BUTTON_PRESSED) != 0) buttons |= (byte)Events.mbButton4;
        if ((buttonState & FROM_LEFT_4TH_BUTTON_PRESSED) != 0) buttons |= (byte)Events.mbButton5;
        return buttons;
    }

    private static TEvent TranslateMouse(MOUSE_EVENT_RECORD m)
        => TranslateMouse(
            m.dwButtonState, m.dwEventFlags, m.dwMousePosition.X, m.dwMousePosition.Y,
            m.dwControlKeyState);

    internal bool ProcessKeyRecord(
        bool keyDown, ushort virtualKey, ushort scanCode, char character,
        uint controlKeyState, ushort repeatCount = 1)
    {
        // Preserve the established Console behavior: one native record produces one
        // public event. wRepeatCount is intentionally not expanded by this driver.
        _ = repeatCount;
        if (!_inputTranslator.TryTranslate(
                keyDown, virtualKey, scanCode, character, controlKeyState, out TEvent ev))
            return false;
        ev.keyDown.raw_scanCode = (byte)scanCode;
        _pendingKeys.Enqueue(ev);
        return true;
    }

    /// <inheritdoc />
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

    /// <summary>Shuts down the backend and releases its display and input resources.</summary>
    public void Dispose() => Shutdown();
}

