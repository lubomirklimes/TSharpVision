namespace SharpVision;

public class TFileCollection : TSortedCollection
{
    public List<TSearchRec> Items = new();

    public static uint SortOptions =
        FileCollectionOptions.fcolParentLast
      | FileCollectionOptions.fcolDirsLast
      | FileCollectionOptions.fcolCaseInsensitive;

    public TFileCollection()
    {
        Duplicates = true;
    }

    public override int Count => Items.Count;
    public override object At(int index) => Items[index];
    public override object KeyOf(object item) => item; // upstream getName(key)

    public override int Compare(object key1, object key2)
    {
        var a = (TSearchRec)key1;
        var b = (TSearchRec)key2;
        uint sort = SortOptions & FileCollectionOptions.fcolTypeMask;
        bool caseInsens = (SortOptions & FileCollectionOptions.fcolCaseInsensitive) != 0;
        string n1 = a.name ?? string.Empty;
        string n2 = b.name ?? string.Empty;
        int Cmp(string x, string y) => caseInsens
            ? string.Compare(x, y, StringComparison.OrdinalIgnoreCase)
            : string.CompareOrdinal(x, y);

        // Dot-files (".*" but not "..") tie-break.
        if ((SortOptions & FileCollectionOptions.fcolDotsLast) != 0
            && n1.Length > 0 && n2.Length > 0 && n1[0] != n2[0])
        {
            if (n1[0] == '.' && n1 != "..") return 1;
            if (n2[0] == '.' && n2 != "..") return -1;
        }

        if (sort == FileCollectionOptions.fcolAlphabetical)
            return Cmp(n1, n2);

        if (Cmp(n1, n2) == 0) return 0;

        // ".." sticks to one end depending on fcolParentLast.
        if (n1 == "..")
            return (SortOptions & FileCollectionOptions.fcolParentLast) != 0 ? 1 : -1;
        if (n2 == "..")
            return (SortOptions & FileCollectionOptions.fcolParentLast) != 0 ? -1 : 1;

        bool d1 = (a.attr & FileAttr.faDirec) != 0;
        bool d2 = (b.attr & FileAttr.faDirec) != 0;
        if (d1 && !d2) return sort == FileCollectionOptions.fcolDirsFirst ? -1 : 1;
        if (d2 && !d1) return sort == FileCollectionOptions.fcolDirsLast  ? -1 : 1;

        return Cmp(n1, n2);
    }

    public virtual void Insert(TSearchRec item)
    {
        if (item == null) return;
        Search(item, out int idx);
        Items.Insert(idx, item);
    }
}
