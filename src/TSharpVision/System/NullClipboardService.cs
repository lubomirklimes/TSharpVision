namespace TSharpVision;

// Represents the absence of an OS clipboard backend. All operations are
// no-ops that report "unavailable". This is the default service installed
// at process start-up.

/// <summary>
/// A clipboard service that always reports unavailable. Reads return null /
/// false; writes return false. Holds no state.
/// </summary>
public sealed class NullClipboardService : IClipboardService
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public string? GetText() => null;

    /// <inheritdoc />
    public bool TryGetText(out string text)
    {
        text = string.Empty;
        return false;
    }

    /// <inheritdoc />
    public bool SetText(string text) => false;
}
