using System.Runtime.InteropServices;
using SDL3;
using TSharpVision.Diagnostics.Keyboard.Protocol;

namespace TSharpVision.Diagnostics.Keyboard.Target;

/// <summary>
/// Level B for both SDL backends: real SDL event structs pushed onto the queue with SDL_PushEvent. The
/// driver's own SDL_PollEvent loop then reads them, so its event decoding, UTF-8 marshalling, modifier
/// carry-over and held-key tracking all run for real. The text is given, so no layout is involved.
/// </summary>
internal sealed class SdlEventQueueInjector : ITargetInjector
{
    // SDL does not copy the text of a pushed TEXT_INPUT event. Each string stays allocated until the
    // step that pushed it has been reported, by which time the driver has long consumed the event.
    private readonly List<IntPtr> _texts = new();

    public void Inject(IReadOnlyList<NativeEvent> events)
    {
        uint window = SDL.GetWindowID(SDL.GetKeyboardFocus());
        if (window == 0)
        {
            IntPtr[]? windows = SDL.GetWindows(out int count);
            if (windows != null && count > 0) window = SDL.GetWindowID(windows[0]);
        }

        foreach (NativeEvent native in events)
        {
            SDL.Event ev = default;
            ulong now = SDL.GetTicksNS();
            switch (native.Kind)
            {
                case NativeEvent.KeyKind:
                    ev.Key.Type = native.Down ? SDL.EventType.KeyDown : SDL.EventType.KeyUp;
                    ev.Key.Timestamp = now;
                    ev.Key.WindowID = window;
                    ev.Key.Scancode = (SDL.Scancode)native.Scan;
                    ev.Key.Key = (SDL.Keycode)native.Key;
                    ev.Key.Mod = (SDL.Keymod)native.State;
                    ev.Key.Down = native.Down;
                    ev.Key.Repeat = native.Repeat;
                    break;
                case NativeEvent.TextKind:
                    IntPtr text = Marshal.StringToCoTaskMemUTF8(native.Text);
                    _texts.Add(text);
                    ev.Text.Type = SDL.EventType.TextInput;
                    ev.Text.Timestamp = now;
                    ev.Text.WindowID = window;
                    ev.Text.Text = text;
                    break;
                case NativeEvent.FocusLostKind:
                    ev.Window.Type = SDL.EventType.WindowFocusLost;
                    ev.Window.Timestamp = now;
                    ev.Window.WindowID = window;
                    break;
                default:
                    continue;
            }
            if (!SDL.PushEvent(ref ev)) throw new InvalidOperationException($"SDL_PushEvent failed: {SDL.GetError()}");
        }
    }

    public void Release()
    {
        foreach (IntPtr text in _texts) Marshal.FreeCoTaskMem(text);
        _texts.Clear();
    }

    public void Dispose() => Release();
}
