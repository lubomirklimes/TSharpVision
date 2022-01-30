using SharpVision.Constants;
namespace SharpVision;

public class TInputLine : TView
{
    public new static readonly string Name = "TInputLine";

    public string Data;
    public int MaxLen;
    public int CurPos;
    public int FirstPos;
    public int SelStart;
    public int SelEnd;

    /// Optional validator. When non-null, Valid() consults it.
    public TValidator Validator;

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

    public virtual bool CanScroll(int delta)
    {
        if (delta < 0) return FirstPos > 0;
        if (delta > 0) return Data.Length - FirstPos + 2 > size.x;
        return false;
    }

    public override bool Valid(ushort command)
    {
        if (command == Constants.Views.cmCancel) return true;
        if (Validator != null) return Validator.Validate(Data);
        return true;
    }

    public virtual ushort DataSize() => (ushort)(MaxLen + 1);

    public override void Draw()
    {
        var b = new TDrawBuffer();
        ushort color = (state & Views.sfFocused) != 0 ? GetColor(2) : GetColor(1);
        b.moveChar(0, ' ', color, size.x);
        int avail = Math.Min(size.x - 2, Math.Max(0, Data.Length - FirstPos));
        if (avail > 0)
        {
            string buf = Data.Substring(FirstPos, avail);
            b.moveStr(1, buf, color);
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

    public virtual void GetData(ref object rec)
    {
        rec = Data;
    }

    private static readonly TPalette _palette = new TPalette("\x13\x13\x14\x15", 4);
    public override TPalette GetPalette() => _palette;

    protected int MouseDelta(TEvent ev)
    {
        TPoint mouse = MakeLocal(ev.mouse.where);
        if (mouse.x <= 0) return -1;
        if (mouse.x >= size.x - 1) return 1;
        return 0;
    }

    protected int MousePos(TEvent ev)
    {
        TPoint mouse = MakeLocal(ev.mouse.where);
        int mx = Math.Max(mouse.x, 1);
        int pos = mx + FirstPos - 1;
        pos = Math.Max(pos, 0);
        pos = Math.Min(pos, Data.Length);
        return pos;
    }

    protected void DeleteSelect()
    {
        if (SelStart < SelEnd)
        {
            Data = Data.Substring(0, SelStart) + Data.Substring(SelEnd);
            CurPos = SelStart;
        }
    }

    public virtual bool InsertChar(char value)
    {
        if ((state & Views.sfCursorIns) == 0)
            DeleteSelect();
        int l = Data.Length;
        if ((state & Views.sfCursorIns) == 0)
        {
            if (l < MaxLen)
            {
                Data = Data.Substring(0, CurPos) + value + Data.Substring(CurPos);
                if (FirstPos > CurPos) FirstPos = CurPos;
                CurPos++;
            }
        }
        else
        {
            if (CurPos < MaxLen)
            {
                if (CurPos < Data.Length)
                    Data = Data.Substring(0, CurPos) + value + Data.Substring(CurPos + 1);
                else
                    Data = Data + value;
                if (FirstPos > CurPos) FirstPos = CurPos;
                CurPos++;
            }
        }
        return true;
    }

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
                            Data = Data.Substring(0, CurPos - 1) + Data.Substring(CurPos);
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
                        if (@event.keyDown.charScan.charCode >= ' ')
                        {
                            if (!InsertChar((char)@event.keyDown.charScan.charCode))
                            {
                                ClearEvent(ref @event);
                                break;
                            }
                        }
                        else
                        {
                            handled = false;
                        }
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

    public virtual void SetData(object rec)
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
    public static readonly TStreamableClass StreamableClassTInputLine =
        new TStreamableClass("TInputLine", () => new TInputLine(StreamableInit.streamableInit), 0);

    protected TInputLine(StreamableInit init) : base(init) { }

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

    public new static TStreamable Build() => new TInputLine(StreamableInit.streamableInit);
}
