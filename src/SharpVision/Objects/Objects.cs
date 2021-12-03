namespace SharpVision;


public interface TNSCollection
{
    void SetLimit(int limit);
}


public delegate int ccIndex();


public abstract class TCollection : TStreamable, TNSCollection
{
    protected object[] items;
    protected int count;
    protected int limit;
    protected int delta;

    public TCollection(int aLimit, int aDelta)
    {
        delta = aDelta;
        SetLimit(aLimit);
    }

    public virtual void SetLimit(int newLimit)
    {
        if (newLimit == limit) return;
        object[] newItems = newLimit > 0 ? new object[newLimit] : null;
        if (items != null && newItems != null)
            System.Array.Copy(items, newItems, System.Math.Min(count, newLimit));
        items = newItems;
        limit = newLimit;
        if (count > limit) count = limit;
    }

    public virtual string StreamableName()
    {
        return Name;
    }

    protected abstract object ReadItem(Ipstream isStream);

    protected abstract void WriteItem(object item, Opstream os);

    public override void Write(Opstream os)
    {
        os.WriteShort((ushort)count);
        os.WriteShort((ushort)limit);
        os.WriteShort((ushort)delta);
        for (int i = 0; i < count; i++) WriteItem(items != null ? items[i] : null, os);
    }

    public override object Read(Ipstream isStream)
    {
        int readCount = isStream.ReadShort();
        int readLimit = isStream.ReadShort();
        delta = isStream.ReadShort();
        SetLimit(0);
        SetLimit(readLimit);
        for (int i = 0; i < readCount; i++)
        {
            var item = ReadItem(isStream);
            if (count < limit) items[count++] = item;
        }
        return this;
    }

    public static readonly string Name = "TCollection";
}
