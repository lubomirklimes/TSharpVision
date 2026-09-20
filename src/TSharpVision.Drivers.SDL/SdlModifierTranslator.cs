using TSharpVision.Constants;
using TSharpVision.Drivers.Internal;

namespace TSharpVision.Drivers.SDL;

/// <summary>Tracks SDL's physical modifier keys and emits logical state transitions.</summary>
internal sealed class SdlModifierTranslator
{
    private readonly ModifierStateTracker _modifiers = new();

    public uint LogicalState => _modifiers.LogicalState;

    public static bool IsModifierKey(uint keycode)
        => keycode is SdlKeyTranslator.SDLK_LSHIFT or SdlKeyTranslator.SDLK_RSHIFT
            or SdlKeyTranslator.SDLK_LCTRL or SdlKeyTranslator.SDLK_RCTRL
            or SdlKeyTranslator.SDLK_LALT or SdlKeyTranslator.SDLK_RALT;

    public bool TryTranslate(uint keycode, bool down, out TEvent ev)
    {
        ev = default;
        if (!TryGetPhysicalModifier(keycode, out PhysicalModifier modifier)
            || !_modifiers.Set(modifier, down, out uint state))
            return false;

        ev.What = Events.evModifierChanged;
        ev.Modifiers = state;
        return true;
    }

    public bool TryReset(out TEvent ev)
    {
        ev = default;
        if (!_modifiers.Reset(out uint state)) return false;
        ev.What = Events.evModifierChanged;
        ev.Modifiers = state;
        return true;
    }

    private static bool TryGetPhysicalModifier(uint keycode, out PhysicalModifier modifier)
    {
        modifier = keycode switch
        {
            SdlKeyTranslator.SDLK_LSHIFT => PhysicalModifier.LeftShift,
            SdlKeyTranslator.SDLK_RSHIFT => PhysicalModifier.RightShift,
            SdlKeyTranslator.SDLK_LCTRL => PhysicalModifier.LeftCtrl,
            SdlKeyTranslator.SDLK_RCTRL => PhysicalModifier.RightCtrl,
            SdlKeyTranslator.SDLK_LALT => PhysicalModifier.LeftAlt,
            SdlKeyTranslator.SDLK_RALT => PhysicalModifier.RightAlt,
            _ => default,
        };
        return IsModifierKey(keycode);
    }
}
