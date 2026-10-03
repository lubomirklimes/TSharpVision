using System.Text;

namespace TSharpVision.Terminal;

/// <summary>What changed in a <see cref="TerminalEmulator"/> since the last <see cref="TerminalEmulator.TakeChanges"/>.</summary>
[Flags]
public enum TerminalChanges
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Cells changed.</summary>
    Content = 1,

    /// <summary>The cursor moved or its visibility changed.</summary>
    Cursor = 2,

    /// <summary>The title changed.</summary>
    Title = 4,

    /// <summary>The application rang the bell.</summary>
    Bell = 8,

    /// <summary>A terminal mode changed (keys, paste, mouse, screen).</summary>
    Modes = 16,

    /// <summary>The size changed.</summary>
    Size = 32,
}

/// <summary>Mouse reporting an application asked for. Recorded for a future mouse protocol; nothing is reported yet.</summary>
public enum TerminalMouseTracking
{
    /// <summary>No mouse reporting.</summary>
    None,

    /// <summary>X10 compatibility (mode 9): presses only.</summary>
    X10,

    /// <summary>Normal tracking (mode 1000): presses and releases.</summary>
    Normal,

    /// <summary>Button-event tracking (mode 1002): also motion with a button down.</summary>
    ButtonEvent,

    /// <summary>Any-event tracking (mode 1003): all motion.</summary>
    AnyEvent,
}

/// <summary>
/// A VT/xterm-compatible terminal emulator: a byte stream in, a grid of cells out. Independent of any view, session or
/// operating system, so it can be driven and inspected directly.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pipeline.</b> Bytes → incremental UTF-8 decoder → control-sequence state machine → screen model. Every stage keeps
/// its state between calls, so input may be split at any byte: a character, a control sequence or an OSC string
/// arriving in several pieces gives exactly the same screen as arriving at once.
/// </para>
/// <para>
/// <b>Screens.</b> A primary screen with bounded scrollback and an alternate screen without it (modes 47, 1047, 1049).
/// </para>
/// <para>
/// <b>Threading.</b> Not thread-safe. The owner serializes every call; <see cref="TTerminal"/> does so with its
/// <see cref="TTerminal.SyncRoot"/>.
/// </para>
/// <para>
/// <b>Untrusted input.</b> Output of a program is treated as untrusted: parsing never throws, never allocates without a
/// bound and never indexes outside the grid, whatever the bytes.
/// </para>
/// </remarks>
public sealed class TerminalEmulator
{
    /// <summary>The scrollback a new emulator keeps unless told otherwise.</summary>
    public const int DefaultMaxScrollbackLines = 1000;

    private const int MaximumResponseLength = 64 * 1024;
    private const int MaximumTitleLength = 1024;
    private const int MaximumTitleStack = 10;

    private readonly TerminalScrollback _scrollback;
    private readonly TerminalScreenBuffer _primary;
    private readonly TerminalScreenBuffer _alternate;
    private readonly VtParser _parser;
    private readonly StringBuilder _responses = new();
    private readonly Stack<string> _titleStack = new();
    private Utf8StreamDecoder _decoder;
    private TerminalScreenBuffer _active;
    private TerminalCharsets _charsets;
    private TerminalChanges _changes;
    private int _lastPrinted = -1;

    /// <summary>Creates an emulator of <paramref name="columns"/> × <paramref name="rows"/> cells (each at least 1).</summary>
    public TerminalEmulator(int columns, int rows, int maxScrollbackLines = DefaultMaxScrollbackLines)
    {
        _scrollback = new TerminalScrollback(maxScrollbackLines);
        _primary = new TerminalScreenBuffer(columns, rows, _scrollback);
        _alternate = new TerminalScreenBuffer(columns, rows, null);
        _active = _primary;
        _parser = new VtParser(new Handler(this));
        _decoder.Reset();
    }

    // ── state ────────────────────────────────────────────────────────────────

    /// <summary>Columns of the grid.</summary>
    public int Columns => _active.Width;

    /// <summary>Rows of the grid.</summary>
    public int Rows => _active.Height;

    /// <summary>The cursor's row on the active screen (zero-based).</summary>
    public int CursorRow => _active.CursorRow;

    /// <summary>The cursor's column on the active screen (zero-based).</summary>
    public int CursorColumn => _active.CursorColumn;

    /// <summary>Whether the cursor is shown (DECTCEM, mode 25). Unrelated to keyboard focus.</summary>
    public bool CursorVisible { get; private set; } = true;

    /// <summary>Whether the alternate screen is shown.</summary>
    public bool IsAlternateScreenActive => ReferenceEquals(_active, _alternate);

    /// <summary>Cursor keys send application sequences (DECCKM, mode 1): ESC O A rather than ESC [ A.</summary>
    public bool ApplicationCursorKeys { get; private set; }

    /// <summary>The keypad is in application mode (DECKPAM, DECNKM).</summary>
    public bool ApplicationKeypad { get; private set; }

    /// <summary>Pasted text is to be bracketed with ESC [200~ … ESC [201~ (mode 2004).</summary>
    public bool BracketedPaste { get; private set; }

    /// <summary>Printing past the right margin wraps (DECAWM, mode 7). On by default.</summary>
    public bool AutoWrap { get; private set; } = true;

    /// <summary>Cursor addressing is relative to the scroll region (DECOM, mode 6).</summary>
    public bool OriginMode { get; private set; }

    /// <summary>Printing inserts rather than overwrites (IRM, mode 4).</summary>
    public bool InsertMode { get; private set; }

    /// <summary>
    /// Line feed also returns the carriage (LNM, mode 20). Off by default, as a PTY already turns "\n" into "\r\n";
    /// a host that writes plain text with bare line feeds turns it on.
    /// </summary>
    public bool NewLineMode { get; set; }

    /// <summary>The whole screen is shown in reverse video (DECSCNM, mode 5).</summary>
    public bool ReverseVideo { get; private set; }

    /// <summary>Mouse reporting the application asked for (recorded; not reported yet).</summary>
    public TerminalMouseTracking MouseTracking { get; private set; }

    /// <summary>Mouse reports would use the SGR encoding (mode 1006; recorded).</summary>
    public bool SgrMouseEncoding { get; private set; }

    /// <summary>
    /// The mouse wheel over the alternate screen may be sent as cursor keys when no mouse reporting is on (mode 1007).
    /// On by default, as in most terminals, so <c>less</c> and <c>vim</c> scroll with the wheel.
    /// </summary>
    public bool AlternateScroll { get; private set; } = true;

    /// <summary>The window title the application set (OSC 0 / OSC 2); empty until then.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>The current graphic rendition.</summary>
    public TerminalStyle CurrentStyle => _active.Style;

    /// <summary>The most scrollback rows kept for the primary screen.</summary>
    public int MaxScrollbackLines
    {
        get => _scrollback.Capacity;
        set => _scrollback.SetCapacity(value);
    }

    /// <summary>Rows of scrollback currently held.</summary>
    public int ScrollbackCount => _scrollback.Count;

    /// <summary>Rows ever moved into scrollback, including those since dropped (monotonic).</summary>
    public long ScrollbackTotalAdded => _scrollback.TotalAdded;

    /// <summary>
    /// Rows a viewer can show: scrollback, when the primary screen is active, followed by the screen rows.
    /// </summary>
    public int BufferRowCount => (IsAlternateScreenActive ? 0 : _scrollback.Count) + Rows;

    // ── feeding ──────────────────────────────────────────────────────────────

    /// <summary>Processes a chunk of the byte stream from the application.</summary>
    public void Feed(ReadOnlySpan<byte> data)
    {
        Span<int> decoded = stackalloc int[2];
        foreach (byte value in data)
        {
            int count = _decoder.Push(value, decoded);
            for (int i = 0; i < count; i++) _parser.Advance(decoded[i]);
        }
    }

    /// <summary>
    /// Processes already-decoded text, for a host that writes strings rather than bytes. Control sequences in it are
    /// interpreted exactly as in <see cref="Feed(ReadOnlySpan{byte})"/>; an unpaired surrogate becomes U+FFFD.
    /// </summary>
    public void Feed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                _parser.Advance(char.ConvertToUtf32(c, text[++i]));
                continue;
            }

            _parser.Advance(char.IsSurrogate(c) ? Utf8StreamDecoder.Replacement : c);
        }
    }

    /// <summary>Returns and clears what changed since the last call.</summary>
    public TerminalChanges TakeChanges()
    {
        TerminalChanges changes = _changes;
        _changes = TerminalChanges.None;
        return changes;
    }

    /// <summary>
    /// Returns and clears the replies the application's queries produced (cursor position, device attributes, …), to
    /// be written back to it; null when there are none.
    /// </summary>
    public string? TakeResponses()
    {
        if (_responses.Length == 0) return null;
        string responses = _responses.ToString();
        _responses.Clear();
        return responses;
    }

    /// <summary>Changes the size of both screens; see the remarks of the class for what is kept.</summary>
    public void Resize(int columns, int rows)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        if (columns == Columns && rows == Rows) return;
        _primary.Resize(columns, rows);
        _alternate.Resize(columns, rows);
        _changes |= TerminalChanges.Size | TerminalChanges.Content | TerminalChanges.Cursor;
    }

    /// <summary>A full reset (RIS): both screens cleared, modes and rendition back to their defaults. Scrollback stays.</summary>
    public void Reset()
    {
        _parser.Reset();
        _decoder.Reset();
        _primary.Reset();
        _alternate.Reset();
        _active = _primary;
        _charsets = default;
        CursorVisible = true;
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        BracketedPaste = false;
        AutoWrap = true;
        OriginMode = false;
        InsertMode = false;
        ReverseVideo = false;
        MouseTracking = TerminalMouseTracking.None;
        SgrMouseEncoding = false;
        AlternateScroll = true;
        _lastPrinted = -1;
        _changes |= TerminalChanges.Content | TerminalChanges.Cursor | TerminalChanges.Modes;
    }

    /// <summary>Discards the scrollback (as ED 3 does).</summary>
    public void ClearScrollback()
    {
        _scrollback.Clear();
        _changes |= TerminalChanges.Content;
    }

    // ── reading ──────────────────────────────────────────────────────────────

    /// <summary>A cell of the active screen.</summary>
    public TerminalCell GetCell(int row, int column) => _active[row].Cells[column];

    /// <summary>The cells of a row of the active screen. Valid until the emulator is next changed.</summary>
    public ReadOnlySpan<TerminalCell> GetRow(int row) => _active[row].Cells;

    /// <summary>Whether a row of the active screen continues on the next one (it wrapped at the right margin).</summary>
    public bool IsRowWrapped(int row) => _active[row].Wrapped;

    /// <summary>The text of a row of the active screen, trailing blanks removed.</summary>
    public string GetRowText(int row) => TerminalText.RowText(_active[row].Cells);

    /// <summary>
    /// A row of <see cref="BufferRowCount"/>: scrollback first (oldest at 0) when the primary screen is active, then the
    /// screen. A scrollback row may be shorter than <see cref="Columns"/>; the missing cells are empty.
    /// </summary>
    public ReadOnlySpan<TerminalCell> GetBufferRow(int index, out bool wrapped)
    {
        int scrollback = IsAlternateScreenActive ? 0 : _scrollback.Count;
        TerminalRow row = index < scrollback ? _scrollback[index] : _active[index - scrollback];
        wrapped = row.Wrapped;
        return row.Cells;
    }

    // ── interpretation ───────────────────────────────────────────────────────

    private void Print(int codePoint)
    {
        if (_charsets.GraphicsActive && codePoint is >= 0x5F and <= 0x7E) codePoint = DecSpecialGraphics[codePoint - 0x5F];

        int width = TerminalCharWidth.GetWidth(codePoint);
        if (width < 0) return;
        if (width == 0)
        {
            _active.AppendCombining(char.ConvertFromUtf32(codePoint));
            _changes |= TerminalChanges.Content;
            return;
        }

        if (codePoint > 0xFFFF) _active.Print('\0', char.ConvertFromUtf32(codePoint), width, AutoWrap, InsertMode);
        else _active.Print((char)codePoint, null, width, AutoWrap, InsertMode);
        _lastPrinted = codePoint;
        _changes |= TerminalChanges.Content | TerminalChanges.Cursor;
    }

    private void Execute(int control)
    {
        switch (control)
        {
            case 0x07: _changes |= TerminalChanges.Bell; return;
            case 0x08: _active.Backspace(); break;
            case 0x09: _active.TabForward(1); break;
            case 0x0A:
            case 0x0B:
            case 0x0C:
                if (NewLineMode) _active.CarriageReturn();
                _active.Index();
                _changes |= TerminalChanges.Content;
                break;
            case 0x0D: _active.CarriageReturn(); break;
            case 0x0E: _charsets = _charsets with { ShiftOut = true }; return;
            case 0x0F: _charsets = _charsets with { ShiftOut = false }; return;
            default: return;
        }

        _changes |= TerminalChanges.Cursor;
    }

    private void EscapeDispatch(char intermediate, char final)
    {
        switch (intermediate)
        {
            case '\0':
                switch (final)
                {
                    case '7': SaveCursor(); return;
                    case '8': RestoreCursor(); return;
                    case 'D': _active.Index(); break;
                    case 'E': _active.CarriageReturn(); _active.Index(); break;
                    case 'M': _active.ReverseIndex(); break;
                    case 'H': _active.SetTabStop(); return;
                    case 'c': Reset(); return;
                    case '=': ApplicationKeypad = true; _changes |= TerminalChanges.Modes; return;
                    case '>': ApplicationKeypad = false; _changes |= TerminalChanges.Modes; return;
                    case 'Z': Respond("\x1b[?62;22c"); return;
                    default: return;   // ST, SS2, SS3 and the rest: nothing to do
                }

                _changes |= TerminalChanges.Content | TerminalChanges.Cursor;
                return;

            case '(':
                _charsets = _charsets with { G0Graphics = final == '0' };
                return;
            case ')':
                _charsets = _charsets with { G1Graphics = final == '0' };
                return;
            case '#':
                if (final == '8')
                {
                    _active.FillWithE();
                    _changes |= TerminalChanges.Content | TerminalChanges.Cursor;
                }

                return;
        }
    }

    private void CsiDispatch(VtParameters p, char marker, char intermediate, char final)
    {
        if (intermediate != '\0')
        {
            if (intermediate == '!' && final == 'p') SoftReset();
            else if (intermediate == '$' && final == 'p') ReportMode(p.ValueOrDefault(0), marker == '?');
            return;   // cursor style (SP q) and the rest are accepted and ignored
        }

        if (marker == '?')
        {
            switch (final)
            {
                case 'h': for (int i = 0; i < p.Count; i++) SetPrivateMode(p.ValueOrDefault(i), true); break;
                case 'l': for (int i = 0; i < p.Count; i++) SetPrivateMode(p.ValueOrDefault(i), false); break;
                case 'J': _active.EraseInDisplay(p.ValueOrDefault(0)); _changes |= TerminalChanges.Content; break;
                case 'K': _active.EraseInLine(p.ValueOrDefault(0)); _changes |= TerminalChanges.Content; break;
                case 'n':
                    if (p.ValueOrDefault(0) == 6) Respond($"\x1b[?{ReportedRow()};{_active.CursorColumn + 1}R");
                    break;
            }

            return;
        }

        if (marker == '>')
        {
            if (final == 'c' && p.ValueOrDefault(0) == 0) Respond("\x1b[>1;10;0c");
            return;
        }

        if (marker != '\0') return;

        TerminalScreenBuffer s = _active;
        switch (final)
        {
            case '@': s.InsertCharacters(p.CountOrDefault(0)); break;
            case 'A': s.MoveUp(p.CountOrDefault(0)); break;
            case 'B': s.MoveDown(p.CountOrDefault(0)); break;
            case 'C': s.MoveRight(p.CountOrDefault(0)); break;
            case 'D': s.MoveLeft(p.CountOrDefault(0)); break;
            case 'E': s.MoveDown(p.CountOrDefault(0)); s.CarriageReturn(); break;
            case 'F': s.MoveUp(p.CountOrDefault(0)); s.CarriageReturn(); break;
            case 'G':
            case '`': s.SetCursor(s.CursorRow, p.CountOrDefault(0) - 1); break;
            case 'H':
            case 'f': MoveTo(p.CountOrDefault(0), p.CountOrDefault(1)); break;
            case 'I': s.TabForward(p.CountOrDefault(0)); break;
            case 'J': s.EraseInDisplay(p.ValueOrDefault(0)); break;
            case 'K': s.EraseInLine(p.ValueOrDefault(0)); break;
            case 'L': s.InsertLines(p.CountOrDefault(0)); break;
            case 'M': s.DeleteLines(p.CountOrDefault(0)); break;
            case 'P': s.DeleteCharacters(p.CountOrDefault(0)); break;
            case 'S': s.ScrollUp(p.CountOrDefault(0)); break;
            case 'T':
                if (p.Count > 1) return;   // with more parameters this is mouse highlight tracking, not SD
                s.ScrollDown(p.CountOrDefault(0));
                break;
            case 'X': s.EraseCharacters(p.CountOrDefault(0)); break;
            case 'Z': s.TabBackward(p.CountOrDefault(0)); break;
            case 'a': s.MoveRight(p.CountOrDefault(0)); break;
            case 'b': Repeat(p.CountOrDefault(0)); return;
            case 'c': if (p.ValueOrDefault(0) == 0) Respond("\x1b[?62;22c"); return;
            case 'd': MoveTo(p.CountOrDefault(0), s.CursorColumn + 1); break;
            case 'e': s.MoveDown(p.CountOrDefault(0)); break;
            case 'g': s.ClearTabStops(p.ValueOrDefault(0)); return;
            case 'h': for (int i = 0; i < p.Count; i++) SetAnsiMode(p.ValueOrDefault(i), true); return;
            case 'l': for (int i = 0; i < p.Count; i++) SetAnsiMode(p.ValueOrDefault(i), false); return;
            case 'm': SelectGraphicRendition(p); return;
            case 'n': DeviceStatusReport(p.ValueOrDefault(0)); return;
            case 'r':
                s.SetScrollRegion(p.CountOrDefault(0) - 1, p.CountOrDefault(1, s.Height) - 1);
                MoveTo(1, 1);
                break;
            case 's': SaveCursor(); return;
            case 'u': RestoreCursor(); return;
            case 't': WindowOperation(p); return;
            default: return;
        }

        _changes |= TerminalChanges.Content | TerminalChanges.Cursor;
    }

    private void OscDispatch(string data)
    {
        int separator = data.IndexOf(';');
        string command = separator < 0 ? data : data[..separator];
        string argument = separator < 0 ? string.Empty : data[(separator + 1)..];

        switch (command)
        {
            case "0":
            case "2":
                SetTitle(argument);
                break;
            case "10":
                if (argument == "?") Respond("\x1b]10;rgb:c0c0/c0c0/c0c0\x1b\\");
                break;
            case "11":
                if (argument == "?") Respond("\x1b]11;rgb:0000/0000/0000\x1b\\");
                break;
            // 1 (icon name), 4 (palette), 7 (directory), 8 (hyperlinks), 52 (clipboard) and others are ignored.
        }
    }

    private void SetTitle(string title)
    {
        var clean = new StringBuilder(Math.Min(title.Length, MaximumTitleLength));
        foreach (char c in title)
        {
            if (clean.Length == MaximumTitleLength) break;
            if (c >= ' ' && c != '\x7F') clean.Append(c);
        }

        string value = clean.ToString();
        if (value == Title) return;
        Title = value;
        _changes |= TerminalChanges.Title;
    }

    private void MoveTo(int row, int column)
    {
        TerminalScreenBuffer s = _active;
        if (OriginMode)
        {
            int top = s.ScrollTop;
            s.SetCursor(Math.Min(s.ScrollBottom, top + row - 1), column - 1);
        }
        else
        {
            s.SetCursor(row - 1, column - 1);
        }
    }

    private int ReportedRow() => OriginMode ? _active.CursorRow - _active.ScrollTop + 1 : _active.CursorRow + 1;

    private void Repeat(int count)
    {
        if (_lastPrinted < 0) return;
        count = Math.Min(count, Columns * Rows);
        for (int i = 0; i < count; i++) Print(_lastPrinted);
    }

    private void SaveCursor()
        => _active.Saved = new TerminalSavedCursor(
            _active.CursorRow, _active.CursorColumn, _active.Style, _active.PendingWrap, OriginMode, _charsets);

    private void RestoreCursor()
    {
        if (_active.Saved is { } saved)
        {
            _active.Restore(saved);
            OriginMode = saved.OriginMode;
            _charsets = saved.Charsets;
        }
        else
        {
            _active.SetCursor(0, 0);
            _active.Style = TerminalStyle.Default;
            OriginMode = false;
            _charsets = default;
        }

        _changes |= TerminalChanges.Cursor;
    }

    private void SoftReset()
    {
        CursorVisible = true;
        InsertMode = false;
        OriginMode = false;
        AutoWrap = true;
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        _charsets = default;
        _active.Style = TerminalStyle.Default;
        _active.SetScrollRegion(0, _active.Height - 1);
        _active.Saved = null;
        _changes |= TerminalChanges.Cursor | TerminalChanges.Modes;
    }

    private void SetAnsiMode(int mode, bool on)
    {
        if (mode == 4) InsertMode = on;
        else if (mode == 20) NewLineMode = on;
        else return;
        _changes |= TerminalChanges.Modes;
    }

    private void SetPrivateMode(int mode, bool on)
    {
        switch (mode)
        {
            case 1: ApplicationCursorKeys = on; break;
            case 5:
                ReverseVideo = on;
                _changes |= TerminalChanges.Content;
                break;
            case 6:
                OriginMode = on;
                MoveTo(1, 1);
                break;
            case 7: AutoWrap = on; break;
            case 25:
                CursorVisible = on;
                _changes |= TerminalChanges.Cursor;
                break;
            case 47: SwitchScreen(on, clearAlternate: false); break;
            case 1047:
                if (on) SwitchScreen(true, clearAlternate: false);
                else
                {
                    if (IsAlternateScreenActive) _alternate.ClearAll(TerminalStyle.Default);
                    SwitchScreen(false, clearAlternate: false);
                }

                break;
            case 1048:
                if (on) SaveCursor();
                else RestoreCursor();
                break;
            case 1049:
                if (on)
                {
                    if (IsAlternateScreenActive) return;
                    SaveCursor();
                    SwitchScreen(true, clearAlternate: true);
                }
                else
                {
                    if (!IsAlternateScreenActive) return;
                    SwitchScreen(false, clearAlternate: false);
                    RestoreCursor();
                }

                break;
            case 9: MouseTracking = on ? TerminalMouseTracking.X10 : TerminalMouseTracking.None; break;
            case 1000: MouseTracking = on ? TerminalMouseTracking.Normal : TerminalMouseTracking.None; break;
            case 1002: MouseTracking = on ? TerminalMouseTracking.ButtonEvent : TerminalMouseTracking.None; break;
            case 1003: MouseTracking = on ? TerminalMouseTracking.AnyEvent : TerminalMouseTracking.None; break;
            case 1006: SgrMouseEncoding = on; break;
            case 1007: AlternateScroll = on; break;
            case 66: ApplicationKeypad = on; break;
            case 2004: BracketedPaste = on; break;
            default: return;   // 3, 4, 8, 12, 1004, 1005, 1015, 2026 and unknown modes are accepted and ignored
        }

        _changes |= TerminalChanges.Modes;
    }

    private bool? PrivateModeState(int mode) => mode switch
    {
        1 => ApplicationCursorKeys,
        5 => ReverseVideo,
        6 => OriginMode,
        7 => AutoWrap,
        25 => CursorVisible,
        47 or 1047 or 1049 => IsAlternateScreenActive,
        66 => ApplicationKeypad,
        1000 => MouseTracking == TerminalMouseTracking.Normal,
        1002 => MouseTracking == TerminalMouseTracking.ButtonEvent,
        1003 => MouseTracking == TerminalMouseTracking.AnyEvent,
        1006 => SgrMouseEncoding,
        1007 => AlternateScroll,
        2004 => BracketedPaste,
        _ => null,
    };

    /// <summary>DECRQM: 1 set, 2 reset, 0 not recognized.</summary>
    private void ReportMode(int mode, bool isPrivate)
    {
        bool? state = isPrivate ? PrivateModeState(mode) : mode switch { 4 => InsertMode, 20 => NewLineMode, _ => null };
        int value = state is null ? 0 : state.Value ? 1 : 2;
        Respond(isPrivate ? $"\x1b[?{mode};{value}$y" : $"\x1b[{mode};{value}$y");
    }

    private void SwitchScreen(bool alternate, bool clearAlternate)
    {
        TerminalScreenBuffer from = _active;
        TerminalScreenBuffer to = alternate ? _alternate : _primary;
        if (alternate && clearAlternate) _alternate.ClearAll(TerminalStyle.Default);
        if (ReferenceEquals(from, to)) return;

        // The cursor and the rendition carry over, as in xterm.
        to.SetCursor(from.CursorRow, from.CursorColumn);
        to.Style = from.Style;
        _active = to;
        _changes |= TerminalChanges.Content | TerminalChanges.Cursor | TerminalChanges.Modes;
    }

    private void DeviceStatusReport(int kind)
    {
        if (kind == 5) Respond("\x1b[0n");
        else if (kind == 6) Respond($"\x1b[{ReportedRow()};{_active.CursorColumn + 1}R");
    }

    private void WindowOperation(VtParameters p)
    {
        switch (p.ValueOrDefault(0))
        {
            case 18:
                Respond($"\x1b[8;{Rows};{Columns}t");
                break;
            case 22:   // push the title
                if (_titleStack.Count < MaximumTitleStack) _titleStack.Push(Title);
                break;
            case 23:   // pop it
                if (_titleStack.Count > 0) SetTitle(_titleStack.Pop());
                break;
        }
    }

    private void Respond(string reply)
    {
        if (_responses.Length + reply.Length <= MaximumResponseLength) _responses.Append(reply);
    }

    // ── SGR ──────────────────────────────────────────────────────────────────

    private void SelectGraphicRendition(VtParameters p)
    {
        TerminalStyle style = _active.Style;
        TerminalColor fg = style.Foreground, bg = style.Background;
        TerminalCellAttributes a = style.Attributes;

        if (p.Count == 0)
        {
            _active.Style = TerminalStyle.Default;
            return;
        }

        int i = 0;
        while (i < p.Count)
        {
            int code = p.ValueOrDefault(i);
            int next = i + 1;
            switch (code)
            {
                case 0: fg = bg = TerminalColor.Default; a = TerminalCellAttributes.None; break;
                case 1: a |= TerminalCellAttributes.Bold; break;
                case 2: a |= TerminalCellAttributes.Dim; break;
                case 3: a |= TerminalCellAttributes.Italic; break;
                case 4:
                    // 4:0 is "no underline"; 4:1..4:5 are underline styles.
                    if (p.IsSubParameter(next) && p.ValueOrDefault(next) == 0) a &= ~TerminalCellAttributes.Underline;
                    else a |= TerminalCellAttributes.Underline;
                    break;
                case 5:
                case 6: a |= TerminalCellAttributes.Blink; break;
                case 7: a |= TerminalCellAttributes.Inverse; break;
                case 8: a |= TerminalCellAttributes.Hidden; break;
                case 9: a |= TerminalCellAttributes.Strikethrough; break;
                case 21: a |= TerminalCellAttributes.Underline; break;
                case 22: a &= ~(TerminalCellAttributes.Bold | TerminalCellAttributes.Dim); break;
                case 23: a &= ~TerminalCellAttributes.Italic; break;
                case 24: a &= ~TerminalCellAttributes.Underline; break;
                case 25: a &= ~TerminalCellAttributes.Blink; break;
                case 27: a &= ~TerminalCellAttributes.Inverse; break;
                case 28: a &= ~TerminalCellAttributes.Hidden; break;
                case 29: a &= ~TerminalCellAttributes.Strikethrough; break;
                case >= 30 and <= 37: fg = TerminalColor.FromIndex((byte)(code - 30)); break;
                case 38:
                    next = ExtendedColor(p, i, out TerminalColor? foreground);
                    if (foreground is { } f) fg = f;
                    break;
                case 39: fg = TerminalColor.Default; break;
                case >= 40 and <= 47: bg = TerminalColor.FromIndex((byte)(code - 40)); break;
                case 48:
                    next = ExtendedColor(p, i, out TerminalColor? background);
                    if (background is { } b) bg = b;
                    break;
                case 49: bg = TerminalColor.Default; break;
                case 58: next = ExtendedColor(p, i, out _); break;   // underline colour: consumed, not shown
                case >= 90 and <= 97: fg = TerminalColor.FromIndex((byte)(code - 90 + 8)); break;
                case >= 100 and <= 107: bg = TerminalColor.FromIndex((byte)(code - 100 + 8)); break;
                // Anything else is ignored.
            }

            // Sub-parameters belong to the parameter they follow; none of them starts a new attribute.
            while (next < p.Count && p.IsSubParameter(next)) next++;
            i = Math.Max(next, i + 1);
        }

        _active.Style = new TerminalStyle(fg, bg, a);
    }

    /// <summary>
    /// Parses the colour after 38/48/58 at <paramref name="index"/>, in either form: 38;5;n and 38;2;r;g;b, or with
    /// colons 38:5:n, 38:2:r:g:b and 38:2:colourspace:r:g:b. Returns the index of the first parameter after it; the
    /// colour is null when the parameters do not form one, and nothing past them is consumed as an attribute.
    /// </summary>
    private static int ExtendedColor(VtParameters p, int index, out TerminalColor? color)
    {
        color = null;
        bool colon = p.IsSubParameter(index + 1);
        int mode = p[index + 1];

        if (colon)
        {
            int subs = 0;
            while (p.IsSubParameter(index + 1 + subs)) subs++;
            if (mode == 5 && subs >= 2) color = TerminalColor.FromIndex(Clamp(p[index + 2]));
            else if (mode == 2 && subs >= 5) color = Rgb(p[index + 3], p[index + 4], p[index + 5]);
            else if (mode == 2 && subs == 4) color = Rgb(p[index + 2], p[index + 3], p[index + 4]);
            return index + 1 + subs;
        }

        if (mode == 5)
        {
            if (index + 2 < p.Count) color = TerminalColor.FromIndex(Clamp(p[index + 2]));
            return index + 3;
        }

        if (mode == 2)
        {
            if (index + 4 < p.Count) color = Rgb(p[index + 2], p[index + 3], p[index + 4]);
            return index + 5;
        }

        return index + 2;   // an unknown colour form: its selector is skipped, nothing more is guessed

        static TerminalColor Rgb(int r, int g, int b) => TerminalColor.FromRgb(Clamp(r), Clamp(g), Clamp(b));
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);

    // DEC Special Graphics for 0x5F..0x7E (ESC ( 0): line drawing and symbols.
    private static readonly int[] DecSpecialGraphics =
    {
        0x00A0, 0x25C6, 0x2592, 0x2409, 0x240C, 0x240D, 0x240A, 0x00B0, 0x00B1, 0x2424, 0x240B, 0x2518, 0x2510, 0x250C,
        0x2514, 0x253C, 0x23BA, 0x23BB, 0x2500, 0x23BC, 0x23BD, 0x251C, 0x2524, 0x2534, 0x252C, 0x2502, 0x2264, 0x2265,
        0x03C0, 0x2260, 0x00A3, 0x00B7,
    };

    private sealed class Handler(TerminalEmulator emulator) : IVtHandler
    {
        public void Print(int codePoint) => emulator.Print(codePoint);

        public void Execute(int control) => emulator.Execute(control);

        public void EscapeDispatch(char intermediate, char final) => emulator.EscapeDispatch(intermediate, final);

        public void CsiDispatch(VtParameters parameters, char privateMarker, char intermediate, char final)
            => emulator.CsiDispatch(parameters, privateMarker, intermediate, final);

        public void OscDispatch(string data) => emulator.OscDispatch(data);
    }
}
