namespace SharpVision;

/// <summary>
/// Optional extension of <see cref="ITerminalSession"/> for sessions that can
/// handle an interrupt/cancel request (analogous to the terminal Ctrl+C signal).
/// </summary>
/// <remarks>
/// <see cref="TTerminal"/> checks at runtime whether the attached session
/// implements this interface. When Ctrl+C is pressed with no active text
/// selection, <see cref="InterruptAsync"/> is called instead of copying.
/// <para>
/// True terminal interrupt semantics — delivering SIGINT to a process group —
/// require a PTY/ConPTY or equivalent. PTY/ConPTY and SSH sessions should write
/// the ETX character (<c>\x03</c>) to the PTY master or forward SIGINT through
/// the channel. Pipe-based sessions (<see cref="ProcessTerminalSession"/>)
/// implement interrupt as a safe stop/cancel fallback because redirected pipes
/// cannot deliver terminal signals.
/// </para>
/// </remarks>
public interface IInterruptibleTerminalSession
{
    /// <summary>
    /// Request an interrupt/cancel on the session. The exact behavior depends
    /// on the implementation:
    /// <list type="bullet">
    ///   <item>PTY/SSH sessions should send SIGINT or the terminal ETX character.</item>
    ///   <item>Pipe-based sessions should perform a safe stop/cancel fallback.</item>
    ///   <item>In-memory test sessions should record the call for assertions.</item>
    /// </list>
    /// This method must be safe to call when the session is not running.
    /// </summary>
    Task InterruptAsync(CancellationToken cancellationToken = default);
}
