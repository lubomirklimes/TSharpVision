using TSharpVision.Diagnostics.Keyboard.Profiles;
using TSharpVision.Diagnostics.Keyboard.Protocol;

namespace TSharpVision.Diagnostics.Keyboard.Sources;

/// <summary>
/// Turns a step into the native events its host would have queued for it, for Level-B injection.
/// The layout's work is done here, from the profile: these events bypass the real layout entirely.
/// </summary>
public static class NativeEvents
{
    // dwControlKeyState bits of a KEY_EVENT_RECORD.
    public const uint RightAltPressed = 0x0001, LeftAltPressed = 0x0002, LeftCtrlPressed = 0x0008,
        ShiftPressed = 0x0010, NumLockOn = 0x0020, CapsLockOn = 0x0080, EnhancedKey = 0x0100;

    // SDL_Keymod bits.
    public const uint SdlLeftShift = 0x0001, SdlLeftCtrl = 0x0040, SdlLeftAlt = 0x0100, SdlRightAlt = 0x0200,
        SdlNum = 0x1000, SdlCaps = 0x2000;

    /// <summary>KEY_EVENT_RECORDs as a console host produces them, with the layout's character already in them.</summary>
    public static IReadOnlyList<NativeEvent> ForConsole(Step step, LayoutProfile profile)
    {
        var events = new List<NativeEvent>();
        uint locks = (step.Locks.HasFlag(LockState.Num) ? NumLockOn : 0) | (step.Locks.HasFlag(LockState.Caps) ? CapsLockOn : 0);
        uint modifiers = 0;
        bool altGrLayout = profile.Keys.Values.Any(k => k.AltGr != null);
        KeyStroke lastDown = step.Strokes.LastOrDefault(s => s.Down && !IsModifier(s.Key));

        void Record(bool down, PhysicalKey key, ushort character, bool repeat = false)
        {
            (byte scan, bool extended) = PhysicalKeys.ScanCode(key);
            events.Add(new NativeEvent(NativeEvent.KeyKind, down, NativeKeys.VirtualKey(key), scan, character,
                modifiers | locks | (extended ? EnhancedKey : 0), repeat));
        }

        foreach (KeyStroke stroke in step.Strokes)
        {
            uint bit = stroke.Key switch
            {
                PhysicalKey.ShiftLeft => ShiftPressed,
                PhysicalKey.ControlLeft => LeftCtrlPressed,
                PhysicalKey.AltLeft => LeftAltPressed,
                PhysicalKey.AltRight => RightAltPressed,
                _ => 0,
            };
            if (bit != 0)
            {
                // On a layout with an AltGr level Windows brackets Right Alt with a synthetic Left Ctrl.
                bool altGr = stroke.Key == PhysicalKey.AltRight && altGrLayout;
                if (stroke.Down)
                {
                    if (altGr)
                    {
                        modifiers |= LeftCtrlPressed;
                        Record(true, PhysicalKey.ControlLeft, 0);
                    }
                    modifiers |= bit;
                    Record(true, stroke.Key, 0);
                }
                else
                {
                    // Observed under conhost: the synthetic Left Ctrl goes up before Right Alt.
                    if (altGr)
                    {
                        modifiers &= ~LeftCtrlPressed;
                        Record(false, PhysicalKey.ControlLeft, 0);
                    }
                    modifiers &= ~bit;
                    Record(false, stroke.Key, 0);
                }
                continue;
            }

            KeyLevel level = (modifiers & ShiftPressed) != 0 ? KeyLevel.Shift : KeyLevel.Plain;
            if (profile.IsDead(stroke.Key, level))
            {
                // Observed under conhost: a dead key's press has no character, and it is released
                // twice with the accent as the character, first by a record without a scan code.
                ushort accent = (level == KeyLevel.Shift ? profile.Keys[stroke.Key].Shift : profile.Keys[stroke.Key].Plain)![0];
                if (stroke.Down) Record(true, stroke.Key, 0);
                else
                {
                    events.Add(new NativeEvent(NativeEvent.KeyKind, false, NativeKeys.VirtualKey(stroke.Key), 0, accent,
                        modifiers | locks));
                    Record(false, stroke.Key, accent);
                }
                continue;
            }

            ushort character = ConsoleCharacter(stroke, step, profile, modifiers, level);
            Record(stroke.Down, stroke.Key, character);
            if (stroke.Down && stroke == lastDown)
                for (int i = 0; i < step.Repeats; i++) Record(true, stroke.Key, character, repeat: true);
        }
        return events;
    }

    // UnicodeChar of a record: the committed text, a control character for the keys that have one,
    // and the Ctrl+letter code. A release carries the character of its press, except after a dead
    // key: the press then carries the composed character and the release the key's own (observed).
    private static ushort ConsoleCharacter(KeyStroke stroke, Step step, LayoutProfile profile, uint modifiers, KeyLevel level)
    {
        string? text = stroke.Text ?? step.Strokes.FirstOrDefault(s => s.Down && s.Key == stroke.Key).Text;
        string? own = profile.Text(stroke.Key, level);
        if (!stroke.Down && text != null && own is { Length: 1 } && text != own && step.Locks == LockState.None
            && (modifiers & (LeftCtrlPressed | LeftAltPressed | RightAltPressed)) == 0)
            return own[0];
        if (text is { Length: 1 }) return text[0];
        bool control = (modifiers & LeftCtrlPressed) != 0, alt = (modifiers & (LeftAltPressed | RightAltPressed)) != 0;
        if (stroke.Key is >= PhysicalKey.KeyA and <= PhysicalKey.KeyZ)
        {
            if (control && !alt) return (ushort)(stroke.Key - PhysicalKey.KeyA + 1);
            // Alt+letter still reports the letter; AltGr on a key without an AltGr character reports nothing.
            if (alt && !control) return profile.Text(stroke.Key, KeyLevel.Plain)![0];
        }
        return stroke.Key switch
        {
            PhysicalKey.Enter or PhysicalKey.NumpadEnter => '\r',
            PhysicalKey.Tab => '\t',
            PhysicalKey.Backspace => '\b',
            PhysicalKey.Escape => 0x1B,
            _ => 0,
        };
    }

    /// <summary>The SDL event group of each key: KEY_DOWN, the TEXT_INPUT commit where the layout types, KEY_UP.</summary>
    public static IReadOnlyList<NativeEvent> ForSdl(Step step, LayoutProfile profile)
    {
        if (step.SdlScript is { } script) return script;

        var events = new List<NativeEvent>();
        uint modifiers = (step.Locks.HasFlag(LockState.Num) ? SdlNum : 0) | (step.Locks.HasFlag(LockState.Caps) ? SdlCaps : 0);
        KeyStroke lastDown = step.Strokes.LastOrDefault(s => s.Down && !IsModifier(s.Key));

        foreach (KeyStroke stroke in step.Strokes)
        {
            uint keycode = NativeKeys.SdlKeycode(profile, stroke.Key);
            ushort scancode = NativeKeys.SdlScancode(stroke.Key);
            uint bit = stroke.Key switch
            {
                PhysicalKey.ShiftLeft => SdlLeftShift,
                PhysicalKey.ControlLeft => SdlLeftCtrl,
                PhysicalKey.AltLeft => SdlLeftAlt,
                // Observed with SendInput on Windows: SDL removes the synthetic Left Ctrl of AltGr.
                PhysicalKey.AltRight => SdlRightAlt,
                _ => 0,
            };
            if (bit != 0)
            {
                // SDL reports the modifier state after the key's own transition.
                modifiers = stroke.Down ? modifiers | bit : modifiers & ~bit;
                events.Add(new NativeEvent(NativeEvent.KeyKind, stroke.Down, keycode, scancode, State: modifiers));
                continue;
            }

            events.Add(new NativeEvent(NativeEvent.KeyKind, stroke.Down, keycode, scancode, State: modifiers));
            if (!stroke.Down) continue;
            if (!string.IsNullOrEmpty(stroke.Text)) events.Add(new NativeEvent(NativeEvent.TextKind, Text: stroke.Text));
            if (stroke != lastDown) continue;
            for (int i = 0; i < step.Repeats; i++)
            {
                events.Add(new NativeEvent(NativeEvent.KeyKind, true, keycode, scancode, State: modifiers, Repeat: true));
                if (!string.IsNullOrEmpty(stroke.Text)) events.Add(new NativeEvent(NativeEvent.TextKind, Text: stroke.Text));
            }
        }
        return events;
    }

    public static bool IsModifier(PhysicalKey key) =>
        key is PhysicalKey.ShiftLeft or PhysicalKey.ControlLeft or PhysicalKey.AltLeft or PhysicalKey.AltRight;
}
