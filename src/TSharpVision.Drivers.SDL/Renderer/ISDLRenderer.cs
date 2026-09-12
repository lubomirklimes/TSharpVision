namespace TSharpVision.Drivers.SDL;

/// <summary>Renders character-cell content and a caret using SDL with pixel-based font metrics.</summary>
public interface ISDLRenderer : IRenderer, IDisposable
{
    /// <summary>Width of one character cell in pixels.</summary>
    public int CellWidth  { get; }
    /// <summary>Height of one character cell in pixels.</summary>
    public int CellHeight { get; }

    /// <summary>Sets the zero-based caret column and row; a cursor type of zero hides the caret.</summary>
    void SetCursor(int x, int y, ushort cursorType);
}
