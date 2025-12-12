namespace TSharpVision;

public abstract class TSortedCollection
{
    public bool Duplicates;

    public abstract int Count { get; }
    public abstract object At(int index);
    public abstract object KeyOf(object item);

    public abstract int Compare(object key1, object key2);

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
