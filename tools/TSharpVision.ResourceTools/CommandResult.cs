// TSharpVision Resource Tools
// CommandResult returned by inspector commands.
// Result of a CLI command: output text, error message, and exit code.
namespace TSharpVision.ResourceTools;

/// <summary>
/// The result of executing a <c>svres</c> command.
/// </summary>
public sealed class CommandResult
{
    /// <summary>Text to write to stdout (empty string if none).</summary>
    public string Output { get; }

    /// <summary>Text to write to stderr (empty string on success).</summary>
    public string Error { get; }

    /// <summary>Process exit code: 0 = success, 1 = user/input error.</summary>
    public int ExitCode { get; }

    /// <summary>True when <see cref="ExitCode"/> is 0.</summary>
    public bool Success => ExitCode == 0;

    private CommandResult(string output, string error, int exitCode)
    {
        Output   = output   ?? string.Empty;
        Error    = error    ?? string.Empty;
        ExitCode = exitCode;
    }

    /// <summary>Creates a successful result with the given output text.</summary>
    public static CommandResult Ok(string output)    => new(output, string.Empty, 0);

    /// <summary>Creates an error result with exit code 1.</summary>
    public static CommandResult Fail(string error)   => new(string.Empty, error, 1);
}
