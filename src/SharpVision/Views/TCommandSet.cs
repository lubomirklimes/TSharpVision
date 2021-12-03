namespace SharpVision;

/// <summary>
/// Reprezentuje sadu příkazů; implementuje metody pro zapnutí, vypnutí a operace nad sadou.
/// </summary>
public class TCommandSet
{
    // Interní pole pro příkazy – 32 bajtů
    private byte[] cmds = new byte[32];

    // Statické pole masek pro operace (8 položek)
    private static readonly int[] masks = new int[8] { 1, 2, 4, 8, 16, 32, 64, 128 };

    public TCommandSet()
    {
        for (int i = 0; i < cmds.Length; i++)
            cmds[i] = 0;
    }

    // Copy konstruktor
    public TCommandSet(TCommandSet other)
    {
        cmds = (byte[])other.cmds.Clone();
    }

    public bool Has(int cmd)
    {
        return (cmds[Loc(cmd)] & Mask(cmd)) != 0;
    }

    private int Loc(int cmd)
    {
        return cmd / 8;
    }

    private int Mask(int cmd)
    {
        return masks[cmd & 0x07];
    }

    // Zapne příkaz (operator += v C++)
    public void Add(int cmd) { EnableCmd(cmd); }
    // Vypne příkaz (operator -= v C++)
    public void Remove(int cmd) { DisableCmd(cmd); }

    public void EnableCmd(int cmd)
    {
        int loc = cmd / 8;
        int mask = masks[cmd & 0x07];
        cmds[loc] |= (byte)mask;
    }

    public void DisableCmd(int cmd)
    {
        int loc = cmd / 8;
        int mask = masks[cmd & 0x07];
        cmds[loc] &= (byte)~mask;
    }

    public void Add(TCommandSet tc)
    {
        for (int i = 0; i < cmds.Length; i++)
            cmds[i] |= tc.cmds[i];
    }

    public void Remove(TCommandSet tc)
    {
        for (int i = 0; i < cmds.Length; i++)
            cmds[i] &= (byte)~tc.cmds[i];
    }

    public bool IsEmpty()
    {
        foreach (byte b in cmds)
            if (b != 0)
                return false;
        return true;
    }

    public static TCommandSet operator &(TCommandSet a, TCommandSet b)
    {
        TCommandSet result = new TCommandSet(a);
        for (int i = 0; i < result.cmds.Length; i++)
            result.cmds[i] &= b.cmds[i];
        return result;
    }

    public static TCommandSet operator |(TCommandSet a, TCommandSet b)
    {
        TCommandSet result = new TCommandSet(a);
        for (int i = 0; i < result.cmds.Length; i++)
            result.cmds[i] |= b.cmds[i];
        return result;
    }

    public override bool Equals(object obj)
    {
        if (obj is TCommandSet other)
        {
            for (int i = 0; i < cmds.Length; i++)
                if (cmds[i] != other.cmds[i])
                    return false;
            return true;
        }
        return false;
    }

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (byte b in cmds)
            hash = hash * 31 + b;
        return hash;
    }

    public static bool operator ==(TCommandSet a, TCommandSet b)
    {
        if (ReferenceEquals(a, b)) return true;
        if ((object)a == null || (object)b == null) return false;
        return a.Equals(b);
    }

    public static bool operator !=(TCommandSet a, TCommandSet b)
    {
        return !(a == b);
    }
}
