using TSharpVision.Constants;
using System.Collections.Generic;
namespace TSharpVision;

// TStringCollection — simple string-list helper used by TListBox.
// now implements TStreamable so TListBox can stream its list.
// Wire layout: short count, then WriteString per item.
/// <summary>Ordered, streamable string list used as list-box data.</summary>
public class TStringCollection : TStreamable
{
    /// <summary>Type name written to the stream registry for string collections.</summary>
    public const string TypeName = "TStringCollection";
    /// <inheritdoc />
    public override string streamableName => TypeName;

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClass =
        new TStreamableClass(TypeName, () => new TStringCollection(), 0);

    /// <summary>Mutable backing list in display and serialization order.</summary>
    public List<string> Items = new();
    /// <summary>Number of stored strings.</summary>
    public int Count => Items.Count;
    /// <summary>Appends a string to the end of the collection.</summary>
    public void Insert(string s) => Items.Add(s);
    /// <summary>Returns the string at a valid zero-based index.</summary>
    public string this[int index] => Items[index];

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        os.WriteShort((ushort)Items.Count);
        foreach (var s in Items) os.WriteString(s);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        int count = isStream.ReadShort();
        Items = new List<string>(count);
        for (int i = 0; i < count; i++) Items.Add(isStream.ReadString());
        return this;
    }
}

/// <summary>Data-transfer record pairing a string collection with its focused item index.</summary>
public class TListBoxRec
{
    /// <summary>String collection supplied to or retrieved from the list box.</summary>
    public TStringCollection Items;
    /// <summary>Zero-based focused item index transferred with the collection.</summary>
    public int Selection;
}

/// <summary>A list viewer backed by an ordered string collection, with record-based data transfer.</summary>
public class TListBox : TListViewer
{
    /// <summary>Type identifier used to register and restore this object in a stream.</summary>
    public new static readonly string Name = "TListBox";

    /// <summary>Referenced string collection, or null when the list is empty or detached from data.</summary>
    protected TStringCollection items;
    /// <summary>Whether retrieved labels are padded for centered display.</summary>
    protected bool center;

    /// <summary>Creates an empty list at owner-relative cell bounds with the requested column count and vertical scrollbar.</summary>
    public TListBox(TRect bounds, ushort aNumCols, TScrollBar aScrollBar)
        : base(bounds, aNumCols, null, aScrollBar)
    {
        items = null;
        SetRange(0);
        center = false;
    }

    /// <inheritdoc />
    public override ushort DataSize() => 0;

    /// <inheritdoc />
    public override void GetData(ref object rec)
    {
        rec = new TListBoxRec { Items = items, Selection = focused };
    }

    /// <inheritdoc />
    public override string GetText(int item, int maxChars)
    {
        if (items == null || item < 0 || item >= items.Count) return string.Empty;
        string s = items[item] ?? string.Empty;
        if (s.Length > maxChars) s = s.Substring(0, maxChars);
        return s;
    }

    /// <summary>References a new collection, resets the item range and focus, and redraws the list.</summary>
    public virtual void NewList(TStringCollection aList)
    {
        items = aList;
        SetRange(aList?.Count ?? 0);
        if (range > 0) FocusItem(0);
        DrawView();
    }

    /// <inheritdoc />
    public override void SetData(object rec)
    {
        if (rec is TListBoxRec p)
        {
            NewList(p.Items);
            FocusItem(p.Selection);
            DrawView();
        }
    }

    /// <summary>Returns the currently referenced string collection, or null before data is assigned.</summary>
    public TStringCollection List() => items;

    // Wire layout (after TListViewer base): pointer to TStringCollection items.

    /// <summary>Stream registry descriptor and factory for restoring this concrete type.</summary>
    public static readonly TStreamableClass StreamableClassTListBox =
        new TStreamableClass("TListBox", () => new TListBox(StreamableInit.streamableInit), 0);

    /// <summary>Creates an instance for restoration from a stream without running normal initialization.</summary>
    protected TListBox(StreamableInit init) : base(init) { }

    /// <inheritdoc />
    public override void Write(Opstream os)
    {
        base.Write(os);        // TListViewer.Write
        os.WritePointer(items);
    }

    /// <inheritdoc />
    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);   // TListViewer.Read
        items = isStream.ReadPointer() as TStringCollection;
        return this;
    }

    /// <summary>Creates an instance for stream restoration; its stored state must be read before use.</summary>
    public new static TStreamable Build() { return new TListBox(StreamableInit.streamableInit); }
    /// <inheritdoc />
    public override string StreamableName() => Name;
}
