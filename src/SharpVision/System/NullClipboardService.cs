namespace SharpVision;

// Represents the absence of an OS clipboard backend. All operations are
// no-ops that report "unavailable". This is the default service installed
// at process start-up.

/// <summary>
/// A clipboard service that always reports unavailable. Reads return null /
/// false; writes return false. Holds no state.
/// </summary>
public sealed class NullClipboardService : IClipboardService
{
    public bool IsAvailable => false;

    public string? GetText() => null;

    public bool TryGetText(out string text)
    {
        text = string.Empty;
        return false;
    }

    public bool SetText(string text) => false;
}
