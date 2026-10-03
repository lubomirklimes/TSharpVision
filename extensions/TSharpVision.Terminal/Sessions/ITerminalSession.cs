using System.Text;

namespace TSharpVision.Terminal;

/// <summary>
/// A terminal transport: a byte stream from a program and a byte stream to it. Implementations wrap a pseudo-terminal
/// (ConPTY, POSIX PTY), a remote channel, a pipe-connected process or a script; <see cref="TTerminal"/> and
/// <see cref="TerminalEmulator"/> do not need to know which.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bytes, not text.</b> A terminal is a byte-stream protocol. Output is delivered exactly as read, with no decoding:
/// a UTF-8 character or a control sequence split between two reads arrives split, and the emulator, which owns
/// decoding, joins it. Input is bytes as well; <see cref="TerminalSessionExtensions.SendTextAsync"/> encodes text as
/// UTF-8.
/// </para>
/// <para>
/// <b>Threads.</b> <see cref="OutputReceived"/> and <see cref="Exited"/> may be raised on any thread.
/// <see cref="SendInputAsync"/> must not block the caller on a program that is not reading.
/// </para>
/// <para>The scriptable <see cref="InMemoryTerminalSession"/> records input even while stopped and has a no-op
/// Dispose; its lifecycle is controlled explicitly by the caller.</para>
/// </remarks>
public interface ITerminalSession : IDisposable
{
    /// <summary>Raised when the program produced output. The data is valid only during the handler.</summary>
    event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    /// <summary>Raised once when the session ends (process exit, disconnect, stop). Never after disposal.</summary>
    event EventHandler? Exited;

    /// <summary>Whether the session started and has not ended.</summary>
    bool IsRunning { get; }

    /// <summary>Starts the session.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends bytes to the program. Real transports silently drop input while stopped; scriptable sessions may record it. The bytes are copied
    /// before the call returns.
    /// </summary>
    Task SendInputAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken = default);

    /// <summary>Requests orderly termination.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>A session that can be told the terminal's size (a PTY forwards it to the program as SIGWINCH).</summary>
public interface IResizableTerminalSession
{
    /// <summary>Tells the session the terminal is now <paramref name="size"/> cells.</summary>
    Task ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default);
}

/// <summary>
/// A session that can interrupt the running program the way Ctrl+C in a terminal does. A PTY writes ETX (0x03) so the
/// line discipline raises SIGINT; a pipe session, which has no terminal to signal through, stops the process.
/// </summary>
public interface IInterruptibleTerminalSession
{
    /// <summary>Interrupts the program. Safe when the session is not running.</summary>
    Task InterruptAsync(CancellationToken cancellationToken = default);
}

/// <summary>Output of an <see cref="ITerminalSession"/>.</summary>
public sealed class TerminalOutputEventArgs : EventArgs
{
    /// <summary>Output bytes, and whether they came from a separate error stream.</summary>
    public TerminalOutputEventArgs(ReadOnlyMemory<byte> data, bool isError = false)
    {
        Data = data;
        IsError = isError;
    }

    /// <summary>The bytes, exactly as read. Valid only while the handler runs; copy them to keep them.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Whether the bytes came from the program's standard error (pipe sessions only; a PTY has one stream).</summary>
    public bool IsError { get; }
}

/// <summary>A terminal size in character cells.</summary>
public readonly struct TerminalSize : IEquatable<TerminalSize>
{
    /// <summary>A size of <paramref name="columns"/> × <paramref name="rows"/>; values below 1 become 1.</summary>
    public TerminalSize(int columns, int rows)
    {
        Columns = Math.Max(1, columns);
        Rows = Math.Max(1, rows);
    }

    /// <summary>Character columns; at least 1.</summary>
    public int Columns { get; }

    /// <summary>Character rows; at least 1.</summary>
    public int Rows { get; }

    /// <inheritdoc />
    public bool Equals(TerminalSize other) => Columns == other.Columns && Rows == other.Rows;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TerminalSize other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Columns, Rows);

    /// <summary>Whether both sizes are the same.</summary>
    public static bool operator ==(TerminalSize left, TerminalSize right) => left.Equals(right);

    /// <summary>Whether the sizes differ.</summary>
    public static bool operator !=(TerminalSize left, TerminalSize right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => $"{Columns}x{Rows}";
}

/// <summary>Conveniences over <see cref="ITerminalSession"/>.</summary>
public static class TerminalSessionExtensions
{
    /// <summary>Sends <paramref name="text"/> encoded as UTF-8.</summary>
    public static Task SendTextAsync(this ITerminalSession session, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(text);
        return text.Length == 0 ? Task.CompletedTask : session.SendInputAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
    }
}
