namespace TSharpVision;

// IClipboardService is the TSharpVision-side abstraction for the OS clipboard.
// It is intentionally minimal: text only, single channel, no rich formats.

/// <summary>
/// Contract for an OS clipboard text bridge.
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Whether this service represents an available clipboard backend.
    /// A return of <c>false</c> means callers should fall back to internal
    /// behaviour without attempting <see cref="GetText"/> / <see cref="SetText"/>.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Returns the current clipboard text, or <c>null</c> if no text is
    /// available or the operation failed. Newlines are normalised to LF.
    /// </summary>
    string? GetText();

    /// <summary>
    /// Tries to read clipboard text. Returns <c>true</c> only if a non-null
    /// text payload is available; <paramref name="text"/> is set to the empty
    /// string on failure.
    /// </summary>
    bool TryGetText(out string text);

    /// <summary>
    /// Writes <paramref name="text"/> to the clipboard. Returns <c>true</c>
    /// on success. Implementations may normalise newlines (e.g. LF→CRLF on
    /// Win32). A null input is treated as the empty string.
    /// </summary>
    bool SetText(string text);
}
