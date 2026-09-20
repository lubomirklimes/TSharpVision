using TSharpVision.Constants;
namespace TSharpVision;

/// <summary>A named palette-entry descriptor in a linked list.</summary>
public class TColorItem : IInfo
{
    /// <summary>Label displayed for this palette entry.</summary>
    public string Name;
    /// <summary>1-based index into the owning TPalette.Data array.</summary>
    public byte Index;
    /// <summary>Next palette-entry descriptor, or null at the end of the chain.</summary>
    public TColorItem? Next;

    /// <summary>Creates a labeled one-based palette-entry reference with an optional next descriptor.</summary>
    public TColorItem(string name, byte index, TColorItem? next = null)
    {
        Name  = name;
        Index = index;
        Next  = next;
    }

    /// <summary>Appends the second chain to the tail of the first chain and returns the first head.</summary>
    public static TColorItem operator +(TColorItem i1, TColorItem i2)
    {
        TColorItem cur = i1;
        while (cur.Next != null) cur = cur.Next;
        cur.Next = i2;
        return i1;
    }
}

/// <summary>A named group of palette-entry descriptors linked to other color groups.</summary>
public class TColorGroup : IInfo
{
    /// <summary>Label displayed for this group of palette entries.</summary>
    public string Name;
    /// <summary>Zero-based focused item remembered for this group.</summary>
    public byte Index;
    /// <summary>First palette-entry descriptor, or null for an empty group.</summary>
    public TColorItem? Items;
    /// <summary>Next color group, or null at the end of the chain.</summary>
    public TColorGroup? Next;

    /// <summary>Creates a named group referencing an optional item chain and following group.</summary>
    public TColorGroup(string name, TColorItem? items = null, TColorGroup? next = null)
    {
        Name  = name;
        Index = 0;
        Items = items;
        Next  = next;
    }

    /// <summary>Appends an item to the last group in the chain and returns the group-chain head.</summary>
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

    /// <summary>Appends the second group chain to the first and returns the first head.</summary>
    public static TColorGroup operator +(TColorGroup g1, TColorGroup g2)
    {
        TColorGroup cur = g1;
        while (cur.Next != null) cur = cur.Next;
        cur.Next = g2;
        return g1;
    }
}

/// <summary>Lists color groups and broadcasts the focused group's items when focus changes.</summary>
public class TColorGroupList : TListViewer
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TColorGroupList";

    private TColorGroup? _groups;

    /// <summary>Creates a single-column group list at owner-relative cell bounds, referencing the supplied groups and vertical scrollbar.</summary>
    public TColorGroupList(TRect bounds, TScrollBar aScrollBar, TColorGroup aGroups)
        : base(bounds, 1, null, aScrollBar)
    {
        _groups = aGroups;
        int count = 0;
        for (TColorGroup? g = aGroups; g != null; g = g.Next) count++;
        SetRange(count);
    }

    /// <inheritdoc />
    /// <remarks>Broadcasts cmNewColorItem with the focused group and its remembered item index.</remarks>
    public override void FocusItem(int item)
    {
        base.FocusItem(item);
        TColorGroup? cur = _groups;
        int n = item;
        while (n-- > 0 && cur != null) cur = cur.Next;
        if (cur == null) return;

        // Broadcast the group itself so the item list can restore its remembered index.
        TEvent ev = default;
        ev.What = Events.evBroadcast;
        ev.message.command = Views.cmNewColorItem;
        ev.message.infoPtr  = cur;
        owner?.HandleEvent(ref ev);
    }

    /// <inheritdoc />
    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What == Events.evBroadcast
            && @event.message.command == Views.cmSaveColorIndex)
        {
            TColorGroup? group = GroupAt(focused);
            if (group != null)
                group.Index = @event.message.infoByte;
        }
    }

    private TColorGroup? GroupAt(int item)
    {
        TColorGroup? group = _groups;
        while (item-- > 0 && group != null)
            group = group.Next;
        return group;
    }

    /// <inheritdoc />
    public override string GetText(int item, int maxLen)
    {
        TColorGroup? cur = _groups;
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
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTColorGroupList =
        new TStreamableClass("TColorGroupList",
            () => new TColorGroupList(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TColorGroupList(StreamableInit init) : base(init) { }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);   // TListViewer.Write

        int gc = 0;
        for (TColorGroup? g = _groups; g != null; g = g.Next) gc++;
        os.WriteShort((ushort)gc);

        for (TColorGroup? g = _groups; g != null; g = g.Next)
        {
            os.WriteString(g.Name);
            int ic = 0;
            for (TColorItem? it = g.Items; it != null; it = it.Next) ic++;
            os.WriteShort((ushort)ic);
            for (TColorItem? it = g.Items; it != null; it = it.Next)
            {
                os.WriteString(it.Name);
                os.WriteByte(it.Index);
            }
        }
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);   // TListViewer.Read

        int gc = isStream.ReadShort();
        TColorGroup? groupsHead = null, groupsTail = null;
        for (int i = 0; i < gc; i++)
        {
            string gName = isStream.ReadString() ?? string.Empty;
            int ic = isStream.ReadShort();
            TColorItem? itemsHead = null, itemsTail = null;
            for (int j = 0; j < ic; j++)
            {
                string iName = isStream.ReadString() ?? string.Empty;
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

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() =>
        new TColorGroupList(StreamableInit.streamableInit);
}

/// <summary>Lists the current group's palette entries, broadcasts the focused index, and switches groups on cmNewColorItem.</summary>
public class TColorItemList : TListViewer
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TColorItemList";

    private TColorItem? _items;

    /// <summary>Creates a palette-entry list at owner-relative cell bounds with the supplied item chain and optional scrollbars.</summary>
    public TColorItemList(TRect bounds, TScrollBar? aVScrollBar,
                          TColorItem? aItems, TScrollBar? aHScrollBar = null)
        : base(bounds, 1, aHScrollBar, aVScrollBar)
    {
        eventMask |= Events.evBroadcast;
        _items = aItems;
        int count = 0;
        for (TColorItem? it = aItems; it != null; it = it.Next) count++;
        SetRange(count);
    }

    /// <inheritdoc />
    /// <remarks>Broadcasts cmNewColorIndex with the focused entry's one-based palette index.</remarks>
    public override void FocusItem(int item)
    {
        base.FocusItem(item);
        TColorItem? cur = _items;
        int n = item;
        while (n-- > 0 && cur != null) cur = cur.Next;
        if (cur == null) return;

        TEvent save = default;
        save.What = Events.evBroadcast;
        save.message.command = Views.cmSaveColorIndex;
        save.message.infoByte = checked((byte)item);
        owner?.HandleEvent(ref save);

        // Broadcast cmNewColorIndex with the palette entry index as infoLong.
        TEvent ev = default;
        ev.What = Events.evBroadcast;
        ev.message.command = Views.cmNewColorIndex;
        ev.message.infoLong = cur.Index;
        owner?.HandleEvent(ref ev);
    }

    /// <inheritdoc />
    public override string GetText(int item, int maxLen)
    {
        TColorItem? cur = _items;
        int n = item;
        while (n-- > 0 && cur != null) cur = cur.Next;
        if (cur == null) return string.Empty;
        string s = cur.Name ?? string.Empty;
        if (s.Length > maxLen) s = s.Substring(0, maxLen);
        return s;
    }

    /// <inheritdoc />
    /// <remarks>Switches the displayed item list on cmNewColorItem broadcasts.</remarks>
    public override void HandleEvent(ref TEvent @event)
    {
        base.HandleEvent(ref @event);
        if (@event.What == Events.evBroadcast
            && @event.message.command == Views.cmNewColorItem
            && @event.message.infoPtr is TColorGroup group)
        {
            _items = group.Items;
            int count = 0;
            for (TColorItem? it = _items; it != null; it = it.Next) count++;
            SetRange(count);
            if (count > 0) FocusItem(Math.Min(group.Index, count - 1));
            DrawView();
        }
    }

    // ── Streaming ────────────────────────────────────────────────────────
    // Items are re-populated via cmNewColorItem broadcast from TColorGroupList.FocusItem.
    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTColorItemList =
        new TStreamableClass("TColorItemList",
            () => new TColorItemList(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TColorItemList(StreamableInit init) : base(init) { }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() =>
        new TColorItemList(StreamableInit.streamableInit);
}
