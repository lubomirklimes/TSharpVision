namespace SharpVision.Menus;

public class TStatusDef
{
    public TStatusDef Next { get; set; }
    public ushort Min { get; set; }
    public ushort Max { get; set; }
    public TStatusItem Items { get; set; }

    public TStatusDef(ushort aMin, ushort aMax, TStatusItem someItems = null, TStatusDef aNext = null)
    {
        Min = aMin;
        Max = aMax;
        Items = someItems;
        Next = aNext;
    }

    //// Přetížení operátoru + pro připojení TStatusItem
    //public static TStatusDef operator +(TStatusDef s1, TStatusItem s2)
    //{
    //    throw new NotImplementedException("Operátor +(TStatusDef, TStatusItem) není implementován.");
    //}
    //// Přetížení operátoru + pro sloučení dvou TStatusDef
    //public static TStatusDef operator +(TStatusDef s1, TStatusDef s2)
    //{
    //    throw new NotImplementedException("Operátor +(TStatusDef, TStatusDef) není implementován.");
    //}

    // Přetížení operátoru + pro připojení TStatusItem
    public static TStatusDef operator +(TStatusDef s1, TStatusItem s2)
    {
        if (s1 == null)
            throw new ArgumentNullException(nameof(s1));
        if (s1.Items == null)
        {
            s1.Items = s2;
        }
        else
        {
            // Projdeme řetězec položek a připojíme novou položku na konec
            TStatusItem last = s1.Items;
            while (last.Next != null)
                last = last.Next;
            last.Next = s2;
        }
        return s1;
    }

    // Přetížení operátoru + pro sloučení dvou TStatusDef
    public static TStatusDef operator +(TStatusDef s1, TStatusDef s2)
    {
        if (s1 == null) return s2;
        if (s2 == null) return s1;

        if (s1.Items == null)
            s1.Items = s2.Items;
        else
        {
            TStatusItem last = s1.Items;
            while (last.Next != null)
                last = last.Next;
            last.Next = s2.Items;
        }
        return s1;
    }
}
