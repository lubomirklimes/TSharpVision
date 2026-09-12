namespace TSharpVision.Constants;

/// <summary>
/// Event codes and masks. Bit values must match upstream Turbo Vision so that
/// driver-emitted events stay binary-compatible with C++ event consumers.
/// </summary>
public static class Events
{
    // Event codes
    /// <summary>A mouse button was pressed.</summary>
    public const ushort evMouseDown  = 0x0001;
    /// <summary>A mouse button was released.</summary>
    public const ushort evMouseUp    = 0x0002;
    /// <summary>The mouse moved to a new screen-cell position.</summary>
    public const ushort evMouseMove  = 0x0004;
    /// <summary>Repeated mouse action while a button remains held.</summary>
    public const ushort evMouseAuto  = 0x0008;
    
    // mouse.buttons: mbButton4 = wheel up, mbButton5 = wheel down.
    /// <summary>Mouse-wheel motion; direction is carried in the wheel pseudo-button bits.</summary>
    public const ushort evMouseWheel = 0x0020;
    /// <summary>A keyboard key was pressed.</summary>
    public const ushort evKeyDown    = 0x0010;
    /// <summary>A command message routed through the focused view chain.</summary>
    public const ushort evCommand    = 0x0100;
    /// <summary>A message offered to child views that accept broadcast events.</summary>
    public const ushort evBroadcast  = 0x0200;

    // Event masks
    /// <summary>No event, also used to mark an event consumed.</summary>
    public const ushort evNothing  = 0x0000;
    // evMouse includes evMouseWheel (0x0020) so positional dispatch routes
    // wheel events to the view under the cursor, matching the behaviour of
    // evMouseDown/evMouseMove.
    /// <summary>Mask of mouse events dispatched by screen position, including wheel motion.</summary>
    public const ushort evMouse    = 0x002f;
    /// <summary>Mask of keyboard events dispatched through focus.</summary>
    public const ushort evKeyboard = 0x0010;
    /// <summary>Mask selecting command and broadcast message kinds.</summary>
    public const ushort evMessage  = 0xff00;

    // Mouse button state masks
    /// <summary>Mouse-button state bit for the left button.</summary>
    public const ushort mbLeftButton  = 0x01;
    /// <summary>Mouse-button state bit for the right button.</summary>
    public const ushort mbRightButton = 0x02;
    // Wheel direction pseudo-buttons (never set for real clicks).
    /// <summary>Wheel-up pseudo-button, directed toward the screen top.</summary>
    public const ushort mbButton4 = 0x04;   // wheel up   (toward screen top)
    /// <summary>Wheel-down pseudo-button, directed toward the screen bottom.</summary>
    public const ushort mbButton5 = 0x08;   // wheel down (toward screen bottom)
}
