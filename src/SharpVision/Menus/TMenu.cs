namespace SharpVision.Menus;

// ========================================================================
// 2. TMenu
// ========================================================================
public class TMenu
{
    // Ukazatel na první položku menu
    public TMenuItem Items { get; set; }
    // Ukazatel na položku, která je výchozí
    public TMenuItem Default { get; set; }

    // Konstruktor bez parametrů
    public TMenu() { Items = null; Default = null; }

    // Konstruktor s jednou položkou – obě pole nastaví na tuto položku
    public TMenu(TMenuItem itemList)
    {
        Items = itemList;
        Default = itemList;
    }

    // Konstruktor s odděleným seznamem a výchozí položkou
    public TMenu(TMenuItem itemList, TMenuItem theDefault)
    {
        Items = itemList;
        Default = theDefault;
    }

    // Destruktor – v C# často nepotřebujeme destruktory, spoléhat se lze na GC.
    ~TMenu()
    {
        // Případné uvolnění zdrojů
    }

    // Statická tovární metoda – vrací instanci TStreamable (stub)
    public static TStreamable Build()
    {
        throw new NotImplementedException("TMenu.Build() není implementováno.");
    }
}
