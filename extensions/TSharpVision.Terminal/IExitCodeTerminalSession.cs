namespace TSharpVision.Terminal;

/// <summary>A terminal session that can report its child process exit code after termination.</summary>
public interface IExitCodeTerminalSession
{
    /// <summary>The child exit code, or null until unavailable or when terminated by a signal.</summary>
    int? ExitCode { get; }
}
