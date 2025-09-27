namespace SharpVision;

/// <summary>
/// Optional extension of <see cref="ITerminalSession"/> for sessions that can
/// react to terminal viewport size changes.
/// </summary>
/// <remarks>
/// <see cref="TTerminal"/> checks at runtime whether the attached session
/// implements this interface and calls <see cref="ResizeAsync"/> whenever the
/// physical cell area changes. Sessions that do not need size information simply
/// do not implement this interface.
/// <para>
/// PTY/ConPTY sessions should forward the new size to the OS (e.g.
/// <c>ResizePseudoConsole</c> on Windows, <c>ioctl TIOCSWINSZ</c> on POSIX).
/// Pipe-based sessions that cannot use size information implement this as a
/// no-op.
/// </para>
/// </remarks>
public interface IResizableTerminalSession
{
    /// <summary>
    /// Notify the session that the terminal viewport has been resized to
    /// <paramref name="size"/>.
    /// </summary>
    Task ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default);
}
