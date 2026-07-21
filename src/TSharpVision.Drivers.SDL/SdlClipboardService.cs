namespace TSharpVision.Drivers.SDL;

/// <summary>
/// <see cref="IClipboardService"/> implementation backed by SDL3 clipboard APIs.
/// <para>
/// Clipboard access is a main-thread operation in SDL, so all calls are expected to originate
/// from the SDL event loop thread.
/// </para>
/// </summary>
public sealed class SdlClipboardService : IClipboardService
{
    private readonly ISdlClipboard _sdl;

    /// <summary>Initialises the service using the real SDL3 clipboard calls.</summary>
    public SdlClipboardService() : this(RealSdlClipboard.Instance) { }

    internal SdlClipboardService(ISdlClipboard sdl) => _sdl = sdl;

    public bool IsAvailable => true;

    public string? GetText()
    {
        try
        {
            if (!_sdl.HasClipboardText()) return null;

            string? text = _sdl.GetClipboardText();
            if (string.IsNullOrEmpty(text)) return null;

            // Normalise to LF (SDL text is already LF on most platforms,
            // but be defensive in case a Windows SDL build returns CRLF).
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }
        catch
        {
            return null;
        }
    }

    public bool TryGetText(out string text)
    {
        text = string.Empty;
        string? s = GetText();
        if (s == null) return false;
        text = s;
        return true;
    }

    public bool SetText(string text)
    {
        try
        {
            return _sdl.SetClipboardText(text ?? string.Empty);
        }
        catch
        {
            return false;
        }
    }
}
