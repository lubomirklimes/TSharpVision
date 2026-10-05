using TSharpVision.Constants;

namespace TSharpVision.Drivers.SDL;

/// <summary>Tracks stable SDL logical key identities so ordinary releases can match presses.</summary>
/// <remarks>
/// Everything is keyed by the native SDL keycode of the key that is down, never by the text it
/// produced. A release reports exactly the identity its published press reported: the translated
/// identity, the identity of its ASCII text commit, or none for a Unicode text commit. A key whose
/// press was never published has no release.
/// </remarks>
internal sealed class SdlHeldKeyTracker
{
    private readonly Dictionary<uint, KeyDownEvent> _held = new();
    private readonly List<uint> _order = new();
    // Keys that are down while their press is still left to a text commit.
    private readonly HashSet<uint> _unpublished = new();
    // Right Alt chords held back until it is known whether the layout turns them into text.
    private readonly Dictionary<uint, KeyDownEvent> _deferred = new();
    // Chords that turned out to be ordinary Alt shortcuts; their repeats are shortcuts too.
    private readonly HashSet<uint> _chords = new();
    private uint? _awaitingText;

    /// <summary>Records a native key press; returns true when <paramref name="ev"/> is to be published now.</summary>
    public bool KeyDown(uint keycode, ushort modifierState, out TEvent ev)
    {
        bool translated = SdlKeyTranslator.TryTranslate(keycode, modifierState, '\0', out ev);
        // A key without a translation can only ever be published by its text.
        SdlKeyIntent intent = translated ? SdlTextInput.Classify(keycode, modifierState) : SdlKeyIntent.Text;
        // The pure translator turns Alt plus punctuation into text; only a real shortcut may be deferred.
        if (intent == SdlKeyIntent.TextOrShortcut && ev.keyDown.text.Length != 0) intent = SdlKeyIntent.Text;

        if (intent == SdlKeyIntent.TextOrShortcut)
        {
            // The text commit of a press precedes its repeat, so a repeat that finds the chord
            // still waiting proves the layout produced no text: it is an ordinary Alt shortcut.
            if (_deferred.Remove(keycode)) _chords.Add(keycode);
            if (_chords.Contains(keycode)) intent = SdlKeyIntent.Shortcut;
            else if (!_held.ContainsKey(keycode)) _deferred[keycode] = ev.keyDown;
        }

        if (intent == SdlKeyIntent.Shortcut)
        {
            _awaitingText = null;
            _unpublished.Remove(keycode);
            _deferred.Remove(keycode);
            if (!_held.ContainsKey(keycode))
            {
                _held.Add(keycode, ev.keyDown);
                _order.Add(keycode);
            }
            return true;
        }

        _awaitingText = keycode;
        if (!_held.ContainsKey(keycode)) _unpublished.Add(keycode);
        ev = default;
        return false;
    }

    /// <summary>Makes the awaited key release with the identity its text commit reported as the press.</summary>
    public void TextCommitted(KeyDownEvent text)
    {
        if (_awaitingText is not uint keycode) return;
        _awaitingText = null;
        _unpublished.Remove(keycode);
        _deferred.Remove(keycode);
        // Unicode text reports no identity (keyCode 0); the key that typed it is still a held
        // key, and its release reports that same absence of identity.
        if (!_held.ContainsKey(keycode)) _order.Add(keycode);
        _held[keycode] = text;
    }

    /// <summary>
    /// Releases a key. <paramref name="deferredPress"/> is the press of a Right Alt chord that was held
    /// back for a text commit which never came; it is published first when its <c>What</c> is set.
    /// </summary>
    public bool TryKeyUp(uint keycode, ushort modifierState, out TEvent deferredPress, out TEvent ev)
    {
        deferredPress = default;
        if (_awaitingText == keycode) _awaitingText = null;
        _chords.Remove(keycode);
        uint state = SdlKeyTranslator.ToShiftState(modifierState);
        if (_deferred.Remove(keycode, out KeyDownEvent chord))
        {
            _unpublished.Remove(keycode);
            deferredPress.What = Events.evKeyDown;
            deferredPress.keyDown = chord;
            ev = MakeKeyUp(chord, state);
            return true;
        }
        if (_unpublished.Remove(keycode))
        {
            // No press was published (a dead key, a swallowed commit), so there is nothing to release.
            ev = default;
            return false;
        }
        if (!_held.Remove(keycode, out KeyDownEvent pressed))
        {
            // A key that was down before the driver saw it. Only a key whose press would have
            // carried its translated identity may release with it; a text key's layout keycode
            // is not an identity any press reports.
            if (!SdlKeyTranslator.TryTranslate(keycode, modifierState, '\0', out ev)
                || SdlTextInput.Classify(keycode, modifierState) != SdlKeyIntent.Shortcut)
            {
                ev = default;
                return false;
            }
            pressed = ev.keyDown;
        }
        else
            _order.Remove(keycode);

        ev = MakeKeyUp(pressed, state);
        return true;
    }

    public IReadOnlyList<TEvent> ReleaseAll(uint controlKeyState)
    {
        var releases = new List<TEvent>(_order.Count);
        foreach (uint keycode in _order)
            if (_held.TryGetValue(keycode, out KeyDownEvent pressed))
                releases.Add(MakeKeyUp(pressed, controlKeyState));
        _held.Clear();
        _order.Clear();
        _unpublished.Clear();
        _deferred.Clear();
        _chords.Clear();
        _awaitingText = null;
        return releases;
    }

    private static TEvent MakeKeyUp(KeyDownEvent pressed, uint controlKeyState)
    {
        TEvent ev = default;
        ev.What = Events.evKeyUp;
        ev.keyDown.keyCode = pressed.keyCode;
        ev.keyDown.charScan = pressed.charScan;
        ev.keyDown.controlKeyState = controlKeyState;
        return ev;
    }
}
