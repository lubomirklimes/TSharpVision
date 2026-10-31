namespace TSharpVision.Tests.Infrastructure;

// Test-only copy of the composed root buffer. Caller owns the UI thread.
// No live mutable buffer escapes and no capture/serialization stack is required.
internal sealed class RootScreenCopy
{
    private readonly TScreenChar[] cells;
    private readonly int width;
    private readonly int height;

    private RootScreenCopy(int width, int height, TScreenChar[] cells)
    {
        this.width = width;
        this.height = height;
        this.cells = cells;
    }

    public TScreenChar this[int x, int y] => x >= 0 && x < width && y >= 0 && y < height
        ? cells[y * width + x] : throw new ArgumentOutOfRangeException();

    public static RootScreenCopy CopyRoot()
    {
        var buffer = TScreen.ScreenBuffer ?? throw new InvalidOperationException("No composed screen.");
        if (buffer.Width != TScreen.ScreenWidth || buffer.Height != TScreen.ScreenHeight)
            throw new InvalidOperationException("Screen geometry mismatch.");
        return new((int)buffer.Width, (int)buffer.Height, buffer.Data.ToArray());
    }
}
