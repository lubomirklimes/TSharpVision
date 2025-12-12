// Source: SDL3 SDL_mouse.h and SDL_events.h. Pure translator from SDL
// mouse-button events into tvision TEvents. Tests can drive it without a
// real SDL window.
using TSharpVision;
using TSharpVision.Constants;

namespace TSharpVision.Drivers.SDL;

public enum SdlMouseEventKind
{
    Down,
    Up,
    Move,
}

public static class SdlMouseTranslator
{
    // SDL3 SDL_BUTTON_* values.
    public const byte SDL_BUTTON_LEFT   = 1;
    public const byte SDL_BUTTON_MIDDLE = 2;
    public const byte SDL_BUTTON_RIGHT  = 3;

    /// <summary>
    /// Convert an SDL pixel position into character cell coordinates.
    /// Returns (-1,-1) when cell metrics are non-positive.
    /// Coordinates are clamped to (0,0) minimum to avoid negative cell indices.
    /// </summary>
    public static TPoint PixelToCell(int pixelX, int pixelY, int cellWidth, int cellHeight)
    {
        if (cellWidth <= 0 || cellHeight <= 0) return new TPoint(-1, -1);
        int cx = pixelX < 0 ? 0 : pixelX / cellWidth;
        int cy = pixelY < 0 ? 0 : pixelY / cellHeight;
        return new TPoint(cx, cy);
    }

    /// <summary>
    /// Build a tvision mouse button event from a translated SDL mouse event.
    /// </summary>
    /// <param name="kind">Down / Up / Move.</param>
    /// <param name="sdlButton">SDL_BUTTON_* value (1=left, 3=right). Ignored for Move.</param>
    /// <param name="cellX">column in character cells (0-based).</param>
    /// <param name="cellY">row in character cells (0-based).</param>
    /// <param name="clicks">number of clicks reported by SDL (≥2 = double).</param>
    /// <param name="heldButtons">
    /// Bitmask of currently held buttons for Move events (0x01 left, 0x02 right, 0x04 middle).
    /// Ignored for Down/Up where the single pressed/released button is used.
    /// </param>
    public static TEvent MakeEvent(
        SdlMouseEventKind kind,
        byte sdlButton,
        int cellX,
        int cellY,
        int clicks = 1,
        byte heldButtons = 0)
    {
        TEvent ev = default;
        ev.mouse.where = new TPoint(cellX, cellY);
        ev.mouse.doubleClick = clicks >= 2 && kind == SdlMouseEventKind.Down;

        // Map button: tvision bitmask uses 0x01 left, 0x02 right, 0x04 middle
        // (matches Win32 driver). Up events report empty mask.
        // Move events preserve the caller-supplied held-button mask.
        byte buttons = 0;
        switch (kind)
        {
            case SdlMouseEventKind.Down:
                switch (sdlButton)
                {
                    case SDL_BUTTON_LEFT:   buttons = (byte)Events.mbLeftButton;  break;
                    case SDL_BUTTON_RIGHT:  buttons = (byte)Events.mbRightButton; break;
                    case SDL_BUTTON_MIDDLE: buttons = 0x04;                       break;
                }
                break;
            case SdlMouseEventKind.Up:
                buttons = 0; // tvision Up event carries empty mask
                break;
            case SdlMouseEventKind.Move:
                buttons = heldButtons;
                break;
        }
        ev.mouse.buttons = buttons;

        ev.What = kind switch
        {
            SdlMouseEventKind.Down => Events.evMouseDown,
            SdlMouseEventKind.Up   => Events.evMouseUp,
            _                       => Events.evMouseMove,
        };
        return ev;
    }

    /// <summary>
    /// Build a tvision wheel event from an SDL_EVENT_MOUSE_WHEEL payload.
    /// Direction convention:
    ///   deltaY > 0 = wheel up (away from user) → <see cref="Events.mbButton4"/>
    ///   deltaY &lt; 0 = wheel down (toward user) → <see cref="Events.mbButton5"/>
    ///   deltaY == 0 = horizontal only — ignored (returns false).
    /// </summary>
    /// <param name="deltaY">SDL wheel.Y delta (positive = up, negative = down).</param>
    /// <param name="cellX">Column of the pointer in character cells.</param>
    /// <param name="cellY">Row of the pointer in character cells.</param>
    /// <param name="ev">Resulting tvision TEvent.</param>
    public static bool MakeWheelEvent(float deltaY, int cellX, int cellY, out TEvent ev)
    {
        ev = default;
        if (deltaY == 0f) return false;

        ev.What = Events.evMouseWheel;
        ev.mouse.where = new TPoint(cellX, cellY);
        ev.mouse.buttons = deltaY > 0f ? (byte)Events.mbButton4 : (byte)Events.mbButton5;
        ev.mouse.doubleClick = false;
        return true;
    }
}
