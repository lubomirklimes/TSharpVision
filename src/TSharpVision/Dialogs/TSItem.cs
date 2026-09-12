namespace TSharpVision;

/// <summary>Linked string node used to supply option labels to controls.</summary>
public class TSItem
{
    /// <summary>Label stored in this node, including any mnemonic markers.</summary>
    public string Value { get; set; }
    /// <summary>Next label node, or null at the end of the chain.</summary>
    public TSItem Next { get; set; }

    /// <summary>Creates a label node referencing the supplied next node, which may be null.</summary>
    public TSItem(string aValue, TSItem aNext)
    {
        Value = aValue;
        Next = aNext;
    }

    /// <summary>Attaches a chain after the last node and returns this chain's head.</summary>
    public TSItem Append(TSItem aNext)
    {
        TSItem p = this;
        while (p.Next != null) p = p.Next;
        p.Next = aNext;
        return this;
    }
}
