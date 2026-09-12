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

    /// <inheritdoc />
    public event EventHandler<TerminalOutputEventArgs> OutputReceived;
    /// <inheritdoc />
    public event EventHandler Exited;

    /// <inheritdoc />
    public bool IsRunning => _isRunning;

    /// <summary>All inputs recorded via <see cref="SendInputAsync"/>.</summary>
    public IReadOnlyList<string> SentInputs => _sentInputs;

    /// <summary>All sizes received via <see cref="ResizeAsync"/>, in order.</summary>
    public IReadOnlyList<TerminalSize> ReceivedSizes => _receivedSizes;

    /// <summary>Number of times <see cref="InterruptAsync"/> has been called.</summary>
    public int InterruptCount => _interruptCount;

    /// <inheritdoc /><remarks>Marks the session running synchronously; cancellation is not observed.</remarks>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _isRunning = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc /><remarks>Records input synchronously, including while stopped; cancellation is not observed.</remarks>
    public Task SendInputAsync(string input, CancellationToken cancellationToken = default)
    {
        _sentInputs.Add(input);
        return Task.CompletedTask;
    }

    /// <inheritdoc /><remarks>Marks a running session stopped and raises Exited synchronously; cancellation is not observed.</remarks>
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

    /// <inheritdoc /><remarks>No resources are released and running state is unchanged; call StopAsync to end the session.</remarks>
    public void Dispose() { }

    /// <inheritdoc /><remarks>Records the supplied size synchronously; cancellation is not observed.</remarks>
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
