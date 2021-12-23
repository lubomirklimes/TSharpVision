using SharpVision.Constants;
using System;
namespace SharpVision;

public class TScroller : TView
{
    public new static readonly string Name = "TScroller";

    private static readonly TPalette _palette = new TPalette("\x06\x07", 2);

    public TScrollBar hScrollBar;
    public TScrollBar vScrollBar;
    public TPoint delta;
    public TPoint limit;
    protected byte drawLock;
    protected bool drawFlag;

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

    public override void ShutDown()
    {
        hScrollBar = null;
        vScrollBar = null;
        base.ShutDown();
    }

    public override void ChangeBounds(TRect bounds)
    {
        SetBounds(bounds);
        drawLock++;
        SetLimit(limit.x, limit.y);
        drawLock--;
        drawFlag = false;
        DrawView();
    }

    public void CheckDraw()
    {
        if (drawLock == 0 && drawFlag)
        {
            drawFlag = false;
            DrawView();
        }
    }

    public override TPalette GetPalette() => _palette;

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

    public void ScrollTo(int x, int y)
    {
        drawLock++;
        if (hScrollBar != null) hScrollBar.SetValue(x);
        if (vScrollBar != null) vScrollBar.SetValue(y);
        drawLock--;
        CheckDraw();
    }

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

    public static readonly TStreamableClass StreamableClassTScroller =
        new TStreamableClass("TScroller", () => new TScroller(StreamableInit.streamableInit), 0);

    protected TScroller(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);
        os.WritePointer(hScrollBar);
        os.WritePointer(vScrollBar);
        os.WriteTPoint(delta);
        os.WriteTPoint(limit);
    }

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

    public new static TStreamable Build() { return new TScroller(StreamableInit.streamableInit); }
    public override string StreamableName() { return Name; }
}
