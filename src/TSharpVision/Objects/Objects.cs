namespace TSharpVision;


/// <summary>Collection contract for adjusting allocated item capacity.</summary>
public interface TNSCollection
{
    /// <summary>Sets a nonnegative item capacity; implementations may discard items beyond a reduced capacity.</summary>
    void SetLimit(int limit);
}


/// <summary>Callback returning an integer collection index for caller-defined traversal logic.</summary>
public delegate int ccIndex();


/// <summary>Streamable array-backed collection base with adjustable capacity and subclass-defined item serialization.</summary>
public abstract class TCollection : TStreamable, TNSCollection
{
    /// <summary>Backing object-reference array; null when no positive capacity is allocated.</summary>
    protected object[] items;
    /// <summary>Number of active elements in the backing array.</summary>
    protected int count;
    /// <summary>Allocated item capacity; changing it through SetLimit may truncate active elements.</summary>
    protected int limit;
    /// <summary>Growth-step metadata persisted with the collection for use by derived implementations.</summary>
    protected int delta;

    /// <summary>Creates empty storage with a nonnegative capacity and a persisted growth-step value.</summary>
    public TCollection(int aLimit, int aDelta)
    {
        delta = aDelta;
        SetLimit(aLimit);
    }

    /// <inheritdoc /><remarks>Preserves the common item prefix; reducing capacity drops references without disposing the removed objects.</remarks>
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

    /// <summary>Returns the collection base type's registry name; subclasses may override this compatibility method.</summary>
    public virtual string StreamableName()
    {
        return Name;
    }

    /// <summary>Restores one element from the current input position using the derived collection's item format.</summary>
    protected abstract object ReadItem(Ipstream isStream);

    /// <summary>Serializes one element at the current output position using the derived collection's item format.</summary>
    protected abstract void WriteItem(object item, Opstream os);

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        os.WriteShort((ushort)count);
        os.WriteShort((ushort)limit);
        os.WriteShort((ushort)delta);
        for (int i = 0; i < count; i++) WriteItem(items != null ? items[i] : null, os);
    }

    /// <inheritdoc />
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

    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public static readonly string Name = "TCollection";
}
