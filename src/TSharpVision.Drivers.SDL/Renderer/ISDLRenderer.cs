namespace TSharpVision.Drivers.SDL;

public interface ISDLRenderer : IRenderer, IDisposable
{
    public int CellWidth  { get; }
    public int CellHeight { get; }

    void SetCursor(int x, int y, ushort cursorType);
}
