using TSharpVision.Constants;
using TSharpVision.Drivers;

namespace TSharpVision;

/// <summary>A rectangular character-cell view that participates in an owning group, event dispatch, drawing, and streaming.</summary>
public class TView : TStreamable, IInfo, IDisposable
{
    private static readonly TPalette palette = new TPalette("\0", 0);

    /// <summary>Width and height in character cells.</summary>
    public TPoint size;
    /// <summary>View behavior flags from the of-prefixed constants in <see cref="Views"/>.</summary>
    public ushort options;
    /// <summary>Bit mask of event kinds this view accepts during group dispatch.</summary>
    public ushort eventMask;
    /// <summary>Current sf-prefixed visibility, focus, cursor, and drawing flags.</summary>
    public ushort state;
    /// <summary>Top-left position in the owning group's character-cell coordinates.</summary>
    public TPoint origin;
    /// <summary>Caret position in local character-cell coordinates.</summary>
    public TPoint cursor;
    /// <summary>gf-prefixed flags controlling how bounds respond to owner resizing.</summary>
    public byte growMode;
    /// <summary>dm-prefixed flags controlling interactive movement, resizing, and edge limits.</summary>
    public byte dragMode;
    /// <summary>Help context identifier used when the view is not being dragged.</summary>
    public ushort helpCtx;
    /// <summary>Whether the shared enabled-command set has changed since the last notification.</summary>
    public static bool commandSetChanged;

    // Every command code is enabled by default; the window commands below start disabled
    // and are enabled by TWindow.SetState when a window becomes selected.
    private static TCommandSet InitCommands()
    {
        TCommandSet temp = new TCommandSet();
        temp.EnableAll();
        temp.DisableCmd(Views.cmZoom);
        temp.DisableCmd(Views.cmClose);
        temp.DisableCmd(Views.cmResize);
        temp.DisableCmd(Views.cmNext);
        temp.DisableCmd(Views.cmPrev);
        return temp;
    }

    /// <summary>Shared set of enabled command identifiers for the application.</summary>
    public static TCommandSet curCommandSet = InitCommands();
    /// <summary>Containing group, or null while the view is detached.</summary>
    public TGroup owner;
    /// <summary>Whether controls draw selection markers in addition to color feedback.</summary>
    public static bool showMarkers;
    /// <summary>Fallback color attribute for invalid palette indices.</summary>
    public static byte errorAttr;

    // From Viewes.H
    /// <summary>Next sibling in the owner's circular child list.</summary>
    public TView Next;

    // From TVIEW.CPP
    /// <summary>Horizontal and vertical shadow extension in character cells.</summary>
    public TPoint shadowSize = new TPoint(2, 1);

    // From TGROUP.CPP
    /// <summary>Optional override for the modal root returned by <see cref="TopView"/>.</summary>
    public TView TheTopView;

    //protected static IDriver driver;
    private bool disposedValue;

    /// <summary>Stages of focused-event dispatch within a group.</summary>
    public enum phaseType
    {
        /// <summary>Dispatch to the selected child.</summary>
        phFocused,
        /// <summary>Offer the event to children requesting pre-processing.</summary>
        phPreProcess,
        /// <summary>Offer an unconsumed event to children requesting post-processing.</summary>
        phPostProcess
    };
    /// <summary>Controls selection-state transitions when a group changes its current child.</summary>
    public enum selectMode
    {
        /// <summary>Clear the old child's selection and select the new child.</summary>
        normalSelect,
        /// <summary>Preserve the old child's selected state while entering the new selection.</summary>
        enterSelect,
        /// <summary>Clear the old selection without setting the new child's selected state.</summary>
        leaveSelect
    };

    /// <summary>Creates a visible, detached view with the specified bounds in owner-relative character cells.</summary>
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
    /// <summary>Creates an uninitialized view whose state will be restored by <see cref="Read"/>.</summary>
    protected TView(StreamableInit init) { }
    // Overload for subclasses that declare their streaming ctor with `object` parameter.
    /// <summary>Creates an uninitialized view for subclasses that restore their state from a stream.</summary>
    protected TView(object _) { }

    /// <summary>Finalizer hook; owned resources must be released through deterministic disposal.</summary>
    ~TView() { }

    /// <summary>Returns minimum and maximum dimensions in cells; the default maximum is the owner's size, or unbounded when detached.</summary>
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

    /// <summary>Returns the view rectangle in owner-relative character-cell coordinates.</summary>
    public virtual TRect GetBounds()
    {
        return new TRect(origin, origin + size);
    }

    /// <summary>Returns the local rectangle from (0, 0) to the view's size, excluding its bottom and right edges.</summary>
    public virtual TRect GetExtent()
    {
        return new TRect(0, 0, size.x, size.y);
    }

    /// <summary>Returns the local drawing rectangle clipped to the owner's current clip rectangle.</summary>
    public virtual TRect GetClipRect()
    {
        TRect clip = GetBounds();
        if (owner != null)
            clip.Intersect(owner.clip);
        clip.Move(-origin.x, -origin.y);
        return clip;
    }

    /// <summary>Tests whether a screen-relative character-cell position lies inside this view.</summary>
    public virtual bool MouseInView(TPoint mouse)
    {
        mouse = MakeLocal(mouse);
        TRect r = GetExtent();
        return r.Contains(mouse);
    }

    /// <summary>Tests whether the view is visible and contains the event's screen-relative mouse position.</summary>
    public virtual bool ContainsMouse(TEvent ev)
    {
        return (state & Views.sfVisible) != 0 && MouseInView(ev.mouse.where);
    }

    /// <summary>Moves and resizes the view within its size limits, then redraws affected areas.</summary>
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

    /// <summary>Interactively moves or resizes the view within cell-coordinate limits; Escape restores the original keyboard-drag bounds.</summary>
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

    /// <summary>Calculates new owner-relative bounds for a change in owner size, applying the view's grow flags.</summary>
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

    /// <summary>Assigns owner-relative cell bounds and redraws the view.</summary>
    public virtual void ChangeBounds(TRect bounds)
    {
        SetBounds(bounds);
        DrawView();
    }

    /// <summary>Resizes to the requested width and height in cells, subject to size limits.</summary>
    public virtual void GrowTo(int x, int y)
    {
        TRect r = new TRect(origin.x, origin.y, origin.x + x, origin.y + y);
        Locate(r);
    }

    /// <summary>Moves the top-left corner to the requested owner-relative cell position.</summary>
    public virtual void MoveTo(int x, int y)
    {
        TRect r = new TRect(x, y, x + size.x, y + size.y);
        Locate(r);
    }

    /// <summary>Assigns origin and size from owner-relative cell bounds without redrawing or applying size limits.</summary>
    public virtual void SetBounds(TRect bounds)
    {
        origin = bounds.a;
        size = bounds.b - bounds.a;
    }

    /// <summary>Returns the dragging help context while dragging, otherwise the configured help context.</summary>
    public virtual ushort GetHelpCtx()
    {
        if ((state & Views.sfDragging) != 0)
            return Views.hcDragging;
        return helpCtx;
    }

    /// <summary>Determines whether the view permits the requested command; the base view accepts all commands.</summary>
    public virtual bool Valid(ushort command) => true;
    /// <summary>Clears the visible state and updates affected drawing and selection.</summary>
    public virtual void Hide() 
    {
        if ((state & Views.sfVisible) != 0)
            SetState(Views.sfVisible, false);
    }
    /// <summary>Sets the visible state and updates affected drawing and selection.</summary>
    public virtual void Show() 
    {
        if ((state & Views.sfVisible) == 0)
            SetState(Views.sfVisible, true);
    }

    /// <summary>Paints the view's content; the base implementation fills it with spaces using palette entry 1.</summary>
    public virtual void Draw()
    {
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        TDrawBuffer b = new TDrawBuffer(row);
        b.moveChar(0, ' ', GetColor(1), size.x);
        WriteLine(0, 0, size.x, size.y, b);
    }

    /// <summary>Draws content and caret when the view is exposed and has a drawable area.</summary>
    public virtual void DrawView() 
    {
        if (Exposed())
        {
            Draw();
            DrawCursor();
        }
    }

    /// <summary>Tests whether the view has a nonempty exposed area that can be drawn through its owner chain.</summary>
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

    /// <summary>Clears the caret-visible flag and refreshes the hardware caret as needed.</summary>
    public virtual void HideCursor() => SetState(Views.sfCursorVis, false);

    /// <summary>Refreshes the caret and redraws siblings uncovered by hiding this view, stopping before the supplied sibling.</summary>
    public virtual void DrawHide(TView lastView)
    {
        DrawCursor();
        DrawUnderView((state & Views.sfShadow) != 0, lastView);
    }
    
    /// <summary>Draws this view and refreshes its shadow over siblings up to the supplied stopping point.</summary>
    public virtual void DrawShow(TView lastView) 
    {
        DrawView();
        if ((state & Views.sfShadow) != 0)
            DrawUnderView(true, lastView);
    }
    
    /// <summary>Redraws underlying siblings within an owner-relative rectangle, stopping before the supplied sibling.</summary>
    public virtual void DrawUnderRect(ref TRect r, TView lastView)
    {
        if (owner == null) return;
        owner.clip.Intersect(r);
        owner.DrawSubViews(NextView(), lastView);
        owner.clip = owner.GetExtent();
    }

    /// <summary>Redraws siblings under this view, optionally including its shadow area.</summary>
    public virtual void DrawUnderView(bool doShadow, TView lastView)
    {
        TRect r = GetBounds();
        if (doShadow) r.b += shadowSize;
        DrawUnderRect(ref r, lastView);
    }

    /// <summary>Returns the size of the view's transferable data record; the base view has no data.</summary>
    public virtual ushort DataSize() => 0;
    /// <summary>Copies control data into a caller-supplied record; the base view leaves it unchanged.</summary>
    public virtual void GetData(ref object rec) { /* upstream: empty */ }
    /// <summary>Applies a control data record; the base view ignores it.</summary>
    public virtual void SetData(object rec) { /* upstream: empty */ }

    /// <summary>Selects the block-style caret state.</summary>
    public virtual void BlockCursor() => SetState(Views.sfCursorIns, true);
    /// <summary>Clears the block-style caret state.</summary>
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

    /// <summary>Sets the caret position in local character cells and refreshes it when focused.</summary>
    public virtual void SetCursor(int x, int y)
    {
        cursor.x = x;
        cursor.y = y;
        DrawCursor();
    }

    /// <summary>Sets the caret-visible flag and refreshes the hardware caret as needed.</summary>
    public virtual void ShowCursor() => SetState(Views.sfCursorVis, true);
    /// <summary>Refreshes the hardware caret when this view is focused.</summary>
    public virtual void DrawCursor()
    {
        if ((state & Views.sfFocused) != 0)
            ResetCursor();
    }

    /// <summary>Marks an event as consumed and records this view as the handler in its message payload.</summary>
    public virtual void ClearEvent(ref TEvent ev)
    {
        ev.What = Events.evNothing;
        ev.message.infoPtr = this;
    }

    /// <summary>Checks for an event and puts it back when present so it can be retrieved again.</summary>
    public virtual bool EventAvail()
    {
        TEvent e = default;
        GetEvent(ref e);
        if (e.What != Events.evNothing)
            PutEvent(ref e);
        return e.What != Events.evNothing;
    }

    /// <summary>Retrieves the next event through the owner chain.</summary>
    public virtual void GetEvent(ref TEvent @event)
    {
        if (owner != null)
            owner.GetEvent(ref @event);
    }

    /// <summary>Handles an event by reference; consumed events are cleared. The base view handles mouse selection.</summary>
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
    /// <summary>Returns an event through the owner chain for later processing.</summary>
    public virtual void PutEvent(ref TEvent ev)
    {
        owner?.PutEvent(ref ev);
    }

    /// <summary>Tests whether a command identifier is present in the shared enabled-command set.</summary>
    public static bool CommandEnabled(ushort command)
    {
        return curCommandSet.Has(command);
    }

    /// <summary>Removes the supplied commands from the shared enabled set and records whether it changed.</summary>
    public static void DisableCommands(TCommandSet commands)
    {
        // Intersects/IsSupersetOf answer the same questions as the previous
        // !(cur & commands).IsEmpty() / !cur.Equals(cur | commands) expressions without
        // allocating a temporary set on every call.
        commandSetChanged = commandSetChanged || curCommandSet.Intersects(commands);
        curCommandSet.Remove(commands);
    }

    /// <summary>Adds the supplied commands to the shared enabled set and records whether it changed.</summary>
    public static void EnableCommands(TCommandSet commands)
    {
        commandSetChanged = commandSetChanged || !curCommandSet.IsSupersetOf(commands);
        curCommandSet.Add(commands);
    }

    /// <summary>Disables a command globally and records whether the enabled set changed.</summary>
    public static void DisableCommand(ushort command)
    {
        commandSetChanged = commandSetChanged || curCommandSet.Has(command);
        curCommandSet.DisableCmd(command);
    }

    /// <summary>Enables a command globally and records whether the enabled set changed.</summary>
    public static void EnableCommand(ushort command)
    {
        commandSetChanged = commandSetChanged || !curCommandSet.Has(command);
        curCommandSet.EnableCmd(command);
    }

    /// <summary>Copies the entire shared enabled-command set into the supplied set.</summary>
    public static void GetCommands(TCommandSet commands)
    {
        // upstream copies into out param; we mutate in place.
        // The copy must span the whole command range: TGroup.ExecView saves the command set
        // here and restores it with SetCommands, so a partial copy would silently drop the
        // state of any command outside the copied range.
        commands.CopyFrom(curCommandSet);
    }

    /// <summary>Replaces the shared enabled-command set with a copy of the supplied set.</summary>
    public static void SetCommands(TCommandSet commands)
    {
        commandSetChanged = commandSetChanged || !curCommandSet.Equals(commands);
        curCommandSet = new TCommandSet(commands);
    }
    /// <summary>Asks the modal root to end execution with the supplied command result.</summary>
    public virtual void EndModal(ushort command)
    {
        var top = TopView();
        if (top != null && top != this)
            top.EndModal(command);
    }
    /// <summary>Executes the view modally; the base implementation immediately returns the cancel command.</summary>
    public virtual ushort Execute() 
    {
        return Views.cmCancel;
    }
    /// <summary>Maps one or two packed palette indices through the owner chain to display attributes.</summary>
    public virtual ushort GetColor(ushort color)
    {
        int colorPair = color >> 8;

        if (colorPair != 0)
            colorPair = MapColor(colorPair) << 8;

        colorPair |= MapColor((byte)color);  // C++ cast: mapColor(uchar(color))

        return (ushort)colorPair;
    }

    /// <summary>Returns the palette mapping used to resolve this view's logical colors.</summary>
    public virtual TPalette GetPalette() 
    {
        return palette;
    }

    /// <summary>Resolves a one-based palette index through the owner chain, using the error attribute for an invalid index.</summary>
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

    /// <summary>Returns whether all bits in the supplied state mask are set.</summary>
    public virtual bool GetState(ushort aState) => (state & aState) == aState;
    /// <summary>Sets or clears state flags and updates affected drawing, cursor, and owner selection.</summary>
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

    /// <summary>Retrieves events until a key-down event is received.</summary>
    public virtual void KeyEvent(ref TEvent ev)
    {
        do { GetEvent(ref ev); }
        while (ev.What != Events.evKeyDown);
    }

    /// <summary>Retrieves events until the mouse mask matches or a button is released; returns false on release.</summary>
    public virtual bool MouseEvent(ref TEvent ev, ushort mask)
    {
        do { GetEvent(ref ev); }
        while ((ev.What & (mask | Events.evMouseUp)) == 0);
        return ev.What != Events.evMouseUp;
    }

    /// <summary>Converts a local character-cell position to screen coordinates through the owner chain.</summary>
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

    /// <summary>Converts a screen character-cell position to this view's local coordinates.</summary>
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
    /// <summary>Returns the next sibling in drawing order, or null after the last sibling.</summary>
    public virtual TView NextView() 
    {
        if (this == owner.last)
            return null;
        else
            return Next;
    }

    /// <summary>Returns the preceding sibling in drawing order, or null before the first sibling.</summary>
    public virtual TView PrevView()
    {
        if (owner != null && this == owner.First())
            return null;
        return Prev();
    }

    /// <summary>Returns the preceding entry in the circular sibling list.</summary>
    public virtual TView Prev()
    {
        TView res = this;
        while (res.Next != this)
            res = res.Next;
        return res;
    }

    /// <summary>Moves this view to the front of its owner's drawing order.</summary>
    public virtual void MakeFirst()
    {
        if (owner != null) PutInFrontOf(owner.First());
    }

    // Reorders this view to appear just before `target` in the owner's
    // z-list. Called by MakeFirst() → PutInFrontOf(owner.First()) to
    // bring a window to the front. Triggers Hide/Show so that the owner's
    // ResetCurrent() runs and updates `current` to the moved view.
    /// <summary>Moves this view before a sibling in the owner's drawing order and refreshes visibility and selection.</summary>
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
    /// <summary>Returns the explicit top-view override or the nearest modal view in the owner chain.</summary>
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
    /// <summary>Writes a rectangle at a local cell position, reading tightly packed rows of the requested width from the source buffer.</summary>
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
    /// <summary>Writes a rectangle at a local cell position, limiting row width to the draw buffer's available length minus x.</summary>
    public virtual void WriteBuf(int x, int y, int w, int h, TDrawBuffer b)
    {
        WriteBuf(x, y, Math.Min(w, (short)(b.Length - x)), h, b.Data);
    }

    // WriteBuf(Span<TScreenChar>) — copy a w×h block from a managed span.
    // The span is treated as a sequence of rows each of exactly w cells.
    // Each call to Slice(0, w) takes one row; b = b.Slice(w) advances to next.
    /// <summary>Writes a rectangle at a local cell position from a span containing at least width times height cells in row order.</summary>
    public virtual void WriteBuf(int x, int y, int w, int h, Span<TScreenChar> b)
    {
        while (h-- > 0)
        {
            var slice = b.Slice(0, w);
            WriteView(x, y++, w, slice);
            b = b.Slice(w);
        }
    }

    /// <summary>Repeats a buffer row over the requested height at a local cell position, limiting width to the buffer length minus x.</summary>
    public virtual void WriteLine(/*short*/ int x, /*short*/ int y, /*short*/ int w, /*short*/ int h, TDrawBuffer b) 
    {
        //writeLine( x, y, min(w, short(b.length() - x)), h, &b.data[0] );
        WriteLine(x, y, Math.Min(w, b.Length - x), h, b.Data);
    }

    /// <summary>Repeats the first width cells of a span over the requested number of rows at a local cell position.</summary>
    public virtual void WriteLine(/*short*/ int x, /*short*/ int y, /*short*/ int w, /*short*/ int h, Span<TScreenChar> b)
    {
        while (h-- > 0)
        {
            WriteView(x, y++, w, b);
        }
    }

    /// <summary>Writes text with the supplied display attribute at a local character-cell position.</summary>
    public virtual void WriteStr(int x, int y, string str, byte color)
    {
        if (string.IsNullOrEmpty(str)) return;
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
        b.moveStr(0, str, color);
        WriteLine(x, y, str.Length, 1, b);
    }

    /// <summary>Writes a run of identical characters with the supplied display attribute at a local cell position.</summary>
    public virtual void WriteChar(int x, int y, char c, byte color, int count)
    {
        if (count <= 0) return;
        Span<TScreenChar> row = stackalloc TScreenChar[size.x > 0 ? size.x : 1];
        var b = new TDrawBuffer(row);
        b.moveChar(0, c, color, count);
        WriteLine(x, y, count, 1, b);
    }

    /// <summary>Selects this view in its owner, bringing it to the front when the top-select option is set.</summary>
    public virtual void Select()
    {
        if ((options & Views.ofTopSelect) != 0)
            MakeFirst();
        else if (owner != null)
            owner.SetCurrent(this, selectMode.normalSelect);
    }

    /// <summary>
    /// Queues <paramref name="command"/> for delivery to this view, later, on the event-loop
    /// thread.
    ///
    /// <b>Safe to call from any thread</b> — this is the way work finishing on a worker hands
    /// its result back to a view. The view's <see cref="HandleEvent"/> is invoked on the
    /// event-loop thread with an <c>evCommand</c> carrying <paramref name="command"/> and
    /// <paramref name="info"/>; nothing about the view is touched on the calling thread.
    ///
    /// Unlike <see cref="PutEvent(ref TEvent)"/>, which feeds an event into the queue for
    /// whichever group currently owns the loop, a post names its target. It therefore reaches
    /// this view even while a modal view is executing through <see cref="TGroup.ExecView"/>,
    /// and that modal view never sees it.
    ///
    /// Posts are delivered in the order they were made, one per pass of the event loop, and
    /// are discarded if this view has left the view tree by the time its turn comes.
    /// </summary>
    public void Post(ushort command, IInfo? info = null)
        => TEventQueue.Post(this, command, info);

    /// <summary>
    /// Whether a posted event should still be delivered to this view. A view that has been
    /// removed from its owner is no longer part of the interface, so pending posts for it are
    /// dropped. <see cref="TProgram"/> overrides this: it is the root and has no owner.
    /// </summary>
    internal virtual bool CanReceivePostedEvents => owner != null;

    // Convenience overload mirroring upstream putEvent(what,command,infoPtr).
    /// <summary>Creates a message event with the supplied kind, command, and optional payload and queues it through the owner.</summary>
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

    /// <summary>Detaches this view from its owner; subclasses can release owned resources before or after detachment.</summary>
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

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTView =
        new TStreamableClass("TView", () => new TView(StreamableInit.streamableInit), 0);

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <summary>Returns the identifier used for this view's streamable type.</summary>
    public virtual string StreamableName() { return Name; }
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public static readonly string Name = "TView";
    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
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

    /// <summary>Marks the view disposed; subclasses release managed resources when disposing is true.</summary>
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

    /// <summary>Runs deterministic resource cleanup and suppresses finalization.</summary>
    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
