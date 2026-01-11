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
/// Mirrors upstream <c>struct MouseEventType</c> (event.h:15). Reports the
/// mouse button bitmask, double-click flag and the cell coordinates of the
/// pointer.
/// </summary>
public struct MouseEventType
{
    public byte buttons;       // uchar buttons
    public bool doubleClick;   // Boolean doubleClick
    public TPoint where;       // TPoint where
}

/// <summary>
/// Mirrors upstream <c>struct CharScanType</c> (event.h:185): two single
/// bytes packed into a 16-bit slot. Upstream code reads the union as either
/// two bytes or one ushort; the helper conversions below provide that.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CharScanType
{
    public byte charCode;
    public byte scanCode;

    public CharScanType(byte ch, byte sc) { charCode = ch; scanCode = sc; }
    public CharScanType(ushort packed)
    {
        charCode = (byte)(packed & 0xFF);
        scanCode = (byte)((packed >> 8) & 0xFF);
    }
    public ushort ToUShort() => (ushort)(charCode | (scanCode << 8));
}

/// <summary>
/// Mirrors upstream <c>struct KeyDownEvent</c> (event.h:190).
/// </summary>
public struct KeyDownEvent
{
    public CharScanType charScan;
    public ushort keyCode;
    public ushort shiftState;
    public byte raw_scanCode;
    public string text;
}

/// <summary>
/// Mirrors upstream <c>struct MessageEvent</c> (event.h:198). The C union
/// (<c>infoPtr</c>/<c>infoLong</c>/<c>infoWord</c>/<c>infoInt</c>/
/// <c>infoByte</c>/<c>infoChar</c>) is preserved as parallel fields; only
/// one is meaningful per event in practice.
/// </summary>
public struct MessageEvent
{
    public ushort command;
    public IInfo? infoPtr;
    public long infoLong;
    public ushort infoWord;
    public short infoInt;
    public byte infoByte;
    public char infoChar;
}

/// <summary>
/// Mirrors upstream <c>struct TEvent</c> (event.h:212). Carries one of the
/// three payload variants based on <see cref="What"/>:
/// <c>evMouse*</c> → <see cref="mouse"/>, <c>evKeyDown</c> →
/// <see cref="keyDown"/>, <c>evCommand|evBroadcast</c> →
/// <see cref="message"/>.
/// </summary>
public struct TEvent
{
    public ushort What;
    public MouseEventType mouse;
    public KeyDownEvent keyDown;
    public MessageEvent message;

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
