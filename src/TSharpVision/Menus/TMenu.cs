namespace TSharpVision;

/// <summary>A linked menu-item chain with a remembered default selection; nodes are referenced without copying.</summary>
public class TMenu
{
    /// <summary>First item in the menu, or null for an empty menu.</summary>
    public TMenuItem Items { get; set; }
    /// <summary>Item initially selected when the menu opens, or null when no default is stored.</summary>
    public TMenuItem Default { get; set; }

    /// <summary>Creates an empty menu with no default selection.</summary>
    public TMenu() { Items = null; Default = null; }

    /// <summary>References the supplied item chain and selects its first item as the default.</summary>
    public TMenu(TMenuItem itemList)
    {
        Items = itemList;
        Default = itemList;
    }

    /// <summary>References an item chain and the item to select initially.</summary>
    public TMenu(TMenuItem itemList, TMenuItem theDefault)
    {
        Items = itemList;
        Default = theDefault;
    }

    /// <summary>Finalizes the menu without additional resource cleanup.</summary>
    ~TMenu()
    {
    }

    /// <summary>Unsupported restoration factory; calling it throws NotImplementedException.</summary>
    public static TStreamable Build()
    {
        throw new NotImplementedException("TMenu.Build() not implemented.");
    }
}
