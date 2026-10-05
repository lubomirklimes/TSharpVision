using TSharpVision.Diagnostics.Keyboard.Profiles;

namespace TSharpVision.Diagnostics.Keyboard.Sources;

/// <summary>What the OS source may never type, whatever a suite says.</summary>
public static class InputSafety
{
    /// <summary>Upper bound on injected key transitions in one run.</summary>
    public const int MaxEvents = 4000;

    /// <summary>
    /// Why a stroke sequence must not be injected, or null. Injection is expressible only as profile
    /// steps, and a step containing a system chord is refused even if a suite were to define one.
    /// </summary>
    public static string? Denied(IReadOnlyList<KeyStroke> strokes)
    {
        var held = new HashSet<PhysicalKey>();
        foreach (KeyStroke stroke in strokes)
        {
            if (!stroke.Down)
            {
                held.Remove(stroke.Key);
                continue;
            }

            bool alt = held.Contains(PhysicalKey.AltLeft) || held.Contains(PhysicalKey.AltRight);
            bool control = held.Contains(PhysicalKey.ControlLeft);
            string? chord = stroke.Key switch
            {
                PhysicalKey.F4 when alt => "Alt+F4 closes the foreground window",
                PhysicalKey.Tab when alt => "Alt+Tab switches windows",
                PhysicalKey.Escape when alt => "Alt+Esc switches windows",
                PhysicalKey.Escape when control => "Ctrl+Esc opens the Start menu",
                PhysicalKey.Space when alt => "Alt+Space opens the window menu",
                PhysicalKey.Enter or PhysicalKey.NumpadEnter when alt => "Alt+Enter toggles full screen",
                PhysicalKey.Delete when alt && control => "Ctrl+Alt+Del is the secure attention sequence",
                _ => null,
            };
            if (chord != null) return chord;
            held.Add(stroke.Key);
        }
        return held.Count == 0 ? null : $"{string.Join(", ", held)} would be left held";
    }
}

/// <summary>Restores a lock key to the state it had before a step pressed it.</summary>
public sealed class LockRestorer(Func<bool> isOn, Action toggle)
{
    private readonly bool _initial = isOn();

    /// <summary>Toggles the lock back when it is not in its initial state; returns whether it had to.</summary>
    public bool Restore()
    {
        if (isOn() == _initial) return false;
        toggle();
        return true;
    }
}

/// <summary>
/// Plays physical key transitions into the foreground window, and only while that window is the
/// target. The foreground is checked before every single transition; on a mismatch everything this
/// injector pressed is released at once and the run is aborted.
/// </summary>
public sealed class PhysicalInjector
{
    private readonly Func<long> _foreground;
    private readonly Action<IReadOnlyList<KeyStroke>> _send;
    private readonly long _target;
    private readonly int _maxEvents;
    private readonly List<PhysicalKey> _pressed = new();

    /// <param name="foreground">Reads the current foreground window.</param>
    /// <param name="send">Injects transitions, atomically when given more than one.</param>
    public PhysicalInjector(Func<long> foreground, Action<IReadOnlyList<KeyStroke>> send, long target,
        int maxEvents = InputSafety.MaxEvents)
    {
        _foreground = foreground;
        _send = send;
        _target = target;
        _maxEvents = maxEvents;
    }

    public int Sent { get; private set; }
    public IReadOnlyList<PhysicalKey> Pressed => _pressed;

    /// <param name="batch">Inject all transitions in one call, after one foreground check.</param>
    /// <param name="pause">Called between transitions so the host can process each one.</param>
    public void Play(IReadOnlyList<KeyStroke> strokes, bool batch = false, Action? pause = null)
    {
        if (InputSafety.Denied(strokes) is { } denied) throw new InputAbortedException($"refused: {denied}");
        if (batch)
        {
            Guard(strokes.Count);
            Track(strokes);
            _send(strokes);
            return;
        }
        foreach (KeyStroke stroke in strokes)
        {
            Guard(1);
            Track([stroke]);
            _send([stroke]);
            pause?.Invoke();
        }
    }

    private void Guard(int count)
    {
        if (Sent + count > _maxEvents)
        {
            ReleaseAll();
            throw new InputAbortedException($"the limit of {_maxEvents} injected key events was reached");
        }
        if (_foreground() == _target) return;
        ReleaseAll();
        throw new InputAbortedException("the target window lost the foreground; injection stopped and all keys were released");
    }

    private void Track(IReadOnlyList<KeyStroke> strokes)
    {
        foreach (KeyStroke stroke in strokes)
        {
            if (stroke.Down) { if (!_pressed.Contains(stroke.Key)) _pressed.Add(stroke.Key); }
            else _pressed.Remove(stroke.Key);
        }
        Sent += strokes.Count;
    }

    /// <summary>Releases every key this injector still holds, most recent first. Safe to call at any time.</summary>
    public void ReleaseAll()
    {
        if (_pressed.Count == 0) return;
        KeyStroke[] releases = _pressed.AsEnumerable().Reverse().Select(k => new KeyStroke(k, false)).ToArray();
        _pressed.Clear();
        _send(releases);
    }
}
