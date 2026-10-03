using System.Text;

namespace TSharpVision.Terminal;

/// <summary>
/// A deterministic session with the observable contract of a real PTY session — single start, exactly one
/// <see cref="Exited"/>, no event after disposal — and no native dependency, for tests and demonstrations.
/// </summary>
public sealed class FakePtyTerminalSession : ITerminalSession, IResizableTerminalSession, IInterruptibleTerminalSession
{
    private readonly object _gate = new();
    private readonly List<byte[]> _sentBytes = new();
    private readonly List<TerminalSize> _receivedSizes = new();
    private volatile bool _isRunning;
    private volatile bool _disposed;
    private int _exitedFired;
    private int _interruptCount;

    /// <inheritdoc/>
    public event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    /// <inheritdoc/>
    public event EventHandler? Exited;

    /// <inheritdoc/>
    public bool IsRunning => _isRunning;

    /// <summary>Each input's bytes decoded as UTF-8, in order.</summary>
    public IReadOnlyList<string> SentInputs
    {
        get { lock (_gate) return _sentBytes.Select(b => Encoding.UTF8.GetString(b)).ToArray(); }
    }

    /// <summary>All sizes received through <see cref="ResizeAsync"/>, in order.</summary>
    public IReadOnlyList<TerminalSize> ReceivedSizes
    {
        get { lock (_gate) return _receivedSizes.ToArray(); }
    }

    /// <summary>The most recently received size, or null.</summary>
    public TerminalSize? LastSize
    {
        get { lock (_gate) return _receivedSizes.Count > 0 ? _receivedSizes[^1] : null; }
    }

    /// <summary>How many times <see cref="InterruptAsync"/> was called.</summary>
    public int InterruptCount => Volatile.Read(ref _interruptCount);

    /// <summary>When true, input is echoed back as output while running, as a PTY in cooked mode would.</summary>
    public bool EchoInput { get; set; }

    /// <summary>Starts the session; a second start, or a start after disposal, throws.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isRunning || Volatile.Read(ref _exitedFired) != 0) throw new InvalidOperationException("A session starts once.");
        _isRunning = true;
        return Task.CompletedTask;
    }

    /// <summary>Stops the session and raises <see cref="Exited"/> once. Safe at any time.</summary>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _isRunning = false;
            FireExited();
        }

        return Task.CompletedTask;
    }

    /// <summary>Releases the session without raising <see cref="Exited"/>. Idempotent.</summary>
    public void Dispose()
    {
        _disposed = true;
        _isRunning = false;
    }

    /// <summary>Raises <see cref="OutputReceived"/> with <paramref name="text"/> as UTF-8; throws when not running.</summary>
    public void Emit(string text) => Emit(Encoding.UTF8.GetBytes(text));

    /// <summary>Raises <see cref="OutputReceived"/> with <paramref name="data"/>; throws when not running.</summary>
    public void Emit(ReadOnlySpan<byte> data)
    {
        if (!_isRunning) throw new InvalidOperationException("Cannot emit output when the session is not running.");
        OutputReceived?.Invoke(this, new TerminalOutputEventArgs(data.ToArray()));
    }

    /// <summary>Emits <paramref name="text"/> followed by CR LF, as a PTY translates a program's newline.</summary>
    public void EmitLine(string text) => Emit(text + "\r\n");

    /// <summary>Records the input and echoes it when <see cref="EchoInput"/> is set.</summary>
    public Task SendInputAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default)
    {
        byte[] bytes = input.ToArray();
        lock (_gate) _sentBytes.Add(bytes);
        if (EchoInput && _isRunning && !_disposed) OutputReceived?.Invoke(this, new TerminalOutputEventArgs(bytes));
        return Task.CompletedTask;
    }

    /// <summary>Records the size.</summary>
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

    /// <summary>Simulates the program exiting: not running, <see cref="Exited"/> raised once.</summary>
    public void Complete()
    {
        _isRunning = false;
        FireExited();
    }

    private void FireExited()
    {
        if (!_disposed && Interlocked.CompareExchange(ref _exitedFired, 1, 0) == 0) Exited?.Invoke(this, EventArgs.Empty);
    }
}
