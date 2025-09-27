namespace SharpVision.Terminal.Posix;

/// <summary>
/// Construction options for <see cref="PosixPtyTerminalSession"/>.
/// </summary>
public sealed class PosixPtyTerminalSessionOptions
{
    /// <summary>Full path or name of the executable (e.g. <c>"/bin/sh"</c> or <c>"sh"</c>).</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Arguments passed to the executable. Split on whitespace by convention.</summary>
    public string Arguments { get; init; } = string.Empty;

    /// <summary>Working directory for the child process. <see langword="null"/> inherits the parent directory.</summary>
    public string WorkingDirectory { get; init; }

    /// <summary>
    /// Terminal size used when creating the PTY.
    /// Defaults to 80 columns × 24 rows when not specified.
    /// </summary>
    public TerminalSize InitialSize { get; init; } = new TerminalSize(80, 24);
}
