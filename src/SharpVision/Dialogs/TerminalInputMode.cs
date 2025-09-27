namespace SharpVision;

/// <summary>
/// Controls how <see cref="TTerminal"/> processes keyboard input.
/// </summary>
public enum TerminalInputMode
{
    /// <summary>
    /// Output-only. Printable key presses are ignored and no prompt is rendered.
    /// Scroll keys (PgUp/PgDn, Up/Down, Home/End) and Ctrl+C (copy/interrupt)
    /// remain active.
    /// </summary>
    None,

    /// <summary>
    /// Local command mode. The terminal manages the prompt, line editing, and
    /// command history. Pressing Enter raises <see cref="TTerminal.CommandSubmitted"/>.
    /// Up/Down navigate command history; PgUp/PgDn scroll the output area.
    /// </summary>
    Command,

    /// <summary>
    /// Raw session mode. Every key press is translated to a VT-compatible
    /// escape sequence and forwarded directly to the attached
    /// <see cref="ITerminalSession"/>. No local prompt, no line editing, and no
    /// command history are maintained. The shell or program running inside the
    /// PTY is responsible for prompt display, echo, editing, and history.
    /// Ctrl+C retains its copy-or-interrupt semantics; Ctrl+V pastes directly
    /// to the session.
    /// </summary>
    RawSession,
}
