// Plain-data record paired with TDirCollection.
namespace TSharpVision;

/// <summary>Directory-list row pairing decorated display text with its target directory path.</summary>
public class TDirEntry
{
    private readonly string _displayText;
    private readonly string _directory;
    private readonly int _nameOffset;

    /// <summary>Stores display text, target path, and name offset within the text; null strings become empty strings.</summary>
    public TDirEntry(string txt, string dir, int anOffset = 0)
    {
        _displayText = txt ?? string.Empty;
        _directory   = dir ?? string.Empty;
        _nameOffset  = anOffset;
    }

    /// <summary>Returns the target directory path associated with the displayed row.</summary>
    public string Dir() => _directory;
    /// <summary>Returns the display text, including any tree-prefix decoration.</summary>
    public string Text() => _displayText;
    /// <summary>Returns the name's starting offset within the decorated display text.</summary>
    public int Offset() => _nameOffset;
}
