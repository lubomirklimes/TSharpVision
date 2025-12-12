namespace TSharpVision;

public class TDirCollection
{
    public List<TDirEntry> Items = new();

    public int Count => Items.Count;
    public TDirEntry this[int index] => Items[index];

    public void Insert(TDirEntry item)
    {
        if (item != null) Items.Add(item);
    }

    public TDirEntry At(int index) => Items[index];
}
