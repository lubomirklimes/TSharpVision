namespace TSharpVision.Terminal.Windows;

/// <summary>
/// Construction options for <see cref="ConPtyTerminalSession"/>.
/// </summary>
public sealed class ConPtyTerminalSessionOptions
{
    /// <summary>The executable path or name (e.g. <c>"cmd.exe"</c>).</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Command-line arguments passed after the executable name.</summary>
    public string Arguments { get; init; } = string.Empty;

    /// <summary>Working directory for the child process. <see langword="null"/> inherits the parent directory.</summary>
    public string WorkingDirectory { get; init; }

    /// <summary>
    /// Terminal size used when creating the pseudo console.
    /// Defaults to 80 columns × 24 rows when not specified.
    /// </summary>
    public TerminalSize InitialSize { get; init; } = new TerminalSize(80, 24);
}
