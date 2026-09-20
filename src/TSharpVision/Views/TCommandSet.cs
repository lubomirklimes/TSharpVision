namespace TSharpVision;

/// <summary>
/// A set of command codes, used to track which commands are currently enabled.
///
/// Covers the whole <see cref="ushort"/> command range (0..65535) — the same range the
/// public command APIs (<see cref="TView.CommandEnabled"/>, <see cref="TView.EnableCommand"/>,
/// <see cref="TMenuItem.Command"/>, <see cref="TStatusItem.Command"/>,
/// <see cref="TEvent.message"/>) accept. A newly constructed set is empty; use
/// <see cref="EnableAll"/> for the opposite starting point.
///
/// The integer-typed members accept any <see cref="int"/> and never throw: a code outside
/// 0..65535 is simply not a member of the set, so <see cref="Has"/> returns
/// <see langword="false"/> and the mutators do nothing.
/// </summary>
public class TCommandSet
{
    /// <summary>Number of distinct command codes a set can hold: the whole ushort range.</summary>
    public const int CommandCount = ushort.MaxValue + 1;

    private const int BitsPerWord = 64;
    private const int WordCount = CommandCount / BitsPerWord;

    private ulong[] cmds = new ulong[WordCount];

    /// <summary>Creates an empty set — no command is a member.</summary>
    public TCommandSet()
    {
    }

    /// <summary>Creates a copy of <paramref name="other"/>.</summary>
    public TCommandSet(TCommandSet other)
    {
        cmds = (ulong[])other.cmds.Clone();
    }

    private static bool InRange(int cmd) => (uint)cmd < (uint)CommandCount;

    /// <summary>True when <paramref name="cmd"/> is a member of the set.</summary>
    public bool Has(int cmd)
        => InRange(cmd) && (cmds[cmd >> 6] & (1UL << (cmd & (BitsPerWord - 1)))) != 0;

    /// <summary>Adds a command code to the set; out-of-range codes are ignored.</summary>
    public void Add(int cmd) { EnableCmd(cmd); }

    /// <summary>Removes a command code from the set; out-of-range codes are ignored.</summary>
    public void Remove(int cmd) { DisableCmd(cmd); }

    /// <summary>Adds <paramref name="cmd"/> to the set. Out-of-range codes are ignored.</summary>
    public void EnableCmd(int cmd)
    {
        if (!InRange(cmd)) return;
        cmds[cmd >> 6] |= 1UL << (cmd & (BitsPerWord - 1));
    }

    /// <summary>Removes <paramref name="cmd"/> from the set. Out-of-range codes are ignored.</summary>
    public void DisableCmd(int cmd)
    {
        if (!InRange(cmd)) return;
        cmds[cmd >> 6] &= ~(1UL << (cmd & (BitsPerWord - 1)));
    }

    /// <summary>Adds every command code to the set.</summary>
    public void EnableAll() => Array.Fill(cmds, ulong.MaxValue);

    /// <summary>Removes every command code from the set.</summary>
    public void DisableAll() => Array.Clear(cmds);

    /// <summary>Union: adds every member of <paramref name="tc"/>.</summary>
    public void Add(TCommandSet tc)
    {
        for (int i = 0; i < cmds.Length; i++)
            cmds[i] |= tc.cmds[i];
    }

    /// <summary>Difference: removes every member of <paramref name="tc"/>.</summary>
    public void Remove(TCommandSet tc)
    {
        for (int i = 0; i < cmds.Length; i++)
            cmds[i] &= ~tc.cmds[i];
    }

    /// <summary>Replaces this set's contents with those of <paramref name="other"/>.</summary>
    public void CopyFrom(TCommandSet other)
    {
        Array.Copy(other.cmds, cmds, WordCount);
    }

    /// <summary>Returns true when the set contains no command codes.</summary>
    public bool IsEmpty()
    {
        foreach (ulong word in cmds)
            if (word != 0)
                return false;
        return true;
    }

    /// <summary>
    /// True when this set and <paramref name="other"/> share at least one member.
    /// Equivalent to <c>!(this &amp; other).IsEmpty()</c> without allocating the intersection.
    /// </summary>
    public bool Intersects(TCommandSet other)
    {
        for (int i = 0; i < cmds.Length; i++)
            if ((cmds[i] & other.cmds[i]) != 0)
                return true;
        return false;
    }

    /// <summary>
    /// True when every member of <paramref name="other"/> is also a member of this set.
    /// Equivalent to <c>this.Equals(this | other)</c> without allocating the union.
    /// </summary>
    public bool IsSupersetOf(TCommandSet other)
    {
        for (int i = 0; i < cmds.Length; i++)
            if ((other.cmds[i] & ~cmds[i]) != 0)
                return false;
        return true;
    }

    /// <summary>Returns a new set containing commands present in both non-null operands.</summary>
    public static TCommandSet operator &(TCommandSet a, TCommandSet b)
    {
        TCommandSet result = new TCommandSet(a);
        for (int i = 0; i < result.cmds.Length; i++)
            result.cmds[i] &= b.cmds[i];
        return result;
    }

    /// <summary>Returns a new set containing commands present in either non-null operand.</summary>
    public static TCommandSet operator |(TCommandSet a, TCommandSet b)
    {
        TCommandSet result = new TCommandSet(a);
        for (int i = 0; i < result.cmds.Length; i++)
            result.cmds[i] |= b.cmds[i];
        return result;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
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

    /// <inheritdoc />
    public override int GetHashCode()
    {
        int hash = 17;
        foreach (ulong word in cmds)
            hash = (hash * 31) + word.GetHashCode();
        return hash;
    }

    /// <summary>Tests equality of command membership, treating two null references as equal.</summary>
    public static bool operator ==(TCommandSet? a, TCommandSet? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return a.Equals(b);
    }

    /// <summary>Tests whether command membership differs, including when exactly one operand is null.</summary>
    public static bool operator !=(TCommandSet? a, TCommandSet? b)
    {
        return !(a == b);
    }
}
