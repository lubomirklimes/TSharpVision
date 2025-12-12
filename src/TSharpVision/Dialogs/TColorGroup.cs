using TSharpVision.Constants;
namespace TSharpVision;

// TColorItem — one palette-entry descriptor in a linked list.
public class TColorItem : IInfo
{
    public string Name;
    /// <summary>1-based index into the owning TPalette.Data array.</summary>
    public byte Index;
    public TColorItem Next;

    public TColorItem(string name, byte index, TColorItem next = null)
    {
        Name  = name;
        Index = index;
        Next  = next;
    }

    // Appends i2 to the tail of the i1 chain and returns i1.
    public static TColorItem operator +(TColorItem i1, TColorItem i2)
    {
        TColorItem cur = i1;
        while (cur.Next != null) cur = cur.Next;
        cur.Next = i2;
        return i1;
    }
}

// TColorGroup — one named group of TColorItems in a linked list.
public class TColorGroup : IInfo
{
    public string Name;
    public TColorItem Items;
    public TColorGroup Next;

    public TColorGroup(string name, TColorItem items = null, TColorGroup next = null)
    {
        Name  = name;
        Items = items;
        Next  = next;
    }

    // Appends item i to the items list of the last group in the g chain.
    public static TColorGroup operator +(TColorGroup g, TColorItem i)
    {
        TColorGroup grp = g;
        while (grp.Next != null) grp = grp.Next;
        if (grp.Items == null)
            grp.Items = i;
        else
        {
            TColorItem cur = grp.Items;
            while (cur.Next != null) cur = cur.Next;
            cur.Next = i;
        }
        return g;
    }

    // Chains g2 onto the tail of g1 and returns g1.
    public static TColorGroup operator +(TColorGroup g1, TColorGroup g2)
    {
        TColorGroup cur = g1;
        while (cur.Next != null) cur = cur.Next;
        cur.Next = g2;
        return g1;
    }
}

// TColorGroupList — TListViewer showing the group names.
// Broadcasts cmNewColorItem when focused group changes.
public class TColorGroupList : TListViewer
{
    public new static readonly string Name = "TColorGroupList";

    private TColorGroup _groups;

    public TColorGroupList(TRect bounds, TScrollBar aScrollBar, TColorGroup aGroups)
        : base(bounds, 1, null, aScrollBar)
    {
        _groups = aGroups;
        int count = 0;
        for (TColorGroup g = aGroups; g != null; g = g.Next) count++;
        SetRange(count);
    }

    // FocusItem broadcasts cmNewColorItem with items pointer.
    public override void FocusItem(int item)
    {
        base.FocusItem(item);
        TColorGroup cur = _groups;
        int n = item;
        while (n-- > 0 && cur != null) cur = cur.Next;
        if (cur == null) return;

        // Broadcast cmNewColorItem with the group's item list as IInfo payload.
        TEvent ev = default;
        ev.What = Events.evBroadcast;
        ev.message.command = Views.cmNewColorItem;
        ev.message.infoPtr  = cur.Items;   // TColorItem : IInfo
        owner?.HandleEvent(ref ev);
    }

    public override string GetText(int item, int maxLen)
    {
        TColorGroup cur = _groups;
        int n = item;
        while (n-- > 0 && cur != null) cur = cur.Next;
        if (cur == null) return string.Empty;
        string s = cur.Name ?? string.Empty;
        if (s.Length > maxLen) s = s.Substring(0, maxLen);
        return s;
    }

    // ── Streaming ────────────────────────────────────────────────────────
    // Wire: TListViewer base + short groupCount
    //       + for each group: WriteString(name) + short itemCount
    //         + for each item: WriteString(name) + byte index.
    public static readonly TStreamableClass StreamableClassTColorGroupList =
        new TStreamableClass("TColorGroupList",
            () => new TColorGroupList(StreamableInit.streamableInit), 0);

    protected TColorGroupList(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);   // TListViewer.Write

        int gc = 0;
        for (TColorGroup g = _groups; g != null; g = g.Next) gc++;
        os.WriteShort((ushort)gc);

        for (TColorGroup g = _groups; g != null; g = g.Next)
        {
            os.WriteString(g.Name);
            int ic = 0;
            for (TColorItem it = g.Items; it != null; it = it.Next) ic++;
            os.WriteShort((ushort)ic);
            for (TColorItem it = g.Items; it != null; it = it.Next)
            {
                os.WriteString(it.Name);
                os.WriteByte(it.Index);
            }
        }
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);   // TListViewer.Read

        int gc = isStream.ReadShort();
        TColorGroup groupsHead = null, groupsTail = null;
        for (int i = 0; i < gc; i++)
        {
            string gName = isStream.ReadString();
            int ic = isStream.ReadShort();
            TColorItem itemsHead = null, itemsTail = null;
            for (int j = 0; j < ic; j++)
            {
                string iName = isStream.ReadString();
                byte idx = (byte)isStream.ReadByte();
                var item = new TColorItem(iName, idx);
                if (itemsTail == null) { itemsHead = item; itemsTail = item; }
                else { itemsTail.Next = item; itemsTail = item; }
            }
            var grp = new TColorGroup(gName, itemsHead);
            if (groupsTail == null) { groupsHead = grp; groupsTail = grp; }
            else { groupsTail.Next = grp; groupsTail = grp; }
        }
        _groups = groupsHead;
        return this;
    }

    public new static TStreamable Build() =>
        new TColorGroupList(StreamableInit.streamableInit);
}

// TColorItemList — TListViewer showing the items of the current group.
// Broadcasts cmNewColorIndex when focused item changes.
// Handles cmNewColorItem broadcast to switch to a new group's items.
public class TColorItemList : TListViewer
{
    public new static readonly string Name = "TColorItemList";

    private TColorItem _items;

    public TColorItemList(TRect bounds, TScrollBar aVScrollBar,
                          TColorItem aItems, TScrollBar aHScrollBar = null)
        : base(bounds, 1, aHScrollBar, aVScrollBar)
    {
        eventMask |= Events.evBroadcast;
        _items = aItems;
        int count = 0;
        for (TColorItem it = aItems; it != null; it = it.Next) count++;
        SetRange(count);
    }

    // FocusItem broadcasts cmNewColorIndex with palette entry index.
    public override void FocusItem(int item)
    {
        base.FocusItem(item);
        TColorItem cur = _items;
        int n = item;
        while (n-- > 0 && cur != null) cur = cur.Next;
        if (cur == null) return;

        // Broadcast cmNewColorIndex with the palette entry index as infoLong.
        TEvent ev = default;
        ev.What = Events.evBroadcast;
        ev.message.command = Views.cmNewColorIndex;
        ev.message.infoLong = cur.Index;
        owner?.HandleEvent(ref ev);
    }

    public override string GetText(int item, int maxLen)
    {
        TColorItem cur = _items;
        int n = item;
        while (n-- > 0 && cur != null) cur = cur.Next;
        if (cur == null) return string.Empty;
        string s = cur.Name ?? string.Empty;
        if (s.Length > maxLen) s = s.Substring(0, maxLen);
        return s;
    }

    // evBroadcast/cmNewColorItem switches the item list.
    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What == Events.evBroadcast
            && @event.message.command == Views.cmNewColorItem
            && @event.message.infoPtr is TColorItem newItems)
        {
            _items = newItems;
            int count = 0;
            for (TColorItem it = _items; it != null; it = it.Next) count++;
            SetRange(count);
            if (count > 0) FocusItem(0);
            DrawView();
        }
    }

    // ── Streaming ────────────────────────────────────────────────────────
    // Items are re-populated via cmNewColorItem broadcast from TColorGroupList.FocusItem.
    public static readonly TStreamableClass StreamableClassTColorItemList =
        new TStreamableClass("TColorItemList",
            () => new TColorItemList(StreamableInit.streamableInit), 0);

    protected TColorItemList(StreamableInit init) : base(init) { }

    public new static TStreamable Build() =>
        new TColorItemList(StreamableInit.streamableInit);
}
