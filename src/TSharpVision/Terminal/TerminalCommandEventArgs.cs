namespace TSharpVision;

/// <summary>Data carried by the <see cref="TTerminal.CommandSubmitted"/> event.</summary>
public sealed class TerminalCommandEventArgs : EventArgs
{
    /// <summary>Captures submitted command text without adding a prompt or newline.</summary>
    public TerminalCommandEventArgs(string command)
    {
        Command = command;
    }

    /// <summary>The command text entered by the user, without the prompt.</summary>
    public string Command { get; }
}
