// Plain-data record paired with TDirCollection.
namespace TSharpVision;

public class TDirEntry
{
    private readonly string _displayText;
    private readonly string _directory;
    private readonly int _nameOffset;

    public TDirEntry(string txt, string dir, int anOffset = 0)
    {
        _displayText = txt ?? string.Empty;
        _directory   = dir ?? string.Empty;
        _nameOffset  = anOffset;
    }

    public string Dir() => _directory;
    public string Text() => _displayText;
    public int Offset() => _nameOffset;
}
