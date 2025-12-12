using TSharpVision.Constants;
namespace TSharpVision;

public class TScrollBar : TView
{
    public new static readonly string Name = "TScrollBar";

    // Component palette indices used by Draw() / DrawPos().
    private const int csbUp = 0, csbDown = 1, csbDark = 2, csbMark = 3, csbBright = 4;

    private static readonly TPalette _palette = new TPalette("\x04\x05\x05", 3);

    // Default glyph tables. Upstream uses cp437 codepage; we substitute the
    // matching Unicode box-drawing glyphs. See TSharpVisionGlyphs for the canonical names.
    // Source: tvision/classes/tvtext1.cc (vChars/hChars defaults)
    // Array layout: [0]=up/left endpoint, [1]=down/right endpoint,
    //               [2]=empty track,      [3]=thumb mark, [4]=full/bright fill.
    protected static readonly char[] vChars = new char[5]
    {
        TSharpVisionGlyphs.ScrollArrowUp,   // ▲  CP437 0x1E — top endpoint
        TSharpVisionGlyphs.ScrollArrowDown, // ▼  CP437 0x1F — bottom endpoint
        TSharpVisionGlyphs.ScrollBarTrack,  // ▒  CP437 0xB1 — empty track
        TSharpVisionGlyphs.ScrollBarThumb,  // ■  CP437 0xFE — thumb mark
        TSharpVisionGlyphs.ScrollBarBright, // ░  CP437 0xB0 — full/bright fill
    };
    protected static readonly char[] hChars = new char[5]
    {
        TSharpVisionGlyphs.ScrollArrowLeft,  // ◄  CP437 0x11 — left endpoint
        TSharpVisionGlyphs.ScrollArrowRight, // ►  CP437 0x10 — right endpoint
        TSharpVisionGlyphs.ScrollBarTrack,   // ▒  CP437 0xB1 — empty track
        TSharpVisionGlyphs.ScrollBarThumb,   // ■  CP437 0xFE — thumb mark
        TSharpVisionGlyphs.ScrollBarBright,  // ░  CP437 0xB0 — full/bright fill
    };

    public int value;
    public int minVal;
    public int maxVal;
    public int pgStep;
    public int arStep;
    public char[] chars = new char[5];

    public TScrollBar(TRect bounds) : base(bounds)
    {
        value = 0;
        minVal = 0;
        maxVal = 0;
        pgStep = 1;
        arStep = 1;
        if (size.x == 1)
        {
            growMode = (byte)(Views.gfGrowLoX | Views.gfGrowHiX | Views.gfGrowHiY);
            Array.Copy(vChars, chars, 5);
        }
        else
        {
            growMode = (byte)(Views.gfGrowLoY | Views.gfGrowHiX | Views.gfGrowHiY);
            Array.Copy(hChars, chars, 5);
        }
    }

    public override void Draw()
    {
        DrawPos(GetPos());
    }

    public virtual void DrawPos(int pos)
    {
        TDrawBuffer b = new TDrawBuffer();
        char[] aChars = (size.x == 1) ? vChars : hChars;

        int s = GetSize() - 1;
        b.moveChar(0, aChars[csbUp], GetColor(2), 1);
        if (maxVal == minVal)
        {
            char unFilled = aChars[csbDark];
            b.moveChar(1, unFilled, GetColor(1), s - 1);
        }
        else
        {
            char filled = aChars[csbBright];
            b.moveChar(1, filled, GetColor(1), s - 1);
            b.moveChar(pos, aChars[csbMark], GetColor(3), 1);
            if ((state & Views.sfFocused) != 0)
            {
                SetCursor(pos, 0);
                ResetCursor();
            }
        }

        b.moveChar(s, aChars[csbDown], GetColor(2), 1);
        WriteBuf(0, 0, size.x, size.y, b);
    }

    public override TPalette GetPalette() => _palette;

    public int GetPos()
    {
        int r = maxVal - minVal;
        if (r == 0)
            return 1;
        return (int)((((long)(value - minVal) * (GetSize() - 3)) + (r >> 1)) / r) + 1;
    }

    public int GetSize()
    {
        int s = (size.x == 1) ? size.y : size.x;
        return Math.Max(3, s);
    }

    private TPoint _mouse;
    private int _p, _s;
    private TRect _extent;

    private int GetPartCode()
    {
        int part = -1;
        if (_extent.Contains(_mouse))
        {
            int mark = (size.x == 1) ? _mouse.y : _mouse.x;
            if (mark == _p)
                part = Views.sbIndicator;
            else
            {
                if (mark < 1) part = Views.sbLeftArrow;
                else if (mark < _p) part = Views.sbPageLeft;
                else if (mark < _s) part = Views.sbPageRight;
                else part = Views.sbRightArrow;
                if (size.x == 1) part += 4;
            }
        }
        return part;
    }

    public override void HandleEvent(ref TEvent @event)
    {
        bool tracking;
        int i = 0, clickPart;

        base.HandleEvent(ref @event);

        switch (@event.What)
        {
            case Events.evMouseDown:
                Message(owner, Events.evBroadcast, Views.cmScrollBarClicked, this);
                _mouse = MakeLocal(@event.mouse.where);
                _extent = GetExtent();
                _extent.Grow(1, 1);
                _p = GetPos();
                _s = GetSize() - 1;
                clickPart = GetPartCode();
                if (clickPart != Views.sbIndicator)
                {
                    do
                    {
                        _mouse = MakeLocal(@event.mouse.where);
                        if (GetPartCode() == clickPart)
                            SetValue(value + ScrollStep(clickPart));
                    } while (MouseEvent(ref @event, Events.evMouseAuto));
                }
                else
                {
                    do
                    {
                        _mouse = MakeLocal(@event.mouse.where);
                        tracking = _extent.Contains(_mouse);
                        if (tracking)
                        {
                            i = (size.x == 1) ? _mouse.y : _mouse.x;
                            i = Math.Max(i, 1);
                            i = Math.Min(i, _s - 1);
                        }
                        else
                            i = GetPos();
                        if (i != _p)
                        {
                            DrawPos(i);
                            _p = i;
                        }
                    } while (MouseEvent(ref @event, Events.evMouseMove));
                    if (tracking && _s > 2)
                    {
                        int s2 = _s - 2;
                        SetValue((int)(((long)(_p - 1) * (maxVal - minVal) + (s2 >> 1)) / s2) + minVal);
                    }
                }
                ClearEvent(ref @event);
                break;

            case Events.evKeyDown:
                if ((state & Views.sfVisible) != 0)
                {
                    clickPart = Views.sbIndicator;
                    if (size.y == 1)
                    {
                        switch (CtrlToArrow(@event.keyDown.keyCode))
                        {
                            case Keys.kbLeft:      clickPart = Views.sbLeftArrow;  break;
                            case Keys.kbRight:     clickPart = Views.sbRightArrow; break;
                            case Keys.kbCtrlLeft:  clickPart = Views.sbPageLeft;   break;
                            case Keys.kbCtrlRight: clickPart = Views.sbPageRight;  break;
                            case Keys.kbHome:      i = minVal; break;
                            case Keys.kbEnd:       i = maxVal; break;
                            default: return;
                        }
                    }
                    else
                    {
                        switch (CtrlToArrow(@event.keyDown.keyCode))
                        {
                            case Keys.kbUp:       clickPart = Views.sbUpArrow;   break;
                            case Keys.kbDown:     clickPart = Views.sbDownArrow; break;
                            case Keys.kbPgUp:     clickPart = Views.sbPageUp;    break;
                            case Keys.kbPgDn:     clickPart = Views.sbPageDown;  break;
                            case Keys.kbCtrlPgUp: i = minVal; break;
                            case Keys.kbCtrlPgDn: i = maxVal; break;
                            default: return;
                        }
                    }
                    Message(owner, Events.evBroadcast, Views.cmScrollBarClicked, this);
                    if (clickPart != Views.sbIndicator)
                        i = value + ScrollStep(clickPart);
                    SetValue(i);
                    ClearEvent(ref @event);
                }
                break;
        }
    }

    public virtual void ScrollDraw()
    {
        Message(owner, Events.evBroadcast, Views.cmScrollBarChanged, this);
    }

    public virtual int ScrollStep(int part)
    {
        int step = ((part & 2) == 0) ? arStep : pgStep;
        return ((part & 1) == 0) ? -step : step;
    }

    public void SetParams(int aValue, int aMin, int aMax, int aPgStep, int aArStep)
    {
        aMax = Math.Max(aMax, aMin);
        aValue = Math.Max(aMin, aValue);
        aValue = Math.Min(aMax, aValue);
        int sValue = value;
        if (sValue != aValue || minVal != aMin || maxVal != aMax)
        {
            value = aValue;
            minVal = aMin;
            maxVal = aMax;
            DrawView();
            if (sValue != aValue)
                ScrollDraw();
        }
        pgStep = aPgStep;
        arStep = aArStep;
    }

    public void SetRange(int aMin, int aMax) => SetParams(value, aMin, aMax, pgStep, arStep);
    public void SetStep(int aPgStep, int aArStep) => SetParams(value, minVal, maxVal, aPgStep, aArStep);
    public void SetValue(int aValue) => SetParams(aValue, minVal, maxVal, pgStep, arStep);

    private static ushort CtrlToArrow(ushort code) => code;

    // Wire layout (after base): value, minVal, maxVal, pgStep, arStep (4B each),
    // then 5 bytes CP437-encoded chars[0..4].

    private static byte ToCP437(char c) => c switch
    {
        '▲' => 0x1E, '▼' => 0x1F,
        '◄' => 0x11, '►' => 0x10,
        '▒' => 0xB1, '■' => 0xFE,
        '░' => 0xB0, '▓' => 0xB2,
        _   => 0x3F,
    };

    private static char FromCP437(byte b) => b switch
    {
        0x1E => '▲', 0x1F => '▼',
        0x11 => '◄', 0x10 => '►',
        0xB1 => '▒', 0xFE => '■',
        0xB0 => '░', 0xB2 => '▓',
        _    => (char)b,
    };

    public static readonly TStreamableClass StreamableClassTScrollBar =
        new TStreamableClass("TScrollBar", () => new TScrollBar(StreamableInit.streamableInit), 0);

    protected TScrollBar(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WriteInt((uint)value);
        os.WriteInt((uint)minVal);
        os.WriteInt((uint)maxVal);
        os.WriteInt((uint)pgStep);
        os.WriteInt((uint)arStep);
        for (int i = 0; i < 5; i++) os.WriteByte(ToCP437(chars[i]));
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        value  = (int)isStream.ReadInt();
        minVal = (int)isStream.ReadInt();
        maxVal = (int)isStream.ReadInt();
        pgStep = (int)isStream.ReadInt();
        arStep = (int)isStream.ReadInt();
        for (int i = 0; i < 5; i++) chars[i] = FromCP437(isStream.ReadByte());
        return this;
    }

    public new static TStreamable Build() { return new TScrollBar(StreamableInit.streamableInit); }
    public override string StreamableName() { return Name; }
}
