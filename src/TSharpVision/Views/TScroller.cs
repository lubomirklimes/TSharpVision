using TSharpVision.Constants;
using System;
namespace TSharpVision;

/// <summary>A selectable viewport whose content offset and dimensions are coordinated with optional scrollbars.</summary>
public class TScroller : TView
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TScroller";

    private static readonly TPalette _palette = new TPalette("\x06\x07", 2);

    /// <summary>Optional horizontal scrollbar associated with the viewport.</summary>
    public TScrollBar hScrollBar;
    /// <summary>Optional vertical scrollbar associated with the viewport.</summary>
    public TScrollBar vScrollBar;
    /// <summary>Current content offset in character cells.</summary>
    public TPoint delta;
    /// <summary>Total scrollable content width and height in character cells.</summary>
    public TPoint limit;
    /// <summary>Nesting count used to defer redraws while related scroll settings change.</summary>
    protected byte drawLock;
    /// <summary>Whether a redraw is pending until the drawing lock is released.</summary>
    protected bool drawFlag;

    /// <summary>Creates a selectable viewport with zero content size and optional horizontal and vertical scrollbars.</summary>
    public TScroller(TRect bounds, TScrollBar aHScrollBar, TScrollBar aVScrollBar)
        : base(bounds)
    {
        drawLock = 0;
        drawFlag = false;
        hScrollBar = aHScrollBar;
        vScrollBar = aVScrollBar;
        delta = new TPoint(0, 0);
        limit = new TPoint(0, 0);
        options |= Views.ofSelectable;
        eventMask |= Events.evBroadcast;
        eventMask |= Events.evMouseWheel;
    }

    /// <inheritdoc />
    public override void ShutDown()
    {
        hScrollBar = null;
        vScrollBar = null;
        base.ShutDown();
    }

    /// <inheritdoc />
    public override void ChangeBounds(TRect bounds)
    {
        SetBounds(bounds);
        drawLock++;
        SetLimit(limit.x, limit.y);
        drawLock--;
        drawFlag = false;
        DrawView();
    }

    /// <summary>Performs a pending redraw when no drawing lock remains.</summary>
    public void CheckDraw()
    {
        if (drawLock == 0 && drawFlag)
        {
            drawFlag = false;
            DrawView();
        }
    }

    /// <inheritdoc />
    public override TPalette GetPalette() => _palette;

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);

        if (@event.What == Events.evBroadcast &&
            @event.message.command == Views.cmScrollBarChanged &&
            (ReferenceEquals(@event.message.infoPtr, hScrollBar) ||
             ReferenceEquals(@event.message.infoPtr, vScrollBar)))
        {
            ScrollDraw();
        }
        else if (@event.What == Events.evMouseWheel)
        {
            // Vertical wheel: scroll content by WheelStep lines.
            // We update delta.y directly (no owner needed) and also sync the
            // vertical scrollbar thumb if present so the visual position stays
            // consistent.
            bool up = (@event.mouse.buttons & Events.mbButton4) != 0;
            int newY = delta.y + (up ? -WheelStep : WheelStep);
            newY = Math.Max(0, Math.Min(newY, limit.y - size.y));
            if (newY != delta.y)
            {
                delta.y = newY;
                if (vScrollBar != null) vScrollBar.SetValue(newY);
                DrawView();
            }
            ClearEvent(ref @event);
        }
    }

    private const int WheelStep = 3;

    /// <summary>Synchronizes the content offset with scrollbar values and updates caret position and drawing.</summary>
    public virtual void ScrollDraw()
    {
        TPoint d;
        d.x = (hScrollBar != null) ? hScrollBar.value : 0;
        d.y = (vScrollBar != null) ? vScrollBar.value : 0;

        if (d.x != delta.x || d.y != delta.y)
        {
            SetCursor(cursor.x + delta.x - d.x, cursor.y + delta.y - d.y);
            delta = d;
            if (drawLock != 0)
                drawFlag = true;
            else
                DrawView();
        }
    }

    /// <summary>Requests content offsets through the attached scrollbars; an axis without a scrollbar is unchanged.</summary>
    public void ScrollTo(int x, int y)
    {
        drawLock++;
        if (hScrollBar != null) hScrollBar.SetValue(x);
        if (vScrollBar != null) vScrollBar.SetValue(y);
        drawLock--;
        CheckDraw();
    }

    /// <summary>Sets content dimensions in cells and updates scrollbar ranges and page sizes for the viewport.</summary>
    public void SetLimit(int x, int y)
    {
        limit.x = x;
        limit.y = y;
        drawLock++;
        if (hScrollBar != null)
            hScrollBar.SetParams(hScrollBar.value, 0, x - size.x, size.x, 1);
        if (vScrollBar != null)
            vScrollBar.SetParams(vScrollBar.value, 0, y - size.y, size.y, 1);
        drawLock--;
        CheckDraw();
    }

    private void ShowSBar(TScrollBar sBar)
    {
        if (sBar != null)
        {
            if (GetState((ushort)(Views.sfActive | Views.sfSelected)))
                sBar.Show();
            else
                sBar.Hide();
        }
    }

    /// <inheritdoc />
    public override void SetState(ushort aState, bool enable)
    {
        base.SetState(aState, enable);
        if ((aState & (Views.sfActive | Views.sfSelected)) != 0)
        {
            ShowSBar(hScrollBar);
            ShowSBar(vScrollBar);
        }
    }

    // Wire layout (after base): hScrollBar ptr, vScrollBar ptr, delta (TPoint),
    // limit (TPoint). drawLock and drawFlag are runtime-only, reset on load.

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTScroller =
        new TStreamableClass("TScroller", () => new TScroller(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TScroller(StreamableInit init) : base(init) { }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WritePointer(hScrollBar);
        os.WritePointer(vScrollBar);
        os.WriteTPoint(delta);
        os.WriteTPoint(limit);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);
        hScrollBar = isStream.ReadPointer() as TScrollBar;
        vScrollBar = isStream.ReadPointer() as TScrollBar;
        delta = isStream.ReadTPoint();
        limit = isStream.ReadTPoint();
        drawLock = 0;
        drawFlag = false;
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() { return new TScroller(StreamableInit.streamableInit); }
    /// <inheritdoc />
    public override string StreamableName() { return Name; }
}
