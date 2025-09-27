namespace SharpVision;

/// <summary>Data carried by the <see cref="TTerminal.CommandSubmitted"/> event.</summary>
public sealed class TerminalCommandEventArgs : EventArgs
{
    public TerminalCommandEventArgs(string command)
    {
        Command = command;
    }

    /// <summary>The command text entered by the user, without the prompt.</summary>
    public string Command { get; }
}
