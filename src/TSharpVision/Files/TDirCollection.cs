namespace TSharpVision;

/// <summary>Ordered, mutable directory-entry list that retains the supplied entry objects.</summary>
public class TDirCollection
{
    /// <summary>Backing list in display order; direct modifications are visible through indexed access.</summary>
    public List<TDirEntry> Items = new();

    /// <summary>Number of directory entries currently stored.</summary>
    public int Count => Items.Count;
    /// <summary>Directory entry at a zero-based index smaller than Count.</summary>
    public TDirEntry this[int index] => Items[index];

    /// <summary>Appends the supplied entry reference; null is ignored.</summary>
    public void Insert(TDirEntry? item)
    {
        if (item != null) Items.Add(item);
    }

    /// <summary>Returns the stored entry at a zero-based index smaller than Count.</summary>
    public TDirEntry At(int index) => Items[index];
}
