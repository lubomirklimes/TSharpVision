namespace SharpVision;

public class TMenu
{
    public TMenuItem Items { get; set; }
    public TMenuItem Default { get; set; }

    public TMenu() { Items = null; Default = null; }

    public TMenu(TMenuItem itemList)
    {
        Items = itemList;
        Default = itemList;
    }

    public TMenu(TMenuItem itemList, TMenuItem theDefault)
    {
        Items = itemList;
        Default = theDefault;
    }

    ~TMenu()
    {
    }

    public static TStreamable Build()
    {
        throw new NotImplementedException("TMenu.Build() not implemented.");
    }
}
