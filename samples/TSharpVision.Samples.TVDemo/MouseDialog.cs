using TSharpVision;
using TSharpVision.Constants;

namespace TSharpVision.Samples.TVDemo;

// ---------------------------------------------------------------------------
// MouseState — pure mouse state model, no UI dependency.
// Updated from TEvent data; testable without a driver.
// ---------------------------------------------------------------------------
public sealed class MouseState
{
    public TPoint Position { get; private set; }
    public byte   Buttons  { get; private set; }
    public int    WheelDelta { get; private set; }  // cumulative wheel clicks (+up/-down)
    public int    LastWheel  { get; private set; }  // +1 up / -1 down / 0 when the last event was not a wheel step
    public bool   LeftDown   => (Buttons & Events.mbLeftButton)   != 0;
    public bool   MiddleDown => (Buttons & Events.mbMiddleButton) != 0;
    public bool   RightDown  => (Buttons & Events.mbRightButton)  != 0;

    public void Update(ref TEvent ev)
    {
        // Update position and buttons from any mouse event.
        if ((ev.What & Events.evMouse) != 0 || ev.What == Events.evMouseWheel)
        {
            Position = ev.mouse.where;
            Buttons = ev.mouse.buttons;
            LastWheel = 0;
            // Wheel direction is an event detail; buttons remain physical state only.
            if (ev.What == Events.evMouseWheel)
            {
                if ((ev.mouse.eventFlags & Events.meWheelUp) != 0) { WheelDelta++; LastWheel = 1; }
                if ((ev.mouse.eventFlags & Events.meWheelDown) != 0) { WheelDelta--; LastWheel = -1; }
            }
        }
    }

    public void Reset()
    {
        Position   = new TPoint(0, 0);
        Buttons    = 0;
        WheelDelta = 0;
        LastWheel  = 0;
    }
}

// ---------------------------------------------------------------------------
// MouseStateView — TView that draws a mouse diagram with live state beside it
// and captures events.
// ---------------------------------------------------------------------------
internal sealed class MouseStateView : TView
{
    internal enum MousePart { Left, Middle, Right, Wheel }
    internal enum PaletteRole : ushort { Text = 1, Region, Pressed }
    internal readonly record struct RegionSpan(MousePart Part, int X, int Y, int Width);

    // The top band is split into the left button, the middle button with the
    // wheel in its centre, and the right button; the rest is the mouse body.
    internal static readonly string[] Diagram =
    {
        @"  _______________  ",
        @" /  L  | M |  R  \ ",
        @"|      |(O)|      |",
        @"|______|___|______|",
        @"|                 |",
        @"|                 |",
        @" \               / ",
        @"  \_____________/  ",
    };

    internal static readonly RegionSpan[] Regions =
    {
        new(MousePart.Left,   2, 1, 5), new(MousePart.Left,   1, 2, 6), new(MousePart.Left,   1, 3, 6),
        new(MousePart.Middle, 8, 1, 3), new(MousePart.Wheel,  8, 2, 3), new(MousePart.Middle, 8, 3, 3),
        new(MousePart.Right, 12, 1, 5), new(MousePart.Right, 12, 2, 6), new(MousePart.Right, 12, 3, 6),
    };

    internal const int WheelX = 9;
    internal const int WheelY = 2;
    internal const int DiagramW = 19;
    internal const int TextX = DiagramW + 3;
    public const int ViewW = TextX + 20;
    public const int ViewH = 8;

    public MouseState State { get; }

    public MouseStateView(TRect bounds, MouseState state) : base(bounds)
    {
        State = state;
        // The click that selects this view is itself a press worth showing.
        options  |= Views.ofSelectable | Views.ofFirstClick;
        eventMask = Events.evMouse | Events.evMouseWheel;
    }

    // Local roles map through the dialog's standard palette, never raw attributes.
    private static readonly TPalette Palette = new("\x06\x10\x14", 3);
    public override TPalette GetPalette() => Palette;

    internal bool IsActive(MousePart part) => part switch
    {
        MousePart.Left   => State.LeftDown,
        MousePart.Middle => State.MiddleDown,
        MousePart.Right  => State.RightDown,
        _                => State.LastWheel != 0,
    };

    internal PaletteRole PartRole(MousePart part) => IsActive(part) ? PaletteRole.Pressed : PaletteRole.Region;

    internal char WheelGlyph => State.LastWheel > 0 ? '^' : State.LastWheel < 0 ? 'v' : 'O';

    internal string? TextLine(int y) => y switch
    {
        1 => $"Position: ({State.Position.x}, {State.Position.y})",
        2 => $"Buttons: {(State.LeftDown ? 'L' : '-')} {(State.MiddleDown ? 'M' : '-')} {(State.RightDown ? 'R' : '-')}",
        3 => "Wheel: " + (State.LastWheel > 0 ? "Up" : State.LastWheel < 0 ? "Down" : "0")
             + (State.WheelDelta != 0 ? $"  net {State.WheelDelta:+0;-0}" : ""),
        _ => null
    };

    public override void Draw()
    {
        var color = (char)GetColor((ushort)PaletteRole.Text);
        for (int y = 0; y < size.y; y++)
        {
            var b = new TDrawBuffer();
            b.moveChar(0, ' ', color, size.x);
            if (y < Diagram.Length)
            {
                b.moveStr(0, Diagram[y], color);
                if (y == WheelY) b.putChar(WheelX, WheelGlyph);
                foreach (var span in Regions)
                {
                    if (span.Y != y) continue;
                    ushort attr = GetColor((ushort)PartRole(span.Part));
                    for (int x = span.X; x < span.X + span.Width; x++)
                        b.putAttribute(x, attr);
                }
            }
            string? line = TextLine(y);
            if (line != null) b.moveStr(TextX, line[..Math.Min(line.Length, size.x - TextX)], color);
            WriteLine(0, (short)y, size.x, 1, b);
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
    public const int DlgW = MouseStateView.ViewW + 4;   // 46
    public const int DlgH = MouseStateView.ViewH + 3;   // 11

    internal MouseStateView View { get; }
    public MouseState State => View.State;

    public MouseDialog(int left = 4, int top = 5)
        : base(new TRect(left, top, left + DlgW, top + DlgH), "Mouse State")
    {
        View = new MouseStateView(
            new TRect(2, 1, 2 + MouseStateView.ViewW, 1 + MouseStateView.ViewH),
            new MouseState());
        Insert(View);

        // Bottom-right corner, level with the base of the diagram.
        Insert(new TButton(
            new TRect(DlgW - 13, DlgH - 4, DlgW - 3, DlgH - 2),
            "~C~lose", Views.cmClose, ButtonConstants.bfDefault));
    }

    public override void HandleEvent(ref TEvent ev)
    {
        // TButton supplies itself as infoPtr; cmClose expects the target window.
        if (ev.What == Events.evCommand && ev.message.command == Views.cmClose
            && ev.message.infoPtr is TButton button && button.owner == this)
            ev.message.infoPtr = this;
        base.HandleEvent(ref ev);
    }
}
