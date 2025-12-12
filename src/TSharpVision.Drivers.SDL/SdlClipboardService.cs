namespace TSharpVision.Drivers.SDL;

// SDL clipboard bridge for the SDL driver.
//
// SDL3-CS marshals SDL_GetClipboardText's UTF-8 native pointer to a managed
// string automatically (including calling SDL_free). This is a main-thread
// operation; all clipboard calls are expected to originate from the SDL/UI
// event loop thread (same constraint as SDL_GetClipboardText documentation).
//
// Newlines: SDL clipboard text uses LF on most platforms. IClipboardService
// GetText() normalises to LF to be defensive against any CRLF that a Windows
// SDL build might return.

/// <summary>
/// <see cref="IClipboardService"/> implementation backed by SDL3 clipboard APIs.
/// Provides cross-platform clipboard support for the SDL driver.
/// </summary>
public sealed class SdlClipboardService : IClipboardService
{
    // ISdlClipboard is injected to allow mocking in unit tests.
    private readonly ISdlClipboard _sdl;

    /// <summary>Initialises the service using the real SDL3 clipboard calls.</summary>
    public SdlClipboardService() : this(RealSdlClipboard.Instance) { }

    internal SdlClipboardService(ISdlClipboard sdl) => _sdl = sdl;

    // SDL clipboard is available on all platforms where SDL3 is initialised.
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
