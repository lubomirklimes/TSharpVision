using SharpVision;

namespace SharpVision.Tests.Infrastructure;

/// <summary>
/// Saves SharpVisionIntl.Current on construction and restores it on Dispose.
/// </summary>
public sealed class IntlProviderScope : IDisposable
{
    private readonly ISharpVisionStringProvider _saved;

    public IntlProviderScope()
    {
        _saved = SharpVisionIntl.Current;
    }

    public IntlProviderScope(ISharpVisionStringProvider replacement) : this()
    {
        SharpVisionIntl.Current = replacement;
    }

    public void Dispose()
    {
        SharpVisionIntl.Current = _saved;
    }
}
