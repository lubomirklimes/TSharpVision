namespace SharpVision.Dialogs;

// ------------------------------------------------------------------------
// TSItem – pomocná třída pro řetězce
// ------------------------------------------------------------------------
public class TSItem
{
    public string Value { get; set; }
    public TSItem Next { get; set; }

    public TSItem(string aValue, TSItem aNext)
    {
        Value = aValue;
        Next = aNext;
    }
}
