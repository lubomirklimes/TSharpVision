using TSharpVision.Constants;

namespace TSharpVision.Drivers.SDL;

/// <summary>Tracks stable SDL logical key identities so ordinary releases can match presses.</summary>
internal sealed class SdlHeldKeyTracker
{
    private readonly Dictionary<uint, KeyDownEvent> _held = new();
    private readonly List<uint> _order = new();

    public void KeyDown(uint keycode, ushort modifierState)
    {
        if (_held.ContainsKey(keycode)
            || !SdlKeyTranslator.TryTranslate(keycode, modifierState, '\0', out TEvent ev))
            return;
        _held.Add(keycode, ev.keyDown);
        _order.Add(keycode);
    }

    public bool TryKeyUp(uint keycode, ushort modifierState, out TEvent ev)
    {
        if (!_held.Remove(keycode, out KeyDownEvent pressed))
        {
            if (!SdlKeyTranslator.TryTranslate(keycode, modifierState, '\0', out ev)) return false;
            pressed = ev.keyDown;
        }
        else
            _order.Remove(keycode);

        ev = MakeKeyUp(pressed, SdlKeyTranslator.ToShiftState(modifierState));
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
