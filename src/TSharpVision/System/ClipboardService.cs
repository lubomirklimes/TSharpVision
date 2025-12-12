namespace TSharpVision;

// ClipboardService is a static facade that holds the active
// IClipboardService used by the editor and other consumers. Hosts opt in
// to a real OS implementation at startup:
//
//   ClipboardService.Current = new Win32ClipboardService();
//

/// <summary>
/// Static provider for the active <see cref="IClipboardService"/>.
/// </summary>
public static class ClipboardService
{
    private static IClipboardService _current = new NullClipboardService();

    /// <summary>
    /// Currently active clipboard service. Assigning <c>null</c> resets to a
    /// fresh <see cref="NullClipboardService"/> rather than throwing.
    /// </summary>
    public static IClipboardService Current
    {
        get => _current;
        set => _current = value ?? new NullClipboardService();
    }

    /// <summary>
    /// Restores the default <see cref="NullClipboardService"/>. Smoke tests
    /// should call this between clipboard blocks to avoid leaking state.
    /// </summary>
    public static void Reset() => _current = new NullClipboardService();
}
