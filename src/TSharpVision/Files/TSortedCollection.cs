namespace TSharpVision;

/// <summary>Indexed collection contract with key extraction, comparison, and sorted lookup.</summary>
public abstract class TSortedCollection
{
    /// <summary>Duplicate-key policy available to derived collections; base Search does not enforce it.</summary>
    public bool Duplicates;

    /// <summary>Number of elements addressable by zero-based indexed access.</summary>
    public abstract int Count { get; }
    /// <summary>Returns the element at a zero-based index smaller than Count.</summary>
    public abstract object At(int index);
    /// <summary>Extracts the comparison key used to order and search an element.</summary>
    public abstract object KeyOf(object item);

    /// <summary>Compares keys, returning a negative value, zero, or a positive value for less than, equal, or greater than.</summary>
    public abstract int Compare(object key1, object key2);

    /// <summary>Searches sorted keys; returns whether a match exists and outputs the first equal index or the insertion position, possibly Count.</summary>
    public virtual bool Search(object key, out int index)
    {
        int l = 0, r = Count;
        while (l < r)
        {
            int m = (l + r) >> 1;
            int c = Compare(KeyOf(At(m)), key);
            if (c < 0) l = m + 1;
            else r = m;
        }
        index = l;
        if (l < Count && Compare(KeyOf(At(l)), key) == 0)
            return true;
        return false;
    }
}
