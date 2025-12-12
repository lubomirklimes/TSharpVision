using TSharpVision.Constants;
using System.Collections.Generic;
namespace TSharpVision;

// TStringCollection — simple string-list helper used by TListBox.
// now implements TStreamable so TListBox can stream its list.
// Wire layout: short count, then WriteString per item.
public class TStringCollection : TStreamable
{
    public const string TypeName = "TStringCollection";
    public override string streamableName => TypeName;

    public static readonly TStreamableClass StreamableClass =
        new TStreamableClass(TypeName, () => new TStringCollection(), 0);

    public List<string> Items = new();
    public int Count => Items.Count;
    public void Insert(string s) => Items.Add(s);
    public string this[int index] => Items[index];

    public override void Write(Opstream os)
    {
        os.WriteShort((ushort)Items.Count);
        foreach (var s in Items) os.WriteString(s);
    }

    public override object Read(Ipstream isStream)
    {
        int count = isStream.ReadShort();
        Items = new List<string>(count);
        for (int i = 0; i < count; i++) Items.Add(isStream.ReadString());
        return this;
    }
}

public class TListBoxRec
{
    public TStringCollection Items;
    public int Selection;
}

public class TListBox : TListViewer
{
    public new static readonly string Name = "TListBox";

    protected TStringCollection items;
    protected bool center;

    public TListBox(TRect bounds, ushort aNumCols, TScrollBar aScrollBar)
        : base(bounds, aNumCols, null, aScrollBar)
    {
        items = null;
        SetRange(0);
        center = false;
    }

    public virtual ushort DataSize() => 0;

    public virtual void GetData(ref object rec)
    {
        rec = new TListBoxRec { Items = items, Selection = focused };
    }

    public override string GetText(int item, int maxChars)
    {
        if (items == null || item < 0 || item >= items.Count) return string.Empty;
        string s = items[item] ?? string.Empty;
        if (s.Length > maxChars) s = s.Substring(0, maxChars);
        return s;
    }

    public virtual void NewList(TStringCollection aList)
    {
        items = aList;
        SetRange(aList?.Count ?? 0);
        if (range > 0) FocusItem(0);
        DrawView();
    }

    public virtual void SetData(object rec)
    {
        if (rec is TListBoxRec p)
        {
            NewList(p.Items);
            FocusItem(p.Selection);
            DrawView();
        }
    }

    public TStringCollection List() => items;

    // Wire layout (after TListViewer base): pointer to TStringCollection items.

    public static readonly TStreamableClass StreamableClassTListBox =
        new TStreamableClass("TListBox", () => new TListBox(StreamableInit.streamableInit), 0);

    protected TListBox(StreamableInit init) : base(init) { }

    public override void Write(Opstream os)
    {
        base.Write(os);        // TListViewer.Write
        os.WritePointer(items);
    }

    public override object Read(Ipstream isStream)
    {
        base.Read(isStream);   // TListViewer.Read
        items = isStream.ReadPointer() as TStringCollection;
        return this;
    }

    public new static TStreamable Build() { return new TListBox(StreamableInit.streamableInit); }
    protected virtual string StreamableName() => Name;
}
