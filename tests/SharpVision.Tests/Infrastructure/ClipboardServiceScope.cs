using SharpVision;

namespace SharpVision.Tests.Infrastructure;

/// <summary>
/// Saves ClipboardService.Current on construction and restores it on Dispose.
/// Always calls ClipboardService.Reset() first so tests start clean.
/// </summary>
public sealed class ClipboardServiceScope : IDisposable
{
    private readonly IClipboardService _saved;

    public ClipboardServiceScope()
    {
        _saved = ClipboardService.Current;
        ClipboardService.Reset();
    }

    public void Dispose()
    {
        ClipboardService.Current = _saved;
    }
}
