namespace SharpVision.Constants;

/// <summary>
/// Event codes and masks. Bit values must match upstream Turbo Vision so that
/// driver-emitted events stay binary-compatible with C++ event consumers.
/// </summary>
public static class Events
{
    // Event codes
    public const ushort evMouseDown  = 0x0001;
    public const ushort evMouseUp    = 0x0002;
    public const ushort evMouseMove  = 0x0004;
    public const ushort evMouseAuto  = 0x0008;
    
    // mouse.buttons: mbButton4 = wheel up, mbButton5 = wheel down.
    public const ushort evMouseWheel = 0x0020;
    public const ushort evKeyDown    = 0x0010;
    public const ushort evCommand    = 0x0100;
    public const ushort evBroadcast  = 0x0200;

    // Event masks
    public const ushort evNothing  = 0x0000;
    // evMouse includes evMouseWheel (0x0020) so positional dispatch routes
    // wheel events to the view under the cursor, matching the behaviour of
    // evMouseDown/evMouseMove.
    public const ushort evMouse    = 0x002f;
    public const ushort evKeyboard = 0x0010;
    public const ushort evMessage  = 0xff00;

    // Mouse button state masks
    public const ushort mbLeftButton  = 0x01;
    public const ushort mbRightButton = 0x02;
    // Wheel direction pseudo-buttons (never set for real clicks).
    public const ushort mbButton4 = 0x04;   // wheel up   (toward screen top)
    public const ushort mbButton5 = 0x08;   // wheel down (toward screen bottom)
}
