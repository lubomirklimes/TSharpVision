namespace TSharpVision;

/// <summary>Data carried by the <see cref="ITerminalSession.OutputReceived"/> event.</summary>
public sealed class TerminalOutputEventArgs : EventArgs
{
    /// <summary>Captures an output text fragment and whether it came from the error channel.</summary>
    public TerminalOutputEventArgs(string text, bool isError = false)
    {
        Text = text;
        IsError = isError;
    }

    /// <summary>Text fragment received from the session.</summary>
    public string Text { get; }

    /// <summary>True when the text originated from the process standard-error stream.</summary>
    public bool IsError { get; }
}
