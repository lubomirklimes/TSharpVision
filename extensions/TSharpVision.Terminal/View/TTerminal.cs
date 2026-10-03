using System.Text;
using TSharpVision.Constants;

namespace TSharpVision.Terminal;

/// <summary>
/// A terminal view: a <see cref="TerminalEmulator"/> drawn cell by cell, the keyboard encoded for the program, and an
/// optional <see cref="ITerminalSession"/> connecting both to a real or simulated program.
/// </summary>
/// <remarks>
/// <para>
/// <b>Modes.</b> <see cref="TerminalInputMode.RawSession"/> is a full terminal for interactive programs (shells,
/// <c>less</c>, <c>vim</c>, <c>htop</c>, <c>mc</c>). <see cref="TerminalInputMode.None"/> is a read-only output view
/// (a log, the output of a command), and <see cref="TerminalInputMode.Command"/> adds a local prompt row with line
/// editing and history. All three show text through the same emulator; hosts that write text with bare line feeds set
/// <see cref="NewLineMode"/>.
/// </para>
/// <para>
/// <b>Threads.</b> Session output is parsed on the thread that delivers it, under <see cref="SyncRoot"/>, so a burst of
/// output never waits for the event loop and is never dropped or altered. Everything that belongs to the view —
/// redrawing, the scroll position, the scroll bar and the <see cref="TitleChanged"/>, <see cref="Bell"/> and
/// <see cref="SessionExited"/> events — happens on the event-loop thread through one coalesced posted event. Input is
/// handed to the session, whose writes never block the caller.
/// </para>
/// <para>
/// <b>Viewing scrollback.</b> Shift+PageUp and Shift+PageDown (and the wheel on the primary screen) move through the
/// scrollback; output arriving meanwhile does not move what is shown, and the next key sent to the program returns to
/// the bottom. The terminal's own cursor is unaffected.
/// </para>
/// </remarks>
public class TTerminal : TView
{
    /// <summary>Scrollback rows kept unless a different bound is given.</summary>
    public const int DefaultMaxLines = TerminalEmulator.DefaultMaxScrollbackLines;

    /// <summary>Commands kept for history navigation in <see cref="TerminalInputMode.Command"/>.</summary>
    public const int DefaultMaxCommandHistory = 100;

    /// <summary>
    /// The <c>TERM</c> value that describes this emulator to programs: xterm with 256 colours. What it implements is
    /// listed in the U-1b report; mouse reporting is the notable absence (programs that enable it keep working from the
    /// keyboard).
    /// </summary>
    public const string TermName = "xterm-256color";

    // Posted to this view to apply output delivered on another thread. Distinct from the other extension views'
    // posts (THexView 0xFE10, TTableView 0xFE11); a post names its target, so it never reaches another view.
    internal const ushort cmTerminalChanged = 0xFE12;

    private const byte SelectionAttribute = 0x30;   // black on cyan
    private const byte PromptAttribute = 0x07;
    private const byte PromptCursorAttribute = 0x70;
    private const int WheelStep = 3;

    private readonly object _sync = new();
    private readonly TerminalEmulator _emulator;
    private readonly TerminalColorMapper _colors = new();
    private ITerminalSession? _session;
    private bool _sessionExited;
    private int _changePosted;
    private long _scrollbackSeen;

    private TerminalInputMode _inputMode;
    private string _prompt = "> ";
    private string _inputBuffer = string.Empty;
    private int _inputCursor;
    private readonly List<string> _commandHistory = new();
    private int _maxCommandHistory = DefaultMaxCommandHistory;
    private int _historyIndex = -1;

    private int _scrollOffset;
    private TScrollBar? _vScrollBar;
    private TerminalSize _lastNotifiedSize;

    private TerminalTextPosition _selStart;
    private TerminalTextPosition _selEnd;
    private bool _hasSelection;

    /// <summary>A terminal at <paramref name="bounds"/> keeping <see cref="DefaultMaxLines"/> rows of scrollback.</summary>
    public TTerminal(TRect bounds)
        : this(bounds, DefaultMaxLines)
    {
    }

    /// <summary>A terminal keeping at most <paramref name="maxLines"/> rows of scrollback (at least 0).</summary>
    public TTerminal(TRect bounds, int maxLines)
        : base(bounds)
    {
        growMode = (byte)(Views.gfGrowHiX | Views.gfGrowHiY);
        options |= Views.ofSelectable;
        eventMask |= Events.evKeyDown | Events.evMouseWheel | Events.evMouseMove | Events.evMouseUp
                   | Events.evMouseDown | Events.evBroadcast | Events.evCommand;
        _emulator = new TerminalEmulator(Math.Max(1, size.x), OutputHeight(), Math.Max(0, maxLines));
    }

    // ── emulator ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The emulator behind the view. Session output changes it on the delivering thread: read it under
    /// <see cref="SyncRoot"/> while a session is attached.
    /// </summary>
    public TerminalEmulator Emulator => _emulator;

    /// <summary>The lock that serializes every use of <see cref="Emulator"/>.</summary>
    public object SyncRoot => _sync;

    /// <summary>The most scrollback rows kept.</summary>
    public int MaxLines
    {
        get { lock (_sync) return _emulator.MaxScrollbackLines; }
        set { lock (_sync) _emulator.MaxScrollbackLines = Math.Max(0, value); }
    }

    /// <summary>
    /// Line feed also returns the carriage (LNM). For text written with bare "\n" — logs, pipes — rather than a
    /// program behind a PTY, which already sends "\r\n".
    /// </summary>
    public bool NewLineMode
    {
        get { lock (_sync) return _emulator.NewLineMode; }
        set { lock (_sync) _emulator.NewLineMode = value; }
    }

    /// <summary>The title the program set (OSC 0/2); empty until it sets one.</summary>
    public string Title
    {
        get { lock (_sync) return _emulator.Title; }
    }

    /// <summary>Raised on the event-loop thread when <see cref="Title"/> changes.</summary>
    public event EventHandler? TitleChanged;

    /// <summary>Raised on the event-loop thread when the program rings the bell.</summary>
    public event EventHandler? Bell;

    /// <summary>Raised on the event-loop thread when the attached session ends, after its last output was shown.</summary>
    public event EventHandler? SessionExited;

    /// <summary>Maps ANSI colours 0–15 to VGA colours 0–15. Fewer than 16 entries leave the table unchanged.</summary>
    public void ApplyColorMap(byte[] map16)
    {
        lock (_sync) _colors.ApplyColorMap(map16);
        DrawView();
    }

    // ── writing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes text as if the program had printed it: control sequences in it are interpreted. Callable from any thread.
    /// </summary>
    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (_sync)
        {
            _emulator.Feed(text);
            _emulator.TakeResponses();   // nobody to answer: the text did not come from a program
        }

        Changed();
    }

    /// <summary>Writes <paramref name="line"/> and a new line.</summary>
    public void WriteLine(string line) => Write(line + "\r\n");

    /// <summary>Clears the screen and the scrollback and resets the terminal.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _emulator.Reset();
            _emulator.ClearScrollback();
            _scrollbackSeen = _emulator.ScrollbackTotalAdded;
        }

        _scrollOffset = 0;
        _hasSelection = false;
        Changed();
    }

    // ── text ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The text of the logical lines before the cursor's line (scrollback, then screen), trailing blanks removed — what a
    /// log view has "committed".
    /// </summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_sync)
            {
                List<TerminalLine> lines = TerminalText.Lines(_emulator);
                int cursorLine = CursorLineIndex(lines);
                var texts = new List<string>(cursorLine);
                for (int i = 0; i < cursorLine; i++) texts.Add(TerminalText.LineText(_emulator, lines[i]));
                return texts;
            }
        }
    }

    /// <summary>The text of the logical line the cursor is on.</summary>
    public string CurrentLine
    {
        get
        {
            lock (_sync)
            {
                List<TerminalLine> lines = TerminalText.Lines(_emulator);
                int cursorLine = CursorLineIndex(lines);
                return cursorLine < 0 ? string.Empty : TerminalText.LineText(_emulator, lines[cursorLine]);
            }
        }
    }

    /// <summary>The cursor's column on the screen.</summary>
    public int CursorColumn
    {
        get { lock (_sync) return _emulator.CursorColumn; }
    }

    /// <summary>Number of logical lines in the buffer.</summary>
    public int LineCount
    {
        get { lock (_sync) return TerminalText.Lines(_emulator).Count; }
    }

    /// <summary>The text of logical line <paramref name="lineIndex"/>, or an empty string outside the buffer.</summary>
    public string GetLineText(int lineIndex)
    {
        lock (_sync)
        {
            List<TerminalLine> lines = TerminalText.Lines(_emulator);
            return lineIndex >= 0 && lineIndex < lines.Count ? TerminalText.LineText(_emulator, lines[lineIndex]) : string.Empty;
        }
    }

    /// <summary>
    /// All the text: every logical line up to the last one with content, joined with "\n". Colours and control sequences
    /// are not part of it.
    /// </summary>
    public string GetText()
    {
        lock (_sync)
        {
            List<TerminalLine> lines = TerminalText.Lines(_emulator);
            var texts = new List<string>(lines.Count);
            foreach (TerminalLine line in lines) texts.Add(TerminalText.LineText(_emulator, line));
            int count = texts.Count;
            while (count > 0 && texts[count - 1].Length == 0) count--;
            return string.Join('\n', texts.Take(count));
        }
    }

    /// <summary>
    /// Finds <paramref name="query"/> from line <paramref name="startLine"/> onwards, wrapping to the start. On success
    /// the match is from <paramref name="start"/> (inclusive) to <paramref name="end"/> (exclusive), as
    /// <see cref="SetSelection"/> takes it.
    /// </summary>
    public bool TryFind(string query, int startLine, StringComparison comparison,
        out TerminalTextPosition start, out TerminalTextPosition end)
    {
        start = end = default;
        if (string.IsNullOrEmpty(query)) return false;
        lock (_sync)
        {
            List<TerminalLine> lines = TerminalText.Lines(_emulator);
            if (lines.Count == 0) return false;
            startLine = Math.Clamp(startLine, 0, lines.Count - 1);
            var columns = new List<int>();
            for (int step = 0; step < lines.Count; step++)
            {
                int index = (startLine + step) % lines.Count;
                columns.Clear();
                string text = TerminalText.LineText(_emulator, lines[index], columns: columns);
                int at = text.IndexOf(query, comparison);
                if (at < 0) continue;
                int last = columns[at + query.Length - 1];
                start = new TerminalTextPosition(index, columns[at]);
                end = new TerminalTextPosition(index, last + CellWidthAt(lines[index], last));
                return true;
            }
        }

        return false;
    }

    private int CellWidthAt(TerminalLine line, int column)
    {
        for (int i = 0; i < line.RowCount; i++)
        {
            ReadOnlySpan<TerminalCell> cells = _emulator.GetBufferRow(line.FirstRow + i, out _);
            if (column < cells.Length) return Math.Max(1, (int)cells[column].Width);
            column -= cells.Length;
        }

        return 1;
    }

    private int CursorLineIndex(List<TerminalLine> lines)
    {
        int cursorRow = (_emulator.IsAlternateScreenActive ? 0 : _emulator.ScrollbackCount) + _emulator.CursorRow;
        return TerminalText.LineOfRow(lines, cursorRow, _emulator, out _);
    }

    // ── input mode and the command line ──────────────────────────────────────

    /// <summary>How keys are treated; see <see cref="TerminalInputMode"/>.</summary>
    public TerminalInputMode InputMode
    {
        get => _inputMode;
        set
        {
            if (_inputMode == value) return;
            _inputMode = value;
            if (value != TerminalInputMode.Command)
            {
                _inputBuffer = string.Empty;
                _inputCursor = 0;
                _historyIndex = -1;
            }

            ResizeEmulator();
            SyncScrollBar();
            DrawView();
        }
    }

    /// <summary>Whether the local command line is on: <see cref="TerminalInputMode.Command"/> rather than None.</summary>
    public bool InputEnabled
    {
        get => _inputMode == TerminalInputMode.Command;
        set => InputMode = value ? TerminalInputMode.Command : TerminalInputMode.None;
    }

    /// <summary>The prompt shown before the command line.</summary>
    public string Prompt
    {
        get => _prompt;
        set
        {
            _prompt = value ?? string.Empty;
            DrawView();
        }
    }

    /// <summary>The text being edited on the command line.</summary>
    public string InputBuffer => _inputBuffer;

    /// <summary>The caret within <see cref="InputBuffer"/>.</summary>
    public int InputCursor => _inputCursor;

    /// <summary>Commands kept for history navigation (at least 1).</summary>
    public int MaxCommandHistory
    {
        get => _maxCommandHistory;
        set
        {
            _maxCommandHistory = Math.Max(1, value);
            TrimCommandHistory();
        }
    }

    /// <summary>Submitted commands, oldest first.</summary>
    public IReadOnlyList<string> CommandHistory => _commandHistory.AsReadOnly();

    /// <summary>Raised when Enter submits the command line.</summary>
    public event EventHandler<TerminalCommandEventArgs>? CommandSubmitted;

    /// <summary>Inserts a character at the command-line caret.</summary>
    public void InputInsertChar(char c)
    {
        _inputBuffer = _inputBuffer.Insert(_inputCursor, c.ToString());
        _inputCursor++;
        DrawView();
    }

    /// <summary>Deletes the character before the command-line caret.</summary>
    public void InputBackspace()
    {
        if (_inputCursor == 0) return;
        _inputBuffer = _inputBuffer.Remove(_inputCursor - 1, 1);
        _inputCursor--;
        DrawView();
    }

    /// <summary>Deletes the character at the command-line caret.</summary>
    public void InputDelete()
    {
        if (_inputCursor >= _inputBuffer.Length) return;
        _inputBuffer = _inputBuffer.Remove(_inputCursor, 1);
        DrawView();
    }

    /// <summary>
    /// Submits the command line: echoes prompt and command, records history, raises <see cref="CommandSubmitted"/> and,
    /// if still in command mode, sends the command and a line feed to the session that was attached when Enter was
    /// pressed.
    /// </summary>
    public void SubmitInput()
    {
        string command = _inputBuffer;
        ITerminalSession? sessionAtSubmit = _session;

        WriteLine(_prompt + command);
        _commandHistory.Add(command);
        TrimCommandHistory();
        _historyIndex = -1;
        _inputBuffer = string.Empty;
        _inputCursor = 0;
        _scrollOffset = 0;

        CommandSubmitted?.Invoke(this, new TerminalCommandEventArgs(command));

        // A handler that switched to RawSession started a shell for this command; sending it again would corrupt it.
        if (_inputMode == TerminalInputMode.Command && sessionAtSubmit is { IsRunning: true })
            _ = SendAsync(sessionAtSubmit, command + "\n");

        SyncScrollBar();
        DrawView();
    }

    private void TrimCommandHistory()
    {
        if (_commandHistory.Count > _maxCommandHistory) _commandHistory.RemoveRange(0, _commandHistory.Count - _maxCommandHistory);
    }

    private void HistoryUp()
    {
        if (_commandHistory.Count == 0) return;
        _historyIndex = _historyIndex == -1 ? _commandHistory.Count - 1 : Math.Max(0, _historyIndex - 1);
        SetInputFromHistory();
    }

    private void HistoryDown()
    {
        if (_historyIndex == -1) return;
        if (_historyIndex < _commandHistory.Count - 1)
        {
            _historyIndex++;
            SetInputFromHistory();
            return;
        }

        _historyIndex = -1;
        _inputBuffer = string.Empty;
        _inputCursor = 0;
        DrawView();
    }

    private void SetInputFromHistory()
    {
        _inputBuffer = _commandHistory[_historyIndex];
        _inputCursor = _inputBuffer.Length;
        DrawView();
    }

    // ── scrolling ────────────────────────────────────────────────────────────

    /// <summary>Rows the view is scrolled back from the bottom; 0 shows the live screen.</summary>
    public int ScrollOffset => _scrollOffset;

    /// <summary>Whether the live screen is shown.</summary>
    public bool IsAtBottom => _scrollOffset == 0;

    /// <summary>Scrolls back one row.</summary>
    public void ScrollLineUp() => ScrollTo(_scrollOffset + 1);

    /// <summary>Scrolls forward one row.</summary>
    public void ScrollLineDown() => ScrollTo(_scrollOffset - 1);

    /// <summary>Scrolls back a page.</summary>
    public void ScrollPageUp() => ScrollTo(_scrollOffset + Math.Max(1, OutputHeight() - 1));

    /// <summary>Scrolls forward a page.</summary>
    public void ScrollPageDown() => ScrollTo(_scrollOffset - Math.Max(1, OutputHeight() - 1));

    /// <summary>Shows the oldest scrollback.</summary>
    public void ScrollToTop() => ScrollTo(int.MaxValue);

    /// <summary>Shows the live screen.</summary>
    public void ScrollToBottom() => ScrollTo(0);

    /// <summary>Scrolls so the first row of logical line <paramref name="lineIndex"/> is visible.</summary>
    public void ScrollToLine(int lineIndex)
    {
        int offset;
        lock (_sync)
        {
            List<TerminalLine> lines = TerminalText.Lines(_emulator);
            if (lineIndex < 0 || lineIndex >= lines.Count) return;
            int row = lines[lineIndex].FirstRow;
            int top = FirstVisibleRow(_scrollOffset);
            if (row >= top && row < top + _emulator.Rows) return;
            offset = _emulator.BufferRowCount - _emulator.Rows - row;
        }

        ScrollTo(offset);
    }

    private void ScrollTo(int offset)
    {
        int clamped;
        lock (_sync)
        {
            clamped = Math.Clamp(offset, 0, MaxScrollOffset());
            _scrollbackSeen = _emulator.ScrollbackTotalAdded;
        }

        if (clamped == _scrollOffset) return;
        _scrollOffset = clamped;
        SyncScrollBar();
        DrawView();
    }

    // Callers hold _sync.
    private int MaxScrollOffset() => Math.Max(0, _emulator.BufferRowCount - _emulator.Rows);

    private int FirstVisibleRow(int offset) => _emulator.BufferRowCount - _emulator.Rows - Math.Clamp(offset, 0, MaxScrollOffset());

    /// <summary>Connects a vertical scroll bar to the scrollback position.</summary>
    public void AttachVerticalScrollBar(TScrollBar scrollBar)
    {
        _vScrollBar = scrollBar;
        SyncScrollBar();
    }

    /// <summary>Disconnects the scroll bar.</summary>
    public void DetachVerticalScrollBar() => _vScrollBar = null;

    private void SyncScrollBar()
    {
        if (_vScrollBar is null) return;
        int max;
        lock (_sync) max = MaxScrollOffset();
        _vScrollBar.SetParams(max - Math.Min(_scrollOffset, max), 0, max, Math.Max(1, OutputHeight()), 1);
    }

    // ── size ─────────────────────────────────────────────────────────────────

    /// <summary>The size of the terminal grid: the view's width by its output rows.</summary>
    public TerminalSize TerminalSize => new(Math.Max(1, size.x), OutputHeight());

    private int OutputHeight() => Math.Max(1, _inputMode == TerminalInputMode.Command ? size.y - 1 : size.y);

    /// <inheritdoc />
    /// <remarks>
    /// The emulator is resized first and the session second, so the redraw a program sends after learning the new size
    /// is already interpreted at that size.
    /// </remarks>
    public override void ChangeBounds(TRect bounds)
    {
        SetBounds(bounds);
        ResizeEmulator();
        SyncScrollBar();
        NotifyResize();
        DrawView();
    }

    private void ResizeEmulator()
    {
        lock (_sync)
        {
            _emulator.Resize(Math.Max(1, size.x), OutputHeight());
            _scrollOffset = Math.Min(_scrollOffset, MaxScrollOffset());
            _scrollbackSeen = _emulator.ScrollbackTotalAdded;
        }
    }

    private void NotifyResize()
    {
        if (_session is not IResizableTerminalSession resizable) return;
        TerminalSize current = TerminalSize;
        if (current == _lastNotifiedSize) return;
        _lastNotifiedSize = current;
        _ = ResizeSessionAsync(resizable, current);
    }

    private static async Task ResizeSessionAsync(IResizableTerminalSession resizable, TerminalSize size)
    {
        try
        {
            await resizable.ResizeAsync(size).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A session that ended in the meantime cannot be resized; that is not an error of the view.
        }
    }

    // ── selection and clipboard ──────────────────────────────────────────────

    /// <summary>Selects from <paramref name="anchor"/> to <paramref name="active"/> (in either order; the later one is exclusive).</summary>
    public void SetSelection(TerminalTextPosition anchor, TerminalTextPosition active)
    {
        (_selStart, _selEnd) = anchor.IsBefore(active) ? (anchor, active) : (active, anchor);
        _hasSelection = _selStart != _selEnd;
        DrawView();
    }

    /// <summary>Whether text is selected.</summary>
    public bool HasSelection => _hasSelection;

    /// <summary>The selected text, lines joined with "\n"; empty without a selection.</summary>
    public string GetSelectedText()
    {
        if (!_hasSelection) return string.Empty;
        lock (_sync)
        {
            List<TerminalLine> lines = TerminalText.Lines(_emulator);
            var parts = new List<string>();
            for (int i = _selStart.LineIndex; i <= _selEnd.LineIndex && i < lines.Count; i++)
            {
                if (i < 0) continue;
                int from = i == _selStart.LineIndex ? _selStart.Column : 0;
                int to = i == _selEnd.LineIndex ? _selEnd.Column : int.MaxValue;
                parts.Add(TerminalText.LineText(_emulator, lines[i], from, to));
            }

            return string.Join('\n', parts);
        }
    }

    /// <summary>Clears the selection.</summary>
    public void ClearSelection()
    {
        if (!_hasSelection) return;
        _hasSelection = false;
        DrawView();
    }

    /// <summary>Puts the selected text on the clipboard and returns it; empty (and the clipboard untouched) without one.</summary>
    public string CopySelection()
    {
        string text = GetSelectedText();
        if (text.Length > 0) ClipboardService.Current.SetText(text);
        return text;
    }

    /// <summary>
    /// Pastes text. In <see cref="TerminalInputMode.RawSession"/> it goes to the program — line breaks as CR, and
    /// bracketed (ESC [200~ … ESC [201~) when the program asked for bracketed paste. In command mode it is inserted into
    /// the command line as one line. Otherwise nothing happens.
    /// </summary>
    public void PasteText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (_inputMode == TerminalInputMode.RawSession)
        {
            bool bracketed;
            lock (_sync) bracketed = _emulator.BracketedPaste;
            SendToProgram(TerminalInputEncoder.EncodePaste(text, bracketed));
            return;
        }

        if (_inputMode != TerminalInputMode.Command) return;
        var line = new StringBuilder(text.Length);
        foreach (char c in text.Replace("\r\n", " ", StringComparison.Ordinal))
            line.Append(c is '\r' or '\n' ? ' ' : c);
        string insert = new(line.ToString().Where(c => c >= ' ').ToArray());
        if (insert.Length == 0) return;
        _inputBuffer = _inputBuffer.Insert(_inputCursor, insert);
        _inputCursor += insert.Length;
        DrawView();
    }

    private void PasteClipboard()
    {
        if (ClipboardService.Current.TryGetText(out string text)) PasteText(text);
    }

    // ── session ──────────────────────────────────────────────────────────────

    /// <summary>The attached session, if any.</summary>
    public ITerminalSession? Session => _session;

    /// <summary>
    /// Connects <paramref name="session"/>: its output is shown and, in <see cref="TerminalInputMode.RawSession"/>, keys
    /// go to it. A previously attached session is detached first. The session is told the current size.
    /// The caller owns the session and must start, stop and dispose it; attaching does not start it.
    /// </summary>
    public void AttachSession(ITerminalSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        DetachSession();
        lock (_sync)
        {
            _session = session;
            _sessionExited = false;
        }

        session.OutputReceived += OnSessionOutput;
        session.Exited += OnSessionExited;
        _lastNotifiedSize = default;
        NotifyResize();
    }

    /// <summary>
    /// Disconnects the session: from now on none of its output reaches the view and no input, resize or interrupt is sent
    /// to it. Does not stop or dispose it.
    /// </summary>
    public void DetachSession()
    {
        ITerminalSession? session;
        lock (_sync)
        {
            session = _session;
            _session = null;
            _sessionExited = false;
        }

        if (session is null) return;
        session.OutputReceived -= OnSessionOutput;
        session.Exited -= OnSessionExited;
    }

    private void OnSessionOutput(object? sender, TerminalOutputEventArgs e)
    {
        ITerminalSession? session;
        string? responses;
        lock (_sync)
        {
            if (sender is null || !ReferenceEquals(sender, _session)) return;   // a detached session is not shown
            session = _session;
            _emulator.Feed(e.Data.Span);
            responses = _emulator.TakeResponses();
        }

        if (responses is not null && session.IsRunning) _ = SendAsync(session, responses);
        Changed();
    }

    private void OnSessionExited(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            if (sender is null || !ReferenceEquals(sender, _session)) return;
            _sessionExited = true;
        }

        Changed();
    }

    /// <summary>
    /// Ctrl+C: copies the selection when there is one, otherwise interrupts the program — through the session's own
    /// interrupt, or by sending ETX.
    /// </summary>
    public void HandleControlC()
    {
        if (_hasSelection)
        {
            CopySelection();
            ClearSelection();
            return;
        }

        ITerminalSession? session = _session;
        if (session is not { IsRunning: true }) return;
        if (session is IInterruptibleTerminalSession interruptible) _ = InterruptAsync(interruptible);
        else _ = SendAsync(session, "\x03");
    }

    private static async Task InterruptAsync(IInterruptibleTerminalSession interruptible)
    {
        try
        {
            await interruptible.InterruptAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The program is gone; there is nothing left to interrupt.
        }
    }

    private void SendToProgram(string encoded)
    {
        ITerminalSession? session = _session;
        if (session is not { IsRunning: true }) return;
        if (_scrollOffset != 0) ScrollToBottom();
        _ = SendAsync(session, encoded);
    }

    private static async Task SendAsync(ITerminalSession session, string text)
    {
        try
        {
            await session.SendInputAsync(TerminalInputEncoder.ToBytes(text)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Input to a session that has just ended is dropped, as the session contract allows.
        }
    }

    /// <inheritdoc />
    /// <remarks>Detaches the session, so nothing reaches a view that is gone.</remarks>
    public override void ShutDown()
    {
        DetachSession();
        _vScrollBar = null;
        base.ShutDown();
    }

    // ── change notification ──────────────────────────────────────────────────

    // Emulator state changed. On the event-loop thread the view catches up at once; from any other thread one post is
    // outstanding at a time, and it catches up with everything that arrived until it is handled.
    private void Changed()
    {
        if (TEventQueue.UiThreadId == 0 || TEventQueue.IsUiThread) ApplyChanges();
        else if (Interlocked.Exchange(ref _changePosted, 1) == 0) Post(cmTerminalChanged);
    }

    private void ApplyChanges()
    {
        TerminalChanges changes;
        bool exited;
        lock (_sync)
        {
            changes = _emulator.TakeChanges();
            exited = _sessionExited;
            _sessionExited = false;

            long added = _emulator.ScrollbackTotalAdded;
            if (_emulator.IsAlternateScreenActive) _scrollOffset = 0;
            else if (_scrollOffset > 0) _scrollOffset += (int)Math.Min(int.MaxValue, added - _scrollbackSeen);   // keep what is shown
            _scrollbackSeen = added;
            _scrollOffset = Math.Clamp(_scrollOffset, 0, MaxScrollOffset());
        }

        SyncScrollBar();
        if ((changes & (TerminalChanges.Title)) != 0) TitleChanged?.Invoke(this, EventArgs.Empty);
        if ((changes & TerminalChanges.Bell) != 0) Bell?.Invoke(this, EventArgs.Empty);
        if (changes != TerminalChanges.None || exited) DrawView();
        if (exited) SessionExited?.Invoke(this, EventArgs.Empty);
    }

    // ── drawing ──────────────────────────────────────────────────────────────

    private static readonly TPalette Palette = new("\x07", 1);

    /// <inheritdoc />
    public override TPalette GetPalette() => Palette;

    /// <inheritdoc />
    /// <remarks>Draws the emulator's cells as they are; nothing is parsed while drawing.</remarks>
    public override void Draw()
    {
        int width = size.x, height = size.y;
        if (width <= 0 || height <= 0) return;
        int outputRows = OutputHeight();
        if (_inputMode == TerminalInputMode.Command && height >= 2) DrawPromptRow(height - 1);
        else outputRows = height;

        Span<TScreenChar> rowBuffer = stackalloc TScreenChar[width];
        lock (_sync)
        {
            int offset = Math.Clamp(_scrollOffset, 0, MaxScrollOffset());
            int bufferRows = _emulator.BufferRowCount;
            int firstRow = bufferRows - _emulator.Rows - offset;
            bool reverse = _emulator.ReverseVideo;
            bool showCursor = offset == 0 && _emulator.CursorVisible && _inputMode != TerminalInputMode.Command;
            List<TerminalLine>? lines = _hasSelection ? TerminalText.Lines(_emulator) : null;
            byte blank = _colors.ToAttribute(TerminalStyle.Default, reverse);

            for (int y = 0; y < outputRows; y++)
            {
                var b = new TDrawBuffer(rowBuffer);
                b.moveChar(0, ' ', blank, width);
                int row = firstRow + y;
                if (row >= 0 && row < bufferRows)
                {
                    ReadOnlySpan<TerminalCell> cells = _emulator.GetBufferRow(row, out _);
                    int count = Math.Min(width, cells.Length);
                    for (int x = 0; x < count; x++)
                    {
                        TerminalCell cell = cells[x];
                        b.putChar(x, cell.IsContinuation ? ' ' : cell.DisplayChar);
                        b.putAttribute(x, _colors.ToAttribute(cell.Style, reverse));
                    }

                    if (lines is not null) DrawSelection(ref b, lines, row, width);
                }

                if (showCursor && y == _emulator.CursorRow && _emulator.CursorColumn < width)
                {
                    int x = _emulator.CursorColumn;
                    byte attr = _colors.ToAttribute(_emulator.GetCell(y, x).Style, reverse);
                    byte inverted = (byte)(((attr & 0x0F) << 4) | (attr >> 4));
                    b.putAttribute(x, (inverted >> 4) == (inverted & 0x0F) ? PromptCursorAttribute : inverted);
                }

                WriteLine(0, y, width, 1, b);
            }
        }
    }

    // Callers hold _sync.
    private void DrawSelection(ref TDrawBuffer b, List<TerminalLine> lines, int row, int width)
    {
        int line = TerminalText.LineOfRow(lines, row, _emulator, out int columnOffset);
        if (line < _selStart.LineIndex || line > _selEnd.LineIndex) return;
        for (int x = 0; x < width; x++)
        {
            var position = new TerminalTextPosition(line, columnOffset + x);
            if (!position.IsBefore(_selStart) && position.IsBefore(_selEnd)) b.putAttribute(x, SelectionAttribute);
        }
    }

    private void DrawPromptRow(int y)
    {
        int width = size.x;
        Span<TScreenChar> rowBuffer = stackalloc TScreenChar[width];
        var b = new TDrawBuffer(rowBuffer);
        b.moveChar(0, ' ', PromptAttribute, width);
        b.moveStr(0, _prompt + _inputBuffer, PromptAttribute);
        int caret = _prompt.Length + _inputCursor;
        if (caret < width)
            b.moveChar(caret, _inputCursor < _inputBuffer.Length ? _inputBuffer[_inputCursor] : ' ', PromptCursorAttribute, 1);
        WriteLine(0, y, width, 1, b);
    }

    // ── events ───────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);

        switch (ev.What)
        {
            case Events.evCommand when ev.message.command == cmTerminalChanged:
                Volatile.Write(ref _changePosted, 0);
                ApplyChanges();
                ClearEvent(ref ev);
                return;

            case Events.evCommand when ev.message.command == Views.cmCopy && _hasSelection:
                CopySelection();
                ClearSelection();
                ClearEvent(ref ev);
                return;

            case Events.evCommand when ev.message.command == Views.cmPaste && _inputMode != TerminalInputMode.None:
                PasteClipboard();
                ClearEvent(ref ev);
                return;

            case Events.evBroadcast when ev.message.command == Views.cmScrollBarChanged
                                         && _vScrollBar is not null && ReferenceEquals(ev.message.infoPtr, _vScrollBar):
            {
                int max;
                lock (_sync) max = MaxScrollOffset();
                ScrollTo(max - _vScrollBar.value);
                return;
            }

            case Events.evMouseWheel:
                HandleWheel(ref ev);
                return;

            case Events.evMouseDown:
                HandleMouseDown(ref ev);
                return;

            case Events.evKeyDown:
                if (_inputMode == TerminalInputMode.RawSession) HandleRawKey(ref ev);
                else if (_inputMode == TerminalInputMode.Command) HandleCommandKey(ref ev);
                else HandleScrollKey(ref ev);
                return;
        }
    }

    private void HandleWheel(ref TEvent ev)
    {
        bool up = (ev.mouse.eventFlags & Events.meWheelUp) != 0;
        bool down = (ev.mouse.eventFlags & Events.meWheelDown) != 0;
        if (!up && !down) return;

        bool alternate, sendKeys, application;
        lock (_sync)
        {
            alternate = _emulator.IsAlternateScreenActive;
            sendKeys = alternate && _emulator.AlternateScroll && _emulator.MouseTracking == TerminalMouseTracking.None;
            application = _emulator.ApplicationCursorKeys;
        }

        if (alternate)
        {
            // A full-screen program has no scrollback to show; with alternate scroll the wheel moves through its content.
            if (sendKeys && _inputMode == TerminalInputMode.RawSession)
            {
                string key = (application ? "\x1bO" : "\x1b[") + (up ? "A" : "B");
                SendToProgram(string.Concat(Enumerable.Repeat(key, WheelStep)));
            }
        }
        else
        {
            ScrollTo(_scrollOffset + (up ? WheelStep : -WheelStep));
        }

        ClearEvent(ref ev);
    }

    private void HandleRawKey(ref TEvent ev)
    {
        KeyDownEvent key = ev.keyDown;
        ushort code = key.keyCode;
        bool shift = (key.controlKeyState & Keys.kbShift) != 0;
        bool ctrl = (key.controlKeyState & Keys.kbCtrlShift) != 0;
        bool alt = (key.controlKeyState & Keys.kbAltShift) != 0;

        // Host conventions that take priority over sending the key (documented in the U-1b report):
        if (code == Keys.kbCtrlC && !alt)
        {
            HandleControlC();                  // copy a selection, else interrupt (ETX)
            ClearEvent(ref ev);
            return;
        }

        if ((code == Keys.kbCtrlV && !alt) || code == Keys.kbShiftIns)
        {
            PasteClipboard();                  // paste, bracketed when the program asked for it
            ClearEvent(ref ev);
            return;
        }

        if (code == Keys.kbCtrlIns && _hasSelection)
        {
            CopySelection();
            ClearSelection();
            ClearEvent(ref ev);
            return;
        }

        if (code == Keys.kbEsc && _hasSelection)
        {
            ClearSelection();
            ClearEvent(ref ev);
            return;
        }

        if (shift && !ctrl && !alt && (code == Keys.kbPgUp || code == Keys.kbPgDn) && !IsAlternate())
        {
            if (code == Keys.kbPgUp) ScrollPageUp();
            else ScrollPageDown();
            ClearEvent(ref ev);
            return;
        }

        bool application;
        lock (_sync) application = _emulator.ApplicationCursorKeys;
        string? encoded = TerminalInputEncoder.EncodeKey(key, application);
        if (encoded is null) return;   // no terminal meaning: left to the host

        ClearSelection();
        SendToProgram(encoded);
        ClearEvent(ref ev);
    }

    private bool IsAlternate()
    {
        lock (_sync) return _emulator.IsAlternateScreenActive;
    }

    private void HandleScrollKey(ref TEvent ev)
    {
        if (KeyText.PrintableText(ev.keyDown, extendedLegacy: false).Length > 0) return;
        switch (ev.keyDown.keyCode)
        {
            case Keys.kbUp: ScrollLineUp(); break;
            case Keys.kbDown: ScrollLineDown(); break;
            case Keys.kbPgUp: ScrollPageUp(); break;
            case Keys.kbPgDn: ScrollPageDown(); break;
            case Keys.kbHome: ScrollToTop(); break;
            case Keys.kbEnd: ScrollToBottom(); break;
            case Keys.kbCtrlC: HandleControlC(); break;
            case Keys.kbEsc when _hasSelection: ClearSelection(); break;
            default: return;
        }

        ClearEvent(ref ev);
    }

    private void HandleCommandKey(ref TEvent ev)
    {
        string text = KeyText.PrintableText(ev.keyDown, extendedLegacy: false);
        if (text.Length > 0)
        {
            foreach (char c in text) InputInsertChar(c);
            ClearEvent(ref ev);
            return;
        }

        switch (ev.keyDown.keyCode)
        {
            case Keys.kbEnter:
            case Keys.kbCtrlM: SubmitInput(); break;
            case Keys.kbBack: InputBackspace(); break;
            case Keys.kbDel: InputDelete(); break;
            case Keys.kbLeft:
                if (_inputCursor > 0) { _inputCursor--; DrawView(); }
                break;
            case Keys.kbRight:
                if (_inputCursor < _inputBuffer.Length) { _inputCursor++; DrawView(); }
                break;
            case Keys.kbHome:
            case Keys.kbCtrlA:
                _inputCursor = 0;
                DrawView();
                break;
            case Keys.kbEnd:
            case Keys.kbCtrlE:
                _inputCursor = _inputBuffer.Length;
                DrawView();
                break;
            case Keys.kbUp: HistoryUp(); break;
            case Keys.kbDown: HistoryDown(); break;
            case Keys.kbPgUp: ScrollPageUp(); break;
            case Keys.kbPgDn: ScrollPageDown(); break;
            case Keys.kbCtrlC: HandleControlC(); break;
            case Keys.kbCtrlV: PasteClipboard(); break;
            case Keys.kbEsc when _hasSelection: ClearSelection(); break;
            default: return;
        }

        ClearEvent(ref ev);
    }

    private void HandleMouseDown(ref TEvent ev)
    {
        if ((ev.mouse.buttons & Events.mbLeftButton) == 0) return;
        TPoint local = MakeLocal(ev.mouse.where);
        int rows = OutputHeight();
        if (local.x < 0 || local.x >= size.x || local.y < 0 || local.y >= rows) return;

        TerminalTextPosition anchor = PositionAt(local.x, local.y);
        _hasSelection = false;
        DrawView();

        while (MouseEvent(ref ev, Events.evMouseMove | Events.evMouseAuto))
        {
            local = MakeLocal(ev.mouse.where);
            if (local.y < 0) ScrollLineUp();
            else if (local.y >= rows) ScrollLineDown();
            SelectTo(anchor, PositionAt(Math.Clamp(local.x, 0, size.x - 1), Math.Clamp(local.y, 0, rows - 1)));
        }

        local = MakeLocal(ev.mouse.where);
        TerminalTextPosition end = PositionAt(Math.Clamp(local.x, 0, size.x - 1), Math.Clamp(local.y, 0, rows - 1));
        if (end == anchor) ClearSelection();   // a click without a drag selects nothing
        else SelectTo(anchor, end);
        ClearEvent(ref ev);
    }

    // The cells under both ends are included.
    private void SelectTo(TerminalTextPosition anchor, TerminalTextPosition current)
    {
        if (current.IsBefore(anchor)) SetSelection(current, new TerminalTextPosition(anchor.LineIndex, anchor.Column + 1));
        else SetSelection(anchor, new TerminalTextPosition(current.LineIndex, current.Column + 1));
    }

    private TerminalTextPosition PositionAt(int x, int y)
    {
        lock (_sync)
        {
            List<TerminalLine> lines = TerminalText.Lines(_emulator);
            int row = FirstVisibleRow(_scrollOffset) + y;
            int line = TerminalText.LineOfRow(lines, Math.Clamp(row, 0, Math.Max(0, _emulator.BufferRowCount - 1)), _emulator,
                out int columnOffset);
            return new TerminalTextPosition(Math.Max(0, line), columnOffset + x);
        }
    }
}
