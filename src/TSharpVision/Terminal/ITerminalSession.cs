namespace TSharpVision;

/// <summary>
/// Abstraction for a terminal I/O source and optional input sink.
/// Implementors may wrap a process, an in-memory script, an SSH channel, or
/// any other I/O backend. <see cref="TTerminal"/> does not need to know which.
/// </summary>
/// <remarks>
/// Input is text-based: <see cref="SendInputAsync"/> accepts a string that the
/// session encodes and forwards as appropriate. Future PTY/ConPTY sessions will
/// encode the string internally (typically UTF-8) before writing to the PTY
/// master. A raw-byte overload is intentionally deferred until a real PTY
/// implementation demonstrates that string-based input is insufficient.
/// </remarks>
public interface ITerminalSession : IDisposable
{
    /// <summary>Raised when the session produces a text fragment for display.</summary>
    event EventHandler<TerminalOutputEventArgs> OutputReceived;

    /// <summary>
    /// Raised when the session ends normally or abnormally (process exit,
    /// disconnect, cancellation, etc.).
    /// </summary>
    event EventHandler Exited;

    /// <summary>
    /// <see langword="true"/> between a successful <see cref="StartAsync"/> call
    /// and session termination.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>Start the session and begin producing output.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send textual input to the session. Implementations that do not support
    /// input (e.g. read-only log viewers) may ignore the call silently.
    /// PTY/ConPTY sessions encode the string and write it to the PTY master.
    /// </summary>
    Task SendInputAsync(string input, CancellationToken cancellationToken = default);

    /// <summary>Request orderly termination of the session.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
