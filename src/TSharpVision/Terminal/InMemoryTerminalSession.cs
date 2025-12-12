namespace TSharpVision;

/// <summary>
/// Scriptable in-memory session. Useful for tests and demo scenarios where
/// no real process or network connection is needed.
/// </summary>
public sealed class InMemoryTerminalSession : ITerminalSession, IResizableTerminalSession, IInterruptibleTerminalSession
{
    private volatile bool _isRunning;
    private readonly List<string> _sentInputs = new List<string>();
    private readonly List<TerminalSize> _receivedSizes = new List<TerminalSize>();
    private int _interruptCount;

    public event EventHandler<TerminalOutputEventArgs> OutputReceived;
    public event EventHandler Exited;

    public bool IsRunning => _isRunning;

    /// <summary>All inputs recorded via <see cref="SendInputAsync"/>.</summary>
    public IReadOnlyList<string> SentInputs => _sentInputs;

    /// <summary>All sizes received via <see cref="ResizeAsync"/>, in order.</summary>
    public IReadOnlyList<TerminalSize> ReceivedSizes => _receivedSizes;

    /// <summary>Number of times <see cref="InterruptAsync"/> has been called.</summary>
    public int InterruptCount => _interruptCount;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _isRunning = true;
        return Task.CompletedTask;
    }

    public Task SendInputAsync(string input, CancellationToken cancellationToken = default)
    {
        _sentInputs.Add(input);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _isRunning = false;
            Exited?.Invoke(this, EventArgs.Empty);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Raise <see cref="OutputReceived"/> with <paramref name="text"/> if the
    /// session is running.
    /// </summary>
    public void Emit(string text)
    {
        if (_isRunning)
            OutputReceived?.Invoke(this, new TerminalOutputEventArgs(text));
    }

    public void Dispose() { }

    public Task ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default)
    {
        _receivedSizes.Add(size);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Records the interrupt request. The session continues running; callers
    /// that need a stopped session should call <see cref="StopAsync"/> explicitly.
    /// </summary>
    public Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        System.Threading.Interlocked.Increment(ref _interruptCount);
        return Task.CompletedTask;
    }
}
