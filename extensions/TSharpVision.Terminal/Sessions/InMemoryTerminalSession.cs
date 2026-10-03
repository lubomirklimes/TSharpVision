using System.Text;

namespace TSharpVision.Terminal;

/// <summary>
/// Scriptable in-memory session for tests and demonstrations: output is whatever <see cref="Emit(string)"/> produces,
/// input is recorded.
/// </summary>
public sealed class InMemoryTerminalSession : ITerminalSession, IResizableTerminalSession, IInterruptibleTerminalSession
{
    private readonly object _gate = new();
    private readonly List<byte[]> _sentBytes = new();
    private readonly List<TerminalSize> _receivedSizes = new();
    private volatile bool _isRunning;
    private int _interruptCount;

    /// <inheritdoc />
    public event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    /// <inheritdoc />
    public event EventHandler? Exited;

    /// <inheritdoc />
    public bool IsRunning => _isRunning;

    /// <summary>Each <see cref="SendInputAsync"/> call's bytes, in order.</summary>
    public IReadOnlyList<byte[]> SentBytes
    {
        get { lock (_gate) return _sentBytes.ToArray(); }
    }

    /// <summary>Each <see cref="SendInputAsync"/> call's bytes decoded as UTF-8, in order.</summary>
    public IReadOnlyList<string> SentInputs
    {
        get { lock (_gate) return _sentBytes.Select(b => Encoding.UTF8.GetString(b)).ToArray(); }
    }

    /// <summary>All sizes received through <see cref="ResizeAsync"/>, in order.</summary>
    public IReadOnlyList<TerminalSize> ReceivedSizes
    {
        get { lock (_gate) return _receivedSizes.ToArray(); }
    }

    /// <summary>How many times <see cref="InterruptAsync"/> was called.</summary>
    public int InterruptCount => Volatile.Read(ref _interruptCount);

    /// <inheritdoc /><remarks>Marks the session running, synchronously.</remarks>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _isRunning = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc /><remarks>Records the bytes synchronously, even while stopped.</remarks>
    public Task SendInputAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default)
    {
        lock (_gate) _sentBytes.Add(input.ToArray());
        return Task.CompletedTask;
    }

    /// <inheritdoc /><remarks>Marks a running session stopped and raises <see cref="Exited"/> synchronously.</remarks>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _isRunning = false;
            Exited?.Invoke(this, EventArgs.Empty);
        }

        return Task.CompletedTask;
    }

    /// <summary>Raises <see cref="OutputReceived"/> with <paramref name="text"/> as UTF-8, if the session is running.</summary>
    public void Emit(string text) => Emit(Encoding.UTF8.GetBytes(text));

    /// <summary>Raises <see cref="OutputReceived"/> with <paramref name="data"/>, if the session is running.</summary>
    public void Emit(ReadOnlySpan<byte> data)
    {
        if (_isRunning) OutputReceived?.Invoke(this, new TerminalOutputEventArgs(data.ToArray()));
    }

    /// <inheritdoc /><remarks>Releases nothing and leaves the running state alone; call <see cref="StopAsync"/> to end.</remarks>
    public void Dispose()
    {
    }

    /// <inheritdoc /><remarks>Records the size synchronously.</remarks>
    public Task ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default)
    {
        lock (_gate) _receivedSizes.Add(size);
        return Task.CompletedTask;
    }

    /// <summary>Records the interrupt; the session keeps running.</summary>
    public Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _interruptCount);
        return Task.CompletedTask;
    }
}
