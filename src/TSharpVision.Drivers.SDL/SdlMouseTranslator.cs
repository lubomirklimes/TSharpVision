// Source: SDL3 SDL_mouse.h and SDL_events.h. Pure translator from SDL
// mouse-button events into tvision TEvents. Tests can drive it without a
// real SDL window.
using TSharpVision;
using TSharpVision.Constants;

namespace TSharpVision.Drivers.SDL;

/// <summary>Mouse action reported to the SDL-to-framework event translator.</summary>
internal enum SdlMouseEventKind
{
    /// <summary>A mouse button was pressed.</summary>
    Down,
    /// <summary>A mouse button was released.</summary>
    Up,
    /// <summary>The mouse position changed.</summary>
    Move,
}

/// <summary>Converts SDL mouse actions and pixel positions to framework events and character-cell coordinates.</summary>
internal static class SdlMouseTranslator
{
    // SDL3 SDL_BUTTON_* values.
    /// <summary>SDL button identifier for the left mouse button; this is an identifier, not a held-button mask.</summary>
    public const byte SDL_BUTTON_LEFT   = 1;
    /// <summary>SDL button identifier for the middle mouse button; this is an identifier, not a held-button mask.</summary>
    public const byte SDL_BUTTON_MIDDLE = 2;
    /// <summary>SDL button identifier for the right mouse button; this is an identifier, not a held-button mask.</summary>
    public const byte SDL_BUTTON_RIGHT  = 3;
    /// <summary>SDL button identifier for the first side mouse button.</summary>
    public const byte SDL_BUTTON_X1 = 4;
    /// <summary>SDL button identifier for the second side mouse button.</summary>
    public const byte SDL_BUTTON_X2 = 5;

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
    /// <param name="controlKeyState">Translated Turbo Vision modifier state captured with the SDL event.</param>
    public static TEvent MakeEvent(
        SdlMouseEventKind kind,
        byte sdlButton,
        int cellX,
        int cellY,
        int clicks = 1,
        byte heldButtons = 0,
        uint controlKeyState = 0)
    {
        TEvent ev = default;
        ev.mouse.where = new TPoint(cellX, cellY);
        ev.mouse.controlKeyState = controlKeyState;
        if (clicks >= 2 && kind == SdlMouseEventKind.Down)
            ev.mouse.eventFlags |= Events.meDoubleClick;
        if (kind == SdlMouseEventKind.Move)
            ev.mouse.eventFlags |= Events.meMouseMoved;

        // Map button: tvision bitmask uses 0x01 left, 0x02 right, 0x04 middle
        // (matches Win32 driver). Up events report empty mask.
        // Move events preserve the caller-supplied held-button mask.
        byte buttons = 0;
        switch (kind)
        {
            case SdlMouseEventKind.Down:
                buttons = TranslateButton(sdlButton);
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

    internal static byte TranslateButton(byte sdlButton) => sdlButton switch
    {
        SDL_BUTTON_LEFT => (byte)Events.mbLeftButton,
        SDL_BUTTON_RIGHT => (byte)Events.mbRightButton,
        SDL_BUTTON_MIDDLE => (byte)Events.mbMiddleButton,
        SDL_BUTTON_X1 => (byte)Events.mbButton4,
        SDL_BUTTON_X2 => (byte)Events.mbButton5,
        _ => 0,
    };

    /// <summary>
    /// Build a vertical tvision wheel event. This source-compatible convenience
    /// overload represents one unflipped vertical axis and no held buttons.
    /// Direction convention:
    ///   deltaY > 0 = wheel up (away from user) → <see cref="Events.meWheelUp"/>
    ///   deltaY &lt; 0 = wheel down (toward user) → <see cref="Events.meWheelDown"/>
    ///   deltaY == 0 = horizontal only — ignored (returns false).
    /// </summary>
    /// <param name="deltaY">SDL wheel.Y delta (positive = up, negative = down).</param>
    /// <param name="cellX">Column of the pointer in character cells.</param>
    /// <param name="cellY">Row of the pointer in character cells.</param>
    /// <param name="ev">Resulting tvision TEvent.</param>
    /// <param name="controlKeyState">Translated Turbo Vision modifier state captured with the SDL event.</param>
    public static bool MakeWheelEvent(
        float deltaY, int cellX, int cellY, out TEvent ev, uint controlKeyState = 0)
    {
        TEvent[] events = MakeWheelEvents(
            0, deltaY, flipped: false, cellX, cellY, heldButtons: 0, controlKeyState);
        if (events.Length == 0)
        {
            ev = default;
            return false;
        }
        ev = events[0];
        return true;
    }

    /// <summary>
    /// Translates an SDL3 wheel payload. Flipped axes are normalized, vertical
    /// output precedes horizontal output, and each result carries one direction.
    /// </summary>
    public static TEvent[] MakeWheelEvents(
        float deltaX,
        float deltaY,
        bool flipped,
        int cellX,
        int cellY,
        byte heldButtons = 0,
        uint controlKeyState = 0)
    {
        if (flipped)
        {
            deltaX = -deltaX;
            deltaY = -deltaY;
        }

        int count = (deltaY != 0 ? 1 : 0) + (deltaX != 0 ? 1 : 0);
        if (count == 0) return [];

        var events = new TEvent[count];
        int index = 0;
        if (deltaY != 0)
            events[index++] = MakeWheel(
                deltaY > 0 ? Events.meWheelUp : Events.meWheelDown,
                cellX, cellY, heldButtons, controlKeyState);
        if (deltaX != 0)
            events[index] = MakeWheel(
                deltaX > 0 ? Events.meWheelRight : Events.meWheelLeft,
                cellX, cellY, heldButtons, controlKeyState);
        return events;
    }

    private static TEvent MakeWheel(
        uint direction, int cellX, int cellY, byte heldButtons, uint controlKeyState)
    {
        TEvent ev = default;
        ev.What = Events.evMouseWheel;
        ev.mouse.where = new TPoint(cellX, cellY);
        ev.mouse.buttons = heldButtons;
        ev.mouse.eventFlags = direction;
        ev.mouse.controlKeyState = controlKeyState;
        return ev;
    }
}
