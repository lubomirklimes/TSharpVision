using TSharpVision.Constants;

namespace TSharpVision.Drivers.SDL;

// SDL text commits have no associated physical key identity. Only ASCII text
// can supply a legacy identity; Unicode remains exclusively in the text field.
internal static class SdlTextInput
{
    internal static TEvent CreateEvent(string text, uint modifiers)
    {
        TEvent ev = default;
        ev.What = Events.evKeyDown;
        if (text.Length > 0 && text[0] <= 0x7F)
        {
            ev.keyDown.keyCode = text[0];
            ev.keyDown.charScan.charCode = (byte)text[0];
        }
        ev.keyDown.text = text;
        ev.keyDown.controlKeyState = modifiers;
        return ev;
    }

    /// <summary>How a native key press is published, decided from its keycode and modifiers alone.</summary>
    internal static SdlKeyIntent Classify(uint keycode, ushort modifierState)
    {
        if (keycode < 0x20 || keycode > 0x7E) return SdlKeyIntent.Shortcut;
        bool leftAlt = (modifierState & SdlKeyTranslator.SDL_KMOD_LALT) != 0;
        bool rightAlt = (modifierState & SdlKeyTranslator.SDL_KMOD_RALT) != 0;
        bool control = (modifierState & SdlKeyTranslator.SDL_KMOD_CTRL) != 0;
        if (leftAlt) return SdlKeyIntent.Shortcut;
        // Right Alt is AltGr on layouts that have one and an ordinary Alt elsewhere. Windows adds a
        // synthetic Left Ctrl for AltGr, which SDL may or may not have removed. Neither form says
        // which it is; only whether the layout commits text does.
        if (rightAlt && (modifierState & SdlKeyTranslator.SDL_KMOD_RCTRL) == 0) return SdlKeyIntent.TextOrShortcut;
        return control || rightAlt ? SdlKeyIntent.Shortcut : SdlKeyIntent.Text;
    }
}

internal enum SdlKeyIntent
{
    /// <summary>Published at once with its translated identity.</summary>
    Shortcut,
    /// <summary>Left to the text commit that follows.</summary>
    Text,
    /// <summary>Left to the text commit; an Alt shortcut if none has come by the repeat or release.</summary>
    TextOrShortcut,
}
