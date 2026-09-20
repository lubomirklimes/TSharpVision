using System;

namespace TSharpVision;

// THelpIndex — sparse map from topic id (int index) to file position
// (long). Grows in chunks of 10; missing slots are filled with -1
// (upstream uses 0xFF byte fill which produces -1 for `long`).
//
// Wire format (helpbase.cc:451):
//   ushort size
//   for each: long position
/// <summary>Persistent mapping from nonnegative help context identifiers to file byte positions.</summary>
public class THelpIndex : TStreamable
{
    /// <summary>Type name used to identify the serialized help index.</summary>
    public const string TypeName = "THelpIndex";
    /// <inheritdoc />
    public override string streamableName => TypeName;

    /// <summary>Number of allocated context slots, including unused entries.</summary>
    public ushort size;
    /// <summary>Position array indexed by help context; unused slots contain -1, and an empty index may be null.</summary>
    public long[]? index;

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClass =
        new TStreamableClass(TypeName, () => new THelpIndex(), 0);

    /// <summary>Creates an empty index with no allocated context slots.</summary>
    public THelpIndex()
    {
        size = 0;
        index = null;
    }

    /// <summary>Returns the file byte position for a nonnegative context identifier, or -1 when its slot is absent or unused.</summary>
    public long Position(int i)
    {
        if (i >= 0 && i < size && index != null) return index[i];
        return -1;
    }

    /// <summary>Stores a file byte position for a nonnegative context identifier, expanding the index as needed.</summary>
    public void Add(int i, long val)
    {
        const int delta = 10;
        if (i >= size)
        {
            int newSize = (i + delta) / delta * delta;
            var p = new long[newSize];
            if (size > 0 && index != null)
                Array.Copy(index, p, size);
            for (int k = size; k < newSize; k++) p[k] = -1;
            index = p;
            size = (ushort)newSize;
        }
        long[] positions = index
            ?? throw new InvalidOperationException("The help index storage was not allocated.");
        positions[i] = val;
    }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        os.WriteShort(size);
        long[] positions = index ?? Array.Empty<long>();
        for (int i = 0; i < size; i++)
            os.WriteLong((uint)positions[i]);
    }

    /// <inheritdoc />
    public override object Read(Ipstream s)
    {
        size = s.ReadShort();
        if (size == 0)
        {
            index = null;
        }
        else
        {
            index = new long[size];
            for (int i = 0; i < size; i++)
                index[i] = (int)s.ReadLong();
        }
        return this;
    }
}
