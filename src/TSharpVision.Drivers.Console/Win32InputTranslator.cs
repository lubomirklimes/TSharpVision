using TSharpVision.Constants;
using TSharpVision.Drivers.Internal;

namespace TSharpVision.Drivers.Console;

/// <summary>Stateful translation for Win32 key records, including modifier-only transitions.</summary>
internal sealed class Win32InputTranslator
{
    private readonly ModifierStateTracker _modifiers = new();
    private readonly Dictionary<(ushort VirtualKey, ushort ScanCode), KeyDownEvent> _heldKeys = new();

    public bool TryTranslate(
        bool keyDown, ushort virtualKey, ushort scanCode, char character,
        uint controlKeyState, out TEvent ev)
    {
        if (TryGetPhysicalModifier(virtualKey, scanCode, controlKeyState, out PhysicalModifier modifier))
        {
            ev = default;
            if (!_modifiers.Set(modifier, keyDown, out uint state)) return false;
            ev.What = Events.evModifierChanged;
            ev.Modifiers = state;
            return true;
        }

        (ushort VirtualKey, ushort ScanCode) identity = (virtualKey, scanCode);
        if (keyDown)
        {
            if (!Win32KeyTranslator.TryTranslate(
                    true, virtualKey, character, controlKeyState, out ev))
                return false;
            _heldKeys.TryAdd(identity, ev.keyDown);
            return true;
        }

        if (_heldKeys.Remove(identity, out KeyDownEvent pressed))
        {
            ev = default;
            ev.What = Events.evKeyUp;
            ev.keyDown.keyCode = pressed.keyCode;
            ev.keyDown.charScan = pressed.charScan;
            ev.keyDown.controlKeyState = Win32KeyTranslator.ToControlKeyState(controlKeyState);
            return true;
        }

        // A driver may attach after the key was pressed. Navigation and other stable
        // identities can still be translated from the release record itself.
        if (Win32KeyTranslator.TryTranslate(
                true, virtualKey, character, controlKeyState, out ev))
        {
            ev.What = Events.evKeyUp;
            return true;
        }

        ev = default;
        return false;
    }

    private static bool TryGetPhysicalModifier(
        ushort virtualKey, ushort scanCode, uint controlKeyState, out PhysicalModifier modifier)
    {
        modifier = default;
        switch (virtualKey)
        {
            case 0xA0: modifier = PhysicalModifier.LeftShift; return true;
            case 0xA1: modifier = PhysicalModifier.RightShift; return true;
            case 0xA2: modifier = PhysicalModifier.LeftCtrl; return true;
            case 0xA3: modifier = PhysicalModifier.RightCtrl; return true;
            case 0xA4: modifier = PhysicalModifier.LeftAlt; return true;
            case 0xA5: modifier = PhysicalModifier.RightAlt; return true;
            case 0x10:
                modifier = scanCode == 0x36
                    ? PhysicalModifier.RightShift : PhysicalModifier.LeftShift;
                return true;
            case 0x11:
                modifier = (controlKeyState & Win32KeyTranslator.ENHANCED_KEY) != 0
                    ? PhysicalModifier.RightCtrl : PhysicalModifier.LeftCtrl;
                return true;
            case 0x12:
                modifier = (controlKeyState & Win32KeyTranslator.ENHANCED_KEY) != 0
                    ? PhysicalModifier.RightAlt : PhysicalModifier.LeftAlt;
                return true;
            default:
                return false;
        }
    }
}
