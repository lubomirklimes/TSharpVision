using System;

namespace SharpVision;

// THelpIndex — sparse map from topic id (int index) to file position
// (long). Grows in chunks of 10; missing slots are filled with -1
// (upstream uses 0xFF byte fill which produces -1 for `long`).
//
// Wire format (helpbase.cc:451):
//   ushort size
//   for each: long position
public class THelpIndex : TStreamable
{
    public const string TypeName = "THelpIndex";
    public override string streamableName => TypeName;

    public ushort size;
    public long[] index;

    public static readonly TStreamableClass StreamableClass =
        new TStreamableClass(TypeName, () => new THelpIndex(), 0);

    public THelpIndex()
    {
        size = 0;
        index = null;
    }

    public long Position(int i)
    {
        if (i < size) return index[i];
        return -1;
    }

    public void Add(int i, long val)
    {
        const int delta = 10;
        if (i >= size)
        {
            int newSize = (i + delta) / delta * delta;
            var p = new long[newSize];
            if (size > 0)
                Array.Copy(index, p, size);
            for (int k = size; k < newSize; k++) p[k] = -1;
            index = p;
            size = (ushort)newSize;
        }
        index[i] = val;
    }

    public override void Write(Opstream os)
    {
        os.WriteShort(size);
        for (int i = 0; i < size; i++)
            os.WriteLong((uint)index[i]);
    }

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
