namespace SharpVision;

/// <summary>
/// A deterministic fake session that mimics the observable behavior expected
/// from a future real PTY/ConPTY session, without any native platform
/// dependency. Intended for tests and demos that need predictable PTY-like
/// semantics.
/// </summary>
/// <remarks>
/// This is not a real PTY. It records inputs, sizes, and interrupt requests
/// so tests can assert on them, and it raises <see cref="OutputReceived"/> on
/// demand via <see cref="Emit"/>. Real ConPTY and POSIX PTY sessions are
/// deferred to a future <c>SharpVision.Terminal</c> assembly.
/// </remarks>
public sealed class FakePtyTerminalSession : ITerminalSession, IResizableTerminalSession, IInterruptibleTerminalSession
{
    private volatile bool _isRunning;
    private bool _disposed;
    private int _exitedFired;   // 0 = not fired, 1 = fired; guarded by CompareExchange
    private readonly List<string> _sentInputs = new();
    private readonly List<TerminalSize> _receivedSizes = new();
    private int _interruptCount;

    /// <inheritdoc/>
    public event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    /// <inheritdoc/>
    public event EventHandler? Exited;

    /// <inheritdoc/>
    public bool IsRunning => _isRunning;

    /// <summary>All text inputs recorded via <see cref="SendInputAsync"/>, in order.</summary>
    public IReadOnlyList<string> SentInputs => _sentInputs;

    /// <summary>All sizes received via <see cref="ResizeAsync"/>, in order.</summary>
    public IReadOnlyList<TerminalSize> ReceivedSizes => _receivedSizes;

    /// <summary>The most recently received terminal size, or <see langword="null"/> if none.</summary>
    public TerminalSize? LastSize => _receivedSizes.Count > 0 ? _receivedSizes[_receivedSizes.Count - 1] : null;

    /// <summary>Number of times <see cref="InterruptAsync"/> has been called.</summary>
    public int InterruptCount => _interruptCount;

    /// <summary>
    /// When <see langword="true"/>, <see cref="SendInputAsync"/> raises
    /// <see cref="OutputReceived"/> with the same text, simulating terminal echo.
    /// Default is <see langword="false"/>.
    /// </summary>
    public bool EchoInput { get; set; }

    /// <summary>
    /// Start the session. Throws <see cref="InvalidOperationException"/> if the
    /// session is already running, and <see cref="ObjectDisposedException"/> if
    /// it has been disposed.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_isRunning)
            throw new InvalidOperationException("Session is already running.");
        _isRunning = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop the session and fire <see cref="Exited"/> exactly once. Safe to call
    /// before <see cref="StartAsync"/>, after <see cref="StopAsync"/>, and after
    /// <see cref="Complete"/>; does not throw in those cases.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            _isRunning = false;
            FireExited();
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Release the session. Safe to call multiple times. Does not raise
    /// <see cref="Exited"/>; callers that need the event should call
    /// <see cref="StopAsync"/> or <see cref="Complete"/> first.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isRunning = false;
    }

    /// <summary>
    /// Raise <see cref="OutputReceived"/> with <paramref name="text"/> as if raw
    /// output arrived from a PTY master. The text is passed through unmodified —
    /// no ANSI processing is applied here.
    /// Throws <see cref="InvalidOperationException"/> when the session is not running.
    /// </summary>
    public void Emit(string text)
    {
        if (!_isRunning)
            throw new InvalidOperationException("Cannot emit output when the session is not running.");
        OutputReceived?.Invoke(this, new TerminalOutputEventArgs(text));
    }

    /// <summary>Convenience wrapper: calls <see cref="Emit"/> with <paramref name="text"/> followed by a newline.</summary>
    public void EmitLine(string text) => Emit(text + "\n");

    /// <summary>
    /// Record <paramref name="input"/> in <see cref="SentInputs"/>. When
    /// <see cref="EchoInput"/> is <see langword="true"/> and the session is
    /// running, also raises <see cref="OutputReceived"/> with the same text.
    /// </summary>
    public Task SendInputAsync(string input, CancellationToken cancellationToken = default)
    {
        _sentInputs.Add(input);
        if (EchoInput && _isRunning)
            OutputReceived?.Invoke(this, new TerminalOutputEventArgs(input));
        return Task.CompletedTask;
    }

    /// <summary>Record <paramref name="size"/> in <see cref="ReceivedSizes"/> and update <see cref="LastSize"/>.</summary>
    public Task ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default)
    {
        _receivedSizes.Add(size);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Record an interrupt request. The session keeps running;
    /// <see cref="InterruptCount"/> increments. A real PTY session would write
    /// the ETX character to the PTY master at this point.
    /// </summary>
    public Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _interruptCount);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Simulate natural session exit: mark the session as not running and fire
    /// <see cref="Exited"/> exactly once. Subsequent calls to
    /// <see cref="StopAsync"/> or <see cref="Dispose"/> will not fire
    /// <see cref="Exited"/> again.
    /// </summary>
    public void Complete()
    {
        _isRunning = false;
        FireExited();
    }

    private void FireExited()
    {
        if (Interlocked.CompareExchange(ref _exitedFired, 1, 0) == 0)
            Exited?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FakePtyTerminalSession));
    }
}
