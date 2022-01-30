namespace SharpVision;

public class TSItem
{
    public string Value { get; set; }
    public TSItem Next { get; set; }

    public TSItem(string aValue, TSItem aNext)
    {
        Value = aValue;
        Next = aNext;
    }

    public TSItem Append(TSItem aNext)
    {
        TSItem p = this;
        while (p.Next != null) p = p.Next;
        p.Next = aNext;
        return this;
    }
}
