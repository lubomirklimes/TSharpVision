namespace TSharpVision;

/// <summary>Linked status-line shortcut binding a key code to a command, with an optional visible label.</summary>
public class TStatusItem
{
    /// <summary>Next shortcut in the current status definition, or null at the end.</summary>
    public TStatusItem Next { get; set; }
    /// <summary>Display label with optional tilde highlighting; null hides the label while retaining the key binding.</summary>
    public string Text { get; set; }
    /// <summary>Key code that invokes the associated command while this status definition is active.</summary>
    public ushort KeyCode { get; set; }
    /// <summary>Command identifier emitted when this shortcut is activated.</summary>
    public ushort Command { get; set; }

    /// <summary>Creates a shortcut with an optional visible label, key code, command, and next shortcut reference.</summary>
    public TStatusItem(string aText, ushort key, ushort cmd, TStatusItem aNext = null)
    {
        Text = aText;
        KeyCode = key;
        Command = cmd;
        Next = aNext;
    }
}
