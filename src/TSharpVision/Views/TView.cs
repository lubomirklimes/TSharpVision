using TSharpVision.Constants;
using TSharpVision.Drivers;

namespace TSharpVision;

public class TView : TStreamable, IInfo, IDisposable
{
    private static readonly TPalette palette = new TPalette("\0", 0);

    public TPoint size;
    public ushort options;
    public ushort eventMask;
    public ushort state;
    public TPoint origin;
    public TPoint cursor;
    public byte growMode;
    public byte dragMode;
    public ushort helpCtx;
    public static bool commandSetChanged;

    private static TCommandSet InitCommands()
    {
        TCommandSet temp = new TCommandSet();
        for (int i = 0; i < 256; i++)
            temp.EnableCmd(i);
        temp.DisableCmd(Views.cmZoom);
        temp.DisableCmd(Views.cmClose);
        temp.DisableCmd(Views.cmResize);
        temp.DisableCmd(Views.cmNext);
        temp.DisableCmd(Views.cmPrev);
        return temp;
    }

    public static TCommandSet curCommandSet = InitCommands();
    public TGroup owner;
    public static bool showMarkers;
    public static byte errorAttr;

    // From Viewes.H
    public TView Next;

    // From TVIEW.CPP
    public TPoint shadowSize = new TPoint(2, 1);

    // From TGROUP.CPP
    public TView TheTopView;

    //protected static IDriver driver;
    private bool disposedValue;

    public enum phaseType { phFocused, phPreProcess, phPostProcess };
    public enum selectMode { normalSelect, enterSelect, leaveSelect };

    public TView(TRect bounds)
    {
        Next = null;
        options = 0;
        eventMask = Events.evMouseDown | Events.evKeyDown | Events.evCommand;
        state = Views.sfVisible;
        growMode = 0;
        dragMode = Views.dmLimitLoY;
        helpCtx = Views.hcNoContext;
        owner = null;

        SetBounds(bounds);
        cursor.x = cursor.y = 0;
    }

    // The Read() method restores all fields from the stream.
    protected TView(StreamableInit init) { }
    // Overload for subclasses that declare their streaming ctor with `object` parameter.
    protected TView(object _) { }

    ~TView() { }

    public virtual void SizeLimits(ref TPoint min, ref TPoint max)
    {
        min.x = min.y = 0;
        if (owner != null)
            max = owner.size;
        else
        {
            max.x = int.MaxValue;
            max.y = int.MaxValue;
        }
    }

    public virtual TRect GetBounds()
    {
        return new TRect(origin, origin + size);
    }

    public virtual TRect GetExtent()
    {
        return new TRect(0, 0, size.x, size.y);
    }

    public virtual TRect GetClipRect()
    {
        TRect clip = GetBounds();
        if (owner != null)
            clip.Intersect(owner.clip);
        clip.Move(-origin.x, -origin.y);
        return clip;
    }

    public virtual bool MouseInView(TPoint mouse)
    {
        mouse = MakeLocal(mouse);
        TRect r = GetExtent();
        return r.Contains(mouse);
    }

    public virtual bool ContainsMouse(TEvent ev)
    {
        return (state & Views.sfVisible) != 0 && MouseInView(ev.mouse.where);
    }

    public virtual void Locate(TRect bounds)
    {
        TPoint min = default, max = default;
        SizeLimits(ref min, ref max);
        bounds.b.x = bounds.a.x + Range(bounds.b.x - bounds.a.x, min.x, max.x);
        bounds.b.y = bounds.a.y + Range(bounds.b.y - bounds.a.y, min.y, max.y);
        TRect r = GetBounds();
        if (bounds != r)
        {
            ChangeBounds(bounds);
            if (owner != null && (state & Views.sfVisible) != 0)
            {
                if ((state & Views.sfShadow) != 0)
                {
                    r.Union(bounds);
                    r.b += shadowSize;
                }
                DrawUnderRect(ref r, null);
            }
        }
    }

    public virtual void DragView(TEvent ev, byte mode, ref TRect limits, TPoint minSize, TPoint maxSize)
    {
        TPoint p, s;
        SetState(Views.sfDragging, true);

        if (ev.What == Events.evMouseDown)
        {
            if ((mode & Views.dmDragMove) != 0)
            {
                p = origin - ev.mouse.where;
                do
                {
                    ev.mouse.where += p;
                    MoveGrow(ev.mouse.where, size, limits, minSize, maxSize, mode);
                } while (MouseEvent(ref ev, Events.evMouseMove));
            }
            else
            {
                p = size - ev.mouse.where;
                do
                {
                    ev.mouse.where += p;
                    MoveGrow(origin, ev.mouse.where, limits, minSize, maxSize, mode);
                } while (MouseEvent(ref ev, Events.evMouseMove));
            }
        }
        else
        {
            // Keyboard drag/grow loop
            TPoint goLeft = new TPoint(-1, 0);
            TPoint goRight = new TPoint(1, 0);
            TPoint goUp = new TPoint(0, -1);
            TPoint goDown = new TPoint(0, 1);
            TPoint goCtrlLeft = new TPoint(-8, 0);
            TPoint goCtrlRight = new TPoint(8, 0);

            TRect saveBounds = GetBounds();
            do
            {
                p = origin;
                s = size;
                KeyEvent(ref ev);
                switch (ev.keyDown.keyCode)
                {
                    case Keys.kbLeft:      Change(mode, goLeft,      ref p, ref s, 0); break;
                    case Keys.kbRight:     Change(mode, goRight,     ref p, ref s, 0); break;
                    case Keys.kbUp:        Change(mode, goUp,        ref p, ref s, 0); break;
                    case Keys.kbDown:      Change(mode, goDown,      ref p, ref s, 0); break;
                    case Keys.kbCtrlLeft:  Change(mode, goCtrlLeft,  ref p, ref s, 0); break;
                    case Keys.kbCtrlRight: Change(mode, goCtrlRight, ref p, ref s, 0); break;
                    // Shifted variants (kbShLeft/kbShRight/...) 
                    case Keys.kbHome: p.x = limits.a.x; break;
                    case Keys.kbEnd:  p.x = limits.b.x - s.x; break;
                    case Keys.kbPgUp: p.y = limits.a.y; break;
                    case Keys.kbPgDn: p.y = limits.b.y - s.y; break;
                }
                MoveGrow(p, s, limits, minSize, maxSize, mode);
            } while (ev.keyDown.keyCode != Keys.kbEsc &&
                     ev.keyDown.keyCode != Keys.kbEnter);
            if (ev.keyDown.keyCode == Keys.kbEsc)
                Locate(saveBounds);
        }
        SetState(Views.sfDragging, false);
    }

    private void MoveGrow(TPoint p, TPoint s, TRect limits, TPoint minSize, TPoint maxSize, byte mode)
    {
        s.x = Math.Min(Math.Max(s.x, minSize.x), maxSize.x);
        s.y = Math.Min(Math.Max(s.y, minSize.y), maxSize.y);
        p.x = Math.Min(Math.Max(p.x, limits.a.x - s.x + 1), limits.b.x - 1);
        p.y = Math.Min(Math.Max(p.y, limits.a.y - s.y + 1), limits.b.y - 1);
        if ((mode & Views.dmLimitLoX) != 0) p.x = Math.Max(p.x, limits.a.x);
        if ((mode & Views.dmLimitLoY) != 0) p.y = Math.Max(p.y, limits.a.y);
        if ((mode & Views.dmLimitHiX) != 0) p.x = Math.Min(p.x, limits.b.x - s.x);
        if ((mode & Views.dmLimitHiY) != 0) p.y = Math.Min(p.y, limits.b.y - s.y);
        TRect r = new TRect(p.x, p.y, p.x + s.x, p.y + s.y);
        Locate(r);
    }

    private void Change(byte mode, TPoint delta, ref TPoint p, ref TPoint s, int grow)
    {
        if ((mode & Views.dmDragMove) != 0 && grow == 0)
            p += delta;
        else if ((mode & Views.dmDragGrow) != 0 && grow != 0)
            s += delta;
    }

    public virtual void CalcBounds(ref TRect bounds, TPoint delta)
    {
        bounds = GetBounds();

        int s = owner != null ? owner.size.x : size.x;
        int d = delta.x;
        if ((growMode & Views.gfGrowLoX) != 0) bounds.a.x = Grow(bounds.a.x, s, d);
        if ((growMode & Views.gfGrowHiX) != 0) bounds.b.x = Grow(bounds.b.x, s, d);

        s = owner != null ? owner.size.y : size.y;
        d = delta.y;
        if ((growMode & Views.gfGrowLoY) != 0) bounds.a.y = Grow(bounds.a.y, s, d);
        if ((growMode & Views.gfGrowHiY) != 0) bounds.b.y = Grow(bounds.b.y, s, d);

        TPoint minLim = default, maxLim = default;
        SizeLimits(ref minLim, ref maxLim);
        bounds.b.x = bounds.a.x + Range(bounds.b.x - bounds.a.x, minLim.x, maxLim.x);
        bounds.b.y = bounds.a.y + Range(bounds.b.y - bounds.a.y, minLim.y, maxLim.y);
    }

    private int Grow(int i, int s, int d)
    {
        if ((growMode & Views.gfGrowRel) != 0)
        {
            // upstream macro: i = (i*s + ((s-d)>>1)) / (s-d)
            int sd = s - d;
            if (sd == 0) return i + d;
            return (i * s + (sd >> 1)) / sd;
        }
        return i + d;
    }

    private static int Range(int val, int min, int max)
        => val < min ? min : (val > max ? max : val);

    public virtual void ChangeBounds(TRect bounds)
    {
        SetBounds(bounds);
        DrawView();
    }

    public virtual void GrowTo(int x, int y)
    {
        TRect r = new TRect(origin.x, origin.y, origin.x + x, origin.y + y);
        Locate(r);
    }

    public virtual void MoveTo(int x, int y)
    {
        TRect r = new TRect(x, y, x + size.x, y + size.y);
        Locate(r);
    }

    public virtual void SetBounds(TRect bounds)
    {
        origin = bounds.a;
        size = bounds.b - bounds.a;
    }

    public virtual ushort GetHelpCtx()
    {
        if ((state & Views.sfDragging) != 0)
            return Views.hcDragging;
        return helpCtx;
    }

    public virtual bool Valid(ushort command) => true;
    public virtual void Hide() 
    {
        if ((state & Views.sfVisible) != 0)
            SetState(Views.sfVisible, false);
    }
    public virtual void Show() 
    {
        if ((state & Views.sfVisible) == 0)
            SetState(Views.sfVisible, true);
    }

    public virtual void Draw()
    {
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        TDrawBuffer b = new TDrawBuffer(row);
        b.moveChar(0, ' ', GetColor(1), size.x);
        WriteLine(0, 0, size.x, size.y, b);
    }

    public virtual void DrawView() 
    {
        if (Exposed())
        {
            Draw();
            DrawCursor();
        }
    }
    public bool Exposed()
    {
        // L0: sfExposed a not zero size
        if ((state & Views.sfExposed) == 0) return false;
        if (size.x <= 0 || size.y <= 0) return false;

        // L10: paremt must be TGroupt and must not have buffer or lock
        var grp = owner;
        if (grp == null) // || grp.buffer != null || grp.lockFlag != 0)
        {
            return false;
        }

        // for y = 0..size.y-1  (L1…L3)
        for (int y = 0; y < size.y; y++)
        {
            int ay = origin.y + y;
            // not vertical clip?
            if (ay < grp.clip.a.y || ay >= grp.clip.b.y)
                continue;
            // for x = 0..size.x-1  (in assembleru AX is pointer y)
            for (int x = 0; x < size.x; x++)
            {
                //int ax0 = origin.x + x;
                //int ax1 = ax0;
                
                //if (ax0 < grp.clip.a.x) ax0 = grp.clip.a.x;
                //if (ax1 >= grp.clip.b.x) continue;

                //bool occluded = false;
                //foreach (var sib in grp.GetSubviewsAfter(this))
                //{
                //    if ((sib.State & Views.sfExposed) == 0)          // sbVisible, ne sfExposed
                //        continue;
                //    if (ay < sib.Origin.Y || ay >= sib.Origin.Y + sib.Size.Y)
                //        continue;
                //    int sx0 = sib.Origin.X;
                //    int sx1 = sx0 + sib.Size.X;
                //    if (ax0 >= sx0 && ax0 < sx1)
                //    {
                //        occluded = true;
                //        break;
                //    }
                //}

                int ax = origin.x + x;

                // horizontal clip (L11–L13)
                if (ax < grp.clip.a.x)
                    ax = grp.clip.a.x;
                if (ax >= grp.clip.b.x)
                    continue;

                // pixel (ax, ay) is inside clip‑rectu parent,
                // test overlapping siblings (L19–L23):

                // L20_L23: goo through all siblings from `last.Next` back to `this`
                bool occluded = false;
                // we start from first in layer, which is last.Next
                TView sib = grp.last.Next;
                while (true)
                {
                    if (sib == this)
                        break;  // we get to targer view

                    // must be visible
                    if ((sib.state & Views.sfVisible) != 0)
                    {
                        // is pixel inside 
                        if (ay >= sib.origin.y
                            && ay < sib.origin.y + sib.size.y
                            && ax >= sib.origin.x
                            && ax < sib.origin.x + sib.size.x)
                        {
                            occluded = true;
                            break;
                        }
                    }

                    // next sibling
                    sib = sib.Next;
                }

                if (!occluded)
                    return true;   // we found first visible
            }
        }

        // nothing found
        return false;
    }

    public virtual void HideCursor() => SetState(Views.sfCursorVis, false);

    public virtual void DrawHide(TView lastView)
    {
        DrawCursor();
        DrawUnderView((state & Views.sfShadow) != 0, lastView);
    }
    
    public virtual void DrawShow(TView lastView) 
    {
        DrawView();
        if ((state & Views.sfShadow) != 0)
            DrawUnderView(true, lastView);
    }
    
    public virtual void DrawUnderRect(ref TRect r, TView lastView)
    {
        if (owner == null) return;
        owner.clip.Intersect(r);
        owner.DrawSubViews(NextView(), lastView);
        owner.clip = owner.GetExtent();
    }

    public virtual void DrawUnderView(bool doShadow, TView lastView)
    {
        TRect r = GetBounds();
        if (doShadow) r.b += shadowSize;
        DrawUnderRect(ref r, lastView);
    }

    public virtual ushort DataSize() => 0;
    public virtual void GetData(ref object rec) { /* upstream: empty */ }
    public virtual void SetData(object rec) { /* upstream: empty */ }

    public virtual void BlockCursor() => SetState(Views.sfCursorIns, true);
    public virtual void NormalCursor() => SetState(Views.sfCursorIns, false);

    /// <summary>
    /// Repositions the hardware caret to this view's <see cref="cursor"/>
    /// translated into screen coordinates, when the view is the focused
    /// chain. Mirrors upstream <c>TVCursor::resetCursor</c>.
    /// </summary>
    public virtual void ResetCursor()
    {
        // Must be visible and focused. sfCursorVis is checked after
        // positioning: if not set, the cursor is hidden (type 0).
        if ((state & (Views.sfVisible | Views.sfFocused))
            != (Views.sfVisible | Views.sfFocused))
        {
            // Not visible or not focused — hide hardware cursor.
            TScreen.driver?.SetCursorType(0);
            return;
        }

        if (cursor.x < 0 || cursor.y < 0
            || cursor.x >= size.x || cursor.y >= size.y)
        {
            TScreen.driver?.SetCursorType(0);
            return;
        }

        TPoint g = MakeGlobal(cursor);

        // Cursor must be within the view's global bounds.
        if (g.x < 0 || g.y < 0
            || g.x >= TScreen.ScreenWidth
            || g.y >= TScreen.ScreenHeight)
        {
            TScreen.driver?.SetCursorType(0);
            return;
        }

        TScreen.driver?.SetCaretPosition(g.x, g.y);

        if ((state & Views.sfCursorVis) != 0)
        {
            // sfCursorIns → block (100 lines), otherwise use CursorLines.
            ushort ct = (state & Views.sfCursorIns) != 0
                ? (ushort)100
                : TScreen.CursorLines;
            TScreen.driver?.SetCursorType(ct != 0 ? ct : (ushort)0x0C0D);
        }
        else
        {
            TScreen.driver?.SetCursorType(0);
        }
    }

    public virtual void SetCursor(int x, int y)
    {
        cursor.x = x;
        cursor.y = y;
        DrawCursor();
    }

    public virtual void ShowCursor() => SetState(Views.sfCursorVis, true);
    public virtual void DrawCursor()
    {
        if ((state & Views.sfFocused) != 0)
            ResetCursor();
    }

    public virtual void ClearEvent(ref TEvent ev)
    {
        ev.What = Events.evNothing;
        ev.message.infoPtr = this;
    }

    public virtual bool EventAvail()
    {
        TEvent e = default;
        GetEvent(ref e);
        if (e.What != Events.evNothing)
            PutEvent(ref e);
        return e.What != Events.evNothing;
    }

    public virtual void GetEvent(ref TEvent @event)
    {
        if (owner != null)
            owner.GetEvent(ref @event);
    }

    public virtual void HandleEvent(ref TEvent @event) 
    {
        if (@event.What == Events.evMouseDown)
        {
            if ((state & (Views.sfSelected | Views.sfDisabled)) == 0
                && (options & Views.ofSelectable) != 0)
            {
                Select();
                if ((state & Views.sfSelected) == 0
                    || (options & Views.ofFirstClick) == 0)
                    ClearEvent(ref @event);
            }
        }
    }
    public virtual void PutEvent(ref TEvent ev)
    {
        owner?.PutEvent(ref ev);
    }

    public static bool CommandEnabled(ushort command)
    {
        return ((command > 255) || curCommandSet.Has(command));
    }

    public static void DisableCommands(TCommandSet commands)
    {
        commandSetChanged = commandSetChanged || !((curCommandSet & commands).IsEmpty());
        curCommandSet.Remove(commands);
    }

    public static void EnableCommands(TCommandSet commands)
    {
        commandSetChanged = commandSetChanged || !curCommandSet.Equals(curCommandSet | commands);
        curCommandSet.Add(commands);
    }

    public static void DisableCommand(ushort command)
    {
        commandSetChanged = commandSetChanged || curCommandSet.Has(command);
        curCommandSet.DisableCmd(command);
    }

    public static void EnableCommand(ushort command)
    {
        commandSetChanged = commandSetChanged || !curCommandSet.Has(command);
        curCommandSet.EnableCmd(command);
    }

    public static void GetCommands(TCommandSet commands)
    {
        // upstream copies into out param; we mutate in place
        for (ushort c = 0; c < 256; c++)
        {
            if (curCommandSet.Has(c)) commands.EnableCmd(c); else commands.DisableCmd(c);
        }
    }

    public static void SetCommands(TCommandSet commands)
    {
        commandSetChanged = commandSetChanged || !curCommandSet.Equals(commands);
        curCommandSet = new TCommandSet(commands);
    }
    public virtual void EndModal(ushort command)
    {
        var top = TopView();
        if (top != null && top != this)
            top.EndModal(command);
    }
    public virtual ushort Execute() 
    {
        return Views.cmCancel;
    }
    public virtual ushort GetColor(ushort color)
    {
        int colorPair = color >> 8;

        if (colorPair != 0)
            colorPair = MapColor(colorPair) << 8;

        colorPair |= MapColor((byte)color);  // C++ cast: mapColor(uchar(color))

        return (ushort)colorPair;
    }

    public virtual TPalette GetPalette() 
    {
        return palette;
    }

    public virtual byte MapColor(int /*color*/ index) 
    {
        TPalette p = GetPalette();
        /*TColorAttr*/ byte color;
        if (p[0] != 0)
        {
            //if (0 < index && index <= p[0])
            if (0 < index && index <= p.Size)
                color = (byte)p[index];
            else
                return errorAttr;
        }
        else
            color = (byte)index;
        if (color == 0)
            return errorAttr;
        if (owner != null)
            return owner.MapColor(color);
        return color;
    }

    public virtual bool GetState(ushort aState) => (state & aState) == aState;
    public virtual void SetState(ushort aState, bool enable) 
    {
        if (enable == true)
            state |= aState;
        else
            state &= (ushort)~aState;

        if (owner == null)
            return;
        
        switch (aState)
        {
            case Views.sfVisible:
                if ((owner.state & Views.sfExposed) != 0)
                    SetState(Views.sfExposed, enable);

                if (enable == true)
                    DrawShow(null);
                else
                    DrawHide(null);

                if ((options & Views.ofSelectable) != 0)
                    owner.ResetCurrent();
                break;

            case Views.sfCursorVis:
            case Views.sfCursorIns:
                DrawCursor();
                break;
            case Views.sfShadow:
                DrawUnderView(true, null);
                break;
            case Views.sfFocused:
                ResetCursor();
                Message(owner,
                         Events.evBroadcast,
                         (enable == true) ? Views.cmReceivedFocus : Views.cmReleasedFocus,
                         this
                       );
                break;
        }        
    }

    public virtual void KeyEvent(ref TEvent ev)
    {
        do { GetEvent(ref ev); }
        while (ev.What != Events.evKeyDown);
    }

    public virtual bool MouseEvent(ref TEvent ev, ushort mask)
    {
        do { GetEvent(ref ev); }
        while ((ev.What & (mask | Events.evMouseUp)) == 0);
        return ev.What != Events.evMouseUp;
    }

    public virtual TPoint MakeGlobal(TPoint source)
    {
        TPoint temp = source + origin;
        TView cur = this;
        while (cur.owner != null)
        {
            cur = cur.owner;
            temp += cur.origin;
        }
        return temp;
    }

    public virtual TPoint MakeLocal(TPoint source)
    {
        TPoint temp = source - origin;
        TView cur = this;
        while (cur.owner != null)
        {
            cur = cur.owner;
            temp -= cur.origin;
        }
        return temp;
    }
    public virtual TView NextView() 
    {
        if (this == owner.last)
            return null;
        else
            return Next;
    }

    public virtual TView PrevView()
    {
        if (owner != null && this == owner.First())
            return null;
        return Prev();
    }

    public virtual TView Prev()
    {
        TView res = this;
        while (res.Next != this)
            res = res.Next;
        return res;
    }

    public virtual void MakeFirst()
    {
        if (owner != null) PutInFrontOf(owner.First());
    }

    // Reorders this view to appear just before `target` in the owner's
    // z-list. Called by MakeFirst() → PutInFrontOf(owner.First()) to
    // bring a window to the front. Triggers Hide/Show so that the owner's
    // ResetCurrent() runs and updates `current` to the moved view.
    public virtual void PutInFrontOf(TView target)
    {
        if (owner == null || target == this || target == NextView()) return;
        if (target != null && target.owner != owner) return;

        TGroup ow = owner;
        ushort saveState = state;

        // Temporarily remove from the visible set without destroying
        // focus state — upstream does p->hide() / removeView / insertView / show().
        Hide();
        ow.RemoveView(this);
        ow.InsertView(this, target);
        if ((saveState & Views.sfVisible) != 0)
            Show();
    }
    public virtual TView TopView()
    {
        if (TheTopView != null)
        {
            return TheTopView;
        }
        else
        {
            TView p = this;
            while (p != null && (p.state & Views.sfModal) == 0)
            {
                p = p.owner;
            }
            return p;
        }
    }
    // WriteBuf(ScreenBuffer) — copy a w×h region from a pre-filled ScreenBuffer
    // into this view's visible area, row by row.
    //
    // buf.Data is treated as a flat array with stride = w (each row is exactly
    // w cells wide).  Row r starts at offset r*w in buf.Data.
    //
    // The L30 stride fix: the slice must be `span.Slice(offset, w)` with
    // `offset += w` each row.  Using `w+1` or advancing by any other amount
    // corrupts subsequent rows (each row would start one cell off, producing
    // a diagonal drift in the output).
    public virtual void WriteBuf(/*short*/ int x, /*short*/ int y, /*short*/ int w, /*short*/ int h, ScreenBuffer buf) 
    {
        var span = buf.Data;
        int offset = 0;
        while (h-- > 0)
        {
            // Slice exactly w cells (= one row of the source buffer).
            // stride = w — matches upstream writeBuf which advances the source
            // pointer by the view width after each row, NOT by the screen width.
            var slice = span.Slice(offset, w);
            WriteView(x, y++, w, slice);
            offset += w; // advance by one source row (stride = w)
        }
    }

    // WriteBuf(TDrawBuffer) — convenience wrapper: clamp w to the buffer length,
    // then delegate to the Span<TScreenChar> overload.
    public virtual void WriteBuf(int x, int y, int w, int h, TDrawBuffer b)
    {
        WriteBuf(x, y, Math.Min(w, (short)(b.Length - x)), h, b.Data);
    }

    // WriteBuf(Span<TScreenChar>) — copy a w×h block from a managed span.
    // The span is treated as a sequence of rows each of exactly w cells.
    // Each call to Slice(0, w) takes one row; b = b.Slice(w) advances to next.
    public virtual void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> b)
    {
        while (h-- > 0)
        {
            var slice = b.Slice(0, w);
            WriteView(x, y++, w, slice);
            b = b.Slice(w);
        }
    }

    public virtual void WriteLine(/*short*/ int x, /*short*/ int y, /*short*/ int w, /*short*/ int h, TDrawBuffer b) 
    {
        //writeLine( x, y, min(w, short(b.length() - x)), h, &b.data[0] );
        WriteLine(x, y, Math.Min(w, b.Length - x), h, b.Data);
    }

    public virtual void WriteLine(/*short*/ int x, /*short*/ int y, /*short*/ int w, /*short*/ int h, Span<TScreenChar> b)
    {
        while (h-- > 0)
        {
            WriteView(x, y++, w, b);
        }
    }

    public virtual void WriteStr(int x, int y, string str, byte color)
    {
        if (string.IsNullOrEmpty(str)) return;
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
        b.moveStr(0, str, color);
        WriteLine(x, y, str.Length, 1, b);
    }

    public virtual void WriteChar(int x, int y, char c, byte color, int count)
    {
        if (count <= 0) return;
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
        b.moveChar(0, c, color, count);
        WriteLine(x, y, count, 1, b);
    }

    public virtual void Select()
    {
        if ((options & Views.ofTopSelect) != 0)
            MakeFirst();
        else if (owner != null)
            owner.SetCurrent(this, selectMode.normalSelect);
    }

    // Convenience overload mirroring upstream putEvent(what,command,infoPtr).
    public void PutEvent(ushort what, ushort command, IInfo? infoPtr)
    {
        TEvent ev = default;
        ev.What = what;
        ev.message.command = command;
        ev.message.infoPtr = infoPtr;
        PutEvent(ref ev);
    }

    // WriteView — internal bridge from WriteBuf/WriteLine to the TVWrite pipeline.
    // Passes the view-local (x, y, count) and the source span to L0, which
    // translates coordinates into owner space, clips, and copies into the buffer.
    private void WriteView(/*short*/ int x, /*short*/ int y, /*short*/ int count, Span<TScreenChar> b)
    {
        var writer = new TVWrite();
        writer.L0(this, x, y, count, b);
    }

    public virtual void ShutDown()
    {
        if (owner != null)
            owner.Remove(this);
    }

    // Wire layout (34 bytes total):
    //   origin   (8B: WriteInt x, WriteInt y)
    //   size     (8B)
    //   cursor   (8B)
    //   growMode (1B)
    //   dragMode (1B)
    //   helpCtx  (2B: WriteShort)
    //   state    (2B: sfActive|sfSelected|sfFocused|sfExposed stripped)
    //   options  (2B)
    //   eventMask(2B)
    private const ushort _sfTransient =
        Views.sfActive | Views.sfSelected | Views.sfFocused | Views.sfExposed;

    public static readonly TStreamableClass StreamableClassTView =
        new TStreamableClass("TView", () => new TView(StreamableInit.streamableInit), 0);

    public override void Write(Opstream os)
    {
        os.WriteTPoint(origin);
        os.WriteTPoint(size);
        os.WriteTPoint(cursor);
        os.WriteByte(growMode);
        os.WriteByte(dragMode);
        os.WriteShort(helpCtx);
        os.WriteShort((ushort)(state & ~_sfTransient));
        os.WriteShort(options);
        os.WriteShort(eventMask);
    }

    public override object Read(Ipstream isStream)
    {
        origin    = isStream.ReadTPoint();
        size      = isStream.ReadTPoint();
        cursor    = isStream.ReadTPoint();
        growMode  = isStream.ReadByte();
        dragMode  = isStream.ReadByte();
        helpCtx   = isStream.ReadShort();
        state     = isStream.ReadShort();
        options   = isStream.ReadShort();
        eventMask = isStream.ReadShort();
        owner = null;
        Next  = null;
        return this;
    }

    public virtual string StreamableName() { return Name; }
    public static readonly string Name = "TView";
    public static TStreamable Build() { return new TView(StreamableInit.streamableInit); }

    /// <summary>
    /// Synthesizes a one-shot event and feeds it directly into <paramref name="receiver"/>'s <c>HandleEvent</c>.
    /// Returns the message <c>infoPtr</c> if the receiver consumed the event
    /// (set <c>What = evNothing</c>), otherwise <c>null</c>.
    /// </summary>
    public static IInfo? Message(TView? receiver, ushort what, ushort command, IInfo? infoPtr)
    {
        if (receiver == null) return null;
        TEvent ev = default;
        ev.What = what;
        ev.message.command = command;
        ev.message.infoPtr = infoPtr;
        receiver.HandleEvent(ref ev);
        return ev.What == Events.evNothing ? ev.message.infoPtr : null;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~TView()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
