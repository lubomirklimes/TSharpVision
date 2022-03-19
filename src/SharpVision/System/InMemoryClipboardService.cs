namespace SharpVision;

// Deterministic in-process clipboard, intended for smoke tests. Safe to use
// in production code as well, but it does not interoperate with any OS
// clipboard.

/// <summary>
/// In-memory <see cref="IClipboardService"/>. Reports available; stores text
/// verbatim (no newline transform) so smoke tests can verify the editor's
/// own normalisation policy.
/// </summary>
public sealed class InMemoryClipboardService : IClipboardService
{
    private string? _text;

    public bool IsAvailable => true;

    public string? GetText() => _text;

    public bool TryGetText(out string text)
    {
        if (_text == null)
        {
            text = string.Empty;
            return false;
        }
        text = _text;
        return true;
    }

    public bool SetText(string text)
    {
        _text = text ?? string.Empty;
        return true;
    }

    /// <summary>
    /// Clears the stored text so subsequent <see cref="TryGetText"/> calls
    /// return false. Useful between smoke sub-blocks.
    /// </summary>
    public void Clear() => _text = null;
}
