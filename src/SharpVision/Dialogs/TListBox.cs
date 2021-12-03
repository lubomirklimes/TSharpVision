namespace SharpVision.Dialogs;

// ------------------------------------------------------------------------
// TListBox
// ------------------------------------------------------------------------
// Předpokládáme, že TListViewer již existuje; TListBox bude odvozeno z něj.
public class TListBox : TListViewer
{
    public static readonly string Name = "TListBox";

    private TCollection items;

    // Konstruktor TListBox(const TRect& bounds, ushort aNumCols, TScrollBar *aScrollBar)
    public TListBox(TRect bounds, ushort aNumCols, TScrollBar aScrollBar)
        : base(bounds, aNumCols, null, aScrollBar)
    {
        //this.Bounds = bounds;
        // Uložení a inicializace – počet sloupců, scrollBar atd.
    }

    ~TListBox()
    {
        // Úklid, pokud je třeba
    }

    public virtual ushort DataSize()
    {
        throw new NotImplementedException("TListBox.DataSize() není implementováno.");
    }

    public virtual void GetData(ref object rec)
    {
        throw new NotImplementedException("TListBox.GetData() není implementováno.");
    }

    public virtual void GetText(char[] dest, short item, short maxLen)
    {
        throw new NotImplementedException("TListBox.GetText() není implementováno.");
    }

    public virtual void NewList(TCollection aList)
    {
        items = aList;
    }

    public virtual void SetData(object rec)
    {
        throw new NotImplementedException("TListBox.SetData() není implementováno.");
    }

    public TCollection List()
    {
        return items;
    }

    // Konstruktor pro streamable inicializaci
    protected TListBox(object streamableInit)
        :base (streamableInit)
    {
        throw new NotImplementedException("TListBox(streamableInit) není implementováno.");
    }

    protected virtual void Write(Opstream os)
    {
        throw new NotImplementedException("TListBox.Write() není implementováno.");
    }
    protected virtual object Read(Ipstream isStream)
    {
        throw new NotImplementedException("TListBox.Read() není implementováno.");
    }

    public static TStreamable Build()
    {
        throw new NotImplementedException("TListBox.Build() není implementováno.");
    }

    protected virtual string StreamableName()
    {
        return Name;
    }
}
