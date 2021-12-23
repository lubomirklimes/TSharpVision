namespace SharpVision;

public class TStatusItem
{
    public TStatusItem Next { get; set; }
    public string Text { get; set; }
    public ushort KeyCode { get; set; }
    public ushort Command { get; set; }

    public TStatusItem(string aText, ushort key, ushort cmd, TStatusItem aNext = null)
    {
        Text = aText;
        KeyCode = key;
        Command = cmd;
        Next = aNext;
    }
}
