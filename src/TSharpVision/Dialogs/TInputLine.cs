using TSharpVision.Constants;
using TSharpVision.Text;
namespace TSharpVision;

/// <summary>
/// Single-line UTF-16 text input with selection, horizontal scrolling, and optional validation.
/// Ctrl+C / Ctrl+Ins copy the selection, Ctrl+X / Shift+Del cut it and Ctrl+V / Shift+Ins paste, all through
/// <see cref="ClipboardService.Current"/>.
/// </summary>
public class TInputLine : TView
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TInputLine";

    /// <summary>Current input text; direct assignment does not update selection or redraw.</summary>
    public string Data;
    /// <summary>Maximum stored text length in UTF-16 code units.</summary>
    public int MaxLen;
    /// <summary>Zero-based insertion position within Data, measured in UTF-16 code units.</summary>
    public int CurPos;
    /// <summary>
    /// Zero-based UTF-16 offset of the text displayed at the left edge of the input area; the line keeps it at the start
    /// of a user-perceived character.
    /// </summary>
    public int FirstPos;
    /// <summary>Inclusive UTF-16 offset of the selected range.</summary>
    public int SelStart;
    /// <summary>Exclusive UTF-16 offset of the selected range.</summary>
    public int SelEnd;

    /// Optional validator. When non-null, Valid() consults it.
    public TValidator? Validator;

    private char? _passwordChar;

    /// <summary>
    /// When set, the line draws this character in place of every user-perceived character (grapheme
    /// cluster) of <see cref="Data"/>, so that a password or other secret is not shown on screen: an emoji,
    /// an accented letter written with a combining mark or a CJK character each shows as one mask character,
    /// so the mask reveals neither how the text is encoded nor how wide it is. Under the
    /// <see cref="TextLayout.LegacyCellWidthSwitch"/> switch it is one mask character per UTF-16 code unit,
    /// as in TSharpVision 1.2. <see langword="null"/>, the default, draws the text itself and is the
    /// behaviour every existing input line has always had.
    ///
    /// <para>
    /// <b>This affects drawing and nothing else.</b> <see cref="Data"/> still holds, and still
    /// returns, exactly what was typed; <see cref="GetData"/>, <see cref="SetData"/>,
    /// <see cref="Valid"/> and any <see cref="Validator"/> all see the real value. Editing and
    /// selection are unchanged too; the cursor, the scroll position, the overflow arrows and mouse
    /// positions are measured in mask characters, from the same cluster boundaries the editing keys use.
    /// </para>
    ///
    /// <para>
    /// Masking is not encryption and this property does not pretend otherwise: the value is an
    /// ordinary managed string in memory, and an application that needs more than "do not put it
    /// on the screen" has to arrange that for itself.
    /// </para>
    ///
    /// Setting it redraws the line if it is currently on screen, so a caller may turn masking on
    /// or off at any time — a "show password" toggle is the obvious use — without touching the
    /// value or the caret.
    /// </summary>
    public char? PasswordChar
    {
        get => _passwordChar;

        set
        {
            if (_passwordChar == value) return;

            _passwordChar = value;
            DrawView();
        }
    }

    /// <summary>Creates empty input at owner-relative cell bounds; the maximum stored length is aMaxLen minus one.</summary>
    public TInputLine(TRect bounds, int aMaxLen)
        : base(bounds)
    {
        Data = string.Empty;
        MaxLen = aMaxLen - 1;
        CurPos = 0;
        FirstPos = 0;
        SelStart = 0;
        SelEnd = 0;
        state |= Views.sfCursorVis;
        options |= (ushort)(Views.ofSelectable | Views.ofFirstClick);
    }

    /// <summary>Tests whether text can scroll left for a negative delta or right for a positive delta; zero returns false.</summary>
    /// <remarks>The text right of <see cref="FirstPos"/> is measured in visual columns (a wide character takes two).</remarks>
    public virtual bool CanScroll(int delta)
    {
        if (delta < 0) return FirstPos > 0;
        if (delta > 0)
            return LegacyLayout
                ? Data.Length - FirstPos + 2 > size.x
                : Columns(FirstPos, Data.Length) + 2 > size.x;
        return false;
    }

    /// <inheritdoc />
    public override bool Valid(ushort command)
    {
        if (command == Constants.Views.cmCancel) return true;
        if (Validator != null) return Validator.Validate(Data);
        return true;
    }

    /// <inheritdoc />
    public override ushort DataSize() => (ushort)(MaxLen + 1);

    /// <inheritdoc />
    public override void Draw()
    {
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
        ushort color = (state & Views.sfFocused) != 0 ? GetColor(2) : GetColor(1);
        b.moveChar(0, ' ', color, size.x);
        if (LegacyLayout)
        {
            // TSharpVision 1.2: one cell per UTF-16 code unit, masked or not.
            int avail = Math.Min(size.x - 2, Math.Max(0, Data.Length - FirstPos));
            if (avail > 0)
            {
                if (_passwordChar is char mask) b.moveChar(1, mask, color, avail);
                else b.moveStr(1, Data, color, avail, FirstPos);
            }
        }
        else if (size.x > 2 && (uint)FirstPos < (uint)Data.Length)
        {
            // Clusters from FirstPos, a wide one that does not fit drawn as blanks; masked, one mask per cluster.
            if (_passwordChar is char mask)
                CellText.Fill(b.Data, 1, mask, color,
                    (int)Math.Min(size.x - 2, (long)ClusterCount(Data.AsSpan(FirstPos)) * MaskWidth(mask)));
            else b.moveStr(1, Data, color, size.x - 2, FirstPos);
        }
        if (CanScroll(1)) b.moveChar((ushort)(size.x - 1), '>', GetColor(4), 1);
        if (CanScroll(-1)) b.moveChar(0, '<', GetColor(4), 1);
        if ((state & Views.sfSelected) != 0)
        {
            int l, r;
            if (LegacyLayout)
            {
                l = SelStart - FirstPos;
                r = SelEnd - FirstPos;
            }
            else
            {
                int selStart = Math.Clamp(SelStart, 0, Data.Length), selEnd = Math.Clamp(SelEnd, 0, Data.Length);
                l = selStart <= FirstPos ? 0 : Columns(FirstPos, selStart);
                r = selEnd <= FirstPos ? 0 : Columns(FirstPos, selEnd);
            }
            l = Math.Max(0, l);
            r = Math.Min(size.x - 2, r);
            if (l < r) b.moveChar((ushort)(l + 1), '\0', GetColor(3), r - l);
        }
        WriteLine(0, 0, size.x, size.y, b);
        SetCursor(CursorColumn() + 1, 0);
    }

    // ── user-perceived characters and visual columns (TV13o) ────────────────
    //
    // Editing keys step over extended grapheme clusters (TextLayout's segmentation, as TEditor), so arrows, Backspace,
    // Delete and overwrite never split a surrogate pair, a combining sequence, an emoji sequence or a flag. Positions
    // (CurPos, FirstPos, SelStart, SelEnd, MaxLen) stay UTF-16 offsets and counts. The caret, the scroll position,
    // the overflow arrows, the selection and the mouse use visual columns: the text is drawn from FirstPos by the
    // cluster-aware moveStr, so a CJK character or an emoji takes two columns. Under the legacy cell-width switch
    // every code unit is one column, exactly as in TSharpVision 1.2; cluster stepping applies in both.

    private static bool LegacyLayout => CellText.Legacy;

    // Start of the cluster that ends at or contains pos - 1 (0 at the start).
    private int PrevBoundary(int pos)
    {
        pos = Math.Clamp(pos, 0, Data.Length);
        int last = 0;
        for (int i = 0; i < pos; i += TextLayout.NextClusterLength(Data, i)) last = i;
        return last;
    }

    // The first cluster boundary after pos (the text length at its end).
    private int NextBoundary(int pos)
    {
        int i = 0;
        while (i < Data.Length && i <= pos) i += TextLayout.NextClusterLength(Data, i);
        return Math.Min(i, Data.Length);
    }

    // The last cluster boundary at or before pos.
    private int FloorBoundary(int pos)
    {
        pos = Math.Clamp(pos, 0, Data.Length);
        int i = 0;
        while (i < pos)
        {
            int next = i + TextLayout.NextClusterLength(Data, i);
            if (next > pos) break;
            i = next;
        }
        return i;
    }

    private static int ClusterCount(ReadOnlySpan<char> text)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i += TextLayout.NextClusterLength(text, i)) count++;
        return count;
    }

    private static int MaskWidth(char mask) => LegacyLayout ? 1 : Math.Max(1, CellText.Measure([mask]));

    // Columns of Data[from, to) as drawn from `from` (tabs count from there, as moveStr lays them out).
    private int Columns(int from, int to)
    {
        from = Math.Clamp(from, 0, Data.Length);
        to = Math.Clamp(to, from, Data.Length);
        ReadOnlySpan<char> text = Data.AsSpan(from, to - from);
        if (LegacyLayout) return text.Length;
        if (_passwordChar is char mask) return ClusterCount(text) * MaskWidth(mask);
        return CellText.Measure(text);
    }

    private int CursorColumn() =>
        LegacyLayout ? CurPos - FirstPos : CurPos < FirstPos ? CurPos - FirstPos : Columns(FirstPos, CurPos);

    // Moves FirstPos right, cluster by cluster, until the caret fits into the size.x - 2 text columns.
    private void ScrollToCursor()
    {
        if (FirstPos > CurPos) FirstPos = CurPos;
        if (LegacyLayout)
        {
            int i = CurPos - size.x + 2;
            if (FirstPos < i) FirstPos = i;
            return;
        }
        int avail = Math.Max(0, size.x - 2);
        bool additive = _passwordChar != null || Data.IndexOf('\t') < 0;
        int width = Columns(FirstPos, CurPos);
        while (width > avail && FirstPos < CurPos)
        {
            int next = NextBoundary(FirstPos);
            width = additive ? width - Columns(FirstPos, next) : Columns(next, CurPos);
            FirstPos = next;
        }
    }

    /// <inheritdoc />
    public override void GetData(ref object rec)
    {
        rec = Data;
    }

    private static readonly TPalette _palette = new TPalette("\x13\x13\x14\x15", 4);
    /// <inheritdoc />
    public override TPalette GetPalette() => _palette;

    /// <summary>Returns minus one or one at the input's left or right edge, otherwise zero, for drag scrolling.</summary>
    protected int MouseDelta(TEvent ev)
    {
        TPoint mouse = MakeLocal(ev.mouse.where);
        if (mouse.x <= 0) return -1;
        if (mouse.x >= size.x - 1) return 1;
        return 0;
    }

    /// <summary>
    /// Maps a screen mouse position to a clamped UTF-16 insertion offset in the input text: the start of the
    /// user-perceived character under the mouse (the second cell of a wide character gives its start).
    /// </summary>
    protected int MousePos(TEvent ev)
    {
        TPoint mouse = MakeLocal(ev.mouse.where);
        int mx = Math.Max(mouse.x, 1);
        if (LegacyLayout)
        {
            int pos = mx + FirstPos - 1;
            pos = Math.Max(pos, 0);
            pos = Math.Min(pos, Data.Length);
            return FloorBoundary(pos);
        }
        int first = Math.Clamp(FirstPos, 0, Data.Length);
        int column = mx - 1;
        if (_passwordChar is char mask)
        {
            int clusters = column / MaskWidth(mask);
            int at = first;
            while (clusters-- > 0 && at < Data.Length) at += TextLayout.NextClusterLength(Data, at);
            return at;
        }
        return first + TextLayout.OffsetAtColumn(Data.AsSpan(first), column, CellText.Options, ColumnBias.Floor, out _);
    }

    /// <summary>Deletes the half-open selected range and moves the insertion position to its start.</summary>
    protected void DeleteSelect()
    {
        if (SelStart < SelEnd)
        {
            Data = string.Concat(Data.AsSpan(0, SelStart), Data.AsSpan(SelEnd));
            CurPos = SelStart;
        }
    }

    /// <summary>
    /// Inserts or overwrites one UTF-16 code unit according to cursor mode; overwriting replaces the whole
    /// user-perceived character at the cursor. Returns false when capacity prevents insertion.
    /// </summary>
    public virtual bool InsertChar(char value)
    {
        if ((state & Views.sfCursorIns) == 0)
            DeleteSelect();
        int l = Data.Length;
        if ((state & Views.sfCursorIns) == 0)
        {
            if (l < MaxLen)
            {
                Data = string.Concat(Data.AsSpan(0, CurPos), stackalloc char[1] { value }, Data.AsSpan(CurPos));
                if (FirstPos > CurPos) FirstPos = CurPos;
                CurPos++;
            }
        }
        else
        {
            if (CurPos < MaxLen)
            {
                if (CurPos < Data.Length)
                    Data = string.Concat(Data.AsSpan(0, CurPos), stackalloc char[1] { value }, Data.AsSpan(NextBoundary(CurPos)));
                else
                    Data = string.Concat(Data.AsSpan(), stackalloc char[1] { value });
                if (FirstPos > CurPos) FirstPos = CurPos;
                CurPos++;
            }
        }
        return true;
    }

    /// <summary>Scrolls the text to expose the insertion position and redraws the control.</summary>
    protected void MakeVisible()
    {
        ScrollToCursor();
        DrawView();
    }

    // Typed text, one user-perceived character at a time: its first unit goes through InsertChar (which replaces
    // the selection, or overwrites the character at the cursor), the rest are inserted after it, so overwriting never
    // splits a character and typing a surrogate pair overwrites one character, not two. A character that does not fit
    // whole is not inserted, nor is anything after it.
    private bool InsertTypedText(string text)
    {
        bool inserted = false;
        for (int i = 0; i < text.Length;)
        {
            int length = TextLayout.NextClusterLength(text, i);
            if (length > 1)
            {
                bool overwriting = (state & Views.sfCursorIns) != 0;
                int replaced = overwriting
                    ? (CurPos < Data.Length ? NextBoundary(CurPos) - CurPos : 0)
                    : (NormalizeSelection() ? SelEnd - SelStart : 0);
                if (Data.Length - replaced + length > MaxLen || (overwriting && CurPos >= MaxLen)) break;
            }
            inserted |= InsertChar(text[i]);
            // The first unit replaced the selection. Left set, the range would be deleted again for every further
            // unit of the same text (a surrogate pair is two).
            SelStart = 0; SelEnd = 0;
            if (length > 1)
            {
                ushort overwrite = (ushort)(state & Views.sfCursorIns);
                state = (ushort)(state & ~Views.sfCursorIns);
                try
                {
                    for (int k = 1; k < length; k++) inserted |= InsertChar(text[i + k]);
                }
                finally { state |= overwrite; }
            }
            i += length;
        }
        return inserted;
    }

    private void AdjustSelectBlock(int anchor)
    {
        if (CurPos < anchor) { SelStart = CurPos; SelEnd = anchor; }
        else { SelStart = anchor; SelEnd = CurPos; }
    }

    private static ushort CtrlToArrow(ushort code) => code;

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if ((state & Views.sfSelected) == 0) return;

        int anchor = 0;
        switch (@event.What)
        {
            case Events.evMouseDown:
            {
                int delta = MouseDelta(@event);
                if (CanScroll(delta))
                {
                    do
                    {
                        if (CanScroll(delta))
                        {
                            FirstPos += delta;
                            DrawView();
                        }
                    } while (MouseEvent(ref @event, Events.evMouseAuto));
                }
                else if (@event.mouse.doubleClick)
                {
                    SelectAll(true);
                }
                else
                {
                    anchor = MousePos(@event);
                    do
                    {
                        if (@event.What == Events.evMouseAuto
                            && CanScroll(delta = MouseDelta(@event)))
                            FirstPos += delta;
                        CurPos = MousePos(@event);
                        AdjustSelectBlock(anchor);
                        DrawView();
                    } while (MouseEvent(ref @event,
                        (ushort)(Events.evMouseMove | Events.evMouseAuto)));
                }
                ClearEvent(ref @event);
                break;
            }
            case Events.evKeyDown:
            {
                ushort key = CtrlToArrow(@event.keyDown.keyCode);
                // Shift-extension of selection (kbShiftCode) deferred:
                // Keys.cs does not currently expose a shift bitmask for keycodes.
                bool extendBlock = false;

                // Printable characters are inserted immediately, before the keyCode
                // switch. This avoids false matches between an uppercase letter's
                // ASCII value and a navigation-key constant (e.g. 'H' == kbLeft,
                // 'E' == kbHome, 'N' == kbDel).
                string text = KeyText.PrintableText(@event.keyDown);
                if (text.Length > 0)
                {
                    if (InsertTypedText(text)) MakeVisible();
                    ClearEvent(ref @event);
                    break;
                }

                byte charCode = @event.keyDown.charScan.charCode;
                if (charCode >= 32)
                {
                    if (InsertChar((char)charCode))
                    {
                        SelStart = 0; SelEnd = 0;
                        MakeVisible();
                        ClearEvent(ref @event);
                    }
                    else
                    {
                        ClearEvent(ref @event);
                    }
                    break;
                }

                bool handled = true;
                switch (key)
                {
                    case Keys.kbLeft:
                        if (CurPos > 0) CurPos = PrevBoundary(CurPos);
                        break;
                    case Keys.kbRight:
                        if (CurPos < Data.Length) CurPos = NextBoundary(CurPos);
                        break;
                    case Keys.kbHome:
                        CurPos = 0;
                        break;
                    case Keys.kbEnd:
                        CurPos = Data.Length;
                        break;
                    case Keys.kbBack:
                        if (CurPos > 0)
                        {
                            // One user-perceived character; the view shows one more at the left, as before.
                            int end = Math.Min(CurPos, Data.Length);
                            int start = PrevBoundary(end);
                            Data = string.Concat(Data.AsSpan(0, start), Data.AsSpan(end));
                            if (FirstPos > 0) FirstPos = FirstPos >= end ? start : PrevBoundary(FirstPos);
                            CurPos = start;
                        }
                        break;
                    case Keys.kbDel:
                        if (SelStart == SelEnd)
                        {
                            if (CurPos < Data.Length)
                            {
                                SelStart = CurPos;
                                SelEnd = NextBoundary(CurPos);
                            }
                        }
                        DeleteSelect();
                        break;
                    case Keys.kbIns:
                        SetState(Views.sfCursorIns,
                            (state & Views.sfCursorIns) == 0);
                        break;
                    case Keys.kbEnter:
                    case Keys.kbTab:
                    case Keys.kbShiftTab:
                        return;
                    // Clipboard keys, the same six TEditor maps. Copy leaves the selection and the cursor alone,
                    // so these return here instead of running the selection reset below.
                    case Keys.kbCtrlC:
                    case Keys.kbCtrlIns:
                        CopySelection();
                        ClearEvent(ref @event);
                        return;
                    case Keys.kbCtrlX:
                    case Keys.kbShiftDel:
                        // Only what reached the clipboard is deleted.
                        if (CopySelection())
                        {
                            DeleteSelect();
                            SelStart = 0; SelEnd = 0;
                            MakeVisible();
                        }
                        ClearEvent(ref @event);
                        return;
                    case Keys.kbCtrlV:
                    case Keys.kbShiftIns:
                        if (PasteClipboard()) MakeVisible();
                        ClearEvent(ref @event);
                        return;
                    default:
                        handled = false;
                        break;
                }

                if (!handled) return;
                if (extendBlock) AdjustSelectBlock(anchor);
                else { SelStart = 0; SelEnd = 0; }
                MakeVisible();
                ClearEvent(ref @event);
                break;
            }
        }
    }

    // Clamps the selection to the text (Data is a public field and may have been replaced under it) and
    // returns whether anything is selected.
    private bool NormalizeSelection()
    {
        SelStart = Math.Clamp(SelStart, 0, Data.Length);
        SelEnd = Math.Clamp(SelEnd, 0, Data.Length);
        return SelStart < SelEnd;
    }

    // Puts the selected text on the clipboard and returns whether it got there. A masked line copies nothing:
    // the clipboard would show what the screen hides.
    private bool CopySelection()
    {
        if (_passwordChar != null || !NormalizeSelection()) return false;

        try
        {
            IClipboardService clipboard = ClipboardService.Current;
            return clipboard.IsAvailable && clipboard.SetText(Data.Substring(SelStart, SelEnd - SelStart));
        }
        catch (Exception)
        {
            // A clipboard service is application code; its failure must not reach the event loop.
            return false;
        }
    }

    // Inserts the clipboard text at the cursor, replacing the selection, and returns whether the line changed.
    // The text goes through InsertChar unit by unit like typed text, so it meets the same capacity; what does
    // not fit is dropped, never half of a surrogate pair. Paste inserts in overwrite mode too, as TEditor does.
    private bool PasteClipboard()
    {
        string text;
        try
        {
            IClipboardService clipboard = ClipboardService.Current;
            if (!clipboard.IsAvailable || !clipboard.TryGetText(out string? read) || read is null) return false;
            text = SingleLine(read);
        }
        catch (Exception)
        {
            return false;
        }

        bool selected = NormalizeSelection();
        int room = Math.Max(0, MaxLen - (Data.Length - (selected ? SelEnd - SelStart : 0)));
        if (text.Length > room)
        {
            // Cut at the last whole user-perceived character that fits.
            int fit = 0;
            for (int i = 0; i < text.Length;)
            {
                int next = i + TextLayout.NextClusterLength(text, i);
                if (next > room) break;
                fit = i = next;
            }
            text = text.Substring(0, fit);
        }
        if (text.Length == 0) return false;

        if (selected) DeleteSelect();
        else CurPos = Math.Clamp(CurPos, 0, Data.Length);
        SelStart = 0; SelEnd = 0;

        ushort overwrite = (ushort)(state & Views.sfCursorIns);
        state = (ushort)(state & ~Views.sfCursorIns);
        try
        {
            foreach (char ch in text)
                InsertChar(ch);
        }
        finally
        {
            state |= overwrite;
        }
        return true;
    }

    // Clipboard text as one line: a run of line breaks between two pieces of text is one space, a run at either
    // end is dropped, a tab is a space, and any other control character or unpaired surrogate is dropped.
    private static string SingleLine(string text)
    {
        var line = new System.Text.StringBuilder(text.Length);
        bool pendingBreak = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029')
            {
                pendingBreak = line.Length > 0;
                continue;
            }

            bool pair = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
            if (c != '\t' && !pair && (char.IsControl(c) || char.IsSurrogate(c))) continue;

            if (pendingBreak) line.Append(' ');
            pendingBreak = false;
            if (c == '\t') line.Append(' ');
            else if (pair) line.Append(c).Append(text[++i]);
            else line.Append(c);
        }

        return line.ToString();
    }

    /// <summary>Selects all text or clears selection, updating the insertion position and display.</summary>
    public virtual void SelectAll(bool enable)
    {
        SelStart = 0;
        if (enable)
        {
            CurPos = Data.Length;
            SelEnd = Data.Length;
        }
        else
        {
            CurPos = 0;
            SelEnd = 0;
        }
        FirstPos = LegacyLayout ? Math.Max(0, CurPos - size.x + 2) : 0;
        ScrollToCursor();
        DrawView();

    }

    /// <inheritdoc />
    public override void SetData(object rec)
    {
        string s = rec switch
        {
            string str => str,
            char[] ca => new string(ca),
            null => string.Empty,
            _ => rec.ToString() ?? string.Empty,
        };
        if (s.Length > MaxLen) s = s.Substring(0, MaxLen);
        Data = s;
        SelectAll(true);
    }

    /// <inheritdoc />
    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if (aState == Views.sfSelected
            || (aState == Views.sfActive && (state & Views.sfSelected) != 0))
            SelectAll(enable);
    }

    // ── Streaming ────────────────────────────────────────────────────────
    // Wire: TView base + WriteInt×5 (maxLen/curPos/firstPos/selStart/selEnd)
    //       + WriteString(data) + WritePointer(null validator).
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTInputLine =
        new TStreamableClass("TInputLine", () => new TInputLine(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TInputLine(StreamableInit init) : base(init) { Data = string.Empty; }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteInt((uint)MaxLen);
        os.WriteInt((uint)CurPos);
        os.WriteInt((uint)FirstPos);
        os.WriteInt((uint)SelStart);
        os.WriteInt((uint)SelEnd);
        os.WriteString(Data);
        os.WritePointer(Validator);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        MaxLen    = (int)isStream.ReadInt();
        CurPos    = (int)isStream.ReadInt();
        FirstPos  = (int)isStream.ReadInt();
        SelStart  = (int)isStream.ReadInt();
        SelEnd    = (int)isStream.ReadInt();
        Data      = isStream.ReadString() ?? string.Empty;
        Validator = isStream.ReadPointer() as TValidator;
        state |= Views.sfCursorVis;
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() => new TInputLine(StreamableInit.streamableInit);
}
