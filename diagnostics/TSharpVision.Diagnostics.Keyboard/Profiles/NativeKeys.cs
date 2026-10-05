namespace TSharpVision.Diagnostics.Keyboard.Profiles;

/// <summary>Native identities of key positions, for building injected events. Pure data: no platform calls.</summary>
public static class NativeKeys
{
    /// <summary>Marks an SDL keycode that is derived from a scancode rather than from a character.</summary>
    public const uint SdlScancodeMask = 0x40000000;

    /// <summary>
    /// The Win32 virtual key of a position. Both built-in layouts keep the US assignment for the text
    /// positions (verified for Czech QWERTY with MapVirtualKeyEx).
    /// </summary>
    public static ushort VirtualKey(PhysicalKey key) => key switch
    {
        >= PhysicalKey.Digit1 and <= PhysicalKey.Digit9 => (ushort)('1' + (key - PhysicalKey.Digit1)),
        PhysicalKey.Digit0 => '0',
        >= PhysicalKey.KeyA and <= PhysicalKey.KeyZ => (ushort)('A' + (key - PhysicalKey.KeyA)),
        PhysicalKey.Backquote => 0xC0, PhysicalKey.Minus => 0xBD, PhysicalKey.Equal => 0xBB,
        PhysicalKey.BracketLeft => 0xDB, PhysicalKey.BracketRight => 0xDD, PhysicalKey.Backslash => 0xDC,
        PhysicalKey.Semicolon => 0xBA, PhysicalKey.Quote => 0xDE, PhysicalKey.Comma => 0xBC,
        PhysicalKey.Period => 0xBE, PhysicalKey.Slash => 0xBF, PhysicalKey.IntlBackslash => 0xE2,
        PhysicalKey.Space => 0x20,
        PhysicalKey.Escape => 0x1B, PhysicalKey.Tab => 0x09, PhysicalKey.Backspace => 0x08, PhysicalKey.Enter => 0x0D,
        >= PhysicalKey.F1 and <= PhysicalKey.F12 => (ushort)(0x70 + (key - PhysicalKey.F1)),
        PhysicalKey.Insert => 0x2D, PhysicalKey.Delete => 0x2E, PhysicalKey.Home => 0x24, PhysicalKey.End => 0x23,
        PhysicalKey.PageUp => 0x21, PhysicalKey.PageDown => 0x22, PhysicalKey.ArrowUp => 0x26,
        PhysicalKey.ArrowDown => 0x28, PhysicalKey.ArrowLeft => 0x25, PhysicalKey.ArrowRight => 0x27,
        >= PhysicalKey.Numpad0 and <= PhysicalKey.Numpad9 => (ushort)(0x60 + (key - PhysicalKey.Numpad0)),
        PhysicalKey.NumpadDecimal => 0x6E, PhysicalKey.NumpadDivide => 0x6F, PhysicalKey.NumpadMultiply => 0x6A,
        PhysicalKey.NumpadSubtract => 0x6D, PhysicalKey.NumpadAdd => 0x6B, PhysicalKey.NumpadEnter => 0x0D,
        PhysicalKey.NumLock => 0x90,
        PhysicalKey.ShiftLeft => 0x10, PhysicalKey.ControlLeft => 0x11,
        PhysicalKey.AltLeft or PhysicalKey.AltRight => 0x12,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    /// <summary>The USB HID usage of a position, which is also its SDL scancode.</summary>
    public static ushort SdlScancode(PhysicalKey key) => key switch
    {
        >= PhysicalKey.KeyA and <= PhysicalKey.KeyZ => (ushort)(4 + (key - PhysicalKey.KeyA)),
        >= PhysicalKey.Digit1 and <= PhysicalKey.Digit9 => (ushort)(30 + (key - PhysicalKey.Digit1)),
        PhysicalKey.Digit0 => 39,
        PhysicalKey.Enter => 40, PhysicalKey.Escape => 41, PhysicalKey.Backspace => 42, PhysicalKey.Tab => 43,
        PhysicalKey.Space => 44, PhysicalKey.Minus => 45, PhysicalKey.Equal => 46, PhysicalKey.BracketLeft => 47,
        PhysicalKey.BracketRight => 48, PhysicalKey.Backslash => 49, PhysicalKey.Semicolon => 51,
        PhysicalKey.Quote => 52, PhysicalKey.Backquote => 53, PhysicalKey.Comma => 54, PhysicalKey.Period => 55,
        PhysicalKey.Slash => 56,
        >= PhysicalKey.F1 and <= PhysicalKey.F12 => (ushort)(58 + (key - PhysicalKey.F1)),
        PhysicalKey.Insert => 73, PhysicalKey.Home => 74, PhysicalKey.PageUp => 75, PhysicalKey.Delete => 76,
        PhysicalKey.End => 77, PhysicalKey.PageDown => 78, PhysicalKey.ArrowRight => 79, PhysicalKey.ArrowLeft => 80,
        PhysicalKey.ArrowDown => 81, PhysicalKey.ArrowUp => 82, PhysicalKey.NumLock => 83,
        PhysicalKey.NumpadDivide => 84, PhysicalKey.NumpadMultiply => 85, PhysicalKey.NumpadSubtract => 86,
        PhysicalKey.NumpadAdd => 87, PhysicalKey.NumpadEnter => 88,
        >= PhysicalKey.Numpad1 and <= PhysicalKey.Numpad9 => (ushort)(89 + (key - PhysicalKey.Numpad1)),
        PhysicalKey.Numpad0 => 98, PhysicalKey.NumpadDecimal => 99, PhysicalKey.IntlBackslash => 100,
        PhysicalKey.ControlLeft => 224, PhysicalKey.ShiftLeft => 225, PhysicalKey.AltLeft => 226,
        PhysicalKey.AltRight => 230,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    /// <summary>
    /// The SDL keycode SDL reports for a position under a layout: the character codes of the control
    /// keys, a scancode-derived code for other named keys, and for text keys the unshifted character,
    /// except that the number row reports its digits whenever the layout puts them on Shift (SDL's
    /// default "french_numbers" keycode option), which also holds trivially for a US row.
    /// </summary>
    public static uint SdlKeycode(LayoutProfile profile, PhysicalKey key) => key switch
    {
        PhysicalKey.Escape => 0x1B, PhysicalKey.Tab => 0x09, PhysicalKey.Backspace => 0x08,
        PhysicalKey.Enter => 0x0D, PhysicalKey.Delete => 0x7F,
        >= PhysicalKey.Digit1 and <= PhysicalKey.Digit9 => (uint)('1' + (key - PhysicalKey.Digit1)),
        PhysicalKey.Digit0 => '0',
        _ when profile.Keys.TryGetValue(key, out KeyLevels? levels) && levels.Plain is { Length: > 0 } plain => plain[0],
        _ => SdlScancodeMask | SdlScancode(key),
    };
}
