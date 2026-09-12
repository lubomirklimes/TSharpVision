using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>Single-line UTF-16 text input with selection, horizontal scrolling, and optional validation.</summary>
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
    /// <summary>Zero-based text offset displayed at the left edge of the input area.</summary>
    public int FirstPos;
    /// <summary>Inclusive UTF-16 offset of the selected range.</summary>
    public int SelStart;
    /// <summary>Exclusive UTF-16 offset of the selected range.</summary>
    public int SelEnd;

    /// Optional validator. When non-null, Valid() consults it.
    public TValidator Validator;

    private char? _passwordChar;

    /// <summary>
    /// When set, the line draws this character in place of every character of
    /// <see cref="Data"/>, so that a password or other secret is not shown on screen.
    /// <see langword="null"/>, the default, draws the text itself and is the behaviour every
    /// existing input line has always had.
    ///
    /// <para>
    /// <b>This affects drawing and nothing else.</b> <see cref="Data"/> still holds, and still
    /// returns, exactly what was typed; <see cref="GetData"/>, <see cref="SetData"/>,
    /// <see cref="Valid"/> and any <see cref="Validator"/> all see the real value. Editing,
    /// selection, the cursor, the scroll position and the overflow arrows are unchanged too,
    /// because every one of them is computed from the text's <em>length</em> rather than its
    /// content — which is what makes masking by substitution correct rather than a second
    /// drawing path that could drift out of step with the first.
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
    public virtual bool CanScroll(int delta)
    {
        if (delta < 0) return FirstPos > 0;
        if (delta > 0) return Data.Length - FirstPos + 2 > size.x;
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
        int avail = Math.Min(size.x - 2, Math.Max(0, Data.Length - FirstPos));
        if (avail > 0)
        //b.moveStr(1, Data, color, avail, FirstPos);
        {
            // Same run, same length, same position — only the glyphs differ. Everything below
            // this point, and every measurement above it, is untouched by masking.
            if (_passwordChar is char mask) b.moveChar(1, mask, color, avail);
            else b.moveStr(1, Data, color, avail, FirstPos);
        }
        if (CanScroll(1)) b.moveChar((ushort)(size.x - 1), '>', GetColor(4), 1);
        if (CanScroll(-1)) b.moveChar(0, '<', GetColor(4), 1);
        if ((state & Views.sfSelected) != 0)
        {
            int l = SelStart - FirstPos;
            int r = SelEnd - FirstPos;
            l = Math.Max(0, l);
            r = Math.Min(size.x - 2, r);
            if (l < r) b.moveChar((ushort)(l + 1), '\0', GetColor(3), r - l);
        }
        WriteLine(0, 0, size.x, size.y, b);
        SetCursor(CurPos - FirstPos + 1, 0);
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

    /// <summary>Maps a screen mouse position to a clamped UTF-16 insertion offset in the input text.</summary>
    protected int MousePos(TEvent ev)
    {
        TPoint mouse = MakeLocal(ev.mouse.where);
        int mx = Math.Max(mouse.x, 1);
        int pos = mx + FirstPos - 1;
        pos = Math.Max(pos, 0);
        pos = Math.Min(pos, Data.Length);
        return pos;
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

    /// <summary>Inserts or overwrites one UTF-16 code unit according to cursor mode; returns false when capacity prevents insertion.</summary>
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
                    Data = string.Concat(Data.AsSpan(0, CurPos), stackalloc char[1] { value }, Data.AsSpan(CurPos + 1));
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
        if (FirstPos > CurPos) FirstPos = CurPos;
        int i = CurPos - size.x + 2;
        if (FirstPos < i) FirstPos = i;
        DrawView();
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
                    bool inserted = false;
                    foreach (char ch in text)
                        inserted |= InsertChar(ch);
                    SelStart = 0; SelEnd = 0;
                    if (inserted) MakeVisible();
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
                        if (CurPos > 0) CurPos--;
                        break;
                    case Keys.kbRight:
                        if (CurPos < Data.Length) CurPos++;
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
                            Data = string.Concat(Data.AsSpan(0, CurPos - 1), Data.AsSpan(CurPos));
                            CurPos--;
                            if (FirstPos > 0) FirstPos--;
                        }
                        break;
                    case Keys.kbDel:
                        if (SelStart == SelEnd)
                        {
                            if (CurPos < Data.Length)
                            {
                                SelStart = CurPos;
                                SelEnd = CurPos + 1;
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
        FirstPos = Math.Max(0, CurPos - size.x + 2);
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
    protected TInputLine(StreamableInit init) : base(init) { }

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
