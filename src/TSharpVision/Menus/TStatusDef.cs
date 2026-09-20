namespace TSharpVision;

/// <summary>Associates an inclusive help-context range with a linked chain of status shortcuts.</summary>
public class TStatusDef
{
    /// <summary>Next context-range definition, or null at the end of the chain.</summary>
    public TStatusDef? Next { get; set; }
    /// <summary>Lowest help context identifier covered by this definition, inclusive.</summary>
    public ushort Min { get; set; }
    /// <summary>Highest help context identifier covered by this definition, inclusive.</summary>
    public ushort Max { get; set; }
    /// <summary>First shortcut associated with this context range, or null for none.</summary>
    public TStatusItem? Items { get; set; }

    /// <summary>Creates an inclusive help-context range referencing an optional shortcut chain and next definition.</summary>
    public TStatusDef(ushort aMin, ushort aMax, TStatusItem? someItems = null, TStatusDef? aNext = null)
    {
        Min = aMin;
        Max = aMax;
        Items = someItems;
        Next = aNext;
    }

    /// <summary>Appends the shortcut chain to the non-null definition's items and returns that definition.</summary>
    public static TStatusDef operator +(TStatusDef s1, TStatusItem s2)
    {
        if (s1 == null)
            throw new ArgumentNullException(nameof(s1));
        if (s1.Items == null)
        {
            s1.Items = s2;
        }
        else
        {
            TStatusItem last = s1.Items;
            while (last.Next != null)
                last = last.Next;
            last.Next = s2;
        }
        return s1;
    }

    /// <summary>Merges the second definition's items into the first and returns the first; a null operand yields the other definition. Context ranges are not merged.</summary>
    public static TStatusDef operator +(TStatusDef s1, TStatusDef s2)
    {
        if (s1 == null) return s2;
        if (s2 == null) return s1;

        if (s1.Items == null)
            s1.Items = s2.Items;
        else
        {
            TStatusItem last = s1.Items;
            while (last.Next != null)
                last = last.Next;
            last.Next = s2.Items;
        }
        return s1;
    }
}
