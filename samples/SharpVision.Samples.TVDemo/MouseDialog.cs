using SharpVision;
using SharpVision.Constants;

namespace SharpVision.Samples.TVDemo;

// ---------------------------------------------------------------------------
// MouseState — pure mouse state model, no UI dependency.
// Updated from TEvent data; testable without a driver.
// ---------------------------------------------------------------------------
public sealed class MouseState
{
    public TPoint Position { get; private set; }
    public byte   Buttons  { get; private set; }
    public int    WheelDelta { get; private set; }  // cumulative wheel clicks (+up/-down)
    public bool   LeftDown  => (Buttons & Events.mbLeftButton)  != 0;
    public bool   RightDown => (Buttons & Events.mbRightButton) != 0;

    public void Update(ref TEvent ev)
    {
        // Update position and buttons from any mouse event.
        if ((ev.What & Events.evMouse) != 0)
        {
            Position = ev.mouse.where;
            // Wheel events carry direction in buttons pseudo-bits rather than real button state.
            if (ev.What == Events.evMouseWheel)
            {
                if ((ev.mouse.buttons & Events.mbButton4) != 0) WheelDelta++;
                if ((ev.mouse.buttons & Events.mbButton5) != 0) WheelDelta--;
            }
            else
            {
                Buttons = ev.mouse.buttons;
            }
        }
    }

    public void Reset()
    {
        Position   = new TPoint(0, 0);
        Buttons    = 0;
        WheelDelta = 0;
    }
}

// ---------------------------------------------------------------------------
// MouseStateView — TView that renders live mouse state and captures events.
// ---------------------------------------------------------------------------
internal sealed class MouseStateView : TView
{
    public const int ViewW = 30;
    public const int ViewH = 5;

    public MouseState State { get; }

    public MouseStateView(TRect bounds, MouseState state) : base(bounds)
    {
        State = state;
        options  |= Views.ofSelectable;
        eventMask = Events.evMouse | Events.evMouseWheel;
    }

    public override void Draw()
    {
        var color = (char)GetColor(1);
        string[] lines =
        {
            $"Position : ({State.Position.x,3}, {State.Position.y,3})",
            $"Left     : {(State.LeftDown  ? "DOWN" : "up  ")}",
            $"Right    : {(State.RightDown ? "DOWN" : "up  ")}",
            $"Wheel    : {State.WheelDelta,+4}",
            "(Move mouse / click to update)",
        };
        for (int i = 0; i < ViewH && i < lines.Length; i++)
        {
            var b = new TDrawBuffer();
            b.moveChar(0, ' ', color, size.x);
            b.moveStr(0, lines[i], color);
            WriteLine(0, (short)i, size.x, 1, b);
        }
    }

    public override void HandleEvent(ref TEvent ev)
    {
        base.HandleEvent(ref ev);
        if ((ev.What & (Events.evMouse | Events.evMouseWheel)) != 0)
        {
            State.Update(ref ev);
            DrawView();
            // Do not clear — let normal dispatch continue so the dialog
            // can still be dragged etc.
        }
    }
}

// ---------------------------------------------------------------------------
// MouseDialog — modeless TDialog showing live mouse state.
// Opened from Demo -> Mouse Dialog.
// ---------------------------------------------------------------------------
public sealed class MouseDialog : TDialog
{
    public const int DlgW = MouseStateView.ViewW + 4;   // 34
    public const int DlgH = MouseStateView.ViewH + 5;   // 10

    internal MouseStateView View { get; }
    public MouseState State => View.State;

    public MouseDialog(int left = 4, int top = 5)
        : base(new TRect(left, top, left + DlgW, top + DlgH), "Mouse State")
    {
        View = new MouseStateView(
            new TRect(2, 1, 2 + MouseStateView.ViewW, 1 + MouseStateView.ViewH),
            new MouseState());
        Insert(View);

        Insert(new TButton(
            new TRect(DlgW / 2 - 5, DlgH - 3, DlgW / 2 + 5, DlgH - 1),
            "~C~lose", Views.cmClose, ButtonConstants.bfDefault));
    }
}
