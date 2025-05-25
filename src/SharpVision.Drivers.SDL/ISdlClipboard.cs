namespace SharpVision.Drivers.SDL;

/// <summary>
/// Thin abstraction over SDL3 clipboard native calls.
/// Exists so that <see cref="SdlClipboardService"/> can be unit-tested
/// without a real SDL window.
/// </summary>
internal interface ISdlClipboard
{
    bool HasClipboardText();
    // SDL3-CS marshals the native UTF-8 pointer to a managed string automatically.
    string? GetClipboardText();
    bool SetClipboardText(string text);
}

/// <summary>
/// Production implementation that delegates directly to SDL3.
/// </summary>
internal sealed class RealSdlClipboard : ISdlClipboard
{
    public static readonly RealSdlClipboard Instance = new();

    private RealSdlClipboard() { }

    public bool HasClipboardText() => SDL3.SDL.HasClipboardText();

    public string? GetClipboardText() => SDL3.SDL.GetClipboardText();

    public bool SetClipboardText(string text) => SDL3.SDL.SetClipboardText(text);
}
