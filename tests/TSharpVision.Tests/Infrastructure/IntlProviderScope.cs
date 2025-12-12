using TSharpVision;

namespace TSharpVision.Tests.Infrastructure;

/// <summary>
/// Saves TSharpVisionIntl.Current on construction and restores it on Dispose.
/// </summary>
public sealed class IntlProviderScope : IDisposable
{
    private readonly ITSharpVisionStringProvider _saved;

    public IntlProviderScope()
    {
        _saved = TSharpVisionIntl.Current;
    }

    public IntlProviderScope(ITSharpVisionStringProvider replacement) : this()
    {
        TSharpVisionIntl.Current = replacement;
    }

    public void Dispose()
    {
        TSharpVisionIntl.Current = _saved;
    }
}
