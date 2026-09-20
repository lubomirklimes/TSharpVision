namespace TSharpVision.Constants;

/// <summary>
/// Event codes and masks. Historical bit values match upstream Turbo Vision so
/// driver-emitted events preserve logical event-kind compatibility. Managed event
/// storage is not binary-compatible with the original C++ layout.
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
    
    /// <summary>Mouse-wheel motion; direction is carried by a <c>meWheel*</c> flag in the mouse payload.</summary>
    public const ushort evMouseWheel = 0x0020;
    /// <summary>An ordinary keyboard key press or native repeat, including the control state at that moment.</summary>
    public const ushort evKeyDown    = 0x0010;
    /// <summary>The complete logical Shift, Ctrl, and Alt state changed independently of an ordinary key press.</summary>
    /// <remarks>The new state is available through <see cref="TEvent.Modifiers"/>.</remarks>
    public const ushort evModifierChanged = 0x0040;
    /// <summary>Release of an ordinary non-modifier key on drivers that support key-release reporting.</summary>
    /// <remarks>Modifier-key releases are represented only by <see cref="evModifierChanged"/>.</remarks>
    public const ushort evKeyUp = 0x0080;
    /// <summary>A command message routed through the focused view chain.</summary>
    public const ushort evCommand    = 0x0100;
    /// <summary>A message offered to child views that accept broadcast events.</summary>
    public const ushort evBroadcast  = 0x0200;

    // Event masks
    /// <summary>No event, also used to mark an event consumed.</summary>
    public const ushort evNothing  = 0x0000;
    // Historical Turbo Vision aggregate: the four classic mouse event bits.
    // Extensions such as evMouseWheel require explicit opt-in.
    /// <summary>Mask of the four historical mouse event kinds.</summary>
    public const ushort evMouse    = 0x000f;
    /// <summary>Historical mask selecting ordinary key-down events dispatched through focus.</summary>
    // Deliberately remains the historical evKeyDown-only mask. Modifier transitions
    // are additive and explicit so existing views do not start receiving new events.
    public const ushort evKeyboard = 0x0010;
    /// <summary>Mask selecting command and broadcast message kinds.</summary>
    public const ushort evMessage  = 0xff00;

    // Physical mouse-button state masks.
    /// <summary>Historical mouse-button state bit for the left button.</summary>
    public const ushort mbLeftButton  = 0x01;
    /// <summary>Historical mouse-button state bit for the right button.</summary>
    public const ushort mbRightButton = 0x02;
    /// <summary>TSharpVision extension for the physical middle mouse button.</summary>
    public const ushort mbMiddleButton = 0x04;
    /// <summary>TSharpVision extension for the first physical side button.</summary>
    public const ushort mbButton4 = 0x08;
    /// <summary>TSharpVision extension for the second physical side button.</summary>
    public const ushort mbButton5 = 0x10;

    // Mouse eventFlags.
    /// <summary>Historical Turbo Vision flag indicating that the pointer position changed.</summary>
    public const uint meMouseMoved = 0x01;
    /// <summary>Historical Turbo Vision flag indicating a classified double-click button-down.</summary>
    public const uint meDoubleClick = 0x02;
    /// <summary>TSharpVision extension indicating upward vertical wheel motion.</summary>
    public const uint meWheelUp = 0x04;
    /// <summary>TSharpVision extension indicating downward vertical wheel motion.</summary>
    public const uint meWheelDown = 0x08;
    /// <summary>TSharpVision extension indicating horizontal wheel motion to the left.</summary>
    public const uint meWheelLeft = 0x10;
    /// <summary>TSharpVision extension indicating horizontal wheel motion to the right.</summary>
    public const uint meWheelRight = 0x20;
}
