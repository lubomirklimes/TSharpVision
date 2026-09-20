// TEvent and friends. Upstream models KeyDownEvent / MouseEventType /
// MessageEvent as a C union sharing the storage that follows the leading
// `what` ushort. We cannot replicate the union perfectly because the
// payload may contain managed references, but we lay the fields out as
// sequential structs and keep the upstream field names (lowercase) so the
// port reads like the original.
using TSharpVision.Drivers;
using System.Runtime.InteropServices;

namespace TSharpVision;

/// <summary>
/// Marker for the <c>void* infoPtr</c> slot in <see cref="MessageEvent"/>.
/// Concrete payloads (commands data, list items, …) implement this so the
/// reference type stays managed.
/// </summary>
public interface IInfo
{
}

/// <summary>
/// Mirrors the historical Turbo Vision <c>MouseEventType</c> declared in
/// <c>SYSTEM.H</c>. Drivers expose translated Turbo Vision flags and control
/// state here rather than backend-native bit masks.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MouseEventType
{
    /// <summary>Mouse position in screen character-cell coordinates.</summary>
    public TPoint where;       // TPoint where
    /// <summary>Historical mouse-event flag mask, including movement and double-click classification.</summary>
    public uint eventFlags;    // ulong eventFlags (32-bit in the original ABI)
    /// <summary>Translated Turbo Vision Shift, Ctrl, Alt, and supported lock-state bits accompanying this mouse event.</summary>
    public uint controlKeyState; // ulong controlKeyState (32-bit in the original ABI)
    /// <summary>Physical mouse-button mask represented by this event; wheel direction is stored in <see cref="eventFlags"/>.</summary>
    public byte buttons;       // uchar buttons

    /// <summary>Compatibility view of <see cref="eventFlags"/>; no independent double-click state is stored.</summary>
    public bool doubleClick
    {
        readonly get => (eventFlags & Constants.Events.meDoubleClick) != 0;
        set
        {
            if (value)
                eventFlags |= Constants.Events.meDoubleClick;
            else
                eventFlags &= ~Constants.Events.meDoubleClick;
        }
    }
}

/// <summary>
/// Mirrors upstream <c>struct CharScanType</c> (event.h:185): two single
/// bytes packed into a 16-bit slot. Upstream code reads the union as either
/// two bytes or one ushort; the helper conversions below provide that.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CharScanType
{
    /// <summary>Legacy single-byte character value associated with the key.</summary>
    public byte charCode;
    /// <summary>Legacy scan-code byte, stored in the high byte of the packed representation.</summary>
    public byte scanCode;

    /// <summary>Stores separate legacy character and scan-code bytes.</summary>
    public CharScanType(byte ch, byte sc) { charCode = ch; scanCode = sc; }
    /// <summary>Unpacks the character from the low byte and the scan code from the high byte.</summary>
    public CharScanType(ushort packed)
    {
        charCode = (byte)(packed & 0xFF);
        scanCode = (byte)((packed >> 8) & 0xFF);
    }
    /// <summary>Packs the character into the low byte and the scan code into the high byte.</summary>
    public ushort ToUShort() => (ushort)(charCode | (scanCode << 8));
}

/// <summary>
/// Mirrors upstream <c>struct KeyDownEvent</c> (event.h:190).
/// </summary>
public struct KeyDownEvent
{
    /// <summary>Historical Turbo Vision character/scan pair accompanying this key event.</summary>
    public CharScanType charScan;
    /// <summary>Historical 16-bit Turbo Vision key identity, packed as scan byte then character byte where representable.</summary>
    public ushort keyCode;
    /// <summary>32-bit unsigned keyboard control-state mask, matching the width of Borland Turbo Vision's public <c>ulong</c> field.</summary>
    /// <remarks>
    /// Ordinary key events retain the driver's existing modifier and lock-state bits.
    /// For <c>evModifierChanged</c>, only logical Shift, Ctrl, and Alt bits are relevant.
    /// </remarks>
    public uint controlKeyState;
    /// <summary>Untranslated scan-code byte supplied by the input driver when available.</summary>
    public byte raw_scanCode;
    private string? _text;

    /// <summary>Unicode text extension supplied by the input driver; empty when the event has no text.</summary>
    /// <remarks>Arbitrary Unicode is intentionally not encoded into <see cref="keyCode"/>. The getter normalizes default-constructed and null-assigned values to <see cref="string.Empty"/>.</remarks>
    public string text
    {
        readonly get => _text ?? string.Empty;
        set => _text = value ?? string.Empty;
    }
}

/// <summary>
/// Mirrors upstream <c>struct MessageEvent</c> (event.h:198). The C union
/// (<c>infoPtr</c>/<c>infoLong</c>/<c>infoWord</c>/<c>infoInt</c>/
/// <c>infoByte</c>/<c>infoChar</c>) is preserved as parallel fields; only
/// one is meaningful per event in practice.
/// </summary>
public struct MessageEvent
{
    /// <summary>Command identifier interpreted by the receiving view for command and broadcast events.</summary>
    public ushort command;
    /// <summary>Optional object payload referenced by the message; its meaning is defined by the command.</summary>
    public IInfo? infoPtr;
    /// <summary>Signed 64-bit managed message payload; meaningful only for commands that use this field.</summary>
    /// <remarks>This deliberately widens the original 32-bit signed Turbo Vision slot; it preserves command semantics, not binary record layout.</remarks>
    public long infoLong;
    /// <summary>Unsigned 16-bit message payload; meaningful only for commands that use this field.</summary>
    public ushort infoWord;
    /// <summary>Signed 16-bit message payload; meaningful only for commands that use this field.</summary>
    public short infoInt;
    /// <summary>Byte message payload; meaningful only for commands that use this field.</summary>
    public byte infoByte;
    /// <summary>UTF-16 character message payload; meaningful only for commands that use this field.</summary>
    public char infoChar;
}

/// <summary>
/// Mirrors upstream <c>struct TEvent</c> (event.h:212). Carries one of the
/// three payload variants based on <see cref="What"/>:
/// <c>evMouse*</c> → <see cref="mouse"/>, <c>evKeyDown</c>, <c>evKeyUp</c>, or
/// <c>evModifierChanged</c> →
/// <see cref="keyDown"/>, <c>evCommand|evBroadcast</c> →
/// <see cref="message"/>.
/// </summary>
public struct TEvent
{
    /// <summary>Event-kind value determining which payload is meaningful; evNothing marks an absent or consumed event.</summary>
    public ushort What;
    /// <summary>Mouse payload used by mouse events, including screen-cell position and button state.</summary>
    public MouseEventType mouse;
    /// <summary>Keyboard payload used by ordinary key press/repeat, key release, and standalone modifier-state events.</summary>
    public KeyDownEvent keyDown;
    /// <summary>Command and optional data used by command and broadcast events.</summary>
    public MessageEvent message;

    /// <summary>
    /// Gets or sets the complete keyboard control-state bit mask carried by a keyboard event.
    /// </summary>
    /// <remarks>
    /// For <c>evKeyDown</c> and <c>evKeyUp</c>, this is the state accompanying the
    /// ordinary key event, including existing lock-state bits where supported. For
    /// <c>evModifierChanged</c>, this is the logical Shift, Ctrl, and Alt state after the transition.
    /// This convenience accessor is for keyboard payloads only; mouse modifiers are in
    /// <see cref="MouseEventType.controlKeyState"/>. The accessor reuses
    /// <see cref="KeyDownEvent.controlKeyState"/> and does not change
    /// the sequential layout of <see cref="TEvent"/>.
    /// </remarks>
    public uint Modifiers
    {
        readonly get => keyDown.controlKeyState;
        set => keyDown.controlKeyState = value;
    }

    /// <summary>
    /// Pulls the next mouse event from <see cref="TEventQueue"/> into this
    /// instance. Mirrors upstream <c>TEvent::getMouseEvent()</c>.
    /// </summary>
    public void GetMouseEvent()
    {
        TEventQueue.GetMouseEvent(ref this);
    }

    /// <summary>
    /// Pulls the next keyboard event from the active driver. Mirrors
    /// upstream <c>TEvent::getKeyEvent()</c>.
    /// </summary>
    public void GetKeyEvent(IDriver driver)
    {
        if (driver != null && driver.ReadKeyEvent(out TEvent ev))
            this = ev;
        else
            What = TSharpVision.Constants.Events.evNothing;
    }

    /// <summary>
    /// Convenience used by <c>TProgram</c>: drains one event of any kind
    /// from the active driver via <see cref="TScreen.GetEvent"/>.
    /// </summary>
    public void GetNextEvent(IDriver driver)
    {
        TScreen.GetEvent(ref this);
    }
}
