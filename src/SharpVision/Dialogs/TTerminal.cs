using SharpVision.Constants;
using System.Diagnostics;
using System.Text;

namespace SharpVision;

/// <summary>
/// Terminal-like view with black background and white text. Supports scrollback
/// history, keyboard/wheel scrolling, and optional command input mode.
/// </summary>
public class TTerminal : TView
{
    public const int DefaultMaxLines = 500;
    public const int DefaultMaxCommandHistory = 100;
    public new static readonly string Name = "TTerminal";

    private const ushort TerminalColor = Colors.fgWhite | Colors.bgBlack;
    // Inverted color for cursor: black on white.
    private const ushort CursorColor   = Colors.fgBlack | Colors.bgLightGray;
    // Selection highlight: black text on cyan background.
    private const ushort SelectionColor = Colors.fgBlack | Colors.bgCyan;
    private const int WheelStep = 3;

    // ── Diagnostic trace ─────────────────────────────────────────────────────
    // Set to true to log raw I/O and parser events to Debug output (DEBUG builds only).
    // Flip to false to silence without removing the instrumentation.
#if DEBUG
    private const bool TraceTerminalRawIo = true;
#else
    private const bool TraceTerminalRawIo = false;
#endif

    [Conditional("DEBUG")]
    private static void TraceTerminal(string message)
    {
        if (TraceTerminalRawIo)
            Debug.WriteLine(message);
    }

    /// <summary>
    /// Renders control characters as visible escape sequences so they can be
    /// read in the debug output without mangling the log line.
    /// </summary>
    private static string EscapeForLog(string text)
    {
        if (text == null) return "(null)";
        var sb = new StringBuilder(text.Length * 2);
        foreach (char ch in text)
            sb.Append(ch switch
            {
                '\r'  => "\\r",
                '\n'  => "\\n",
                '\b'  => "\\b",
                '\t'  => "\\t",
                '\x1b' => "\\x1B",
                _ when char.IsControl(ch) => $"\\x{(int)ch:X2}",
                _ => ch.ToString()
            });
        return sb.ToString();
    }

    private static string EscapeForLog(char ch) => EscapeForLog(ch.ToString());

    // ── Output buffer ─────────────────────────────────────────────────────────

    private readonly List<string> _lines = new();
    private string _currentLine = string.Empty;
    private int _maxLines = DefaultMaxLines;
    private int _scrollOffset;   // 0 = bottom (newest); k = k lines above bottom

    // ── Input state ───────────────────────────────────────────────────────────

    private TerminalInputMode _inputMode;
    private string _prompt = "> ";
    private string _inputBuffer = string.Empty;
    private int _inputCursor;    // caret position within _inputBuffer

    // ── Command history ───────────────────────────────────────────────────────

    private readonly List<string> _commandHistory = new();
    private int _maxCommandHistory = DefaultMaxCommandHistory;
    private int _historyIndex = -1;   // -1 = not navigating; 0 = oldest entry

    // ── Session ───────────────────────────────────────────────────────────────

    private ITerminalSession _session;

    // ── Selection ─────────────────────────────────────────────────────────────

    private TerminalTextPosition _selAnchor;
    private TerminalTextPosition _selActive;
    private bool _hasSelection;

    // ── Scrollbar ─────────────────────────────────────────────────────────────

    private TScrollBar? _vScrollBar;

    // ── Terminal size ─────────────────────────────────────────────────────────

    // The last size that was notified to an IResizableTerminalSession, used to
    // suppress redundant notifications.
    private TerminalSize _lastNotifiedSize;

    // ── ANSI support ─────────────────────────────────────────────────────────

    private bool _ansiEnabled = true;
    private readonly AnsiTerminalParser _parser = new();
    private readonly List<TerminalCell[]> _cellLines = new();
    private readonly List<TerminalCell> _currentCells = new();

    // Column at which the next output character will be written (0-based).
    // Updated by CR, LF, printable chars, and cursor-movement CSI sequences.
    private int _cursorColumn;

    // The absolute terminal column (0-based) at which _currentLine[0] lives.
    // When the shell sends an absolute CUP sequence (ESC[row;colH), we subtract
    // this base to convert to a logical column within _currentLine.
    // Reset to 0 on CommitLine; updated to the new cursor column on CR.
    private int _currentLineBaseColumn;
    // True once _currentLineBaseColumn has been established for the current line
    // via a CUP sequence.  Reset to false on CommitLine so the next CUP can
    // re-bootstrap the base for the following line.
    private bool _currentLineBaseColumnKnown;

    // ── Construction ─────────────────────────────────────────────────────────

    public TTerminal(TRect bounds)
        : base(bounds)
    {
        growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
        options |= Views.ofSelectable;
        eventMask |= Events.evKeyDown | Events.evMouseWheel | Events.evMouseMove | Events.evMouseUp
                   | Events.evMouseDown | Events.evBroadcast;
    }

    public TTerminal(TRect bounds, int maxLines)
        : this(bounds)
    {
        _maxLines = maxLines > 0 ? maxLines : 1;
    }

    // ── Output API ────────────────────────────────────────────────────────────

    public IReadOnlyList<string> Lines => _lines.AsReadOnly();

    /// <summary>The uncommitted partial line currently being built.</summary>
    public string CurrentLine => _currentLine;
    /// <summary>The current cursor column within <see cref="CurrentLine"/> (0-based).</summary>
    public int CursorColumn => _cursorColumn;

    /// <summary>
    /// When true, text written to the terminal is parsed for ANSI SGR color
    /// sequences and rendered with per-character VGA attributes. When false
    /// the terminal behaves as a plain-text output area. Default is true.
    /// </summary>
    public bool AnsiEnabled
    {
        get => _ansiEnabled;
        set
        {
            _ansiEnabled = value;
            if (!value)
            {
                _cellLines.Clear();
                _currentCells.Clear();
                _parser.ResetState();
            }
        }
    }

    public int MaxLines
    {
        get => _maxLines;
        set
        {
            _maxLines = value > 0 ? value : 1;
            TrimHistory();
            SyncScrollBar();
        }
    }

    /// <summary>Appends text to the current line, flushing completed lines.</summary>
    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        AppendText(text);
        DrawView();
    }

    /// <summary>Appends text and terminates the line.</summary>
    public void WriteLine(string line)
    {
        Write(line + "\n");
    }

    /// <summary>Removes all output lines and resets scroll to the bottom.</summary>
    public void Clear()
    {
        _lines.Clear();
        _cellLines.Clear();
        _currentLine = string.Empty;
        _currentCells.Clear();
        _cursorColumn = 0;
        _scrollOffset = 0;
        _hasSelection = false;
        SyncScrollBar();
        DrawView();
    }

    // ── Input mode ────────────────────────────────────────────────────────────

    /// <summary>
    /// Controls whether the terminal accepts typed input and how keystrokes are
    /// processed. Default is <see cref="TerminalInputMode.None"/> (output-only).
    /// </summary>
    public TerminalInputMode InputMode
    {
        get => _inputMode;
        set
        {
            if (_inputMode == value) return;
            if (_inputMode == TerminalInputMode.Command)
            {
                // Leaving Command mode: discard local editing state.
                _inputBuffer = string.Empty;
                _inputCursor = 0;
                _historyIndex = -1;
            }
            _inputMode = value;
            DrawView();
        }
    }

    /// <summary>
    /// Compatibility shim for existing code.
    /// Getting returns <see langword="true"/> when <see cref="InputMode"/> is
    /// <see cref="TerminalInputMode.Command"/>.
    /// Setting <see langword="true"/> switches to
    /// <see cref="TerminalInputMode.Command"/>.
    /// Setting <see langword="false"/> switches to
    /// <see cref="TerminalInputMode.None"/> unless
    /// <see cref="TerminalInputMode.RawSession"/> is already active, in which
    /// case the mode is left unchanged.
    /// </summary>
    public bool InputEnabled
    {
        get => _inputMode == TerminalInputMode.Command;
        set
        {
            if (value)
                InputMode = TerminalInputMode.Command;
            else if (_inputMode != TerminalInputMode.RawSession)
                InputMode = TerminalInputMode.None;
        }
    }

    /// <summary>The prompt string shown before the input cursor. Default is "> ".</summary>
    public string Prompt
    {
        get => _prompt;
        set => _prompt = value ?? string.Empty;
    }

    /// <summary>Current contents of the input buffer (without the prompt).</summary>
    public string InputBuffer => _inputBuffer;

    /// <summary>Current cursor position within <see cref="InputBuffer"/>.</summary>
    public int InputCursor => _inputCursor;

    // ── Command history ───────────────────────────────────────────────────────

    /// <summary>Maximum number of commands retained in history.</summary>
    public int MaxCommandHistory
    {
        get => _maxCommandHistory;
        set
        {
            _maxCommandHistory = value > 0 ? value : 1;
            TrimCommandHistory();
        }
    }

    /// <summary>Read-only view of submitted commands, oldest first.</summary>
    public IReadOnlyList<string> CommandHistory => _commandHistory.AsReadOnly();

    // ── CommandSubmitted event ────────────────────────────────────────────────

    /// <summary>
    /// Raised when the user presses Enter in input mode. Empty commands are
    /// allowed; the prompt line is echoed in all cases.
    /// </summary>
    public event EventHandler<TerminalCommandEventArgs> CommandSubmitted;

    // ── Scroll API ────────────────────────────────────────────────────────────

    public int ScrollOffset => _scrollOffset;
    public bool IsAtBottom => _scrollOffset == 0;

    public void ScrollLineUp()
    {
        _scrollOffset = Math.Min(_scrollOffset + 1, MaxScrollOffset(OutputHeight()));
        SyncScrollBar();
        DrawView();
    }

    public void ScrollLineDown()
    {
        _scrollOffset = Math.Max(_scrollOffset - 1, 0);
        SyncScrollBar();
        DrawView();
    }

    public void ScrollPageUp()
    {
        int page = OutputHeight();
        _scrollOffset = Math.Min(_scrollOffset + page, MaxScrollOffset(page));
        SyncScrollBar();
        DrawView();
    }

    public void ScrollPageDown()
    {
        int page = OutputHeight();
        _scrollOffset = Math.Max(_scrollOffset - page, 0);
        SyncScrollBar();
        DrawView();
    }

    public void ScrollToTop()
    {
        _scrollOffset = MaxScrollOffset(OutputHeight());
        SyncScrollBar();
        DrawView();
    }

    public void ScrollToBottom()
    {
        _scrollOffset = 0;
        SyncScrollBar();
        DrawView();
    }

    // ── Vertical scrollbar API ────────────────────────────────────────────────

    /// <summary>
    /// Attaches a vertical <see cref="TScrollBar"/> to this terminal.
    /// The scrollbar is kept synchronized with the scrollback position.
    /// The terminal does not take ownership of the scrollbar lifetime.
    /// </summary>
    public void AttachVerticalScrollBar(TScrollBar scrollBar)
    {
        _vScrollBar = scrollBar;
        SyncScrollBar();
    }

    /// <summary>Detaches the current vertical scrollbar, if any.</summary>
    public void DetachVerticalScrollBar()
    {
        _vScrollBar = null;
    }

    // ── Terminal size API ─────────────────────────────────────────────────────

    /// <summary>
    /// Current terminal size in character cells. Reflects <c>size.x</c> ×
    /// <c>size.y</c> and is updated whenever <see cref="ChangeBounds"/> is
    /// called. Both dimensions are always at least 1.
    ///
    /// <para>
    /// <see cref="TerminalSize.Rows"/> is the full view height, not the
    /// reduced output-only height used internally by the scroll engine.
    /// Sessions receive the physical cell count so that PTY drivers can set
    /// the correct window size.
    /// </para>
    /// </summary>
    public TerminalSize TerminalSize => new TerminalSize(Math.Max(1, size.x), Math.Max(1, size.y));

    // ── Selection API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Programmatically sets the selection to the range between
    /// <paramref name="anchor"/> and <paramref name="active"/>. The positions
    /// are document-order independent; the selection is cleared if they are
    /// equal. Useful for testing and script-driven selection.
    /// </summary>
    public void SetSelection(TerminalTextPosition anchor, TerminalTextPosition active)
    {
        _selAnchor = anchor;
        _selActive = active;
        _hasSelection = anchor != active;
        DrawView();
    }

    /// <summary>True when a text range is currently selected.</summary>
    public bool HasSelection => _hasSelection;

    /// <summary>
    /// Returns the plain text of the current selection, preserving line breaks
    /// between selected rows. Returns an empty string when there is no selection.
    /// ANSI color/attribute information is not included.
    /// </summary>
    public string GetSelectedText()
    {
        if (!_hasSelection) return string.Empty;
        var (start, end) = NormalizeSelection();
        return BuildSelectedText(start, end);
    }

    /// <summary>
    /// Clears the current selection without affecting terminal content.
    /// </summary>
    public void ClearSelection()
    {
        _hasSelection = false;
        DrawView();
    }

    /// <summary>
    /// Returns the plain text of the current selection and writes it to the
    /// active <see cref="ClipboardService"/>. Returns an empty string when
    /// there is no selection; the clipboard is not changed in that case.
    /// </summary>
    public string CopySelection()
    {
        string text = GetSelectedText();
        if (text.Length > 0)
            ClipboardService.Current.SetText(text);
        return text;
    }

    /// <summary>
    /// In <see cref="TerminalInputMode.Command"/> mode, inserts plain text into
    /// the input line at the current cursor position (line endings become spaces).
    /// In <see cref="TerminalInputMode.RawSession"/> mode, normalizes line endings
    /// to <c>"\r"</c> and forwards the text directly to the attached session.
    /// Has no effect in <see cref="TerminalInputMode.None"/> mode or when
    /// <paramref name="text"/> is null/empty.
    /// </summary>
    public void PasteText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (_inputMode == TerminalInputMode.RawSession)
        {
            // Normalize CRLF and LF to CR for PTY shell compatibility.
            string normalized = text.Replace("\r\n", "\r").Replace('\n', '\r');
            SendRaw(normalized);
            return;
        }

        if (_inputMode != TerminalInputMode.Command) return;

        // Normalize all newline variants to space (single-line paste MVP).
        string singleLine = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

        // Filter out non-printable control characters; keep all printable chars.
        var sb = new StringBuilder(singleLine.Length);
        foreach (char c in singleLine)
            if (c >= ' ') sb.Append(c);

        string toInsert = sb.ToString();
        if (toInsert.Length == 0) return;

        _inputBuffer = _inputBuffer.Insert(_inputCursor, toInsert);
        _inputCursor += toInsert.Length;
        DrawView();
    }

    // ── Visible-line calculation ──────────────────────────────────────────────

    /// <summary>
    /// Returns the output lines that would fill a viewport of
    /// <paramref name="height"/> rows. Empty rows are returned as empty strings.
    /// When input mode is active the caller is responsible for reserving the
    /// prompt row; this method covers the output region only.
    /// </summary>
    public IReadOnlyList<string> GetVisibleLines(int height)
    {
        if (height <= 0) return Array.Empty<string>();
        bool hasCurrentLine = _currentLine.Length > 0;
        int totalLines = _lines.Count + (hasCurrentLine ? 1 : 0);
        int maxOffset = Math.Max(0, totalLines - height);
        int effectiveOffset = Math.Min(_scrollOffset, maxOffset);
        int firstVisible = maxOffset - effectiveOffset;

        var result = new List<string>(height);
        for (int i = 0; i < height; i++)
        {
            int idx = firstVisible + i;
            if (idx < _lines.Count)
                result.Add(_lines[idx]);
            else if (hasCurrentLine && idx == _lines.Count)
                result.Add(_currentLine);
            else
                result.Add(string.Empty);
        }
        return result;
    }

    /// <summary>
    /// Returns the cell arrays (character + VGA attribute) for the same viewport
    /// as <see cref="GetVisibleLines"/>. Elements are null for rows that have no
    /// cell data (e.g. lines written before ANSI mode was enabled).
    /// Returns null when <see cref="AnsiEnabled"/> is false.
    /// </summary>
    public TerminalCell[][]? GetVisibleCellLines(int height)
    {
        if (!_ansiEnabled || height <= 0) return null;
        bool hasCurrentLine = _currentCells.Count > 0 || _currentLine.Length > 0;
        int totalLines = _lines.Count + (hasCurrentLine ? 1 : 0);
        int maxOffset = Math.Max(0, totalLines - height);
        int effectiveOffset = Math.Min(_scrollOffset, maxOffset);
        int firstVisible = maxOffset - effectiveOffset;

        var result = new TerminalCell[height][];
        for (int i = 0; i < height; i++)
        {
            int idx = firstVisible + i;
            if (idx < _cellLines.Count)
                result[i] = _cellLines[idx];
            else if (hasCurrentLine && idx == _lines.Count)
                result[i] = _currentCells.ToArray();
            // else result[i] remains null
        }
        return result;
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    public override void Draw()
    {
        int h = size.y;
        if (h <= 0) return;

        if (_inputMode == TerminalInputMode.Command && h >= 1)
        {
            // Reserve the last row for the prompt line.
            int outputRows = h - 1;
            DrawOutputRows(outputRows);
            DrawPromptRow(h - 1);
        }
        else
        {
            DrawOutputRows(h);
        }
    }

    private void DrawOutputRows(int count)
    {
        if (count <= 0) return;
        var visible = GetVisibleLines(count);
        TerminalCell[][]? visibleCells = _ansiEnabled ? GetVisibleCellLines(count) : null;

        for (int row = 0; row < count; row++)
        {
            var b = new TDrawBuffer();
            b.moveChar(0, ' ', TerminalColor, size.x);

            TerminalCell[]? cells = visibleCells != null && row < visibleCells.Length
                ? visibleCells[row]
                : null;

            string lineText = row < visible.Count ? visible[row] : string.Empty;

            if (cells != null && cells.Length > 0)
            {
                DrawCellsIntoBuffer(ref b, cells);
            }
            else
            {
                if (lineText.Length > 0)
                    b.moveStr(0, lineText, TerminalColor);
            }

            // Overlay selection highlight.
            if (_hasSelection)
            {
                int lineIndex = VisualRowToLineIndex(row, count);
                GetSelectionColumnsForLine(lineIndex, out int selColStart, out int selColEnd);
                if (selColStart >= 0)
                {
                    int highlightStart = Math.Min(selColStart, size.x);
                    int highlightEnd = selColEnd == int.MaxValue
                        ? size.x
                        : Math.Min(selColEnd + 1, size.x);
                    for (int col = highlightStart; col < highlightEnd; col++)
                    {
                        char ch = col < lineText.Length ? lineText[col] : ' ';
                        b.moveChar(col, ch, SelectionColor, 1);
                    }
                }
            }

            WriteLine(0, row, size.x, 1, b);
        }
    }

    private void DrawCellsIntoBuffer(ref TDrawBuffer b, TerminalCell[] cells)
    {
        int limit = Math.Min(cells.Length, size.x);
        int col = 0;
        int ci = 0;
        while (ci < limit)
        {
            ushort attr = cells[ci].Attr;
            int runStart = col;
            var sb = new StringBuilder();
            while (ci < limit && cells[ci].Attr == attr)
            {
                sb.Append(cells[ci].Character);
                ci++;
                col++;
            }
            b.moveStr(runStart, sb.ToString(), attr);
        }
    }

    private void DrawPromptRow(int row)
    {
        var b = new TDrawBuffer();
        b.moveChar(0, ' ', TerminalColor, size.x);

        string promptText = _prompt + _inputBuffer;
        b.moveStr(0, promptText, TerminalColor);

        // Render cursor: invert the cell under the caret.
        int cursorCol = _prompt.Length + _inputCursor;
        if (cursorCol < size.x)
        {
            char cursorChar = _inputCursor < _inputBuffer.Length
                ? _inputBuffer[_inputCursor]
                : ' ';
            b.moveChar(cursorCol, cursorChar, CursorColor, 1);
        }

        WriteLine(0, row, size.x, 1, b);
    }

    private static readonly TPalette _palette = new TPalette("\x0F", 1);
    public override TPalette GetPalette() => _palette;

    // ── Event handling ────────────────────────────────────────────────────────

    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);

        // Scrollbar change broadcast — scroll position driven by scrollbar thumb.
        if (ev.What == Events.evBroadcast &&
            ev.message.command == Views.cmScrollBarChanged &&
            _vScrollBar != null &&
            ReferenceEquals(ev.message.infoPtr, _vScrollBar))
        {
            int maxOff = MaxScrollOffset(OutputHeight());
            _scrollOffset = Math.Max(0, Math.Min(maxOff, maxOff - _vScrollBar.value));
            DrawView();
            return;
        }

        if (ev.What == Events.evMouseWheel)
        {
            bool up = (ev.mouse.buttons & Events.mbButton4) != 0;
            int h = OutputHeight();
            for (int i = 0; i < WheelStep; i++)
            {
                if (up) _scrollOffset = Math.Min(_scrollOffset + 1, MaxScrollOffset(h));
                else    _scrollOffset = Math.Max(_scrollOffset - 1, 0);
            }
            SyncScrollBar();
            DrawView();
            ClearEvent(ref ev);
            return;
        }

        if (ev.What == Events.evMouseDown)
        {
            HandleMouseDown(ref ev);
            return;
        }

        if (ev.What != Events.evKeyDown) return;

        ushort keyCode  = ev.keyDown.keyCode;
        byte   charCode = ev.keyDown.charScan.charCode;

        if (_inputMode == TerminalInputMode.Command)
            HandleInputModeKey(ref ev, keyCode, charCode);
        else if (_inputMode == TerminalInputMode.RawSession)
            HandleRawSessionKey(ref ev, keyCode, charCode);
        else
            HandleScrollKey(ref ev, keyCode);
    }

    private void HandleScrollKey(ref TEvent ev, ushort keyCode)
    {
        // Printable characters have no scroll action; skip to avoid misidentifying
        // an uppercase letter as a navigation key (e.g. 'G' == kbPgUp).
        if (ev.keyDown.charScan.charCode >= 32 && ev.keyDown.charScan.charCode < 127)
            return;

        bool handled = true;
        switch (keyCode)
        {
            case Keys.kbUp:   ScrollLineUp();   break;
            case Keys.kbDown: ScrollLineDown(); break;
            case Keys.kbPgUp: ScrollPageUp();   break;
            case Keys.kbPgDn: ScrollPageDown(); break;
            case Keys.kbHome: ScrollToTop();    break;
            case Keys.kbEnd:  ScrollToBottom(); break;

            case Keys.kbCtrlC:
                HandleControlC();
                break;

            case Keys.kbEsc:
                if (_hasSelection) ClearSelection();
                else handled = false;
                break;

            default: handled = false; break;
        }
        if (handled) ClearEvent(ref ev);
    }

    private void HandleRawSessionKey(ref TEvent ev, ushort keyCode, byte charCode)
    {
        // Ctrl+C: copy selection if active, otherwise interrupt the session.
        if (keyCode == Keys.kbCtrlC)
        {
            HandleControlC();
            ClearEvent(ref ev);
            return;
        }

        // Ctrl+V: paste clipboard text directly to the session.
        if (keyCode == Keys.kbCtrlV)
        {
            if (ClipboardService.Current.TryGetText(out string pasteText))
                PasteText(pasteText);
            ClearEvent(ref ev);
            return;
        }

        // Escape: clear selection first; if no selection, forward \x1b to session.
        if (keyCode == Keys.kbEsc && _hasSelection)
        {
            ClearSelection();
            ClearEvent(ref ev);
            return;
        }

        string? seq = RawSessionKeyMap.GetSequence(keyCode, charCode);
        if (seq != null)
        {
            SendRaw(seq);
            ClearEvent(ref ev);
        }
    }

    private void HandleInputModeKey(ref TEvent ev, ushort keyCode, byte charCode)
    {
        // Printable characters are inserted before the keyCode switch to avoid
        // false matches between an uppercase letter's ASCII value and a TV
        // navigation-key constant (e.g. 'G' == kbPgUp, 'L' == kbPgDn).
        if (charCode >= 32 && charCode < 127)
        {
            InputInsertChar((char)charCode);
            ClearEvent(ref ev);
            return;
        }

        switch (keyCode)
        {
            // Submit
            case Keys.kbEnter:
            case Keys.kbCtrlM:
                SubmitInput();
                ClearEvent(ref ev);
                return;

            // Editing
            case Keys.kbBack:
                InputBackspace();
                ClearEvent(ref ev);
                return;

            case Keys.kbDel:
                InputDelete();
                ClearEvent(ref ev);
                return;

            case Keys.kbLeft:
                if (_inputCursor > 0) { _inputCursor--; DrawView(); }
                ClearEvent(ref ev);
                return;

            case Keys.kbRight:
                if (_inputCursor < _inputBuffer.Length) { _inputCursor++; DrawView(); }
                ClearEvent(ref ev);
                return;

            case Keys.kbHome:
            case Keys.kbCtrlA:
                _inputCursor = 0;
                DrawView();
                ClearEvent(ref ev);
                return;

            case Keys.kbEnd:
            case Keys.kbCtrlE:
                _inputCursor = _inputBuffer.Length;
                DrawView();
                ClearEvent(ref ev);
                return;

            // History — Up/Down navigate history; PgUp/PgDn scroll output.
            case Keys.kbUp:
                HistoryUp();
                ClearEvent(ref ev);
                return;

            case Keys.kbDown:
                HistoryDown();
                ClearEvent(ref ev);
                return;

            case Keys.kbPgUp:
                ScrollPageUp();
                ClearEvent(ref ev);
                return;

            case Keys.kbPgDn:
                ScrollPageDown();
                ClearEvent(ref ev);
                return;

            case Keys.kbCtrlC:
                HandleControlC();
                ClearEvent(ref ev);
                return;

            case Keys.kbCtrlV:
                if (ClipboardService.Current.TryGetText(out string pasteText))
                    PasteText(pasteText);
                ClearEvent(ref ev);
                return;

            case Keys.kbEsc:
                if (_hasSelection)
                {
                    ClearSelection();
                    ClearEvent(ref ev);
                }
                return;
        }
    }

    // ── Input editing helpers ─────────────────────────────────────────────────

    /// <summary>Inserts a character at the current cursor position.</summary>
    public void InputInsertChar(char c)
    {
        _inputBuffer = _inputBuffer.Insert(_inputCursor, c.ToString());
        _inputCursor++;
        DrawView();
    }

    /// <summary>Deletes the character before the cursor (Backspace).</summary>
    public void InputBackspace()
    {
        if (_inputCursor > 0)
        {
            _inputBuffer = _inputBuffer.Remove(_inputCursor - 1, 1);
            _inputCursor--;
            DrawView();
        }
    }

    /// <summary>Deletes the character at the cursor (Delete).</summary>
    public void InputDelete()
    {
        if (_inputCursor < _inputBuffer.Length)
        {
            _inputBuffer = _inputBuffer.Remove(_inputCursor, 1);
            DrawView();
        }
    }

    /// <summary>
    /// Submits the current input buffer: echoes the prompt+command, raises
    /// <see cref="CommandSubmitted"/>, clears the buffer, and optionally
    /// forwards the command to an attached session.
    /// </summary>
    public void SubmitInput()
    {
        string command = _inputBuffer;

        // Capture the active session before the event fires so that a handler
        // that swaps the session (e.g. StartShell switching to RawSession) does
        // not cause the command text to be forwarded to the newly attached session.
        ITerminalSession sessionAtSubmit = _session;

        // Echo to output.
        WriteLine(_prompt + command);

        // Update history.
        _commandHistory.Add(command);
        TrimCommandHistory();
        _historyIndex = -1;

        // Clear input.
        _inputBuffer = string.Empty;
        _inputCursor = 0;

        // Always scroll to bottom after submit.
        _scrollOffset = 0;

        // Raise event — handler may swap _session and/or _inputMode.
        CommandSubmitted?.Invoke(this, new TerminalCommandEventArgs(command));

        // Forward to the session that was active when Enter was pressed, and only
        // if we are still in Command mode.  If the handler switched to RawSession
        // the user typed a shell-launcher command; sending it again would corrupt
        // the shell's stdin.
        if (_inputMode == TerminalInputMode.Command
            && sessionAtSubmit != null
            && sessionAtSubmit.IsRunning)
        {
            _ = SendToSessionAsync(command + "\n", sessionAtSubmit);
        }

        DrawView();
    }

    private void SendRaw(string text)
    {
        if (_session == null || !_session.IsRunning || string.IsNullOrEmpty(text)) return;
        TraceTerminal($"TTerminal RAW IN  -> [{EscapeForLog(text)}]");
        _ = SendToSessionAsync(text, _session);
    }

    private System.Threading.Tasks.Task SendToSessionAsync(string text)
        => SendToSessionAsync(text, _session);

    private async System.Threading.Tasks.Task SendToSessionAsync(string text, ITerminalSession session)
    {
        try
        {
            await session.SendInputAsync(text).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Write($"[session error: {ex.Message}]\n");
        }
    }

    /// <summary>
    /// Handles Ctrl+C: copies the selection if one is active, otherwise
    /// requests an interrupt on the attached session (if supported).
    /// </summary>
    public void HandleControlC()
    {
        if (_hasSelection)
        {
            CopySelection();
            return;
        }

        if (_session is IInterruptibleTerminalSession interruptible && _session.IsRunning)
            _ = InterruptSessionAsync(interruptible);
    }

    private async System.Threading.Tasks.Task InterruptSessionAsync(IInterruptibleTerminalSession interruptible)
    {
        try
        {
            await interruptible.InterruptAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Write($"[interrupt error: {ex.Message}]\n");
        }
    }

    private void NotifyResize()
    {
        if (_session is not IResizableTerminalSession resizable) return;
        var current = TerminalSize;
        if (current == _lastNotifiedSize) return;
        _lastNotifiedSize = current;
        _ = NotifyResizeAsync(resizable, current);
    }

    private async System.Threading.Tasks.Task NotifyResizeAsync(IResizableTerminalSession resizable, TerminalSize size)
    {
        try
        {
            await resizable.ResizeAsync(size).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Write($"[resize error: {ex.Message}]\n");
        }
    }

    // ── History navigation ────────────────────────────────────────────────────

    private void HistoryUp()
    {
        if (_commandHistory.Count == 0) return;
        if (_historyIndex == -1)
            _historyIndex = _commandHistory.Count - 1;
        else if (_historyIndex > 0)
            _historyIndex--;
        SetInputFromHistory();
    }

    private void HistoryDown()
    {
        if (_historyIndex == -1) return;
        if (_historyIndex < _commandHistory.Count - 1)
        {
            _historyIndex++;
            SetInputFromHistory();
        }
        else
        {
            _historyIndex = -1;
            _inputBuffer = string.Empty;
            _inputCursor = 0;
            DrawView();
        }
    }

    private void SetInputFromHistory()
    {
        _inputBuffer = _commandHistory[_historyIndex];
        _inputCursor = _inputBuffer.Length;
        DrawView();
    }

    private void TrimCommandHistory()
    {
        while (_commandHistory.Count > _maxCommandHistory)
            _commandHistory.RemoveAt(0);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Height available for output rows. When input is enabled the prompt
    /// row consumes one row from the bottom.
    /// </summary>
    private int OutputHeight() => Math.Max(1, _inputMode == TerminalInputMode.Command ? size.y - 1 : size.y);

    private int MaxScrollOffset(int height)
    {
        bool hasCurrentLine = _currentLine.Length > 0;
        int totalLines = _lines.Count + (hasCurrentLine ? 1 : 0);
        return Math.Max(0, totalLines - height);
    }

    /// <summary>
    /// Synchronizes the attached vertical scrollbar to the current scroll state.
    /// scrollbar value 0 = top/oldest; value maxVal = bottom/newest.
    /// _scrollOffset 0 = bottom; _scrollOffset maxOffset = top.
    /// Therefore: scrollbar.value = MaxScrollOffset - _scrollOffset.
    /// </summary>
    private void SyncScrollBar()
    {
        if (_vScrollBar == null) return;
        int h = OutputHeight();
        int maxOff = MaxScrollOffset(h);
        int sbValue = maxOff - _scrollOffset;
        // SetParams(value, min, max, pageStep, arrowStep)
        _vScrollBar.SetParams(sbValue, 0, maxOff, h, 1);
    }

    /// <summary>
    /// Updates the scroll state after a bounds change to keep the terminal
    /// pinned to the bottom if it was already there, or to preserve a valid
    /// scroll position if it was scrolled up.
    /// </summary>
    public override void ChangeBounds(TRect bounds)
    {
        bool wasAtBottom = _scrollOffset == 0;
        SetBounds(bounds);
        int h = OutputHeight();
        int maxOff = MaxScrollOffset(h);
        if (wasAtBottom)
            _scrollOffset = 0;
        else
            _scrollOffset = Math.Min(_scrollOffset, maxOff);
        SyncScrollBar();
        NotifyResize();
        DrawView();
    }

    private void AppendText(string text)
    {
        if (_ansiEnabled)
            AppendAnsiText(text);
        else
            AppendPlainText(text);
    }

    private void AppendPlainText(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                CommitLine();
            }
            else if (c == '\n')
            {
                CommitLine();
            }
            else
            {
                _currentLine += c;
            }
            i++;
        }
    }

    private void AppendAnsiText(string text)
    {
        _parser.Parse(
            text,
            onChar: (ch, attr) =>
            {
                TraceTerminal($"TTerminal PARSER: Char('{EscapeForLog(ch)}') at col {_cursorColumn}, lineLen={_currentLine.Length}");
                if (_cursorColumn < _currentLine.Length)
                {
                    // Overwrite the character at the current column.
                    var chars = _currentLine.ToCharArray();
                    chars[_cursorColumn] = ch;
                    _currentLine = new string(chars);
                    if (_cursorColumn < _currentCells.Count)
                        _currentCells[_cursorColumn] = new TerminalCell(ch, attr);
                }
                else
                {
                    // Append, padding with spaces if the cursor moved past the end.
                    while (_currentLine.Length < _cursorColumn)
                    {
                        _currentLine += ' ';
                        _currentCells.Add(new TerminalCell(' ', _parser.CurrentAttr));
                    }
                    _currentLine += ch;
                    _currentCells.Add(new TerminalCell(ch, attr));
                }
                _cursorColumn++;
            },
            onNewLine: () =>
            {
                TraceTerminal($"TTerminal PARSER: LF  (col={_cursorColumn}, lineLen={_currentLine.Length})");
                CommitLine();
            },
            onCarriageReturn: () =>
            {
                TraceTerminal($"TTerminal PARSER: CR  (col={_cursorColumn}, lineLen={_currentLine.Length})");
                // CR moves the terminal cursor to the start of the current visual line.
                // Record the absolute column before resetting so that _currentLineBaseColumn
                // stays at the left edge of the line (which is 0 for the first write,
                // unchanged on subsequent CRs to the same line).
                _currentLineBaseColumn = 0;
                _cursorColumn = 0;
            },
            onBackspace: () =>
            {
                TraceTerminal($"TTerminal PARSER: BS  (col={_cursorColumn}, lineLen={_currentLine.Length})");
                if (_cursorColumn > 0)
                    _cursorColumn--;
            },
            onClearScreen: Clear,
            onEraseInLine: (mode) =>
            {
                TraceTerminal($"TTerminal PARSER: EraseInLine({mode}) at col {_cursorColumn}, lineLen={_currentLine.Length}");
                switch (mode)
                {
                    case 0: // erase from cursor to end of line
                        if (_cursorColumn < _currentLine.Length)
                        {
                            _currentLine = _currentLine[.._cursorColumn];
                            if (_currentCells.Count > _cursorColumn)
                                _currentCells.RemoveRange(_cursorColumn, _currentCells.Count - _cursorColumn);
                        }
                        break;
                    case 1: // erase from start of line to cursor (inclusive)
                        {
                            int len = Math.Min(_cursorColumn + 1, _currentLine.Length);
                            _currentLine = new string(' ', len) + _currentLine[len..];
                            for (int ci = 0; ci < len && ci < _currentCells.Count; ci++)
                                _currentCells[ci] = new TerminalCell(' ', TerminalColor);
                        }
                        break;
                    case 2: // erase entire line and reset cursor to column 0
                        _currentLine = string.Empty;
                        _currentCells.Clear();
                        _cursorColumn = 0;
                        break;
                }
            },
            onCursorColumn: (n) =>
            {
                int newCol = Math.Max(0, n - 1);
                TraceTerminal($"TTerminal PARSER: CursorColumn({n}) -> col {newCol}, lineLen={_currentLine.Length}");
                _cursorColumn = newCol;
            },
            onCursorRight: (n) =>
            {
                TraceTerminal($"TTerminal PARSER: CursorRight({n}) col {_cursorColumn}->{_cursorColumn + n}, lineLen={_currentLine.Length}");
                _cursorColumn += n;
            },
            onCursorLeft: (n) =>
            {
                int newCol = Math.Max(0, _cursorColumn - n);
                TraceTerminal($"TTerminal PARSER: CursorLeft({n}) col {_cursorColumn}->{newCol}, lineLen={_currentLine.Length}");
                _cursorColumn = newCol;
            },
            onCursorPosition: (row, col) =>
            {
                // Convert absolute 1-based terminal column to a logical index within
                // _currentLine.  _currentLineBaseColumn holds the 0-based absolute column
                // at which _currentLine[0] lives.
                int absCol = col - 1;          // 0-based absolute terminal column
                int logCol;
                if (!_currentLineBaseColumnKnown)
                {
                    // First CUP for this logical line while the base is unknown.
                    // The shell (e.g. cmd.exe ESC) sends CUP to reposition at the
                    // start of the editable region (logical column 0).  Bootstrap
                    // the base from this absolute position.
                    _currentLineBaseColumn = absCol;
                    _currentLineBaseColumnKnown = true;
                    logCol = 0;
                }
                else
                {
                    logCol = Math.Max(0, absCol - _currentLineBaseColumn);
                }
                TraceTerminal($"TTerminal PARSER: CursorPosition(row={row}, col={col}) absCol={absCol} base={_currentLineBaseColumn} known={_currentLineBaseColumnKnown} -> logCol={logCol}, lineLen={_currentLine.Length}");

                // Before repositioning: if there is content after the new logical
                // column and it is all spaces (i.e. the shell just overwrote typed
                // chars with spaces), trim it so the line does not retain stale blanks.
                if (logCol < _currentLine.Length)
                {
                    bool trailingAllSpaces = true;
                    for (int ci = logCol; ci < _currentLine.Length; ci++)
                    {
                        if (_currentLine[ci] != ' ') { trailingAllSpaces = false; break; }
                    }
                    if (trailingAllSpaces)
                    {
                        _currentLine = _currentLine[..logCol];
                        if (_currentCells.Count > logCol)
                            _currentCells.RemoveRange(logCol, _currentCells.Count - logCol);
                    }
                }

                _cursorColumn = logCol;
            }
        );
    }

    private void CommitLine()
    {
        bool wasAtBottom = _scrollOffset == 0;
        _lines.Add(_currentLine);
        _currentLine = string.Empty;
        _cursorColumn = 0;
        _currentLineBaseColumn = 0;
        _currentLineBaseColumnKnown = false;
        if (_ansiEnabled)
        {
            _cellLines.Add(_currentCells.ToArray());
            _currentCells.Clear();
        }
        if (!wasAtBottom)
            _scrollOffset++;
        // Selection line indices may shift when lines are trimmed; clear to avoid stale ranges.
        _hasSelection = false;
        TrimHistory();
        SyncScrollBar();
    }

    private void TrimHistory()
    {
        while (_lines.Count > _maxLines)
        {
            _lines.RemoveAt(0);
            if (_cellLines.Count > 0)
                _cellLines.RemoveAt(0);
        }
    }

    // ── Mouse selection helpers ───────────────────────────────────────────────

    private void HandleMouseDown(ref TEvent ev)
    {
        if ((ev.mouse.buttons & Events.mbLeftButton) == 0) return;

        TPoint local = MakeLocal(ev.mouse.where);
        int outputH = OutputHeight();

        // Only start selection when the click is within the output area.
        if (local.x < 0 || local.x >= size.x || local.y < 0 || local.y >= outputH)
            return;

        _selAnchor = VisualPointToPosition(local.x, local.y, outputH);
        _selActive = _selAnchor;
        _hasSelection = false;
        DrawView();

        // Track mouse moves until release.
        while (MouseEvent(ref ev, Events.evMouseMove))
        {
            local = MakeLocal(ev.mouse.where);
            int cx = Math.Clamp(local.x, 0, size.x - 1);
            int cy = Math.Clamp(local.y, 0, outputH - 1);
            _selActive = VisualPointToPosition(cx, cy, outputH);
            _hasSelection = _selAnchor != _selActive;
            DrawView();
        }

        // Mouse up: finalize selection.
        local = MakeLocal(ev.mouse.where);
        int fx = Math.Clamp(local.x, 0, size.x - 1);
        int fy = Math.Clamp(local.y, 0, outputH - 1);
        _selActive = VisualPointToPosition(fx, fy, outputH);
        // A click without drag clears the selection.
        _hasSelection = _selAnchor != _selActive;
        DrawView();
        ClearEvent(ref ev);
    }

    /// <summary>
    /// Converts a visual (column, row) coordinate inside the output area to a
    /// <see cref="TerminalTextPosition"/> in the backing line buffer.
    /// </summary>
    public TerminalTextPosition VisualPointToPosition(int col, int row, int outputHeight)
    {
        int lineIndex = VisualRowToLineIndex(row, outputHeight);
        return new TerminalTextPosition(lineIndex, col);
    }

    /// <summary>
    /// Maps a visual row within an output area of <paramref name="outputHeight"/>
    /// rows to the corresponding index in the backing line buffer.
    /// </summary>
    public int VisualRowToLineIndex(int row, int outputHeight)
    {
        bool hasCurrentLine = _currentLine.Length > 0;
        int totalLines = _lines.Count + (hasCurrentLine ? 1 : 0);
        int maxOffset = Math.Max(0, totalLines - outputHeight);
        int effectiveOffset = Math.Min(_scrollOffset, maxOffset);
        int firstVisible = maxOffset - effectiveOffset;
        return firstVisible + row;
    }

    private string GetLineText(int lineIndex)
    {
        if (lineIndex < 0) return string.Empty;
        if (lineIndex < _lines.Count) return _lines[lineIndex];
        if (_currentLine.Length > 0 && lineIndex == _lines.Count) return _currentLine;
        return string.Empty;
    }

    private (TerminalTextPosition Start, TerminalTextPosition End) NormalizeSelection()
    {
        return _selAnchor.IsBefore(_selActive)
            ? (_selAnchor, _selActive)
            : (_selActive, _selAnchor);
    }

    /// <summary>
    /// Computes the selected column range for a given backing line index.
    /// Sets <paramref name="selColStart"/> and <paramref name="selColEnd"/> to
    /// -1 when the line is not selected. <paramref name="selColEnd"/> is
    /// <see cref="int.MaxValue"/> when the selection extends to the end of the
    /// visible row.
    /// </summary>
    private void GetSelectionColumnsForLine(int lineIndex, out int selColStart, out int selColEnd)
    {
        selColStart = -1;
        selColEnd = -1;
        if (!_hasSelection) return;

        var (start, end) = NormalizeSelection();
        if (lineIndex < start.LineIndex || lineIndex > end.LineIndex) return;

        if (start.LineIndex == end.LineIndex)
        {
            selColStart = start.Column;
            selColEnd = end.Column;
        }
        else if (lineIndex == start.LineIndex)
        {
            selColStart = start.Column;
            selColEnd = int.MaxValue;
        }
        else if (lineIndex == end.LineIndex)
        {
            selColStart = 0;
            selColEnd = end.Column;
        }
        else
        {
            selColStart = 0;
            selColEnd = int.MaxValue;
        }
    }

    private string BuildSelectedText(TerminalTextPosition start, TerminalTextPosition end)
    {
        if (start.LineIndex == end.LineIndex)
        {
            string line = GetLineText(start.LineIndex);
            int s = Math.Min(start.Column, line.Length);
            int e = Math.Min(end.Column + 1, line.Length);
            return s < e ? line[s..e] : string.Empty;
        }

        var sb = new StringBuilder();

        // First line: from start column to end of line.
        string firstLine = GetLineText(start.LineIndex);
        int sc = Math.Min(start.Column, firstLine.Length);
        sb.Append(firstLine[sc..]);

        // Middle lines: complete lines.
        for (int li = start.LineIndex + 1; li < end.LineIndex; li++)
        {
            sb.Append('\n');
            sb.Append(GetLineText(li));
        }

        // Last line: from beginning to end column (inclusive).
        sb.Append('\n');
        string lastLine = GetLineText(end.LineIndex);
        int ec = Math.Min(end.Column + 1, lastLine.Length);
        sb.Append(lastLine[..ec]);

        return sb.ToString();
    }

    // ── Session attachment ────────────────────────────────────────────────────

    /// <summary>
    /// Subscribe to <paramref name="session"/> so that output events are written
    /// into this terminal. Any previously attached session is detached first.
    /// TTerminal does not take ownership of the session lifetime.
    ///
    /// Threading note: <see cref="ITerminalSession.OutputReceived"/> from
    /// <see cref="ProcessTerminalSession"/> fires on background threads.
    /// <see cref="Write"/> is safe to call from any thread for buffer mutations,
    /// but <see cref="DrawView"/> requires the SharpVision UI thread when the
    /// view is in a live group.
    /// </summary>
    public void AttachSession(ITerminalSession session)
    {
        DetachSession();
        _session = session;
        session.OutputReceived += OnSessionOutput;
        // Notify the new session of the current size immediately so PTY
        // drivers can set the correct window size from the start.
        _lastNotifiedSize = default; // force notification even if unchanged
        NotifyResize();
    }

    /// <summary>Unsubscribe from the current session without disposing it.</summary>
    public void DetachSession()
    {
        if (_session != null)
        {
            _session.OutputReceived -= OnSessionOutput;
            _session = null;
        }
    }

    private void OnSessionOutput(object sender, TerminalOutputEventArgs e)
    {
        TraceTerminal($"TTerminal RAW OUT <- [{EscapeForLog(e.Text)}]");
        Write(e.Text);
    }

    // ── Streaming ─────────────────────────────────────────────────────────────

    public static readonly TStreamableClass StreamableClassTTerminal =
        new TStreamableClass("TTerminal", () => new TTerminal(StreamableInit.streamableInit), 0);

    protected TTerminal(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteInt((uint)_maxLines);
        os.WriteInt((uint)_lines.Count);
        foreach (string line in _lines)
            os.WriteString(line);
        os.WriteString(_currentLine);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        _maxLines = (int)isStream.ReadInt();
        int count = (int)isStream.ReadInt();
        _lines.Clear();
        for (int i = 0; i < count; i++)
            _lines.Add(isStream.ReadString() ?? string.Empty);
        _currentLine = isStream.ReadString() ?? string.Empty;
        _scrollOffset = 0;
        return this;
    }

    public new static TStreamable Build() => new TTerminal(StreamableInit.streamableInit);
}
